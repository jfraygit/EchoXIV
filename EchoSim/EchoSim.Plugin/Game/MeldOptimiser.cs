using EchoSim.Sim.Engine;
using EchoSim.Sim;

namespace EchoSim.Game;

/// Works out the best materia to put in a set.
public static class MeldOptimiser
{
    /// Ranking function for a stat line.
    public static double DamageIndex(PlayerStats stats, AutoCritProfile profile = default)
        => GearMath.DamageIndex(stats, profile);

    /// Describes the set for the solver: every slot's base stats, caps and socket count.
    private static List<SolverSlot> DescribeSlots(IReadOnlyDictionary<string, uint> selection)
    {
        var slots = new List<SolverSlot>();

        foreach (var (label, itemId) in selection)
        {
            var piece = GearCatalog.ById(itemId);
            if (piece is null)
                continue;

            var baseStats = new Dictionary<SubStat, int>();
            var caps = new Dictionary<SubStat, int>();

            foreach (var stat in Enum.GetValues<SubStat>())
            {
                baseStats[stat] = piece.StatFor(stat);
                caps[stat] = piece.Caps.TryGetValue(stat, out var cap) ? cap : 0;
            }

            var sockets = piece.IsCustomisableRelic ? 0 : piece.MateriaSlots;

            slots.Add(new SolverSlot(label, sockets, baseStats, caps));
        }

        return slots;
    }

    /// Fills every materia slot in the set with whatever maximises damage.
    private const double GcdTolerance = 0.0005;

    /// Added to a candidate that reaches the target global cooldown.
    private const double TierReward = 1000.0;

    /// targetGcd: A global cooldown this job builds around, or null to rank purely on damage.
    public static Dictionary<string, List<int>> Optimise(
        IReadOnlyDictionary<string, uint> selection,
        RelicAllocation? relic,
        Func<StatPreset, PlayerStats> toStats,
        IReadOnlyDictionary<SubStat, int>? inferredRelicStats = null,
        double? targetGcd = null,
        AutoCritProfile profile = default)
    {
        var catalogue = MateriaCatalog.All();
        if (catalogue.Count == 0)
            return [];

        var slots = DescribeSlots(selection);
        var options = catalogue.Select(o => new SolverMateria(o.Encoded, o.Stat, o.Value)).ToList();

        const int jobModifier = 110;
        var level = LevelStats.Lv100;

        var fixedPreset = GearCatalog.ToStatPreset(selection, jobModifier, "fixed", null, relic, inferredRelicStats);

        int RelicAndBase(SubStat stat)
        {
            var fromRelic = inferredRelicStats is not null && inferredRelicStats.TryGetValue(stat, out var inferred)
                ? inferred
                : relic?.ValueFor(stat) ?? 0;

            return GearMath.BaseFor(stat, level) + fromRelic;
        }

        double Evaluate(IReadOnlyDictionary<SubStat, int> gearTotals)
        {
            var preset = new StatPreset
            {
                Name = "candidate",
                WeaponDamage = fixedPreset.WeaponDamage,
                WeaponDelay = fixedPreset.WeaponDelay,
                MainStat = fixedPreset.MainStat,
                Crit = RelicAndBase(SubStat.Crit) + gearTotals.GetValueOrDefault(SubStat.Crit),
                Determination = RelicAndBase(SubStat.Determination) + gearTotals.GetValueOrDefault(SubStat.Determination),
                DirectHit = RelicAndBase(SubStat.DirectHit) + gearTotals.GetValueOrDefault(SubStat.DirectHit),
                SkillSpeed = RelicAndBase(SubStat.Speed) + gearTotals.GetValueOrDefault(SubStat.Speed),
                Tenacity = RelicAndBase(SubStat.Tenacity) + gearTotals.GetValueOrDefault(SubStat.Tenacity),
            };

            var stats = toStats(preset);
            var damage = GearMath.DamageIndex(stats, profile);

            if (targetGcd is not { } target)
                return damage;

            var reached = stats.Gcd <= target + GcdTolerance;
            return damage + (reached ? TierReward : 0);
        }

        return MeldSolver.Solve(slots, options, Evaluate);
    }

    /// Picks the relic's substats the same way: the two majors and the minor that gain the most, tried as a
    /// set because the three interact through the same tier boundaries.
    public static RelicAllocation OptimiseRelic(
        IReadOnlyDictionary<string, uint> selection,
        IReadOnlyDictionary<string, List<int>> melds,
        Func<StatPreset, PlayerStats> toStats,
        CombatRole role,
        AutoCritProfile profile = default)
    {
        var candidates = role.RelicSubstats().ToArray();

        var best = new RelicAllocation();
        var bestScore = double.NegativeInfinity;

        foreach (var majorA in candidates)
        {
            foreach (var majorB in candidates)
            {
                foreach (var minor in candidates)
                {
                    var allocation = new RelicAllocation
                    {
                        MajorA = (int)majorA,
                        MajorB = (int)majorB,
                        Minor = (int)minor,
                    };

                    if (!allocation.IsDistinct)
                        continue;

                    var preset = GearCatalog.ToStatPreset(selection, 110, "candidate", melds, allocation);
                    var score = DamageIndex(toStats(preset), profile);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = allocation;
                    }
                }
            }
        }

        return best;
    }
}
