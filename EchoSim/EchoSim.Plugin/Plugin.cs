using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EchoSim.Game;
using EchoSim.Sim;
using EchoSim.Sim.Engine;
using EchoSim.Sim.Jobs;
using EchoSim.UI;
using System.Text;

namespace EchoSim;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/echosim";

    /// The short form, for the command people actually type several times a session.
    private const string ShortCommandName = "/es";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    /// Needed to hop back onto the game thread after a network call, before touching game data.
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    /// Combat state, for starting and stopping the live score without a hook of its own.
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    /// Only used to watch the local player use an action - see CombatTracker.
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;

    /// The game's own account of whether a duty was completed.
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;

    public Configuration Configuration { get; }
    public Fonts Fonts { get; }

    /// Assembly version, included in bug reports.
    public string Version { get; } =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public readonly WindowSystem WindowSystem = new("EchoSim");
    private readonly MainWindow mainWindow;

    /// The in-combat overlay.
    public LiveWindow Live { get; }

    /// Watches the player's own actions, so a fight can be scored while it is happening.
    public CombatTracker Combat { get; }

    public Plugin()
    {
        Log.Information(
            $"EchoSim {Version} starting - relay {Game.RelayEndpoints.BaseUrl} " +
            $"({(Game.RelayEndpoints.CanChoose ? "development build" : "release build")}).");

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        if (Configuration.Migrate())
        {
            PluginInterface.SavePluginConfig(Configuration);
            Log.Information("EchoSim: converted stat names in a configuration written by an older build.");
        }

        Fonts = new Fonts(PluginInterface);
        Combat = new CombatTracker();

        Game.GearCatalog.Warm();
        Game.GameData.WarmFoods();
        Game.GameData.WarmActionIcons();
        Game.MateriaCatalog.Warm();

        Kills = new KillWatcher(Combat);
        Kills.Cleared += OnCleared;

        UI.Theme.ApplyAccent(new System.Numerics.Vector4(
            Configuration.AccentR, Configuration.AccentG, Configuration.AccentB, 1f));

        mainWindow = new MainWindow(this);
        WindowSystem.AddWindow(mainWindow);
        mainWindow.IsOpen = Configuration.WindowOpen;

        Live = new LiveWindow(this) { IsOpen = true };
        WindowSystem.AddWindow(Live);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Open the EchoSim rotation simulator. Also {ShortCommandName}.",
        });

        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the EchoSim rotation simulator.",
            ShowInHelp = false,
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;

        Framework.Update += OnFrameworkUpdate;

        Game.GameData.VerifyJobModifiers();
        Game.GameData.VerifyAnalysisActions();
    }

    public void SaveConfig()
    {
        Configuration.WindowOpen = mainWindow.IsOpen;

        Configuration.CaptureActiveSetup();

        PluginInterface.SavePluginConfig(Configuration);
    }

    /// "/echosim dump &lt;job&gt;" writes that job's action data next to the plugin's config, for building a
    /// new job's sim against.
    private void OnCommand(string command, string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length >= 1 && parts[0].Equals("bis", StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("EchoSim: building best-in-slot sets for every job - this takes a moment.");
            _ = Task.Run(DumpBestInSlot);
            return;
        }

        if (parts.Length < 2 || !parts[0].Equals("dump", StringComparison.OrdinalIgnoreCase))
        {
            ToggleWindow();
            return;
        }

        var wanted = parts[1];
        var job = JobList.All().FirstOrDefault(j => j.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));

        if (job.Id == 0)
        {
            Log.Warning($"EchoSim: no job called '{wanted}'. Try one of: " +
                        string.Join(", ", JobList.All().Select(j => j.Name)));
            return;
        }

        try
        {
            var path = Path.Combine(PluginInterface.GetPluginConfigDirectory(), $"{job.Name.ToLowerInvariant()}-actions.txt");
            File.WriteAllText(path, JobResearch.Dump(job.Id, job.Name));
            Log.Information($"EchoSim: wrote {job.Name} action data to {path}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "EchoSim: could not write the action dump");
        }
    }

    /// Writes a real best-in-slot stat line for every implemented job, built from the game's own item data
    /// and melded by the solver.
    private void DumpBestInSlot()
    {
        var lines = new StringBuilder();
        lines.AppendLine("EchoSim best-in-slot, from game item data + solved melds. Unbuffed, unfed.");
        lines.AppendLine();

        var selectedJob = GearCatalog.ActiveJob;

        using var pin = GearCatalog.Pin();

        foreach (var jobId in Sim.Jobs.JobRegistry.Implemented)
        {
            var definition = Sim.Jobs.JobRegistry.For(jobId)!;

            try
            {
                var sim = definition.CreateSim();

                GearCatalog.SetJob(jobId, warm: false, force: true);
                GearCatalog.BuildNow();

                var selection = GearCatalog.BestAvailable(definition.TargetGcd is not null);

                if (selection.Count == 0)
                {
                    lines.AppendLine($"{definition.Name}: no gear found for this job.");
                    continue;
                }

                PlayerStats ToStats(StatPreset p) => p.ToPlayerStats(
                    sim,
                    partyBonus: false, food: FoodDef.None, potion: PotionDef.Grade4);

                var profile = JobRegistry.AutoCritProfileFor(definition.ClassJobId);

                var relic = SolveRelic(selection, ToStats, sim.Role, profile);

                var melds = MeldOptimiser.Optimise(
                    selection, relic, ToStats, null, definition.TargetGcd, profile);
                var preset = GearCatalog.ToStatPreset(
                    selection, sim.MainStatModifier, $"{definition.Name} BiS", melds, relic);

                var stats = ToStats(preset);

                var substats = preset.Crit + preset.Determination + preset.DirectHit
                               + preset.SkillSpeed + preset.Tenacity;

                lines.AppendLine($"{definition.Name}:");
                lines.AppendLine($"    WeaponDamage = {preset.WeaponDamage}, WeaponDelay = {preset.WeaponDelay:F2},");
                lines.AppendLine($"    MainStat = {preset.MainStat},");
                lines.AppendLine($"    Crit = {preset.Crit}, Determination = {preset.Determination}, " +
                                 $"DirectHit = {preset.DirectHit}, SkillSpeed = {preset.SkillSpeed},");
                lines.AppendLine($"    Tenacity = {preset.Tenacity}, Piety = {preset.Piety},");
                lines.AppendLine($"    // substats {substats} incl. Tenacity, " +
                                 $"GCD {stats.Gcd:F2}s at {sim.HastePercent}% haste, role {sim.Role}");
                lines.AppendLine();
            }
            catch (Exception ex)
            {
                lines.AppendLine($"{definition.Name}: FAILED - {ex.Message}");
            }
        }

        GearCatalog.SetJob(selectedJob, force: true);

        try
        {
            var path = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "bis-presets.txt");
            File.WriteAllText(path, lines.ToString());
            Log.Information($"EchoSim: wrote best-in-slot presets to {path}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "EchoSim: could not write the best-in-slot dump");
        }

        foreach (var line in lines.ToString().Split('\n'))
            Log.Information(line.TrimEnd());
    }

    /// Picks the relic's three substats the same way the melds are picked: by trying them.
    private static RelicAllocation SolveRelic(
        IReadOnlyDictionary<string, uint> selection,
        Func<StatPreset, PlayerStats> toStats,
        CombatRole role,
        AutoCritProfile profile = default)
    {
        var stats = role.RelicSubstats().ToArray();

        var best = new RelicAllocation();
        var bestIndex = double.NegativeInfinity;

        foreach (var majorA in stats)
        {
            foreach (var majorB in stats)
            {
                if (majorB <= majorA)
                    continue;

                foreach (var minor in stats)
                {
                    if (minor == majorA || minor == majorB)
                        continue;

                    var candidate = new RelicAllocation
                    {
                        MajorA = (int)majorA,
                        MajorB = (int)majorB,
                        Minor = (int)minor,
                    };

                    var melds = MeldOptimiser.Optimise(selection, candidate, toStats, null, null, profile);
                    var preset = GearCatalog.ToStatPreset(selection, 100, "probe", melds, candidate);
                    var index = GearMath.DamageIndex(toStats(preset), profile);

                    if (index <= bestIndex)
                        continue;

                    bestIndex = index;
                    best = candidate;
                }
            }
        }

        return best;
    }

    private void ToggleWindow() => mainWindow.Toggle();

    /// Nothing is drawn outside the world.
    private void DrawUi()
    {
        if (!ClientState.IsLoggedIn)
            return;

        WindowSystem.Draw();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!ClientState.IsLoggedIn)
            return;

        Combat.Update();
        Kills.Update();
    }

    /// Notices cleared leaderboard fights.
    public KillWatcher Kills { get; }

    /// Posts a cleared fight to its board, if the player asked for that.
    private void OnCleared(
        Shared.EncounterDef encounter,
        IReadOnlyList<Sim.Analysis.TimelineCast> casts,
        double activeSeconds,
        double elapsed,
        IReadOnlyList<Sim.Analysis.DowntimeWindow> downtime)
    {
        if (!Configuration.ShareToLeaderboards)
            return;

        var player = ObjectTable.LocalPlayer;
        var world = player?.HomeWorld.ValueNullable?.Name.ExtractText();

        if (player is null || string.IsNullOrWhiteSpace(world))
            return;

        var submission = new Shared.LeaderboardSubmission
        {
            Character = player.Name.TextValue,
            World = world,

            Region = Game.WorldRegions.ForLocalPlayer(),
            JobId = player.ClassJob.RowId,
            EncounterKey = encounter.Key,
            ActiveSeconds = activeSeconds,
            Duration = elapsed,
            OwnerKey = OwnerKey(),
            PluginVersion = Version,
            KilledAt = DateTime.UtcNow,
            Casts = [.. casts.Select(c => new Shared.LeaderboardCast
            {
                Time = c.Time,
                Action = c.Action,
                IsGcd = c.IsGcd,
            })],
            Downtime = [.. downtime.Select(d => new Shared.LeaderboardDowntime
            {
                Start = d.Start,
                End = d.End,
            })],
        };

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await Game.LeaderboardClient
                    .SubmitAsync(submission, CancellationToken.None)
                    .ConfigureAwait(false);

                Log.Information($"EchoSim: leaderboard - {result.Reason}");
                LastSubmission = result;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EchoSim: could not post to the leaderboard");
            }
        });
    }

    /// The last board reply, so the Leaderboards tab can say what happened to the last kill.
    public Shared.LeaderboardSubmitResult? LastSubmission { get; private set; }

    /// This installation's leaderboard owner key, made on first use.
    public string OwnerKey()
    {
        if (!string.IsNullOrWhiteSpace(Configuration.LeaderboardOwnerKey))
            return Configuration.LeaderboardOwnerKey;

        Configuration.LeaderboardOwnerKey = Guid.NewGuid().ToString("n") + Guid.NewGuid().ToString("n");
        SaveConfig();

        return Configuration.LeaderboardOwnerKey;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;

        Kills.Cleared -= OnCleared;
        Kills.Dispose();
        Combat.Dispose();

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);

        Game.Portraits.Dispose();

        Configuration.WindowOpen = mainWindow.IsOpen;
        Configuration.CaptureActiveSetup();
        PluginInterface.SavePluginConfig(Configuration);

        WindowSystem.RemoveAllWindows();
    }
}
