namespace EchoSim.Sim;

/// One gear slot, described without any game-data types.
public sealed record SolverSlot(
    string Label,
    int MateriaSlots,
    IReadOnlyDictionary<SubStat, int> BaseStats,
    IReadOnlyDictionary<SubStat, int> Caps)
{
    public int BaseStat(SubStat stat) => BaseStats.GetValueOrDefault(stat);

    public int Cap(SubStat stat) => Caps.GetValueOrDefault(stat);
}

/// A meldable materia, reduced to what the solver needs.
public readonly record struct SolverMateria(int Id, SubStat Stat, int Value);

/// Chooses the best materia for a set of slots.
public static class MeldSolver
{
    /// Assignment of materia ids per slot label.
    public static Dictionary<string, List<int>> Solve(
        IReadOnlyList<SolverSlot> slots,
        IReadOnlyList<SolverMateria> options,
        Func<IReadOnlyDictionary<SubStat, int>, double> evaluate)
    {
        if (options.Count == 0)
            return slots.ToDictionary(s => s.Label, s => new List<int>());

        var byId = options.ToDictionary(o => o.Id);

        double ScoreOf(Dictionary<string, List<int>> a) => evaluate(GearTotals(slots, a, byId));

        var seeds = new List<Dictionary<string, List<int>>> { GreedyFill(slots, options, byId, evaluate) };

        foreach (var stat in options.Select(o => o.Stat).Distinct())
            seeds.Add(UniformFill(slots, options, stat));

        Dictionary<string, List<int>>? best = null;
        var bestScore = double.NegativeInfinity;

        foreach (var seed in seeds)
        {
            Improve(slots, options, byId, evaluate, seed);
            var score = ScoreOf(seed);

            if (score > bestScore)
            {
                bestScore = score;
                best = seed;
            }
        }

        var assignment = best!;

        foreach (var label in assignment.Keys.ToList())
            assignment[label] = [.. assignment[label].Where(id => id != 0)];

        return assignment;
    }

    /// Every slot filled with the strongest materia of one stat, as a starting point.
    private static Dictionary<string, List<int>> UniformFill(
        IReadOnlyList<SolverSlot> slots,
        IReadOnlyList<SolverMateria> options,
        SubStat stat)
    {
        var best = options.Where(o => o.Stat == stat).OrderByDescending(o => o.Value).First();
        return slots.ToDictionary(s => s.Label, s => Enumerable.Repeat(best.Id, s.MateriaSlots).ToList());
    }

    /// Best-first fill: repeatedly commit the single most valuable placement.
    private static Dictionary<string, List<int>> GreedyFill(
        IReadOnlyList<SolverSlot> slots,
        IReadOnlyList<SolverMateria> options,
        IReadOnlyDictionary<int, SolverMateria> byId,
        Func<IReadOnlyDictionary<SubStat, int>, double> evaluate)
    {
        var assignment = slots.ToDictionary(s => s.Label, s => Enumerable.Repeat(0, s.MateriaSlots).ToList());

        double Score() => evaluate(GearTotals(slots, assignment, byId));

        var current = Score();

        var open = slots.SelectMany(s => Enumerable.Range(0, s.MateriaSlots).Select(i => (s.Label, Index: i))).ToList();

        while (open.Count > 0)
        {
            var bestGain = 0.0;
            var bestSlot = -1;
            var bestOption = 0;

            for (var s = 0; s < open.Count; s++)
            {
                var (label, index) = open[s];
                var original = assignment[label][index];

                foreach (var option in options)
                {
                    assignment[label][index] = option.Id;
                    var gain = Score() - current;
                    assignment[label][index] = original;

                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestSlot = s;
                        bestOption = option.Id;
                    }
                }
            }

            if (bestSlot < 0)
                break;

            var chosen = open[bestSlot];
            assignment[chosen.Label][chosen.Index] = bestOption;

            current = Score();
            open.RemoveAt(bestSlot);
        }

        return assignment;
    }

    /// Sweeps every slot against every alternative until a full pass changes nothing, taking the assignment
    /// to a local optimum.
    private static void Improve(
        IReadOnlyList<SolverSlot> slots,
        IReadOnlyList<SolverMateria> options,
        IReadOnlyDictionary<int, SolverMateria> byId,
        Func<IReadOnlyDictionary<SubStat, int>, double> evaluate,
        Dictionary<string, List<int>> assignment)
    {
        double Score() => evaluate(GearTotals(slots, assignment, byId));

        for (var pass = 0; pass < 12; pass++)
        {
            var improved = false;

            foreach (var slot in slots)
            {
                var list = assignment[slot.Label];
                for (var i = 0; i < list.Count; i++)
                {
                    var original = list[i];
                    var bestChoice = original;

                    var bestScore = Score();

                    foreach (var candidate in options.Select(o => o.Id).Append(0))
                    {
                        if (candidate == original)
                            continue;

                        list[i] = candidate;
                        var score = Score();
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestChoice = candidate;
                        }
                    }

                    list[i] = bestChoice;
                    if (bestChoice != original)
                        improved = true;
                }
            }

            if (!improved)
                return;
        }
    }

    /// Substats contributed by gear and melds, with each item's cap applied separately.
    public static Dictionary<SubStat, int> GearTotals(
        IReadOnlyList<SolverSlot> slots,
        IReadOnlyDictionary<string, List<int>> assignment,
        IReadOnlyDictionary<int, SolverMateria> byId)
    {
        var totals = new Dictionary<SubStat, int>();

        foreach (var stat in Enum.GetValues<SubStat>())
            totals[stat] = 0;

        foreach (var slot in slots)
        {
            var melded = new Dictionary<SubStat, int>();
            if (assignment.TryGetValue(slot.Label, out var ids))
            {
                foreach (var id in ids)
                {
                    if (id != 0 && byId.TryGetValue(id, out var materia))
                        melded[materia.Stat] = melded.GetValueOrDefault(materia.Stat) + materia.Value;
                }
            }

            foreach (var stat in Enum.GetValues<SubStat>())
            {
                totals[stat] += GearMath.EffectiveSubstat(
                    slot.BaseStat(stat), melded.GetValueOrDefault(stat), slot.Cap(stat));
            }
        }

        return totals;
    }
}
