using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Machinist;

/// Machinist's two gauges and Wildfire's pending payout, on top of the generic sim state.
public sealed class MachinistState : SimState
{
    /// Heat, spent 50 at a time on Hypercharge.
    public int Heat { get; set; }

    /// Battery, spent on the Queen.
    public int Battery { get; set; }

    /// Weaponskills landed inside the current Wildfire, capped at six.
    public int WildfireStacks { get; set; }

    /// When the running Wildfire pays out.
    public double WildfireEndsAt { get; set; }

    public void GainHeat(int amount)
    {
        var room = MachinistData.MaxHeat - Heat;
        if (amount > room)
            Count("heat.overcapped", amount - room);

        Heat = System.Math.Min(MachinistData.MaxHeat, Heat + amount);
    }

    public void GainBattery(int amount)
    {
        var room = MachinistData.MaxBattery - Battery;
        if (amount > room)
            Count("battery.overcapped", amount - room);

        Battery = System.Math.Min(MachinistData.MaxBattery, Battery + amount);
    }

    public override SimState Clone()
    {
        var copy = new MachinistState
        {
            Heat = Heat,
            Battery = Battery,
            WildfireStacks = WildfireStacks,
            WildfireEndsAt = WildfireEndsAt,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Machinist's rules: what's legal, what it's worth, and what it does to the two gauges.
public sealed class MachinistSim : IJobSim
{
    public string JobName => "Machinist";

    public MainAttribute MainAttribute => MainAttribute.Dexterity;

    public CombatRole Role => CombatRole.PhysicalRanged;

    public int MainStatModifier { get; init; } = 115;

    /// None.
    public int HastePercent => 0;

    /// 80, not the melee 90.
    public int AutoAttackPotency => 80;

    /// Increased Action Damage II.
    public double TraitMultiplier => 1.20;

    public IReadOnlyDictionary<string, ActionDef> Actions => MachinistData.Actions;

    public SimState CreateState()
    {
        var state = new MachinistState();

        foreach (var (name, action) in MachinistData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var mch = (MachinistState)state;

        return action.Name switch
        {
            Mch.BlazingShot => mch.HasStatus(Mch.Overheated),
            Mch.Excavator => mch.HasStatus(Mch.ExcavatorReady),
            Mch.FullMetalField => mch.HasStatus(Mch.FullMetalMachinist),

            Mch.HyperchargeAction => mch.Heat >= MachinistData.HyperchargeHeatCost
                                     || mch.HasStatus(Mch.Hypercharged),

            Mch.AutomatonQueen => mch.Battery >= MachinistData.QueenMinimumBattery,

            _ when action.ComboFrom is not null => mch.IsComboReady(action),

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var mch = (MachinistState)state;

        var potency = action.ComboFrom is not null && mch.IsComboReady(action)
            ? action.ComboPotency
            : action.Potency;

        if (mch.HasStatus(Mch.Overheated) && MachinistData.SingleTargetWeaponskills.Contains(action.Name))
            potency += MachinistData.OverheatedPotencyBonus;

        return potency;
    }

    /// Reassemble and Full Metal Field are guaranteed critical DIRECT hits - both rolls, not just the
    /// critical one.
    public bool IsAutoCrit(ActionDef action, SimState state)
        => action.Name == Mch.FullMetalField
           || (action.IsGcd && state.HasStatus(Mch.Reassembled));

    public bool IsAutoDirectHit(ActionDef action, SimState state)
        => IsAutoCrit(action, state);

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var mch = (MachinistState)state;

        FlushWildfire(mch, recordHit);

        if (action.IsGcd && action.Name != Mch.FullMetalField && mch.HasStatus(Mch.Reassembled))
            mch.RemoveStatus(Mch.Reassembled);

        if (action.IsGcd && mch.HasStatus(Mch.WildfireStatus))
        {
            if (mch.WildfireStacks < MachinistData.WildfireMaxStacks)
                mch.WildfireStacks++;
            else
                mch.Count("wildfire.wasted", 1);
        }

        switch (action.Name)
        {
            case Mch.Scattergun:
            case Mch.HeatedSplitShot:
                mch.GainHeat(MachinistData.HeatPerComboStep);
                mch.AdvanceCombo(action);
                return;

            case Mch.HeatedSlugShot:
                if (mch.LastComboAction == Mch.HeatedSplitShot)
                    mch.GainHeat(MachinistData.HeatPerComboStep);

                mch.AdvanceCombo(action);
                return;

            case Mch.HeatedCleanShot:
                if (mch.LastComboAction == Mch.HeatedSlugShot)
                {
                    mch.GainHeat(MachinistData.HeatPerComboStep);
                    mch.GainBattery(MachinistData.BatteryPerCombo);
                }

                mch.BreakCombo();
                return;

            case Mch.AirAnchor:
                mch.GainBattery(MachinistData.BatteryPerTool);
                return;

            case Mch.ChainSaw:
                mch.GainBattery(MachinistData.BatteryPerTool);
                mch.ApplyStatus(Mch.ExcavatorReady, MachinistData.ExcavatorReadyDuration);
                return;

            case Mch.Excavator:
                mch.GainBattery(MachinistData.BatteryPerTool);
                mch.RemoveStatus(Mch.ExcavatorReady);
                return;

            case Mch.FullMetalField:
                mch.RemoveStatus(Mch.FullMetalMachinist);
                return;

            case Mch.BlazingShot:
            case Mch.AutoCrossbow:
                mch.ConsumeStack(Mch.Overheated);
                mch.Cooldown(Mch.DoubleCheck).Reduce(MachinistData.BlazingShotRecastReduction, mch.Time);
                mch.Cooldown(Mch.Checkmate).Reduce(MachinistData.BlazingShotRecastReduction, mch.Time);
                return;

            case Mch.Bioblaster:
                recordHit(
                    Mch.BioblasterDot,
                    MachinistData.BioblasterDotPotency * action.TargetMultiplier(state.Targets));

                return;

            case Mch.HyperchargeAction:
                if (mch.HasStatus(Mch.Hypercharged))
                    mch.RemoveStatus(Mch.Hypercharged);
                else
                    mch.Heat -= MachinistData.HyperchargeHeatCost;

                mch.ApplyStatus(Mch.Overheated, MachinistData.OverheatedDuration,
                    stacks: MachinistData.OverheatedStacks);
                return;

            case Mch.BarrelStabilizer:
                mch.ApplyStatus(Mch.Hypercharged, MachinistData.BarrelStabilizerDuration);
                mch.ApplyStatus(Mch.FullMetalMachinist, MachinistData.BarrelStabilizerDuration);
                return;

            case Mch.ReassembleAction:
                mch.ApplyStatus(Mch.Reassembled, MachinistData.ReassembleDuration);
                return;

            case Mch.WildfireAction:
                mch.ApplyStatus(Mch.WildfireStatus, MachinistData.WildfireDuration);
                mch.WildfireStacks = 0;
                mch.WildfireEndsAt = mch.Time + MachinistData.WildfireDuration;
                return;

            case Mch.AutomatonQueen:
            {
                var spent = mch.Battery;
                mch.Battery = 0;
                mch.Count("queen.battery", spent);

                var listed = spent * MachinistData.QueenPotencyPerBattery;
                recordHit(Mch.QueenDamage, listed * MachinistData.PetPotencyMultiplier);
                return;
            }
        }
    }

    /// Pays out a Wildfire whose ten seconds have run out.
    private static void FlushWildfire(MachinistState mch, HitRecorder recordHit)
    {
        if (mch.WildfireEndsAt <= 0 || mch.Time + 1e-9 < mch.WildfireEndsAt)
            return;

        var stacks = mch.WildfireStacks;
        mch.WildfireEndsAt = 0;
        mch.WildfireStacks = 0;

        if (stacks <= 0)
            return;

        recordHit(Mch.WildfireDetonation, stacks * MachinistData.WildfirePerWeaponskill);
        mch.Count("wildfire.detonated", 1);
        mch.Count("wildfire.stacks", stacks);
    }
}
