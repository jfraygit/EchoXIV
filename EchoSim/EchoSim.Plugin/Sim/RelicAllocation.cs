namespace EchoSim.Sim;

/// What kind of weapon slot a relic occupies, which is what its substats are worth.
public enum RelicSlot
{
    TwoHand,
    OneHand,
    OffHand,
}

/// Substats allocated onto a customisable relic weapon.
public sealed class RelicAllocation
{
    /// The two larger allocations.
    public int MajorA { get; set; } = -1;

    public int MajorB { get; set; } = -1;

    /// The smaller third allocation.
    public int Minor { get; set; } = -1;

    /// What one major allocation is worth on each kind of weapon, at the current relic step.
    public static int MajorValue(RelicSlot slot) => slot switch
    {
        RelicSlot.TwoHand => 447,
        RelicSlot.OneHand => 319,
        _ => 128,
    };

    public static int MinorValue(RelicSlot slot) => slot switch
    {
        RelicSlot.TwoHand => 108,
        RelicSlot.OneHand => 76,
        _ => 32,
    };

    public bool AnySet => MajorA >= 0 || MajorB >= 0 || Minor >= 0;

    /// What this allocation contributes to a given substat on a given kind of weapon.
    public int ValueFor(SubStat stat, RelicSlot slot = RelicSlot.TwoHand)
    {
        var value = 0;
        if (MajorA == (int)stat) value += MajorValue(slot);
        if (MajorB == (int)stat) value += MajorValue(slot);
        if (Minor == (int)stat) value += MinorValue(slot);
        return value;
    }

    /// Every stat this allocation touches, for display and validation.
    public IEnumerable<(SubStat Stat, int Value)> Entries(RelicSlot slot = RelicSlot.TwoHand)
    {
        if (MajorA >= 0) yield return ((SubStat)MajorA, MajorValue(slot));
        if (MajorB >= 0) yield return ((SubStat)MajorB, MajorValue(slot));
        if (Minor >= 0) yield return ((SubStat)Minor, MinorValue(slot));
    }

    public bool IsComplete => MajorA >= 0 && MajorB >= 0 && Minor >= 0;

    /// True when no stat is used twice - the game enforces this, so the sim should too.
    public bool IsDistinct
    {
        get
        {
            var set = Entries().Select(e => e.Stat).ToList();
            return set.Count == set.Distinct().Count();
        }
    }

    public RelicAllocation Clone() => new() { MajorA = MajorA, MajorB = MajorB, Minor = Minor };

    /// This allocation as a substat map, in the same shape as inferred relic stats.
    public Dictionary<SubStat, int> ToSubstats(RelicSlot slot = RelicSlot.TwoHand)
    {
        var result = new Dictionary<SubStat, int>();
        foreach (var (stat, value) in Entries(slot))
            result[stat] = result.GetValueOrDefault(stat) + value;

        return result;
    }

    /// What changed between two relics, per substat.
    public static Dictionary<SubStat, int> Delta(
        IReadOnlyDictionary<SubStat, int>? from,
        IReadOnlyDictionary<SubStat, int>? to)
    {
        var result = new Dictionary<SubStat, int>();

        foreach (var stat in Enum.GetValues<SubStat>())
        {
            var change = (to?.GetValueOrDefault(stat) ?? 0) - (from?.GetValueOrDefault(stat) ?? 0);
            if (change != 0)
                result[stat] = change;
        }

        return result;
    }

    /// "+447 Det, +447 Crit, +108 DH" for a relic's allocated substats, largest first.
    public static string Describe(
        IReadOnlyDictionary<SubStat, int>? substats,
        Func<SubStat, string>? label = null)
    {
        if (substats is null || substats.Count == 0)
            return string.Empty;

        label ??= FoodDef.Label;

        return string.Join(", ", substats
            .Where(kv => kv.Value != 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => $"+{kv.Value} {label(kv.Key)}"));
    }

    public void Clear()
    {
        MajorA = -1;
        MajorB = -1;
        Minor = -1;
    }
}
