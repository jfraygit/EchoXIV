using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Viper;

/// Viper's gauges on top of the generic sim state.
public sealed class ViperState : SimState
{
    /// Serpent Offerings, capped at 100.
    public int SerpentOfferings { get; set; }

    /// Rattling Coil, capped at three.
    public int RattlingCoil { get; set; }

    /// Anguine Tribute: four Generations and the Ouroboros that closes the window.
    public int AnguineTribute { get; set; }

    /// Which of the four finisher branches comes next.
    public int FinisherCycle { get; set; }

    /// Which follow-up ability the last weaponskill unlocked, if any.
    public string? PendingLegacy { get; set; }

    /// Position in the Vicewinder chain: 0 none, 1 after Vicewinder, 2 after Hunter's Coil.
    public int CoilStep { get; set; }

    public void GainOfferings(int amount)
    {
        var room = ViperData.MaxSerpentOfferings - SerpentOfferings;
        if (amount > room)
            Count("offerings.overcapped", amount - room);

        SerpentOfferings = Math.Min(ViperData.MaxSerpentOfferings, SerpentOfferings + amount);
    }

    public void GainCoil()
    {
        if (RattlingCoil >= ViperData.MaxRattlingCoil)
        {
            Count("coil.overcapped", 1);
            return;
        }

        RattlingCoil++;
    }

