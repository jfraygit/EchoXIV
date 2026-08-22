namespace EchoSim.Sim.Validation;

/// One melded materia, reduced to what validation cares about.
public readonly record struct MeldInfo(SubStat Stat, int Value, string Name);

/// One equipped slot, described independently of where the data came from.
public sealed record GearSlotSetup
{
    public required string SlotName { get; init; }
    public uint ItemId { get; init; }
    public string ItemName { get; init; } = "-";
    public int ItemLevel { get; init; }
    public bool IsUnique { get; init; }
    public int MateriaSlots { get; init; }

    /// The most materia this item can carry, which exceeds MateriaSlots whenever advanced melding is
    /// permitted.
    public int MaxMelds { get; init; }

    /// Never below the slot count, whatever MaxMelds was set to.
    public int MeldCapacity => Math.Max(MaxMelds, MateriaSlots);

    public int WeaponDamage { get; init; }

    public IReadOnlyList<MeldInfo> Melds { get; init; } = [];

    /// Substats allocated onto a customisable relic, if this slot holds one.
    public IReadOnlyList<MeldInfo> RelicStats { get; init; } = [];

    /// True when this piece is a relic whose substats are chosen rather than fixed.
    public bool IsRelic { get; init; }
    public IReadOnlyDictionary<SubStat, int> BaseStats { get; init; } = new Dictionary<SubStat, int>();
    public IReadOnlyDictionary<SubStat, int> Caps { get; init; } = new Dictionary<SubStat, int>();

    public bool IsEmpty => ItemId == 0;

    public int BaseStat(SubStat stat) => BaseStats.GetValueOrDefault(stat);

    public int Cap(SubStat stat) => Caps.TryGetValue(stat, out var cap) ? cap : int.MaxValue;

    public int MeldedTotal(SubStat stat) => Melds.Where(m => m.Stat == stat).Sum(m => m.Value);
}

public sealed record GearSetup(string JobName, IReadOnlyList<GearSlotSetup> Slots)
{
    /// True when the stat line was read from the character sheet rather than built from the catalogue.
    public bool StatsFromCharacterSheet { get; init; }

    /// Substats that do nothing for this job, so melding them is pure waste.
    public IReadOnlyList<SubStat> UselessStats { get; init; } = [];

    /// Substats this job's relic cannot be allocated, whether or not they would help.
    public IReadOnlyList<SubStat> UnavailableStats { get; init; } = [];

    /// Substats the job technically uses but doesn't want, so melding them is a small loss rather than a
    /// total one.
    public IReadOnlyList<SubStat> AvoidedStats { get; init; } = [];

    /// What to call the speed substat in a message, since the enum has one member for two stats.
    public string SpeedStatLabel { get; init; } = "SkS";
}

/// The gear and materia equivalent of the rotation linter.
public static class GearValidator
{
    public static List<Finding> Validate(GearSetup setup)
    {
        var findings = new List<Finding>();

        if (setup.Slots.Count == 0 || setup.Slots.All(s => s.IsEmpty))
        {
            findings.Add(new Finding(Severity.Info, "no-gear",
                "No gear selected yet - read your equipped set, or start from Current BIS."));

            return findings;
        }

        CheckSlotsFilled(setup, findings);
        CheckWeapon(setup, findings);
        CheckDuplicateUniques(setup, findings);
        CheckMeldCounts(setup, findings);
        CheckCapsArePossible(setup, findings);
        CheckOvercappedMelds(setup, findings);
        CheckWastedStats(setup, findings);
        CheckRelic(setup, findings);
        Summarise(setup, findings);

        return findings;
    }

    private static void CheckSlotsFilled(GearSetup setup, List<Finding> f)
    {
        var empty = setup.Slots.Where(s => s.IsEmpty).Select(s => s.SlotName).ToList();
        if (empty.Count > 0)
        {
            f.Add(new Finding(Severity.Warning, "empty-slot",
                $"{empty.Count} slot(s) empty: {string.Join(", ", empty)}."));
        }
    }

    private static void CheckWeapon(GearSetup setup, List<Finding> f)
    {
        var weapon = setup.Slots.FirstOrDefault(s => s.WeaponDamage > 0);
        if (weapon is null)
        {
            f.Add(new Finding(Severity.Error, "no-weapon",
                "No weapon - weapon damage is the single largest term in the damage formula, so the result is meaningless without one."));
        }
    }

