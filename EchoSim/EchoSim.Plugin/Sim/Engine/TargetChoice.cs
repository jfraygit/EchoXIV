namespace EchoSim.Sim.Engine;

/// Which of two actions is worth more at the target count currently being fought.
public static class TargetChoice
{
    /// The higher-potency of two actions at SimState.Targets, judged on what they are actually worth right
    /// now.
    public static ActionDef Best(IJobSim job, SimState state, ActionDef single, ActionDef area)
        => Value(job, state, area) > Value(job, state, single) ? area : single;

    /// What one press of this action is worth against the current target count.
    public static double Value(IJobSim job, SimState state, ActionDef action)
        => action.WithoutMissedPositional(job.EffectivePotency(action, state), state.Targets, state.PositionalsLostFrom)
           * action.TargetMultiplier(state.Targets);

    /// Potency per global of a repeating combo cycle, for the choice between two whole rotations rather than
    /// two actions.
    public static double CyclePotencyPerGcd(
        IReadOnlyDictionary<string, ActionDef> actions, int targets, params string[] cycle)
        => CyclePotencyPerGcd(actions, targets, ActionDef.DefaultPositionalsLostFrom, cycle);

    /// The same, with an explicit positional threshold - see SimState.PositionalsLostFrom.
    public static double CyclePotencyPerGcd(
        IReadOnlyDictionary<string, ActionDef> actions, int targets, int lostFrom, params string[] cycle)
    {
        if (cycle.Length == 0)
            return 0;

        var total = 0.0;
        string? previous = null;

        foreach (var name in cycle)
        {
            if (!actions.TryGetValue(name, out var action))
                continue;

            var combos = action.ComboFrom is not null && action.ComboFrom == previous;
            var potency = action.WithoutMissedPositional(
                combos ? action.ComboPotency : action.Potency, targets, lostFrom);

            total += potency * action.TargetMultiplier(targets);

            if (action.IsGcd && !action.PreservesCombo)
                previous = action.Name;
        }

        return total / cycle.Length;
    }
}
