using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Dragoon;

/// Dragoon's gauge on top of the generic sim state.
public sealed class DragoonState : SimState
{
    /// Firstminds' Focus, spent two at a time on Wyrmwind Thrust.
    public int Focus { get; set; }

    /// Which combo line the next starter should feed.
    public bool NextLineIsBuff { get; set; } = true;

    public void GainFocus()
    {
        if (Focus >= DragoonData.MaxFocus)
        {
            Count("focus.overcapped", 1);
            return;
        }

        Focus++;
    }

    public override SimState Clone()
    {
        var copy = new DragoonState { Focus = Focus, NextLineIsBuff = NextLineIsBuff };
        CopyInto(copy);
        return copy;
    }
}

/// Dragoon's rules: what's legal, what it's worth, and what it does to the gauge.
public sealed class DragoonSim : IJobSim
{
    public string JobName => "Dragoon";

    public MainAttribute MainAttribute => MainAttribute.Strength;

    public CombatRole Role => CombatRole.Melee;

    public int MainStatModifier { get; init; } = 115;

    /// None.
    public int HastePercent => 0;

    public int AutoAttackPotency => 90;

    public IReadOnlyDictionary<string, ActionDef> Actions => DragoonData.Actions;

    public SimState CreateState()
    {
        var state = new DragoonState();

        foreach (var (name, action) in DragoonData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var drg = (DragoonState)state;

        return action.Name switch
        {
            Drg.RaidenThrust => drg.HasStatus(Drg.DraconianFire),
            Drg.TrueThrust => !drg.HasStatus(Drg.DraconianFire),

            Drg.MirageDive => drg.HasStatus(Drg.DiveReady),
            Drg.Nastrond => drg.HasStatus(Drg.NastrondReady),
            Drg.Starcross => drg.HasStatus(Drg.StarcrossReady),
            Drg.RiseOfTheDragon => drg.HasStatus(Drg.DragonsFlight),
            Drg.Stardiver => drg.HasStatus(Drg.LifeOfTheDragon),
            Drg.WyrmwindThrust => drg.Focus >= DragoonData.FocusCost,

            _ when action.ComboFrom is not null => drg.IsComboReady(action),

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var drg = (DragoonState)state;
        return action.ComboFrom is not null && drg.IsComboReady(action) ? action.ComboPotency : action.Potency;
    }

    /// Life Surge guarantees a critical hit on the next weaponskill, which is why it is spent on Drakesbane
    /// rather than on whatever happens to be next.
    public bool IsAutoCrit(ActionDef action, SimState state)
        => action.IsGcd && state.HasStatus(Drg.LifeSurge);

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var drg = (DragoonState)state;

        if (action.IsGcd && drg.HasStatus(Drg.LifeSurge))
            drg.RemoveStatus(Drg.LifeSurge);

        switch (action.Name)
        {
            case Drg.LifeSurgeAction:
                drg.ApplyStatus(Drg.LifeSurge, 5.0);
                return;

            case Drg.LanceChargeAction:
                drg.ApplyStatus(Drg.LanceCharge, DragoonData.LanceChargeDuration, damageMulti: DragoonData.LanceChargeMulti);
                return;

            case Drg.BattleLitanyAction:
                drg.ApplyStatus(Drg.BattleLitany, DragoonData.BattleLitanyDuration,
                    critBonus: DragoonData.BattleLitanyCritBonus);
                return;

            case Drg.Geirskogul:
                drg.ApplyStatus(Drg.LifeOfTheDragon, DragoonData.LifeOfTheDragonDuration,
                    damageMulti: DragoonData.LifeOfTheDragonMulti);
                drg.ApplyStatus(Drg.NastrondReady, DragoonData.LifeOfTheDragonDuration);
                return;

            case Drg.Nastrond:
                drg.RemoveStatus(Drg.NastrondReady);
                return;

            case Drg.Stardiver:
                drg.ApplyStatus(Drg.StarcrossReady, 20.0);
                return;

            case Drg.Starcross:
                drg.RemoveStatus(Drg.StarcrossReady);
                return;

            case Drg.HighJump:
                drg.ApplyStatus(Drg.DiveReady, 15.0);
                return;

            case Drg.MirageDive:
                drg.RemoveStatus(Drg.DiveReady);
                return;

            case Drg.DragonfireDive:
                drg.ApplyStatus(Drg.DragonsFlight, 30.0);
                return;

            case Drg.RiseOfTheDragon:
                drg.RemoveStatus(Drg.DragonsFlight);
                return;

            case Drg.WyrmwindThrust:
                drg.Focus -= DragoonData.FocusCost;
                return;

            case Drg.RaidenThrust:
                drg.RemoveStatus(Drg.DraconianFire);
                drg.GainFocus();
                drg.AdvanceCombo(DragoonData.Get(Drg.RaidenThrust));
                return;

            case Drg.TrueThrust:
                drg.AdvanceCombo(DragoonData.Get(Drg.RaidenThrust));
                return;

            case Drg.SpiralBlow:
            case Drg.SonicThrust:
                drg.ApplyStatus(Drg.PowerSurge, DragoonData.PowerSurgeDuration, damageMulti: DragoonData.PowerSurgeMulti);
                drg.AdvanceCombo(action);
                return;

            case Drg.DoomSpike:
                drg.AdvanceCombo(DragoonData.Get(Drg.DraconianFury));
                return;

            case Drg.DraconianFury:
                drg.RemoveStatus(Drg.DraconianFire);
                drg.GainFocus();
                drg.AdvanceCombo(DragoonData.Get(Drg.DraconianFury));
                return;

            case Drg.CoerthanTorment:
                drg.ApplyStatus(Drg.DraconianFire, DragoonData.DraconianFireDuration);
                drg.AdvanceCombo(action);
                return;

            case Drg.ChaoticSpring:
                recordHit(Drg.ChaoticSpringDot, DragoonData.ChaoticSpringDotPotency);
                drg.AdvanceCombo(action);
                return;

            case Drg.Drakesbane:
                drg.ApplyStatus(Drg.DraconianFire, DragoonData.DraconianFireDuration);
                drg.NextLineIsBuff = !drg.NextLineIsBuff;
                drg.BreakCombo();
                return;
        }

        if (action.IsGcd && action.ComboFrom is not null)
            drg.AdvanceCombo(action);
    }
}
