namespace EchoSim.Shared;

/// One damage event, reduced to what downtime detection needs.
public readonly record struct DamageHit(double Time, long TargetId, double Amount);

/// Works out when there was nothing the raid could hit.
public static class DowntimeDetector
{
    /// How long the raid must go with nothing to hit before it counts.
    public const double DefaultGapSeconds = 4.0;

    /// Stretches where no enemy took damage for longer than gapSeconds.
    public static List<LogDowntime> Windows(
        IEnumerable<DamageHit> hits,
        double duration,
        double gapSeconds = DefaultGapSeconds)
    {
        var windows = new List<LogDowntime>();

        var times = hits
            .Where(h => h.Time >= 0 && h.Time <= duration)
            .Select(h => h.Time)
            .OrderBy(t => t)
            .ToList();

        for (var i = 1; i < times.Count; i++)
        {
            if (times[i] - times[i - 1] >= gapSeconds)
                windows.Add(new LogDowntime { Start = times[i - 1], End = times[i] });
        }

        return windows;
    }
}
