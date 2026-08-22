using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Gunbreaker;

/// Gunbreaker's gauge on top of the generic sim state.
public sealed class GunbreakerState : SimState
{
    /// The Powder Gauge.
    public int Cartridges { get; set; }

    /// The Gnashing Fang and Reign of Beasts chains, each on its own channel.
    public string? LastFangAction { get; set; }

    public double FangChainExpiresAt { get; set; }

    public string? LastReignAction { get; set; }

    public double ReignChainExpiresAt { get; set; }

    public bool IsFangReady(ActionDef action)
        => action.ComboFrom != null && LastFangAction == action.ComboFrom
                                    && FangChainExpiresAt > Time + 1e-9;

    public bool IsReignReady(ActionDef action)
        => action.ComboFrom != null && LastReignAction == action.ComboFrom
                                    && ReignChainExpiresAt > Time + 1e-9;

    public void AdvanceFangChain(ActionDef action)
    {
        LastFangAction = action.Name;
        FangChainExpiresAt = Time + 30.0;
    }

    public void AdvanceReignChain(ActionDef action)
    {
        LastReignAction = action.Name;
        ReignChainExpiresAt = Time + 30.0;
    }

    public void BreakFangChain()
    {
        LastFangAction = null;
        FangChainExpiresAt = 0;
    }

    public void BreakReignChain()
    {
        LastReignAction = null;
        ReignChainExpiresAt = 0;
    }

    /// The cap right now, which is not a constant - Bloodfest doubles it for thirty seconds.
    public int CartridgeCap => HasStatus(Gnb.BloodfestStatus)
        ? GunbreakerData.MaxCartridgesUnderBloodfest
        : GunbreakerData.MaxCartridges;

    public void GainCartridges(int amount)
    {
        var ceiling = Math.Max(CartridgeCap, Cartridges);

        var wasted = Math.Max(0, Cartridges + amount - ceiling);
        if (wasted > 0)
            Count("cartridge.overcapped", wasted);

        Cartridges = Math.Min(ceiling, Cartridges + amount);
    }

    public override SimState Clone()
    {
        var copy = new GunbreakerState { Cartridges = Cartridges };
        CopyInto(copy);
        copy.LastFangAction = LastFangAction;
        copy.FangChainExpiresAt = FangChainExpiresAt;
        copy.LastReignAction = LastReignAction;
        copy.ReignChainExpiresAt = ReignChainExpiresAt;
        return copy;
    }
}

/// Gunbreaker's rules: what is legal, what it is worth, and what a press does to the gauge.
public sealed class GunbreakerSim : IJobSim
{
    public string JobName => "Gunbreaker";

    public MainAttribute MainAttribute => MainAttribute.Strength;

    public CombatRole Role => CombatRole.Tank;

    /// 100, off the ClassJob sheet's ModifierStrength - the same as Paladin.
    public int MainStatModifier { get; init; } = 100;

    public int HastePercent => 0;

    public int AutoAttackPotency => 90;

    public IReadOnlyDictionary<string, ActionDef> Actions => GunbreakerData.Actions;

