namespace EchoSim.Sim.Engine;

/// One damage instance landing at a point in time.
public readonly record struct DamageEvent(
    double Time,
    string Source,
    double Potency,
    double BuffMulti,
    double Damage,
    bool IsAutoAttack,

    /// Whether this hit was a guaranteed critical hit, and whether a guaranteed direct hit.
    bool AutoCrit = false,
    bool AutoDirectHit = false);

/// One action press on the timeline, damaging or not.
public readonly record struct TimelineEntry(
    double Time,
    string Action,
    ActionKind Kind,
    double Damage,
    /// Seconds this GCD landed later than the moment it came off cooldown, from any cause - an over-long
    /// weave, or mudra presses that didn't fit in the remaining window.
    double GcdDelaySeconds);

public sealed class SkillBreakdown
{
    public required string Name { get; init; }
    public int Casts { get; set; }
    public double TotalDamage { get; set; }
    public double TotalPotency { get; set; }
    public double DamageShare { get; set; }
}

public sealed class SimResult
{
    public required string JobName { get; init; }
    public required double Duration { get; init; }
    public required PlayerStats Stats { get; init; }

    public List<DamageEvent> Damage { get; } = [];
    public List<TimelineEntry> Timeline { get; } = [];

    /// Rotation problems the simulator caught, such as an action whose requirements weren't met.
    public List<string> Warnings { get; } = [];

    /// State as it stood when the fight ended, for the rotation linter to inspect.
    public SimState? FinalState { get; set; }

    public double TotalDamage => Damage.Sum(d => d.Damage);
    public double Dps => Duration > 0 ? TotalDamage / Duration : 0;

    /// Total GCD time lost to delays.
    public double TotalGcdDelay => Timeline.Sum(t => t.GcdDelaySeconds);

    /// Total time the global cooldown was occupied, accumulated as the fight ran.
    public double GcdTimeConsumed { get; set; }

    /// Fraction of the fight actually spent executing GCDs.
    public double GcdUptime => Duration > 0 ? System.Math.Min(1.0, GcdTimeConsumed / Duration) : 0;

    public int GcdCount => Timeline.Count(t => t.Kind is ActionKind.Gcd or ActionKind.Ninjutsu);

    public List<SkillBreakdown> Breakdown()
    {
        var total = TotalDamage;
        var byName = new Dictionary<string, SkillBreakdown>();

        foreach (var d in Damage)
        {
            if (!byName.TryGetValue(d.Source, out var entry))
                byName[d.Source] = entry = new SkillBreakdown { Name = d.Source };

            entry.Casts++;
            entry.TotalDamage += d.Damage;
            entry.TotalPotency += d.Potency;
        }

        foreach (var entry in byName.Values)
            entry.DamageShare = total > 0 ? entry.TotalDamage / total : 0;

        return [.. byName.Values.OrderByDescending(e => e.TotalDamage)];
    }

    /// Damage per fixed-width slice of the fight, expressed as DPS within that slice.
    public float[] DpsBuckets(double bucketSeconds = 10.0)
    {
        var count = System.Math.Max(1, (int)System.Math.Ceiling(Duration / bucketSeconds));
        var buckets = new double[count];

        foreach (var d in Damage)
        {
            var index = (int)(d.Time / bucketSeconds);
            if (index >= 0 && index < count)
                buckets[index] += d.Damage;
        }

        var result = new float[count];
        for (var i = 0; i < count; i++)
        {
            var width = System.Math.Min(bucketSeconds, Duration - (i * bucketSeconds));
            result[i] = width > 0 ? (float)(buckets[i] / width) : 0;
        }

        return result;
    }
}
