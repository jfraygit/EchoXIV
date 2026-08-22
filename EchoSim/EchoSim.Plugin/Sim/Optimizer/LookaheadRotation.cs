using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Optimizer;

/// Searches for a better rotation instead of following a fixed one.
public sealed class LookaheadRotation(
    Func<IRotation> rolloutPolicyFactory,
    IReadOnlyList<ActionDef> candidatePool,
    double horizon = 25.0) : IRotation
{
    public string Name => $"Lookahead search ({horizon:F0}s horizon)";

    /// How far past the next GCD a candidate may sit before it's dropped.
    private const double MaxIdleTolerance = 1.0;

    public void Reset(SimState state, PlayerStats stats)
        => rolloutPolicyFactory().Reset(state, stats);

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var candidates = Candidates(state, job);
        if (candidates.Count == 0)
            return null;

        if (candidates.Count == 1)
            return candidates[0];

        ActionDef? best = null;
        var bestScore = double.NegativeInfinity;

        foreach (var candidate in candidates)
        {
            var branch = state.Clone();
            var policy = new ForcedOpening(candidate, rolloutPolicyFactory());
            var score = Simulator.Rollout(job, policy, stats, branch, horizon);

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// Legal actions that could reasonably be pressed now rather than far in the future.
    private List<ActionDef> Candidates(SimState state, IJobSim job)
    {
        var results = new List<ActionDef>();
        var reference = System.Math.Max(state.Time, state.NextGcdAt);

        foreach (var action in candidatePool)
        {
            if (!job.CanUse(action, state))
                continue;

            if (Simulator.EarliestUsable(action, state) - reference > MaxIdleTolerance)
                continue;

            results.Add(action);
        }

        return results;
    }

    /// Plays one forced action, then hands over to the rollout policy.
    private sealed class ForcedOpening(ActionDef first, IRotation continuation) : IRotation
    {
        private bool played;

        public string Name => "rollout";

        public void Reset(SimState state, PlayerStats stats) => continuation.Reset(state, stats);

        public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
        {
            if (played)
                return continuation.NextAction(state, job, stats);

            played = true;
            return first;
        }
    }
}