    private static void CheckDuplicateUniques(GearSetup setup, List<Finding> f)
    {
        var duplicates = setup.Slots
            .Where(s => !s.IsEmpty && s.IsUnique)
            .GroupBy(s => s.ItemId)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicates)
        {
            f.Add(new Finding(Severity.Error, "duplicate-unique",
                $"{group.First().ItemName} is unique but equipped in {group.Count()} slots ({string.Join(", ", group.Select(s => s.SlotName))})."));
        }
    }

    private static void CheckMeldCounts(GearSetup setup, List<Finding> f)
    {
        foreach (var slot in setup.Slots)
        {
            if (slot.IsEmpty || slot.Melds.Count <= slot.MeldCapacity)
                continue;

            f.Add(new Finding(Severity.Error, "too-many-melds",
                $"{slot.ItemName} has {slot.Melds.Count} materia but can hold only {slot.MeldCapacity}.",
                slot.SlotName));
        }
    }

    /// An item's own substats cannot exceed its cap - the game wouldn't have produced it.
    private static void CheckCapsArePossible(GearSetup setup, List<Finding> f)
    {
        foreach (var slot in setup.Slots)
        {
            if (slot.IsEmpty)
                continue;

            if (slot.MateriaSlots > 0 && slot.Caps.Count == 0)
            {
                f.Add(new Finding(Severity.Error, "no-caps",
                    $"{slot.ItemName} offers {slot.MateriaSlots} materia slot(s) but EchoSim couldn't work out its stat caps. " +
                    "Melds there would be treated as uncapped.",
                    slot.SlotName));
            }

            foreach (var stat in Enum.GetValues<SubStat>())
            {
                var onItem = slot.BaseStat(stat);
                var cap = slot.Cap(stat);

                if (onItem <= 0 || cap <= 0 || cap == int.MaxValue || onItem <= cap)
                    continue;

                f.Add(new Finding(Severity.Error, "impossible-cap",
                    $"{slot.ItemName} has {onItem} {SpeedAware(setup, stat)} but EchoSim computed a cap of {cap}. " +
                    "That can't be right - the cap maths is wrong, not your gear.",
                    slot.SlotName));
            }
        }
    }

    /// Melds whose value spills past the item's cap are simply thrown away.
    private static void CheckOvercappedMelds(GearSetup setup, List<Finding> f)
    {
        var totalWasted = 0;

        foreach (var slot in setup.Slots)
        {
            if (slot.IsEmpty)
                continue;

            foreach (var stat in Enum.GetValues<SubStat>())
            {
                var melded = slot.MeldedTotal(stat);
                if (melded == 0)
                    continue;

                var combined = slot.BaseStat(stat) + melded;
                var cap = slot.Cap(stat);
                if (combined <= cap)
                    continue;

                var wasted = combined - cap;
                totalWasted += wasted;

                f.Add(new Finding(Severity.Warning, "overcapped-meld",
                    $"{slot.ItemName} is over its {SpeedAware(setup, stat)} cap - {wasted} points of materia are doing nothing.",
                    slot.SlotName));
            }
        }

        if (totalWasted > 0)
        {
            f.Add(new Finding(Severity.Info, "overcapped-meld",
                $"{totalWasted} total substat points lost to caps."));
        }
    }

    /// A substat named as this job names it.
    private static string SpeedAware(GearSetup setup, SubStat stat)
        => stat == SubStat.Speed ? setup.SpeedStatLabel : FoodDef.Label(stat);

    /// Materia in a stat the job can't use, or actively doesn't want.
    private static void CheckWastedStats(GearSetup setup, List<Finding> f)
    {
        string Label(SubStat stat) => SpeedAware(setup, stat);

        foreach (var stat in setup.UselessStats)
        {
            var count = setup.Slots.Sum(s => s.Melds.Count(m => m.Stat == stat));
            if (count > 0)
            {
                f.Add(new Finding(Severity.Warning, "useless-meld",
                    $"{count} {Label(stat)} materia melded - it does nothing for {setup.JobName}."));
            }
        }

        foreach (var stat in setup.AvoidedStats)
        {
            var count = setup.Slots.Sum(s => s.Melds.Count(m => m.Stat == stat));
            if (count > 0)
            {
                f.Add(stat == SubStat.Speed
                    ? new Finding(Severity.Info, "avoided-meld",
                        $"{count} {Label(stat)} materia melded. For pure damage {setup.JobName} ranks it last, "
                        + "but a speed tier also buys cast-lock and cooldown alignment this simulation "
                        + "doesn't measure - so this is a note, not a mistake.")
                    : new Finding(Severity.Warning, "avoided-meld",
                        $"{count} {Label(stat)} materia melded - {setup.JobName} treats it as the lowest-value substat."));
            }
        }
    }

    /// A relic's substats are chosen rather than rolled, so an unfinished or duplicated allocation is
    /// silently costing damage in a way no other slot can.
    private static void CheckRelic(GearSetup setup, List<Finding> f)
    {
        var relics = setup.Slots.Where(s => s.IsRelic && !s.IsEmpty).ToList();
        var primary = relics
            .OrderByDescending(s => s.RelicStats.Count)
            .FirstOrDefault();

        foreach (var slot in relics.Where(s => s == primary))
        {
            if (slot.RelicStats.Count == 0)
            {
                f.Add(setup.StatsFromCharacterSheet
                    ? new Finding(Severity.Info, "relic-not-readable",
                        "Your relic's substats can't be read directly, but they're already included in the stats above.",
                        slot.SlotName)
                    : new Finding(Severity.Warning, "relic-unallocated",
                        "Your relic has no substats chosen yet. Click the weapon to pick them - it's a large amount of free stat.",
                        slot.SlotName));
                continue;
            }

            var stats = slot.RelicStats.Select(r => r.Stat).ToList();
            if (stats.Count != stats.Distinct().Count())
            {
                f.Add(new Finding(Severity.Error, "relic-duplicate",
                    "Your relic uses the same substat twice. The game only allows each one once.",
                    slot.SlotName));
            }

            if (slot.RelicStats.Count < 3)
            {
                f.Add(new Finding(Severity.Warning, "relic-unallocated",
                    $"Your relic has {slot.RelicStats.Count} of 3 substats chosen. Click the weapon to finish it.",
                    slot.SlotName));
            }

            foreach (var impossible in slot.RelicStats.Where(r => setup.UnavailableStats.Contains(r.Stat)))
            {
                f.Add(new Finding(Severity.Error, "relic-unavailable",
                    $"{setup.JobName} relics can't be given {SpeedAware(setup, impossible.Stat)} - the game won't " +
                    "offer it. Pick another stat, or these totals describe a weapon you can't build.",
                    slot.SlotName));
            }

            foreach (var wasted in slot.RelicStats.Where(r =>
                         !setup.UnavailableStats.Contains(r.Stat) && setup.UselessStats.Contains(r.Stat)))
            {
                f.Add(new Finding(Severity.Warning, "relic-wasted",
                    $"Your relic is using {SpeedAware(setup, wasted.Stat)}, which does nothing for {setup.JobName}.",
                    slot.SlotName));
            }

            foreach (var avoided in slot.RelicStats.Where(r =>
                         !setup.UnavailableStats.Contains(r.Stat)
                         && !setup.UselessStats.Contains(r.Stat)
                         && setup.AvoidedStats.Contains(r.Stat)))
            {
                f.Add(new Finding(Severity.Info, "relic-avoided",
                    $"Your relic puts its smallest allocation on {SpeedAware(setup, avoided.Stat)}. " +
                    $"It is the least valuable of the stats {setup.JobName} relics can be given, " +
                    "but it is not wasted.",
                    slot.SlotName));
            }
        }
    }

    private static void Summarise(GearSetup setup, List<Finding> f)
    {
        var filled = setup.Slots.Count(s => !s.IsEmpty);
        var melds = setup.Slots.Sum(s => s.Melds.Count);
        var slotsAvailable = setup.Slots.Where(s => !s.IsEmpty).Sum(s => s.MateriaSlots);

        f.Add(new Finding(Severity.Info, "summary",
            $"{filled}/{setup.Slots.Count} slots filled, {melds}/{slotsAvailable} materia slots used."));

        if (melds < slotsAvailable)
        {
            f.Add(new Finding(Severity.Warning, "unused-slots",
                $"{slotsAvailable - melds} materia slot(s) left empty."));
        }
    }
}