    public SimState CreateState()
    {
        var state = new GunbreakerState();

        foreach (var (name, action) in GunbreakerData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var gnb = (GunbreakerState)state;

        return action.Name switch
        {
            Gnb.BurstStrike => gnb.Cartridges >= GunbreakerData.BurstStrikeCost,
            Gnb.GnashingFang => gnb.Cartridges >= GunbreakerData.GnashingFangCost,
            Gnb.DoubleDown => gnb.Cartridges >= GunbreakerData.DoubleDownCost,

            Gnb.JugularRip => gnb.HasStatus(Gnb.ReadyToRip),
            Gnb.AbdomenTear => gnb.HasStatus(Gnb.ReadyToTear),
            Gnb.EyeGouge => gnb.HasStatus(Gnb.ReadyToGouge),
            Gnb.Hypervelocity => gnb.HasStatus(Gnb.ReadyToBlast),
            Gnb.FatedBrand => gnb.HasStatus(Gnb.ReadyToRaze),

            Gnb.SonicBreak => gnb.HasStatus(Gnb.ReadyToBreak),
            Gnb.ReignOfBeasts => gnb.HasStatus(Gnb.ReadyToReign),

            Gnb.SavageClaw or Gnb.WickedTalon => gnb.IsFangReady(action),
            Gnb.NobleBlood or Gnb.LionHeart => gnb.IsReignReady(action),

            _ when action.ComboFrom is not null => gnb.IsComboReady(action),

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var gnb = (GunbreakerState)state;

        if (action.Name is Gnb.SavageClaw or Gnb.WickedTalon)
            return gnb.IsFangReady(action) ? action.ComboPotency : action.Potency;

        if (action.Name is Gnb.NobleBlood or Gnb.LionHeart)
            return gnb.IsReignReady(action) ? action.ComboPotency : action.Potency;

        return action.ComboFrom is not null && gnb.IsComboReady(action) ? action.ComboPotency : action.Potency;
    }

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var gnb = (GunbreakerState)state;

        if (action.IsGcd && action.Name is Gnb.KeenEdge or Gnb.BrutalShell or Gnb.SolidBarrel
                                        or Gnb.DemonSlice or Gnb.DemonSlaughter)
        {
            if (action.IsComboStarter || (action.ComboFrom is not null && gnb.IsComboReady(action)))
                gnb.AdvanceCombo(action);
            else
                gnb.BreakCombo();
        }

        switch (action.Name)
        {
            case Gnb.DemonSlaughter:
            case Gnb.SolidBarrel:
                gnb.GainCartridges(1);
                gnb.BreakCombo();
                return;

            case Gnb.BurstStrike:
                gnb.Cartridges -= GunbreakerData.BurstStrikeCost;
                gnb.ApplyStatus(Gnb.ReadyToBlast, GunbreakerData.ContinuationWindow);
                return;

            case Gnb.FatedCircle:
                gnb.Cartridges -= GunbreakerData.BurstStrikeCost;
                gnb.ApplyStatus(Gnb.ReadyToRaze, GunbreakerData.ContinuationWindow);
                return;

            case Gnb.GnashingFang:
                gnb.Cartridges -= GunbreakerData.GnashingFangCost;
                gnb.ApplyStatus(Gnb.ReadyToRip, GunbreakerData.ContinuationWindow);

                gnb.AdvanceFangChain(action);
                return;

            case Gnb.SavageClaw:
                gnb.ApplyStatus(Gnb.ReadyToTear, GunbreakerData.ContinuationWindow);
                gnb.AdvanceFangChain(action);
                return;

            case Gnb.WickedTalon:
                gnb.ApplyStatus(Gnb.ReadyToGouge, GunbreakerData.ContinuationWindow);
                gnb.BreakFangChain();
                return;

            case Gnb.DoubleDown:
                gnb.Cartridges -= GunbreakerData.DoubleDownCost;
                return;

            case Gnb.JugularRip:
                gnb.RemoveStatus(Gnb.ReadyToRip);
                return;

            case Gnb.AbdomenTear:
                gnb.RemoveStatus(Gnb.ReadyToTear);
                return;

            case Gnb.EyeGouge:
                gnb.RemoveStatus(Gnb.ReadyToGouge);
                return;

            case Gnb.Hypervelocity:
                gnb.RemoveStatus(Gnb.ReadyToBlast);
                return;

            case Gnb.FatedBrand:
                gnb.RemoveStatus(Gnb.ReadyToRaze);
                return;

            case Gnb.NoMercyAction:
                gnb.ApplyStatus(Gnb.NoMercyStatus, GunbreakerData.NoMercyDuration,
                    damageMulti: GunbreakerData.NoMercyMulti);
                gnb.ApplyStatus(Gnb.ReadyToBreak, GunbreakerData.ReadyToBreakDuration);
                return;

            case Gnb.SonicBreak:
                gnb.RemoveStatus(Gnb.ReadyToBreak);
                recordHit(Gnb.SonicBreakDot,
                    GunbreakerData.SonicBreakTickPotency * GunbreakerData.SonicBreakTicks);
                return;

            case Gnb.BowShock:
                recordHit(Gnb.BowShockDot,
                    GunbreakerData.BowShockTickPotency * GunbreakerData.BowShockTicks
                        * action.TargetMultiplier(state.Targets));
                return;

            case Gnb.BloodfestAction:
                gnb.ApplyStatus(Gnb.BloodfestStatus, GunbreakerData.BloodfestDuration);
                gnb.GainCartridges(GunbreakerData.BloodfestCartridges);
                gnb.ApplyStatus(Gnb.ReadyToReign, GunbreakerData.ReadyToReignDuration);
                return;

            case Gnb.ReignOfBeasts:
                gnb.RemoveStatus(Gnb.ReadyToReign);
                gnb.AdvanceReignChain(action);
                return;

            case Gnb.NobleBlood:
                gnb.AdvanceReignChain(action);
                return;

            case Gnb.LionHeart:
                gnb.BreakReignChain();
                return;
        }
    }
}
