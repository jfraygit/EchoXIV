using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Analysis;

/// How close a player's actual button-pressing is to the modelled ceiling, from 0 to 100.
public readonly record struct ExecutionScore(double Overall, double Rotation, double Uptime, double Coverage = 1)
{
    /// Nothing has happened yet - shown as a dash rather than as a zero.
    public static readonly ExecutionScore None = new(-1, -1, -1);

    public bool HasValue => Overall >= 0;

    /// The score as it should be shown: never above a hundred.
    public double Displayed => Math.Min(Overall, 100);

    /// Whether enough of what the player pressed is actually modelled for the score to mean anything.
    public bool IsRepresentative => Coverage >= 0.8;

    /// The two halves multiply out to the whole, which is what makes the split worth showing.
    public string Weakness => Rotation < Uptime ? "rotation" : "uptime";
}

public static class Execution
{
    /// The smallest sample that can produce a number at all: one priceable global.
    public const int MinimumGcds = 1;

    /// How long the reference run behind every ceiling is.
    public const double ReferenceDuration = 600;


    /// Total potency of a sequence of actions, counting a combo bonus only where the sequence earned it.
    public static double SequencePotency(IJobSim job, IEnumerable<TimelineCast> casts)
        => SequencePotency(job, casts.Select(c => (c.Action, c.IsGcd)));

    public static double SequencePotency(IJobSim job, IEnumerable<(string Action, bool IsGcd)> casts)
    {
        var total = 0.0;
        string? previousGcd = null;

        foreach (var (name, isGcd) in casts)
        {
            if (!job.Actions.TryGetValue(name, out var action))
            {
                if (isGcd)
                    previousGcd = null;

                continue;
            }

            var combos = action.ComboFrom is not null && action.ComboFrom == previousGcd;
            total += combos ? action.ComboPotency : action.Potency;

            if (action.IsGcd && !action.PreservesCombo)
                previousGcd = action.Name;
        }

        return total;
    }

    /// Where the modelled rotation has got to at each moment, rather than what it averages.
    public sealed class CeilingCurve
    {
        private readonly double[] times;
        private readonly double[] potency;
        private readonly double[] gcds;

        private CeilingCurve(double[] times, double[] potency, double[] gcds, double duration)
        {
            this.times = times;
            this.potency = potency;
            this.gcds = gcds;
            Duration = duration;
        }

        public double Duration { get; }

        public bool IsUsable => times.Length > 1 && Duration > 0;

        public static CeilingCurve From(IJobSim job, SimResult reference)
        {
            var times = new List<double>();
            var runningPotency = new List<double>();
            var runningGcds = new List<double>();

            var total = 0.0;
            var count = 0.0;
            string? previousGcd = null;

            foreach (var entry in reference.Timeline.OrderBy(t => t.Time))
            {
                var isGcd = entry.Kind is not ActionKind.OffGcd;

                if (job.Actions.TryGetValue(entry.Action, out var action))
                {
                    var combos = action.ComboFrom is not null && action.ComboFrom == previousGcd;
                    total += combos ? action.ComboPotency : action.Potency;

                    if (action.IsGcd && !action.PreservesCombo)
                        previousGcd = action.Name;
                }
                else if (isGcd)
                {
                    previousGcd = null;
                }

                if (isGcd)
                    count++;

                times.Add(entry.Time);
                runningPotency.Add(total);
                runningGcds.Add(count);
            }

            return new CeilingCurve([.. times], [.. runningPotency], [.. runningGcds], reference.Duration);
        }

        /// What the rotation had done by this point.
        public (double Potency, double Gcds) At(double elapsed)
        {
            if (!IsUsable || elapsed <= 0)
                return (0, 0);

            if (elapsed >= Duration)
            {
                var scale = elapsed / Duration;
                return (potency[^1] * scale, gcds[^1] * scale);
            }

            var index = Array.BinarySearch(times, elapsed);
            if (index < 0)
                index = ~index - 1;

            return index < 0 ? (0, 0) : (potency[index], gcds[index]);
        }
    }

    /// The player's score so far, given what they have pressed and how long they have been at it.
    public static ExecutionScore Score(
        IJobSim job,
        IReadOnlyList<TimelineCast> casts,
        double elapsed,
        CeilingCurve ceiling)
    {
        if (elapsed <= 0 || !ceiling.IsUsable || casts.Count == 0)
            return ExecutionScore.None;


        var expected = ceiling.At(elapsed);
        if (expected.Gcds <= 0 || expected.Potency <= 0)
            return ExecutionScore.None;

        var modelled = casts.Where(c => job.Actions.ContainsKey(c.Action)).ToList();

        var potency = SequencePotency(job, casts);
        var modelledGcds = modelled.Count(c => c.IsGcd);

        var allGcds = casts.Count(c => c.IsGcd);
        var coverage = allGcds > 0 ? (double)modelledGcds / allGcds : 1;

        if (modelledGcds < MinimumGcds || allGcds == 0)
            return ExecutionScore.None with { Coverage = coverage };

        var rotation = potency / modelledGcds / (expected.Potency / expected.Gcds);
        var uptime = allGcds / expected.Gcds;

        return new ExecutionScore(
            Math.Clamp(rotation * uptime * 100, 0, 999),
            Math.Clamp(rotation * 100, 0, 999),
            Math.Clamp(uptime * 100, 0, 999),
            coverage);
    }
}
