using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Warrior;

/// Warrior's gauge on top of the generic sim state.
public sealed class WarriorState : SimState
{
    /// The Beast Gauge, 0-100.
    public int Beast { get; set; }

    public void GainBeast(int amount)
    {
        var wasted = Math.Max(0, Beast + amount - WarriorData.MaxBeastGauge);
        if (wasted > 0)
            Count("beast.overcapped", wasted);

        Beast = Math.Min(WarriorData.MaxBeastGauge, Beast + amount);
    }

    public override SimState Clone()
    {
        var copy = new WarriorState { Beast = Beast };
        CopyInto(copy);
        return copy;
    }
}

/// Warrior's rules: what is legal, what it is worth, and what a press does to the gauge.
public sealed class WarriorSim : IJobSim
{
    public string JobName => "Warrior";

    public MainAttribute MainAttribute => MainAttribute.Strength;

    public CombatRole Role => CombatRole.Tank;

    /// 105, off the ClassJob sheet's ModifierStrength.
    public int MainStatModifier { get; init; } = 105;

    public int HastePercent => 0;

    public int AutoAttackPotency => 90;

    public IReadOnlyDictionary<string, ActionDef> Actions => WarriorData.Actions;

    public SimState CreateState()
    {
        var state = new WarriorState();

        foreach (var (name, action) in WarriorData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var war = (WarriorState)state;

        return action.Name switch
        {
            War.InnerChaos => war.HasStatus(War.NascentChaos) && war.Beast >= WarriorData.FellCleaveCost,

            War.FellCleave => !(war.HasStatus(War.NascentChaos) && war.Beast >= WarriorData.FellCleaveCost)
                              && (war.HasStatus(War.InnerReleaseStatus) || war.Beast >= WarriorData.FellCleaveCost),

            War.PrimalRend => war.HasStatus(War.PrimalRendReady),
            War.PrimalRuination => war.HasStatus(War.PrimalRuinationReady),
            War.PrimalWrath => war.StatusStacks(War.BurgeoningFury) >= WarriorData.WrathfulStacks,

            _ when action.ComboFrom is not null => war.IsComboReady(action),

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var war = (WarriorState)state;
        return action.ComboFrom is not null && war.IsComboReady(action) ? action.ComboPotency : action.Potency;
    }

    /// The guaranteed critical hits, which are a large share of this job's damage.
    public bool IsAutoCrit(ActionDef action, SimState state)
        => action.Name switch
        {
            War.InnerChaos or War.PrimalRend or War.PrimalRuination => true,

            War.ChaoticCyclone => true,
            War.Decimate => state.HasStatus(War.InnerReleaseStatus),
            War.FellCleave => state.HasStatus(War.InnerReleaseStatus),
            _ => false,
        };

    /// The same four.
    public bool IsAutoDirectHit(ActionDef action, SimState state) => IsAutoCrit(action, state);

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var war = (WarriorState)state;

        if (action.IsGcd && war.Time > 10.0 && !war.HasStatus(War.SurgingTempest))
            war.Count("tempest.lapsed", 1);

        var inCombo = action.ComboFrom is not null && war.IsComboReady(action);

        if (action.IsGcd)
        {
            if (action.IsComboStarter || (action.ComboFrom is not null && war.IsComboReady(action)))
                war.AdvanceCombo(action);
            else
                war.BreakCombo();
        }

        switch (action.Name)
        {
            case War.MythrilTempest:
                if (inCombo)
                {
                    war.GainBeast(WarriorData.MythrilTempestGauge);
                    ExtendSurgingTempest(war, WarriorData.SurgingTempestDuration);
                }

                war.BreakCombo();
                return;

            case War.Maim:
                if (inCombo)
                    war.GainBeast(WarriorData.MaimGauge);

                return;

            case War.StormsPath:
                if (inCombo)
                    war.GainBeast(WarriorData.StormsPathGauge);

                war.BreakCombo();
                return;

            case War.StormsEye:
                if (inCombo)
                {
                    war.GainBeast(WarriorData.StormsEyeGauge);
                    ExtendSurgingTempest(war, WarriorData.SurgingTempestDuration);
                }

                war.BreakCombo();
                return;

            case War.Decimate:
            case War.FellCleave:
                if (war.HasStatus(War.InnerReleaseStatus))
                {
                    war.ConsumeStack(War.InnerReleaseStatus);
                    war.AddStack(War.BurgeoningFury, 30.0, WarriorData.WrathfulStacks);

                    war.Count("fellcleave.free", 1);
                }
                else
                {
                    war.Beast -= WarriorData.FellCleaveCost;
                }

                ReduceInfuriate(war);
                return;

            case War.ChaoticCyclone:
            case War.InnerChaos:
                war.Beast -= WarriorData.FellCleaveCost;
                war.RemoveStatus(War.NascentChaos);
                ReduceInfuriate(war);
                return;

            case War.InfuriateAction:
                war.GainBeast(WarriorData.InfuriateGauge);
                war.ApplyStatus(War.NascentChaos, 30.0);
                return;

            case War.InnerReleaseAction:
                war.ApplyStatus(War.InnerReleaseStatus, WarriorData.InnerReleaseDuration,
                    stacks: WarriorData.InnerReleaseStacks);
                war.ApplyStatus(War.PrimalRendReady, 30.0);
                ExtendSurgingTempest(war, WarriorData.InnerReleaseTempestExtension, refreshOnly: true);
                return;

            case War.PrimalRend:
                war.RemoveStatus(War.PrimalRendReady);
                war.ApplyStatus(War.PrimalRuinationReady, 20.0);
                return;

            case War.PrimalRuination:
                war.RemoveStatus(War.PrimalRuinationReady);
                return;

            case War.PrimalWrath:
                war.RemoveStatus(War.BurgeoningFury);
                return;
        }
    }

    /// Five seconds off Infuriate, from Fell Cleave and Inner Chaos alike.
    private static void ReduceInfuriate(WarriorState war)
        => war.Cooldown(War.InfuriateAction).Reduce(WarriorData.InfuriateReduction, war.Time);

    /// Surging Tempest extends rather than replaces, and stops at sixty seconds.
    private static void ExtendSurgingTempest(WarriorState war, double seconds, bool refreshOnly = false)
    {
        var remaining = war.StatusRemaining(War.SurgingTempest);

        if (refreshOnly && remaining <= 0)
            return;

        var total = Math.Min(remaining + seconds, WarriorData.SurgingTempestMax);

        if (remaining + seconds > WarriorData.SurgingTempestMax)
            war.Count("tempest.overcapped", remaining + seconds - WarriorData.SurgingTempestMax);

        war.ApplyStatus(War.SurgingTempest, total, damageMulti: WarriorData.SurgingTempestMulti);
    }
}
