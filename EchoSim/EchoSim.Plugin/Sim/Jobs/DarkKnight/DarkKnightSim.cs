using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.DarkKnight;

/// Dark Knight's gauge on top of the generic sim state: Blood, and MP.
public sealed class DarkKnightState : SimState
{
    /// The Blood gauge, 0-100.
    public int Blood { get; set; }

    /// The Delirium chain's own combo channel, kept separate from the physical one.
    public string? LastDeliriumAction { get; set; }

    public double DeliriumChainExpiresAt { get; set; }

    public bool IsDeliriumReady(ActionDef action)
        => action.ComboFrom != null && LastDeliriumAction == action.ComboFrom
                                    && DeliriumChainExpiresAt > Time + 1e-9;

    public void AdvanceDeliriumChain(ActionDef action)
    {
        LastDeliriumAction = action.Name;
        DeliriumChainExpiresAt = Time + 30.0;
    }

    public void BreakDeliriumChain()
    {
        LastDeliriumAction = null;
        DeliriumChainExpiresAt = 0;
    }

    private double mp = DarkKnightData.MaxMp;
    private double mpSettledAt;

    /// MP at a given moment, with passive regeneration since the last spend folded in.
    public double MpAt(double time)
        => Math.Min(DarkKnightData.MaxMp, mp + (Math.Max(0, time - mpSettledAt) * DarkKnightData.MpPerSecond));

    /// Settles accrual up to now and takes the cost off.
    public void SpendMp(double amount, double time)
    {
        var available = MpAt(time);

        var uncapped = mp + (Math.Max(0, time - mpSettledAt) * DarkKnightData.MpPerSecond);
        if (uncapped > DarkKnightData.MaxMp)
            Count("mp.overcapped", uncapped - DarkKnightData.MaxMp);

        mp = available - amount;
        mpSettledAt = time;
    }

    /// A discrete grant - Syphon Strike, Carve and Spit, Blood Weapon.
    public void GainMp(int amount, double time) => SpendMp(-amount, time);

    public void GainBlood(int amount)
    {
        var wasted = Math.Max(0, Blood + amount - DarkKnightData.MaxBlood);
        if (wasted > 0)
            Count("blood.overcapped", wasted);

        Blood = Math.Min(DarkKnightData.MaxBlood, Blood + amount);
    }

    public override SimState Clone()
    {
        var copy = new DarkKnightState { Blood = Blood, mp = mp, mpSettledAt = mpSettledAt };
        CopyInto(copy);
        copy.LastDeliriumAction = LastDeliriumAction;
        copy.DeliriumChainExpiresAt = DeliriumChainExpiresAt;
        return copy;
    }
}

/// Dark Knight's rules: what is legal, what it is worth, and what a press does to Blood and MP.
public sealed class DarkKnightSim : IJobSim
{
    public string JobName => "Dark Knight";

    public MainAttribute MainAttribute => MainAttribute.Strength;

    public CombatRole Role => CombatRole.Tank;

    /// 105, off the ClassJob sheet's ModifierStrength - the same as Warrior.
    public int MainStatModifier { get; init; } = 105;

    public int HastePercent => 0;

    public int AutoAttackPotency => 90;

    public IReadOnlyDictionary<string, ActionDef> Actions => DarkKnightData.Actions;

