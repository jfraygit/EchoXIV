using EchoSim.Shared;
using EchoSim.Sim.Analysis;

namespace EchoSim.Game;

/// Notices when a leaderboard fight has actually been cleared, and hands the pull over.
public sealed class KillWatcher : IDisposable
{
    private readonly CombatTracker tracker;

    /// One finished stretch of combat, before it is known which phase it was.
    private readonly record struct Pull(
        IReadOnlyList<TimelineCast> Casts, double Active, double Elapsed,
        IReadOnlyList<DowntimeWindow> Downtime, DateTime At);

    /// Every stretch of combat fought in the current instance, oldest first.
    private readonly List<Pull> pulls = [];

    /// Which instance pulls belongs to, so leaving one empties it.
    private uint pullsTerritory;

    private bool wasInCombat;

    /// Raised when a fight with a board was cleared, with everything a submission needs.
    public event Action<EncounterDef, IReadOnlyList<TimelineCast>, double, double,
        IReadOnlyList<DowntimeWindow>>? Cleared;

    /// Kept so it can be unsubscribed, and a lambda rather than a method for one specific reason: the event's
    /// argument type is INTERNAL to Dalamud and so cannot be named from a plugin.
    private readonly Dalamud.Plugin.Services.IDutyState.DutyCompletedDelegate completed;

    public KillWatcher(CombatTracker tracker)
    {
        this.tracker = tracker;

        completed = _ => OnDutyCompleted();
        Plugin.DutyState.DutyCompleted += completed;
    }

    public void Dispose() => Plugin.DutyState.DutyCompleted -= completed;

    /// Polled from the framework tick, alongside the tracker's own update.
    public void Update()
    {
        var territory = Plugin.ClientState.TerritoryType;

        if (territory != pullsTerritory)
        {
            pulls.Clear();
            pending = null;
            intermediates = 0;
            pullsTerritory = territory;
        }

        var boards = Encounters.InTerritory(territory);
        var inCombat = tracker.InCombat;

        if (boards.Length > 1 && tracker.ConsumePhaseBoundary())
        {
            var partial = tracker.Partial;
            var split = tracker.Split();

            if (partial)
            {
                Plugin.Log.Information(
                    "EchoSim: a phase ended, but this fight was already under way when EchoSim started " +
                    "watching - not posting a partial timeline.");
            }
            else if (split.Casts.Count > 0)
            {
                pending = new Pending(
                    split.Casts, split.Active, split.Elapsed, split.Downtime, DateTime.UtcNow);

                Plugin.Log.Information(
                    $"EchoSim: phase ended after {split.Elapsed:F0}s with {split.Casts.Count} casts - " +
                    "holding it to see whether the duty completes.");
            }
        }

        if (pending is { } waiting && (DateTime.UtcNow - waiting.At).TotalSeconds > PhaseGraceSeconds)
        {
            pending = null;

            var index = Math.Min(intermediates, Math.Max(0, boards.Length - 2));
            intermediates++;

            Plugin.Log.Information($"EchoSim: no duty completion followed, so posting {boards[index].Short}.");
            Report(boards[index], waiting.Casts, waiting.Active, waiting.Elapsed, waiting.Downtime);
        }

        if (wasInCombat && !inCombat && boards.Length > 0)
        {
            var casts = tracker.Casts;
            if (casts.Count > 0 && !tracker.Partial)
            {
                pulls.Add(new Pull(
                    casts, tracker.ActiveSeconds, tracker.Elapsed, tracker.Downtime, DateTime.UtcNow));

                if (pulls.Count > MaximumHeldPulls)
                    pulls.RemoveAt(0);
            }
        }

        wasInCombat = inCombat;
    }

    /// A kill whose phase is not settled yet - see Update.
    private readonly record struct Pending(
        IReadOnlyList<TimelineCast> Casts, double Active, double Elapsed,
        IReadOnlyList<DowntimeWindow> Downtime, DateTime At);

    private Pending? pending;

    /// How many non-final phases have been posted in this instance.
    private int intermediates;

    /// How long a boss death waits for a duty completion before it counts as an earlier phase.
    private const double PhaseGraceSeconds = 8;

    /// How many finished pulls are worth keeping.
    private const int MaximumHeldPulls = 4;

    private void Report(
        EncounterDef encounter, IReadOnlyList<TimelineCast> casts, double active, double elapsed,
        IReadOnlyList<DowntimeWindow> downtime)
    {
        Plugin.Log.Information(
            $"EchoSim: posting to board \"{encounter.Key}\" ({encounter.Name}) - " +
            $"{casts.Count} casts, {elapsed:F0}s elapsed, {active:F0}s active, " +
            $"{downtime.Count} downtime window(s).");

        try
        {
            Cleared?.Invoke(encounter, casts, active, elapsed, downtime);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"EchoSim: handling a clear of {encounter.Name} failed");
        }
    }

    /// The duty is cleared, which settles the kill that was waiting: it was the final phase.
    private void OnDutyCompleted()
    {
        var territory = Plugin.ClientState.TerritoryType;
        var boards = Encounters.InTerritory(territory);

        var completedAt = DateTime.UtcNow;
        var held = pulls.Count > 0 ? pulls[^1] : (Pull?)null;
        var settled = pending;

        var intermediatesPosted = intermediates;

        pulls.Clear();
        pending = null;
        intermediates = 0;

        if (boards.Length == 0 || territory != pullsTerritory)
            return;

        if (boards.Length > 1)
        {
            if (settled is not { } final)
            {
                Plugin.Log.Warning(
                    $"EchoSim: cleared {boards[^1].Name} but no boss death was seen, so nothing was " +
                    "posted - the phases could not be told apart.");

                return;
            }

            Plugin.Log.Information(
                $"EchoSim: duty completed with {boards.Length} phase boards - posting the final one, " +
                $"{boards[^1].Short}, from the kill held {(completedAt - final.At).TotalSeconds:F1}s ago " +
                $"({final.Casts.Count} casts over {final.Elapsed:F0}s). " +
                $"{intermediatesPosted} earlier phase(s) were posted this instance.");

            Report(boards[^1], final.Casts, final.Active, final.Elapsed, final.Downtime);
            return;
        }

        var partial = tracker.Partial;
        Pull? live = null;

        if (tracker.InCombat)
        {
            var split = tracker.Split();

            if (partial)
            {
                Plugin.Log.Information(
                    $"EchoSim: cleared {boards[0].Short}, but this fight was already under way when " +
                    "EchoSim started watching - not posting a partial timeline.");

                return;
            }

            if (split.Casts.Count > 0)
                live = new Pull(split.Casts, split.Active, split.Elapsed, split.Downtime, completedAt);
        }

        var chosen = live ?? (held is { } recent && (completedAt - recent.At).TotalSeconds <= StalePullSeconds
            ? recent
            : null);

        if (chosen is { } pull)
        {
            Report(boards[0], pull.Casts, pull.Active, pull.Elapsed, pull.Downtime);
            return;
        }

        Plugin.Log.Warning(
            $"EchoSim: cleared {boards[0].Short} but no timeline belonged to it - nothing was posted.");
    }

    /// How long after a stretch of combat ends it can still be the pull that was cleared.
    private const double StalePullSeconds = 15;
}
