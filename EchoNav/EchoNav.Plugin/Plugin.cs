using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EchoNav.Game;
using EchoNav.Nav;
using EchoNav.UI;

namespace EchoNav;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/echonav";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    /// Only for LocalPlayer - the player's own position is the origin of every route, and as of API 13 it
    /// hangs off the object table rather than IClientState.
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;

    /// Reading either target source touches client memory, so all of it happens here rather than during ImGui
    /// drawing - see NavSnapshot.
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    /// The managed half of the target list.
    [PluginService] internal static IFateTable FateTable { get; private set; } = null!;

    /// Read only, to report the player's movement mode in diagnostics.
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;

    /// Needed for the movement hook - see MovementOverride.
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;

    /// Tells the mount controller whether the character is already mounted, casting, or in a state where
    /// mounting would be refused.
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    /// Reads the Aetheryte sheet, to put names to the shards found in the world.
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

    /// Used to find the aethernet window - see AethernetMenu.
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    /// Used to watch what the aethernet window receives when a row is clicked.
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    /// Loads the map icons the game already has for each encounter, and any artwork supplied alongside the
    /// plugin - see EncounterArt.
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    public Configuration Configuration { get; }

    public string Version { get; } =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public readonly WindowSystem WindowSystem = new("EchoNav");
    private readonly MainWindow mainWindow;

    /// Null in a release build, where there is no diagnostics window at all - see Build.
    private readonly DebugWindow? debugWindow;

    /// Whether the diagnostics window is up.
    private bool DebugWindowOpen => debugWindow is { IsOpen: true };

    /// Header and icon fonts, shared with the rest of the suite - see UI/Fonts.
    public Fonts Fonts { get; }

    /// Opened from Settings as well as from the command, so the window needs a way in.
    public void ToggleDebugWindow()
    {
        if (debugWindow != null)
            debugWindow.IsOpen = !debugWindow.IsOpen;
    }

    private readonly FateReader fateReader;
    private readonly DynamicEventReader dynamicEventReader = new();

    /// Keeps the list in the order things appeared rather than re-sorting it as you move.
    public TargetOrder TargetOrder { get; } = new();

    /// The player's mount collection, for the picker in Settings.
    public MountRoster MountRoster { get; }

    /// The pot cycle, which is what everyone in the zone plans around.
    public PotTracker PotTracker { get; }

    /// How old this copy of the zone is, where the game will say.
    public InstanceReader InstanceReader { get; } = new();

    /// Keeps the knowledge-crystal buffs up while standing at a crystal.
    public PhantomBuffer PhantomBuffer { get; }

    /// Picks up coffers that lie on the way.
    public CofferCollector CofferCollector { get; }

    /// Records a walk, for teaching the mesh a crossing it could not work out - see TrailRecorder.
    public TrailRecorder TrailRecorder { get; }

    /// Phantom job levels, read on the framework thread so Settings can show them without touching client
    /// memory while drawing.
    public PhantomSnapshot PhantomState { get; private set; } = PhantomSnapshot.Unavailable;

    public MovementOverride MovementOverride { get; }
    public MountController MountController { get; }
    public MovementDriver MovementDriver { get; }

    /// Holds the current zone's navmesh, built from the game's own collision files.
    public NavMeshProvider NavMesh { get; }

    /// Routes over that mesh.
    public MeshRoutePlanner Router { get; }

    /// Learns where the zone's aetheryte shards are, for shard-assisted routing.
    public AetheryteRegistry AetheryteRegistry { get; }

    /// Puts teleport-menu names to those shards.
    public AethernetDirectory AethernetDirectory { get; }

    /// Reads the game's aethernet window, which is the only place the real shard names live - see
    /// AethernetMenu.
    public AethernetMenu AethernetMenu { get; } = new();

    /// Occult Return, the straight-to-base-camp action - see ReturnAction.
    public ReturnAction ReturnAction { get; }

    /// Runs a whole trip end to end: walk, teleport, walk.
    public JourneyController Journey { get; }

    private JourneyStage lastJourneyStage = JourneyStage.Idle;

    /// Chimes once when a trip actually finishes.
    private void AnnounceArrival()
    {
        var stage = Journey.Stage;

        if (stage == JourneyStage.Done && lastJourneyStage != JourneyStage.Done
            && Configuration.ArrivalSound && Journey.AnnouncesArrival)
            SoundEffects.Play(SoundEffects.Arrival);

        lastJourneyStage = stage;
    }

    /// Works out how to get to a point, over the zone's navmesh.
    public IReadOnlyList<System.Numerics.Vector3> PlanRoute(System.Numerics.Vector3 destination)
    {
        var from = ObjectTable.LocalPlayer?.Position ?? destination;

        var obstacles = AetheryteRegistry.Current
            .Select(shard => new MeshRoutePlanner.Obstacle(shard.Position, 2.5f))
            .ToList();

        if (Configuration.AvoidMonsters)
            obstacles.AddRange(NearbyMonsters(from, destination));

        Router.SolidObjects = obstacles;

        return Router.Plan(from, destination);
    }

    /// How wide a berth to give a monster.
    private const float MonsterRadius = 9f;

    /// How many to bother with.
    private const int MaxMonstersConsidered = 12;

    /// Live enemies worth steering around, nearest the route first.
    private IEnumerable<MeshRoutePlanner.Obstacle> NearbyMonsters(
        System.Numerics.Vector3 from, System.Numerics.Vector3 destination)
    {
        var scored = new List<(float Distance, MeshRoutePlanner.Obstacle Obstacle)>();

        try
        {
            foreach (var obj in ObjectTable)
            {
                if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc)
                    continue;

                if (npc.BattleNpcKind != Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Combatant)
                    continue;

                if (npc.IsDead || npc.CurrentHp == 0)
                    continue;

                var along = ClosestApproach(from, destination, npc.Position);
                if (along > MonsterRadius * 3f)
                    continue;

                scored.Add((along, new MeshRoutePlanner.Obstacle(npc.Position, MonsterRadius, OnlyInTheOpen: true)));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EchoNav] Could not read nearby monsters");
        }

        scored.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return scored.Take(MaxMonstersConsidered).Select(entry => entry.Obstacle);
    }

    /// How far out to bother collecting monsters for the steering to bend around.
    private const float ThreatScanRange = 28f;

    /// How much of the destination counts as its arena.
    private const float ArenaRadius = 40f;

    /// The zone's knowledge level, which is what monsters are worth measuring against.
    private byte KnowledgeLevel()
    {
        if (!PhantomState.Available)
            return 0;

        if (PhantomState.KnowledgeLevel is > 0 and < 100)
            return PhantomState.KnowledgeLevel;

        if (PhantomState.KnowledgePoints is > 0 and < 100)
            return (byte)PhantomState.KnowledgePoints;

        return 0;
    }

    /// Room a sidestep needs before the driver will take it - see MovementDriver.Deflect.
    private const float DeflectionClearance = 2f;

    /// Live enemies close enough to steer around, as bare positions.
    public int BattleNpcsInRange { get; private set; }

    private List<System.Numerics.Vector3> ThreatsNear(System.Numerics.Vector3 playerPosition)
    {
        var threats = new List<System.Numerics.Vector3>();
        var inRange = 0;
        var player = ObjectTable.LocalPlayer;
        var me = player?.GameObjectId ?? 0;
        var floor = Configuration.IgnoreMonstersBelowLevel;
        var arena = MovementDriver.FinalDestination;

        try
        {
            foreach (var obj in ObjectTable)
            {
                if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc)
                    continue;

                if (System.Numerics.Vector3.Distance(npc.Position, playerPosition) > ThreatScanRange)
                    continue;

                inRange++;

                if (npc.BattleNpcKind != Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Combatant)
                    continue;

                if (npc.IsDead || npc.CurrentHp == 0)
                    continue;

                if (floor > 0 && npc.Level < floor)
                    continue;

                if (System.Numerics.Vector3.Distance(npc.Position, arena) <= ArenaRadius)
                    continue;

                if (npc.TargetObjectId == me)
                    continue;

                threats.Add(npc.Position);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EchoNav] Could not read nearby monsters");
        }

        BattleNpcsInRange = inRange;
        return threats;
    }

    private static float ClosestApproach(
        System.Numerics.Vector3 from, System.Numerics.Vector3 to, System.Numerics.Vector3 point)
    {
        var segment = to - from;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared < 0.01f)
            return System.Numerics.Vector3.Distance(from, point);

        var t = Math.Clamp(System.Numerics.Vector3.Dot(point - from, segment) / lengthSquared, 0f, 1f);
        return System.Numerics.Vector3.Distance(System.Numerics.Vector3.Lerp(from, to, t), point);
    }

    /// Same question, but allowed to answer "teleport" - see TravelPlanner.
    public TravelPlan PlanTravel(System.Numerics.Vector3 destination)
    {
        var from = ObjectTable.LocalPlayer?.Position ?? destination;
        return TravelPlanner.Plan(
            Router, AetheryteRegistry.Current, from, destination,
            AetheryteRegistry.MainAetheryte, ReturnAction.IsAvailable);
    }

    /// Latest reading of the zone.
    public NavSnapshot Snapshot { get; private set; } = NavSnapshot.Empty;


    /// Territory the visit counter was last brought up to date against.
    private uint lastSeenTerritory;

    /// Counts entries into the supported zone, standing in for an instance number the game doesn't report -
    /// see PotInstance.
    private void TrackZoneVisits(uint territory)
    {
        if (territory == lastSeenTerritory)
            return;

        lastSeenTerritory = territory;

        if (!SupportedZones.Supports(territory))
            return;

        Configuration.ZoneVisit++;
        Configuration.Save();
    }

    private PotInstance CurrentPotInstance() => new(Configuration.ZoneVisit);

    /// Neither table changes fast enough to justify reading it every frame, and both cost real work - the
    /// FATE table allocates per entry and the DynamicEvent walk copies sixteen slots.
    private const long RefreshIntervalMs = 500;
    private long lastRefreshTick;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Migrate();
        fateReader = new FateReader(FateTable);
        MountRoster = new MountRoster(DataManager);
        PotTracker = new PotTracker(Configuration);
        MovementOverride = new MovementOverride(GameInterop, SigScanner);
        MountController = new MountController(Condition, Configuration);
        MovementDriver = new MovementDriver(GameConfig, Configuration, MovementOverride, MountController);
        NavMesh = new NavMeshProvider(
            DataManager, ClientState,
            PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty,
            PluginInterface.GetPluginConfigDirectory());
        Router = new MeshRoutePlanner(NavMesh) { Log = message => Log.Warning($"[EchoNav] {message}") };

        MovementDriver.IsOpenGround = point => Router.IsOpenGround(point, DeflectionClearance);
        AetheryteRegistry = new AetheryteRegistry(ObjectTable, ClientState, PluginInterface.GetPluginConfigDirectory());
        AethernetDirectory = new AethernetDirectory(DataManager);
        ReturnAction = new ReturnAction(DataManager, Condition);
        PhantomBuffer = new PhantomBuffer(Configuration, Condition, ObjectTable, MovementDriver, MountController);
        CofferCollector = new CofferCollector(Configuration, Condition, ObjectTable, MovementDriver);
        TrailRecorder = new TrailRecorder(PluginInterface.GetPluginConfigDirectory());
        Journey = new JourneyController(ObjectTable, MovementDriver, AethernetMenu, ReturnAction, PhantomBuffer, CofferCollector);

        Fonts = new Fonts(PluginInterface);

        if (Configuration.AccentColour is { } accent)
            Theme.ApplyAccent(accent);

        var art = new EncounterArt(TextureProvider);

        mainWindow = new MainWindow(this, art);
        WindowSystem.AddWindow(mainWindow);
        mainWindow.IsOpen = Configuration.IsMainWindowOpen;

        if (Build.Diagnostics)
        {
            debugWindow = new DebugWindow(this);
            WindowSystem.AddWindow(debugWindow);
            debugWindow.IsOpen = Configuration.IsDebugWindowOpen;
        }

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = Build.Diagnostics
                ? "Opens EchoNav. \"/echonav debug\" opens the diagnostics window."
                : "Opens EchoNav."
        });

        lastSeenTerritory = ClientState.TerritoryType;

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainWindow;
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Journey.Stop();
        MovementDriver.Stop();

        Configuration.IsMainWindowOpen = mainWindow.IsOpen;

        if (debugWindow != null)
            Configuration.IsDebugWindowOpen = debugWindow.IsOpen;

        Configuration.Save();

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainWindow;
        Framework.Update -= OnFrameworkUpdate;

        AetheryteRegistry.SaveNow();
        NavMesh.Dispose();
        MovementOverride.Dispose();

        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        if (debugWindow != null && args.Trim().Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            debugWindow.IsOpen = !debugWindow.IsOpen;
            return;
        }

        ToggleMainWindow();
    }

    /// Read the UI scale before anything draws, then draw.
    private void DrawUi()
    {
        if (!ClientState.IsLoggedIn)
            return;

        UiHelpers.SampleScale();
        WindowSystem.Draw();
    }

    private void ToggleMainWindow() => mainWindow.IsOpen = !mainWindow.IsOpen;

    private void OnFrameworkUpdate(IFramework framework)
    {
        var localPlayer = ObjectTable.LocalPlayer;

        NavMesh.Tick();
        TrackZoneVisits(ClientState.TerritoryType);

        InstanceReader.Tick(ClientState.TerritoryType);

        if (mainWindow.IsOpen || DebugWindowOpen)
            MountRoster.Tick();

        if (!SupportedZones.Supports(ClientState.TerritoryType))
        {
            if (Journey.Stage != JourneyStage.Idle || MovementDriver.IsRunning)
                Journey.Stop();

            Snapshot = NavSnapshot.Empty with
            {
                TerritoryType = ClientState.TerritoryType,
                MeshDetail = NavMesh.Detail,
            };

            return;
        }

        if (localPlayer != null)
        {
            if (MovementDriver.IsRunning)
            {
                MovementDriver.Threats = Configuration.AvoidMonsters
                    ? ThreatsNear(localPlayer.Position)
                    : [];

                MovementDriver.Tick(localPlayer.Position);
            }

            AetheryteRegistry.Tick();
            AethernetMenu.Refresh();
            AethernetMenu.Tick();
            Journey.Tick(localPlayer.Position, PlanRoute);
            AnnounceArrival();

            PhantomBuffer.Tick(
                localPlayer.Position,
                busy: !Journey.IsBuffing && (Journey.IsRunning || MovementDriver.IsRunning));

            CofferCollector.Tick(localPlayer.Position);
            TrailRecorder.Tick(localPlayer.Position);

            if (AethernetMenu.IsOpen)
            {
                AetheryteRegistry.LearnName(localPlayer.Position, AethernetMenu.CurrentLocation);
                AetheryteRegistry.LearnAllFromMenu(
                    AethernetMenu.Destinations, AethernetMenu.CurrentLocation, localPlayer.Position);
            }
        }

        if (!mainWindow.IsOpen && !DebugWindowOpen)
            return;

        var now = Environment.TickCount64;
        if (now - lastRefreshTick < RefreshIntervalMs)
            return;
        lastRefreshTick = now;

        try
        {
            var baseCamp = AetheryteRegistry.MainAetheryte;
            var fates = fateReader.Read();

            PotTracker.Observe(fates, CurrentPotInstance());

            Snapshot = new NavSnapshot
            {
                TerritoryType = ClientState.TerritoryType,
                Pots = PotTracker.Forecast,
                HasPlayer = localPlayer != null,
                PlayerPosition = localPlayer?.Position ?? default,
                BaseCamp = baseCamp?.Position,
                BaseCampName = baseCamp?.DisplayName ?? string.Empty,
                ReturnAvailable = ReturnAction.IsAvailable,
                Fates = fates,
                DynamicEvents = dynamicEventReader.Read(ClientState.TerritoryType),
                MeshState = NavMesh.State,
                MeshDetail = NavMesh.Detail,
            };

            TargetOrder.Observe(Snapshot.AllTargets);

            PhantomState = PhantomReader.Read();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EchoNav] Failed to refresh zone snapshot");
        }
    }
}