    public override SimState Clone()
    {
        var copy = new ViperState
        {
            SerpentOfferings = SerpentOfferings,
            RattlingCoil = RattlingCoil,
            AnguineTribute = AnguineTribute,
            FinisherCycle = FinisherCycle,
            PendingLegacy = PendingLegacy,
            CoilStep = CoilStep,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Viper's rules: what's legal, what it's worth, and what it does to the gauges.
public sealed class ViperSim : IJobSim
{
    public string JobName => "Viper";

    /// Viper's DEX modifier from the ClassJob sheet: 110, tying Monk and Ninja for the lowest of the six
    /// melee.
    public MainAttribute MainAttribute => MainAttribute.Dexterity;

    public CombatRole Role => CombatRole.Melee;

    public int MainStatModifier { get; init; } = 110;

    /// Swiftscaled's 15%, modelled as permanent for the reason Samurai's Fuka is.
    public int HastePercent => 15;

    public int AutoAttackPotency => 90;

    public IReadOnlyDictionary<string, ActionDef> Actions => ViperData.Actions;

    public SimState CreateState()
    {
        var state = new ViperState();

        foreach (var (name, action) in ViperData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var vpr = (ViperState)state;

        return action.Name switch
        {
            Vpr.ReawakenAction => !vpr.HasStatus(Vpr.Reawakened)
                                  && (vpr.HasStatus(Vpr.ReadyToReawaken)
                                      || vpr.SerpentOfferings >= ViperData.ReawakenCost),

            Vpr.FirstGeneration => vpr.HasStatus(Vpr.Reawakened) && vpr.AnguineTribute == 5,
            Vpr.SecondGeneration => vpr.HasStatus(Vpr.Reawakened) && vpr.AnguineTribute == 4,
            Vpr.ThirdGeneration => vpr.HasStatus(Vpr.Reawakened) && vpr.AnguineTribute == 3,
            Vpr.FourthGeneration => vpr.HasStatus(Vpr.Reawakened) && vpr.AnguineTribute == 2,
            Vpr.Ouroboros => vpr.HasStatus(Vpr.Reawakened) && vpr.AnguineTribute == 1,

            Vpr.DeathRattle or Vpr.TwinfangBite or Vpr.TwinbloodBite
                or Vpr.UncoiledTwinfang or Vpr.UncoiledTwinblood
                or Vpr.FirstLegacy or Vpr.SecondLegacy or Vpr.ThirdLegacy or Vpr.FourthLegacy
                => vpr.PendingLegacy == action.Name,

            Vpr.UncoiledFury => vpr.RattlingCoil > 0 && !vpr.HasStatus(Vpr.Reawakened),

            Vpr.HuntersCoil => vpr.CoilStep == 1,
            Vpr.SwiftskinsCoil => vpr.CoilStep == 2,

            Vpr.HuntersSting or Vpr.SwiftskinsSting
                => vpr.LastComboAction is Vpr.SteelFangs or Vpr.ReavingFangs,

            Vpr.FlankstingStrike or Vpr.HindstingStrike => vpr.LastComboAction == Vpr.HuntersSting,
            Vpr.FlanksbaneFang or Vpr.HindsbaneFang => vpr.LastComboAction == Vpr.SwiftskinsSting,

            _ when action.IsGcd => !vpr.HasStatus(Vpr.Reawakened),

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state) => action.Potency;

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var vpr = (ViperState)state;

        if (action.IsGcd)
            vpr.PendingLegacy = null;

        switch (action.Name)
        {
            case Vpr.SteelFangs:
            case Vpr.SteelMaw:
            case Vpr.ReavingMaw:
            case Vpr.ReavingFangs:
                vpr.AdvanceCombo(action);
                return;

            case Vpr.HuntersSting:
            case Vpr.HuntersBite:
                vpr.ApplyStatus(Vpr.HuntersInstinct, ViperData.BuffDuration,
                    damageMulti: ViperData.HuntersInstinctMulti);
                vpr.AdvanceCombo(action);
                return;

            case Vpr.SwiftskinsSting:
            case Vpr.SwiftskinsBite:
                vpr.ApplyStatus(Vpr.Swiftscaled, ViperData.BuffDuration);
                vpr.AdvanceCombo(action);
                return;

            case Vpr.JaggedMaw:
            case Vpr.BloodiedMaw:
            case Vpr.FlankstingStrike:
            case Vpr.HindstingStrike:
            case Vpr.FlanksbaneFang:
            case Vpr.HindsbaneFang:
                vpr.GainOfferings(10);
                vpr.FinisherCycle = (vpr.FinisherCycle + 1) % 4;
                vpr.PendingLegacy = Vpr.DeathRattle;
                vpr.BreakCombo();
                return;

            case Vpr.Vicewinder:
            case Vpr.Vicepit:
                vpr.GainCoil();
                vpr.CoilStep = 1;
                return;

            case Vpr.HuntersCoil:
            case Vpr.HuntersDen:
                vpr.GainOfferings(5);
                vpr.ApplyStatus(Vpr.HuntersInstinct, ViperData.BuffDuration,
                    damageMulti: ViperData.HuntersInstinctMulti);
                vpr.ApplyStatus(Vpr.HuntersVenom, ViperData.VenomDuration);
                vpr.PendingLegacy = Vpr.TwinfangBite;
                vpr.CoilStep = 2;
                return;

            case Vpr.SwiftskinsCoil:
            case Vpr.SwiftskinsDen:
                vpr.GainOfferings(5);
                vpr.ApplyStatus(Vpr.Swiftscaled, ViperData.BuffDuration);
                vpr.ApplyStatus(Vpr.SwiftskinsVenom, ViperData.VenomDuration);
                vpr.PendingLegacy = Vpr.TwinbloodBite;
                vpr.CoilStep = 0;
                return;

            case Vpr.UncoiledFury:
                vpr.RattlingCoil--;
                vpr.PendingLegacy = Vpr.UncoiledTwinfang;
                return;

            case Vpr.ReawakenAction:
                if (vpr.HasStatus(Vpr.ReadyToReawaken))
                    vpr.RemoveStatus(Vpr.ReadyToReawaken);
                else
                    vpr.SerpentOfferings -= ViperData.ReawakenCost;

                vpr.ApplyStatus(Vpr.Reawakened, ViperData.ReawakenDuration);
                vpr.AnguineTribute = ViperData.AnguineTributeStacks;
                return;

            case Vpr.FirstGeneration:
                vpr.AnguineTribute--;
                vpr.PendingLegacy = Vpr.FirstLegacy;
                return;

            case Vpr.SecondGeneration:
                vpr.AnguineTribute--;
                vpr.PendingLegacy = Vpr.SecondLegacy;
                return;

            case Vpr.ThirdGeneration:
                vpr.AnguineTribute--;
                vpr.PendingLegacy = Vpr.ThirdLegacy;
                return;

            case Vpr.FourthGeneration:
                vpr.AnguineTribute--;
                vpr.PendingLegacy = Vpr.FourthLegacy;
                return;

            case Vpr.Ouroboros:
                vpr.AnguineTribute = 0;
                vpr.RemoveStatus(Vpr.Reawakened);
                return;

            case Vpr.TwinfangBite:
                if (vpr.HasStatus(Vpr.HuntersVenom))
                {
                    vpr.RemoveStatus(Vpr.HuntersVenom);
                    vpr.PendingLegacy = Vpr.TwinbloodBite;
                    return;
                }

                vpr.PendingLegacy = null;
                return;

            case Vpr.TwinbloodBite:
                if (vpr.HasStatus(Vpr.SwiftskinsVenom))
                {
                    vpr.RemoveStatus(Vpr.SwiftskinsVenom);
                    vpr.PendingLegacy = Vpr.TwinfangBite;
                    return;
                }

                vpr.PendingLegacy = null;
                return;

            case Vpr.UncoiledTwinfang:
                vpr.PendingLegacy = Vpr.UncoiledTwinblood;
                return;

            case Vpr.DeathRattle:
            case Vpr.UncoiledTwinblood:
            case Vpr.FirstLegacy:
            case Vpr.SecondLegacy:
            case Vpr.ThirdLegacy:
            case Vpr.FourthLegacy:
                vpr.PendingLegacy = null;
                return;

            case Vpr.SerpentsIre:
                vpr.GainCoil();
                vpr.ApplyStatus(Vpr.ReadyToReawaken, ViperData.ReadyToReawakenDuration);
                return;
        }
    }
}