    public SimState CreateState()
    {
        var state = new DarkKnightState();

        foreach (var (name, action) in DarkKnightData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var drk = (DarkKnightState)state;

        return action.Name switch
        {
            Drk.EdgeOfShadow => drk.MpAt(drk.Time) >= DarkKnightData.EdgeOfShadowCost,

            Drk.Bloodspiller => !drk.HasStatus(Drk.DeliriumStatus)
                                && drk.Blood >= DarkKnightData.BloodspillerCost,

            Drk.Quietus => !drk.HasStatus(Drk.DeliriumStatus)
                           && drk.Blood >= DarkKnightData.BloodspillerCost,

            Drk.ScarletDelirium or Drk.Impalement => drk.HasStatus(Drk.DeliriumStatus),
            Drk.Disesteem => drk.HasStatus(Drk.Scorn),

            Drk.SaltAndDarkness => drk.HasStatus(Drk.SaltedEarthActive),

            Drk.Comeuppance or Drk.Torcleaver => drk.IsDeliriumReady(action),

            _ when action.ComboFrom is not null => drk.IsComboReady(action),

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var drk = (DarkKnightState)state;
        if (action.Name is Drk.Comeuppance or Drk.Torcleaver)
            return drk.IsDeliriumReady(action) ? action.ComboPotency : action.Potency;

        return action.ComboFrom is not null && drk.IsComboReady(action) ? action.ComboPotency : action.Potency;
    }

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var drk = (DarkKnightState)state;

        if (action.IsGcd && drk.HasStatus(Drk.BloodWeapon))
        {
            drk.ConsumeStack(Drk.BloodWeapon);
            drk.GainMp(DarkKnightData.BloodWeaponMp, drk.Time);
            drk.GainBlood(DarkKnightData.BloodWeaponBlood);
        }

        if (action.IsGcd && drk.Time > 10.0 && !drk.HasStatus(Drk.Darkside))
            drk.Count("darkside.lapsed", 1);

        if (action.IsGcd && action.Name is Drk.HardSlash or Drk.SyphonStrike or Drk.Souleater
                                        or Drk.Unleash or Drk.StalwartSoul)
        {
            if (action.IsComboStarter || (action.ComboFrom is not null && drk.IsComboReady(action)))
                drk.AdvanceCombo(action);
            else
                drk.BreakCombo();
        }

        switch (action.Name)
        {
            case Drk.SyphonStrike:
                drk.GainMp(DarkKnightData.SyphonStrikeMp, drk.Time);
                return;

            case Drk.Souleater:
                drk.GainBlood(DarkKnightData.SouleaterBlood);
                drk.BreakCombo();
                return;

            case Drk.StalwartSoul:
                drk.GainBlood(DarkKnightData.SouleaterBlood);
                drk.GainMp(DarkKnightData.StalwartSoulMp, drk.Time);
                drk.BreakCombo();
                return;

            case Drk.Quietus:
            case Drk.Bloodspiller:
                drk.Blood -= DarkKnightData.BloodspillerCost;
                return;

            case Drk.Impalement:
                drk.ConsumeStack(Drk.DeliriumStatus);
                return;

            case Drk.ScarletDelirium:
                drk.ConsumeStack(Drk.DeliriumStatus);
                drk.AdvanceDeliriumChain(action);
                return;

            case Drk.Comeuppance:
                drk.ConsumeStack(Drk.DeliriumStatus);
                drk.GainMp(DarkKnightData.DeliriumComboMp, drk.Time);
                drk.AdvanceDeliriumChain(action);
                return;

            case Drk.Torcleaver:
                drk.ConsumeStack(Drk.DeliriumStatus);
                drk.GainMp(DarkKnightData.DeliriumComboMp, drk.Time);
                drk.BreakDeliriumChain();
                return;

            case Drk.Disesteem:
                drk.RemoveStatus(Drk.Scorn);
                return;

            case Drk.FloodOfShadow:
            case Drk.EdgeOfShadow:
                drk.SpendMp(DarkKnightData.EdgeOfShadowCost, drk.Time);
                ExtendDarkside(drk);
                return;

            case Drk.DeliriumAction:
                drk.ApplyStatus(Drk.DeliriumStatus, DarkKnightData.DeliriumDuration,
                    stacks: DarkKnightData.DeliriumStacks);
                drk.ApplyStatus(Drk.BloodWeapon, DarkKnightData.DeliriumDuration,
                    stacks: DarkKnightData.DeliriumStacks);
                return;

            case Drk.CarveAndSpit:
            case Drk.AbyssalDrain:
                drk.GainMp(DarkKnightData.CarveAndSpitMp, drk.Time);
                return;

            case Drk.LivingShadow:
                drk.ApplyStatus(Drk.Scorn, 30.0);

                foreach (var (source, potency) in DarkKnightData.SimulacrumAttacks)
                    recordHit(source, potency);

                return;

            case Drk.SaltedEarth:
                drk.ApplyStatus(Drk.SaltedEarthActive, DarkKnightData.SaltedEarthDuration);
                recordHit(Drk.SaltedEarthDot,
                    DarkKnightData.SaltedEarthTickPotency * DarkKnightData.SaltedEarthTicks
                        * action.TargetMultiplier(state.Targets));
                return;
        }
    }

    /// Darkside extends to a sixty-second ceiling, exactly like Warrior's Surging Tempest.
    private static void ExtendDarkside(DarkKnightState drk)
    {
        var remaining = drk.StatusRemaining(Drk.Darkside);
        var total = Math.Min(remaining + DarkKnightData.DarksideDuration, DarkKnightData.DarksideMax);

        if (remaining + DarkKnightData.DarksideDuration > DarkKnightData.DarksideMax)
            drk.Count("darkside.overcapped", remaining + DarkKnightData.DarksideDuration - DarkKnightData.DarksideMax);

        drk.ApplyStatus(Drk.Darkside, total, damageMulti: DarkKnightData.DarksideMulti);
    }
}
