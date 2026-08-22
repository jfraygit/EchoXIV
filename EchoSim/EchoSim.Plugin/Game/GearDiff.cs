using EchoSim.Sim;

namespace EchoSim.Game;

/// One slot's difference between two gear sets.
public sealed record SlotDiff
{
    public required string Slot { get; init; }
    public GearPiece? From { get; init; }
    public GearPiece? To { get; init; }
    public string FromMelds { get; init; } = string.Empty;
    public string ToMelds { get; init; } = string.Empty;

    /// The relic's allocated substats, which are a gear choice rather than a property of the item.
    public string FromRelic { get; init; } = string.Empty;
    public string ToRelic { get; init; } = string.Empty;

    public bool ItemChanged => (From?.ItemId ?? 0) != (To?.ItemId ?? 0);

    public bool MeldsChanged => !string.Equals(FromMelds, ToMelds, StringComparison.Ordinal);

    public bool RelicChanged => !string.Equals(FromRelic, ToRelic, StringComparison.Ordinal);

    public bool AnyChange => ItemChanged || MeldsChanged || RelicChanged;
}

/// One stat's difference between two sets.
public readonly record struct StatDiff(string Name, int From, int To)
{
    public int Delta => To - From;
}

/// Compares two gear sets so the difference can be shown as a to-do list rather than as two unrelated
/// numbers.
public static class GearDiff
{
    public static List<SlotDiff> Compare(
        IReadOnlyDictionary<string, uint> fromGear,
        IReadOnlyDictionary<string, List<int>> fromMelds,
        IReadOnlyDictionary<string, uint> toGear,
        IReadOnlyDictionary<string, List<int>> toMelds,
        IReadOnlyDictionary<SubStat, int>? fromRelic = null,
        IReadOnlyDictionary<SubStat, int>? toRelic = null)
    {
        var diffs = new List<SlotDiff>();
        var weaponLabel = GearCatalog.Label(GearSlot.Weapon);

        foreach (var slot in Enum.GetValues<GearSlot>())
        {
            var label = GearCatalog.Label(slot);
            var isWeapon = label == weaponLabel;

            diffs.Add(new SlotDiff
            {
                Slot = label,
                From = GearCatalog.ById(fromGear.GetValueOrDefault(label)),
                To = GearCatalog.ById(toGear.GetValueOrDefault(label)),
                FromMelds = Describe(fromMelds.GetValueOrDefault(label)),
                ToMelds = Describe(toMelds.GetValueOrDefault(label)),

                FromRelic = isWeapon ? DescribeRelic(fromRelic) : string.Empty,
                ToRelic = isWeapon ? DescribeRelic(toRelic) : string.Empty,
            });
        }

        return diffs;
    }

    /// "+447 DET, +447 CRT, +108 DH" for a relic's allocated substats, largest first.
    public static string DescribeRelic(IReadOnlyDictionary<SubStat, int>? substats)
        => RelicAllocation.Describe(substats, GearCatalog.StatLabel);

    /// "2x CRT, 1x DET" style summary of a slot's melds, ordered so it compares stably.
    public static string Describe(IReadOnlyList<int>? melds)
    {
        if (melds is null || melds.Count == 0)
            return string.Empty;

        var counts = new Dictionary<SubStat, int>();
        foreach (var encoded in melds)
        {
            if (MateriaCatalog.Decode(encoded) is { } option)
                counts[option.Stat] = counts.GetValueOrDefault(option.Stat) + 1;
        }

        return string.Join(", ", counts
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Value}x {GearCatalog.StatLabel(kv.Key)}"));
    }

    /// The stat-by-stat difference between two sets, named as the ACTIVE JOB names them.
    public static List<StatDiff> CompareStats(StatPreset from, StatPreset to)
    {
        var role = Sim.Jobs.JobRegistry.ForOrDefault(GearCatalog.ActiveJob).CreateSim().Role;

        var rows = new List<StatDiff>
        {
            new("Weapon Damage", from.WeaponDamage, to.WeaponDamage),
            new(GearCatalog.MainStatName, from.MainStat, to.MainStat),
            new("Critical Hit", from.Crit, to.Crit),
            new("Determination", from.Determination, to.Determination),
            new("Direct Hit", from.DirectHit, to.DirectHit),
            new(GearCatalog.SpeedStatName, from.SkillSpeed, to.SkillSpeed),
        };

        if (Sim.Engine.CombatRoles.UsesTenacity(role))
            rows.Add(new StatDiff("Tenacity", from.Tenacity, to.Tenacity));

        if (Sim.Engine.CombatRoles.UsesPiety(role))
            rows.Add(new StatDiff("Piety", from.Piety, to.Piety));

        return rows;
    }
}
