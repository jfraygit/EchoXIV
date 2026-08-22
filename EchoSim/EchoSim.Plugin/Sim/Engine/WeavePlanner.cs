namespace EchoSim.Sim.Engine;

/// When an off-GCD ability is worth pressing between two weaponskills, for every job.
public static class WeavePlanner
{
    /// The most a weave may push the next weaponskill back before the ability is held instead.
    public static double ClipBudget(SimState state, PlayerStats stats)
        => (state.CurrentGcdLength > 0 ? state.CurrentGcdLength : stats.Gcd) / 3.0;

    /// Whether an ability with this animation lock is worth weaving right now.
    public static bool CanWeave(SimState state, PlayerStats stats, double animationLock = Simulator.StandardAnimationLock)
    {
        var earliest = System.Math.Max(state.Time, state.AnimationLockUntil);

        if (earliest >= state.NextGcdAt - 1e-9)
            return false;

        var clip = System.Math.Max(0, earliest + animationLock - state.NextGcdAt);
        return clip <= ClipBudget(state, stats) + 1e-9;
    }

    /// Whether an ability fits before the next GCD with no clip at all.
    public static bool FitsWithoutClipping(
        SimState state,
        double animationLock = Simulator.StandardAnimationLock)
    {
        var earliest = System.Math.Max(state.Time, state.AnimationLockUntil);
        return earliest < state.NextGcdAt - 1e-9
               && earliest + animationLock <= state.NextGcdAt + 1e-9;
    }
}
