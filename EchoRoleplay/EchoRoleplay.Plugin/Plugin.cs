using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EchoRoleplay.Game;
using EchoRoleplay.UI;

namespace EchoRoleplay;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/echorp";

    /// The short one.
    private const string ShortCommandName = "/erp";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    /// The nameplate pass.
    [PluginService] internal static INamePlateGui NamePlateGui { get; private set; } = null!;

    /// Reaching the NamePlate addon, projecting a character's world position to the screen, and knowing when
    /// the player has hidden the game's interface so the overlay can get out of the way.
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    /// The players nearby and where they are standing, and the local player - who decides which profile is in
    /// use.
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;

    /// Anything read from the character happens on the framework thread rather than while drawing - and the
    /// expired-status sweep runs from here.
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    /// The Emote and Status sheets, which are where a status icon's name comes from.
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

    /// Who the cursor is on.
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;

    /// The game's own right-click menu, which is the only way into somebody else's profile - see
    /// ProfileContextMenu for why nothing this plugin draws can carry that affordance.
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;

    /// Loads the game's own icon art for statuses.
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    /// Which zone the player is in.
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    /// What the player is currently doing, for the one question this plugin asks of it - whether they are in
    /// a duty.
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    public Configuration Configuration { get; }

    /// Whether the player is inside instanced content.
    internal static bool InDuty =>
        Condition[ConditionFlag.BoundByDuty]
        || Condition[ConditionFlag.BoundByDuty56]
        || Condition[ConditionFlag.BoundByDuty95];

    /// Whether the two things this plugin draws over the world are standing down.
    internal bool HiddenInDuty => Configuration.HideInDuty && InDuty;

    public string Version { get; } =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public readonly WindowSystem WindowSystem = new("EchoRoleplay");
    private readonly MainWindow mainWindow;

    /// Header and icon fonts, shared with the rest of the suite - see UI/Fonts.
    public Fonts Fonts { get; }

    /// Whether the game is showing a label for a given player.
    public NamePlateTracker NamePlates { get; }

    /// Whether anything solid is between the camera and a character.
    public LineOfSight Sight { get; }

    /// Writes a profile's roleplay name onto the character's nameplate.
    public NamePlateNames PlateNames { get; }

    /// The player's own profiles, in their own file beside the configuration.
    public ProfileStore Profiles { get; }

    /// Given a character, their profile.
    public ProfileDirectory Directory { get; }

    /// Everybody else's profiles, in three tiers, off the relay.
    public RelayDirectory Relay { get; }

    /// Raw calls to the relay.
    public RelayClient RelayClient { get; }

    /// Putting a profile on the relay and taking it back off, and the one honest answer to "am I published" -
    /// which is the relay's, not a flag.
    public ProfilePublisher Publisher { get; }

    /// Proving a character is yours through the Lodestone - the badge the claim cannot give.
    public ProfileVerifier Verifier { get; }

    /// Where the player is standing, and whether it is private.
    public GameLocation Location { get; }

    /// Friendships, which are agreed on the relay rather than declared locally.
    public Friendships Friends { get; }

    /// Dalamud's own file picker, for choosing a portrait.
    public FileDialogManager FileDialogs { get; } = new();

    /// Portraits - this installation's own on disk, everybody else's off the relay, and both as textures
    /// ready to draw.
    public Portraits Portraits { get; }

    /// The game's emote and status art, which is what a status icon is chosen from.
    public IconCatalogue Icons { get; }

    /// The game's own closed lists - guardian deities, Grand Companies, classes - for the profile fields that
    /// have a right answer.
    public GameLists Lists { get; }

    /// Every track a theme song can be.
    public MusicCatalogue Music { get; }

    /// Plays a theme over the zone's own music, and puts it back afterwards.
    public GameMusic GameMusic { get; }

    /// "Name@HomeWorld" for whoever is logged in, or empty when nobody is.
    public string LocalCharacterKey { get; private set; } = string.Empty;

    /// The status row drawn out in the world.
    public StatusRowOverlay StatusRow { get; }

    /// The card shown when the cursor rests on somebody.
    public ProfileTooltip Tooltip { get; }

    /// Everyone whose profile has been opened, and the notes written about them.
    public ContactBook Contacts { get; }

    /// Somebody else's sheet, opened from the right-click menu or from Contacts.
    public ProfileView ProfileView { get; }

    private readonly ProfileContextMenu contextMenuEntry;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Migrate();

        Fonts = new Fonts(PluginInterface);
        NamePlates = new NamePlateTracker(NamePlateGui);
        Sight = new LineOfSight();
        Profiles = new ProfileStore(PluginInterface.GetPluginConfigDirectory());
        Contacts = new ContactBook(PluginInterface.GetPluginConfigDirectory());
        Directory = new ProfileDirectory(Profiles, Contacts, () => Configuration.OnlyShowFriends);

        Configuration.EnsureOwnerKey();
        RelayClient = new RelayClient(() => Configuration.OwnerKey);
        Relay = new RelayDirectory(
            RelayClient, new ProfileCache(PluginInterface.GetPluginConfigDirectory()), ObjectTable,
            () => LocalCharacterKey);

        Directory.Attach(Relay);

        Portraits = new Portraits(RelayClient, PluginInterface.GetPluginConfigDirectory());
        Publisher = new ProfilePublisher(RelayClient, Relay, Profiles, Configuration, Portraits);
        Verifier = new ProfileVerifier(RelayClient, Relay, Configuration);
        Friends = new Friendships(RelayClient, Contacts, () => LocalCharacterKey);
        Location = new GameLocation(ClientState, DataManager);

        Icons = new IconCatalogue(DataManager, TextureProvider);
        Lists = new GameLists(DataManager);
        Music = new MusicCatalogue(DataManager);
        GameMusic = new GameMusic(Framework, Music);
        StatusRow = new StatusRowOverlay(GameGui, ObjectTable, Sight, Directory, Icons);

        PlateNames = new NamePlateNames(NamePlateGui, Directory, Configuration, () => LocalCharacterKey);

        Profiles.Changed += PlateNames.Refresh;

        Profiles.Changed += Publisher.NoteChanged;

        Tooltip = new ProfileTooltip(this);

        if (Configuration.AccentColour is { } accent)
            Theme.ApplyAccent(accent);

        mainWindow = new MainWindow(this);
        WindowSystem.AddWindow(mainWindow);
        mainWindow.IsOpen = Configuration.IsMainWindowOpen;

        ProfileView = new ProfileView(this);
        WindowSystem.AddWindow(ProfileView);

        contextMenuEntry = new ProfileContextMenu(
            ContextMenu, Directory, () => LocalCharacterKey, key => ProfileView.Open(key));

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens EchoRoleplay.",
        });

        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens EchoRoleplay. Short for /echorp.",
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainWindow;
        Framework.Update += OnFrameworkUpdate;
    }

    /// How often expired statuses are swept up, in framework ticks.
    private const int PruneIntervalTicks = 300;

    private int tick;

    /// Who is logged in, and whether anything has timed out.
    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            var player = ObjectTable.LocalPlayer;

            var key = player is null ? string.Empty : ProfileDirectory.KeyFor(player);

            if (!string.Equals(key, LocalCharacterKey, StringComparison.Ordinal))
            {
                LocalCharacterKey = key;

                PlateNames.Refresh();
            }

            Profiles.FlushPending();
            Contacts.FlushPending();

            if (!ClientState.IsLoggedIn)
            {
                GameMusic.Stop();
                return;
            }

            Relay.Tick();

            Publisher.Sync(LocalCharacterKey);

            Verifier.Tick();

            Friends.Tick();

            SyncBlocks();

            GameMusic.Tick();

            if (++tick % PruneIntervalTicks == 0)
                Profiles.PruneExpired(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EchoRoleplay] Framework update failed");
        }
    }

    /// What the relay knows about who has been shut out, and when it last changed.
    private int pushedBlockRevision = -1;

    /// Sends the block list when it has moved, so a block cuts both ways.
    private void SyncBlocks()
    {
        if (pushedBlockRevision == Contacts.BlockRevision || LocalCharacterKey.Length == 0)
            return;

        pushedBlockRevision = Contacts.BlockRevision;

        var hashes = Contacts.Blocks
            .Select(c => Shared.CharacterHash.Of(c.CharacterKey))
            .Where(h => h.Length > 0)
            .ToList();

        _ = RelayClient.SetBlocksAsync(hashes, System.Threading.CancellationToken.None);
    }

    /// Shuts everything down.
    public void Dispose()
    {
        Safely(() => PluginInterface.UiBuilder.Draw -= DrawUi);
        Safely(() => PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow);
        Safely(() => PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainWindow);
        Safely(() => Framework.Update -= OnFrameworkUpdate);

        Safely(NamePlates.Dispose);

        Safely(() =>
        {
            Configuration.IsMainWindowOpen = mainWindow.IsOpen;
            Configuration.Save();
        });

        Safely(() => Profiles.FlushPending(force: true));
        Safely(() => Contacts.FlushPending(force: true));

        Safely(contextMenuEntry.Dispose);

        Safely(Friends.Dispose);
        Safely(Relay.Dispose);
        Safely(RelayClient.Dispose);

        Safely(Portraits.Dispose);

        Safely(GameMusic.Dispose);

        Safely(() =>
        {
            Profiles.Changed -= PlateNames.Refresh;
            Profiles.Changed -= Publisher.NoteChanged;
            PlateNames.Dispose();
        });

        Safely(WindowSystem.RemoveAllWindows);
        Safely(() => CommandManager.RemoveHandler(CommandName));
        Safely(() => CommandManager.RemoveHandler(ShortCommandName));
    }

    /// Runs one teardown step, and carries on if it fails.
    private static void Safely(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            try
            {
                Log.Error(ex, "[EchoRoleplay] A step of the plugin's shutdown failed");
            }
            catch (Exception)
            {
            }
        }
    }

    private void OnCommand(string command, string args) => ToggleMainWindow();

    /// Read the UI scale before anything draws, then draw.
    private void DrawUi()
    {
        if (!ClientState.IsLoggedIn)
            return;

        UiHelpers.SampleScale();

        if (Configuration.Statuses == StatusPlacement.UnderCharacter
            && !HiddenInDuty
            && (!Build.Diagnostics || Configuration.ShowStatusRowSpike))
        {
            try
            {
                StatusRow.Draw(
                    Configuration.StatusRowYOffset,
                    Configuration.MarkStatusRowAnchor,
                    Configuration.StatusRowSmoothing,
                    Configuration.StatusRowMaxDistance,
                    Configuration.StatusRowIconSize,
                    LocalCharacterKey,
                    Configuration.AvoidGameUi,
                    Configuration.SnapStatusRowToPixels);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EchoRoleplay] Status row overlay failed");
            }
        }

        WindowSystem.Draw();

        FileDialogs.Draw();

        try
        {
            Tooltip.Draw();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EchoRoleplay] Hover tooltip failed");
        }
    }

    private void ToggleMainWindow() => mainWindow.IsOpen = !mainWindow.IsOpen;
}
