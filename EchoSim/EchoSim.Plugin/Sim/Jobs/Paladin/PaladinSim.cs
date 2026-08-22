using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Paladin;

/// Paladin's state, which is entirely statuses.
public sealed class PaladinState : SimState
{
    /// The Confiteor chain's own combo channel, kept separate from the physical one.
    public string? LastBladeAction { get; set; }

    public double BladeChainExpiresAt { get; set; }

    public bool IsBladeReady(ActionDef action)
        => action.ComboFrom != null && LastBladeAction == action.ComboFrom
                                    && BladeChainExpiresAt > Time + 1e-9;

    public void AdvanceBladeChain(ActionDef action)
    {
        LastBladeAction = action.Name;
        BladeChainExpiresAt = Time + 30.0;
    }

    public void BreakBladeChain()
    {
        LastBladeAction = null;
        BladeChainExpiresAt = 0;
    }

    public override SimState Clone()
    {
        var copy = new PaladinState();
        CopyInto(copy);
        copy.LastBladeAction = LastBladeAction;
        copy.BladeChainExpiresAt = BladeChainExpiresAt;
        return copy;
    }
}

/// Paladin's rules: what is legal, what it is worth, and what a press grants.
public sealed class PaladinSim : IJobSim
{
    public string JobName => "Paladin";

    public MainAttribute MainAttribute => MainAttribute.Strength;

    public CombatRole Role => CombatRole.Tank;

    /// 100, off the ClassJob sheet's ModifierStrength.
    public int MainStatModifier { get; init; } = 100;

    /// None.
    public int HastePercent => 0;

    /// 90, the same as every melee - confirmed off the reference rather than assumed.
    public int AutoAttackPotency => 90;

    /// One.
    public double TraitMultiplier => 1.0;

    public IReadOnlyDictionary<string, ActionDef> Actions => PaladinData.Actions;

    public SimState CreateState()
    {
        var state = new PaladinState();

        foreach (var (name, action) in PaladinData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var pld = (PaladinState)state;

        return action.Name switch
        {
            Pld.Atonement => pld.HasStatus(Pld.AtonementReady),
            Pld.Supplication => pld.HasStatus(Pld.SupplicationReady),
            Pld.Sepulchre => pld.HasStatus(Pld.SepulchreReady),

            Pld.GoringBlade => pld.HasStatus(Pld.GoringBladeReady),
            Pld.BladeOfHonor => pld.HasStatus(Pld.BladeOfHonorReady),

            Pld.HolySpirit => pld.HasStatus(Pld.DivineMight),

            Pld.Confiteor => pld.HasStatus(Pld.ConfiteorReady) && pld.StatusStacks(Pld.RequiescatStatus) > 0,

            Pld.BladeOfFaith or Pld.BladeOfTruth or Pld.BladeOfValor =>
                pld.IsBladeReady(action) && pld.StatusStacks(Pld.RequiescatStatus) > 0,

            _ when action.ComboFrom is not null => pld.IsComboReady(action),

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var pld = (PaladinState)state;

        if (action.Name == Pld.HolySpirit)
            return pld.HasStatus(Pld.DivineMight) ? PaladinData.HolySpiritDivineMight : action.Potency;

        if (action.Name == Pld.HolyCircle)
            return pld.HasStatus(Pld.DivineMight) ? PaladinData.HolyCircleDivineMight : action.Potency;

        if (action.Name == Pld.Confiteor)
            return pld.StatusStacks(Pld.RequiescatStatus) > 0 ? action.ComboPotency : action.Potency;

        if (action.Name is Pld.BladeOfFaith or Pld.BladeOfTruth or Pld.BladeOfValor)
            return pld.IsBladeReady(action) ? action.ComboPotency : action.Potency;

        return action.ComboFrom is not null && pld.IsComboReady(action) ? action.ComboPotency : action.Potency;
    }

    /// Whether this spell is instant right now.
    public double CastTimeOf(ActionDef action, SimState state)
    {
        var pld = (PaladinState)state;

        if (action.Name != Pld.HolySpirit)
            return action.CastTime;

        return pld.HasStatus(Pld.DivineMight) || pld.StatusStacks(Pld.RequiescatStatus) > 0 ? 0 : action.CastTime;
    }

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var pld = (PaladinState)state;

        if (action.IsGcd && action.Name is Pld.FastBlade or Pld.RiotBlade or Pld.RoyalAuthority
                                        or Pld.TotalEclipse or Pld.Prominence)
        {
            if (action.IsComboStarter || (action.ComboFrom is not null && pld.IsComboReady(action)))
                pld.AdvanceCombo(action);
            else
                pld.BreakCombo();
        }

        switch (action.Name)
        {
            case Pld.FightOrFlightAction:
                pld.ApplyStatus(Pld.FightOrFlight, PaladinData.FightOrFlightDuration,
                    damageMulti: PaladinData.FightOrFlightMulti);

                pld.ApplyStatus(Pld.GoringBladeReady, PaladinData.GrantDuration);
                return;

            case Pld.Imperator:
                pld.ApplyStatus(Pld.RequiescatStatus, PaladinData.RequiescatDuration,
                    stacks: PaladinData.RequiescatStacks);
                pld.ApplyStatus(Pld.ConfiteorReady, PaladinData.GrantDuration);
                return;

            case Pld.GoringBlade:
                pld.RemoveStatus(Pld.GoringBladeReady);
                return;

            case Pld.RoyalAuthority:
                pld.ApplyStatus(Pld.DivineMight, PaladinData.GrantDuration);
                pld.ApplyStatus(Pld.AtonementReady, PaladinData.GrantDuration);

                pld.BreakCombo();
                return;

            case Pld.Prominence:
                pld.ApplyStatus(Pld.DivineMight, PaladinData.GrantDuration);
                pld.BreakCombo();
                return;

            case Pld.HolySpirit:
            case Pld.HolyCircle:
                pld.RemoveStatus(Pld.DivineMight);
                return;

            case Pld.Atonement:
                pld.RemoveStatus(Pld.AtonementReady);
                pld.ApplyStatus(Pld.SupplicationReady, PaladinData.GrantDuration);
                return;

            case Pld.Supplication:
                pld.RemoveStatus(Pld.SupplicationReady);
                pld.ApplyStatus(Pld.SepulchreReady, PaladinData.GrantDuration);
                return;

            case Pld.Sepulchre:
                pld.RemoveStatus(Pld.SepulchreReady);
                return;

            case Pld.Confiteor:
                pld.ConsumeStack(Pld.RequiescatStatus);
                pld.RemoveStatus(Pld.ConfiteorReady);
                pld.AdvanceBladeChain(action);
                return;

            case Pld.BladeOfValor:
                pld.ConsumeStack(Pld.RequiescatStatus);
                pld.ApplyStatus(Pld.BladeOfHonorReady, PaladinData.GrantDuration);
                pld.BreakBladeChain();
                return;

            case Pld.BladeOfFaith:
            case Pld.BladeOfTruth:
                pld.ConsumeStack(Pld.RequiescatStatus);
                pld.AdvanceBladeChain(action);
                return;

            case Pld.BladeOfHonor:
                pld.RemoveStatus(Pld.BladeOfHonorReady);
                return;

            case Pld.CircleOfScorn:
                recordHit(Pld.CircleOfScornDot,
                    PaladinData.CircleOfScornTickPotency * PaladinData.CircleOfScornTicks
                        * action.TargetMultiplier(state.Targets));
                return;
        }
    }
}
