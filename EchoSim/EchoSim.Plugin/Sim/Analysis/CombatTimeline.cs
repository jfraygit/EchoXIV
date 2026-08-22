namespace EchoSim.Sim.Analysis;

/// One action that was used, from either a simulation or a real log.
public readonly record struct TimelineCast(double Time, string Action, bool IsGcd, double Damage);

/// A window where the boss couldn't be hit - a phase transition, an invulnerability, or simply being out of
/// range.
public readonly record struct DowntimeWindow(double Start, double End)
{
    public double Duration => End - Start;

    public bool Contains(double time) => time >= Start && time < End;
}

/// A death and the recovery that followed it.
public readonly record struct DeathWindow(
    double Time,
    double ResumedAt,
    double ResurrectedAt = 0,
    bool ByLimitBreak = false)
{
    /// Seconds spent not dealing damage - death until the next thing they pressed.
    public double Duration => Math.Max(0, ResumedAt - Time);

    /// Where the Weakness clock starts, falling back to the first action when no raise was seen.
    public double PenaltyStartsAt => ResurrectedAt > 0 ? ResurrectedAt : ResumedAt;
}

/// What both the simulator and a parsed FFLogs report reduce to.
public sealed class CombatTimeline
{
    public required string JobName { get; init; }

    /// Total fight length in seconds.
    public required double Duration { get; init; }

    public required IReadOnlyList<TimelineCast> Casts { get; init; }

    /// Windows where the target couldn't be hit.
    public IReadOnlyList<DowntimeWindow> Downtime { get; init; } = [];

    /// Every time the player died.
    public IReadOnlyList<DeathWindow> Deaths { get; init; } = [];

    /// Where this came from, for the UI to label its findings.
    public string Source { get; init; } = "simulation";

    /// Total damage, when known.
    public double TotalDamage => Casts.Sum(c => c.Damage);

    /// Seconds the target was actually attackable.
    public double UptimeSeconds => Duration - Downtime.Sum(d => d.Duration);

    public bool IsDowntime(double time) => Downtime.Any(d => d.Contains(time));

    /// How many times an ability on this recast could have been used.
    public int PossibleUses(double recast)
        => recast <= 0 ? 0 : (int)Math.Floor(UptimeSeconds / recast) + 1;

    public int CastCount(string action) => Casts.Count(c => c.Action == action);

    /// How many times this action was used, counting whatever names a log records it under.
    public int CastCount(Engine.ActionDef action) => Casts.Count(c => action.MatchesLoggedName(c.Action));

    public IEnumerable<TimelineCast> Of(string action) => Casts.Where(c => c.Action == action);
}
