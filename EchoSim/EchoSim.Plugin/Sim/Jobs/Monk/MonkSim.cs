using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Monk;

/// Which Beast Chakra a weaponskill opens, decided by the form it belongs to.
public enum BeastChakra
{
    None,
    OpoOpo,
    Raptor,
    Coeurl,
}

/// Monk's gauge on top of the generic sim state.
public sealed class MonkState : SimState
{
    /// Chakra, spent five at a time on The Forbidden Chakra.
    public int Chakra { get; set; }

    /// The Beast Chakra opened so far under Perfect Balance, in order.
    public List<BeastChakra> Beast { get; } = [];

    /// The two Nadi.
    public bool LunarNadi { get; set; }

    public bool SolarNadi { get; set; }

    /// Brotherhood raises the chakra ceiling from five to ten, so chakra generated during burst isn't thrown
    /// away while The Forbidden Chakra is still on its one-second recast.
    public int ChakraCap => HasStatus(Mnk.Brotherhood) ? 10 : 5;

    public void GainChakra(int amount = 1)
    {
        var wasted = Math.Max(0, Chakra + amount - ChakraCap);
        if (wasted > 0)
            Count("chakra.overcapped", wasted);

        Chakra = Math.Min(ChakraCap, Chakra + amount);
    }

    public void SpendChakra(int amount) => Chakra = Math.Max(0, Chakra - amount);

    public override SimState Clone()
    {
        var copy = new MonkState
        {
            Chakra = Chakra,
            LunarNadi = LunarNadi,
            SolarNadi = SolarNadi,
        };

        copy.Beast.AddRange(Beast);
        CopyInto(copy);
        return copy;
    }
}

/// Monk's rules: what's legal, what it's worth, and what it does to the gauge.
public sealed class MonkSim : IJobSim
{
    public string JobName => "Monk";

    /// Monk's STR modifier from the ClassJob sheet.
    public MainAttribute MainAttribute => MainAttribute.Strength;

    public CombatRole Role => CombatRole.Melee;

    public int MainStatModifier { get; init; } = 110;

    /// A permanent 20%, which is where the 2.00s GCD comes from.
    public int HastePercent => 20;

    public int AutoAttackPotency => 90;

    public IReadOnlyDictionary<string, ActionDef> Actions => MonkData.Actions;

    /// Which form each weaponskill demands, and therefore which Beast Chakra it opens.
    private static readonly Dictionary<string, BeastChakra> Family = new()
    {
        [Mnk.LeapingOpo] = BeastChakra.OpoOpo,
        [Mnk.DragonKick] = BeastChakra.OpoOpo,
        [Mnk.RisingRaptor] = BeastChakra.Raptor,
        [Mnk.TwinSnakes] = BeastChakra.Raptor,
        [Mnk.PouncingCoeurl] = BeastChakra.Coeurl,
        [Mnk.Demolish] = BeastChakra.Coeurl,

        [Mnk.ShadowOfTheDestroyer] = BeastChakra.OpoOpo,
        [Mnk.FourPointFury] = BeastChakra.Raptor,
        [Mnk.Rockbreaker] = BeastChakra.Coeurl,
    };

    /// The form a weaponskill leaves behind, when not under Perfect Balance.
    private static readonly Dictionary<BeastChakra, string> NextForm = new()
    {
        [BeastChakra.OpoOpo] = Mnk.RaptorForm,
        [BeastChakra.Raptor] = Mnk.CoeurlForm,
        [BeastChakra.Coeurl] = Mnk.OpoForm,
    };

    /// The Fury stack each weaponskill spends, and which combo action grants it.
    private static readonly Dictionary<string, string> SpendsFury = new()
    {
        [Mnk.LeapingOpo] = Mnk.OpoFury,
        [Mnk.RisingRaptor] = Mnk.RaptorFury,
        [Mnk.PouncingCoeurl] = Mnk.CoeurlFury,
    };

    public SimState CreateState()
    {
        var state = new MonkState();

        foreach (var (name, action) in MonkData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        state.RegisterCooldown(Buffs.PotionAction, PlayerStats.PotionRecast);

        state.ApplyStatus(Mnk.OpoForm, MonkData.FormDuration);
        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var mnk = (MonkState)state;

        return action.Name switch
        {
            Mnk.ElixirBurst or Mnk.RisingPhoenix or Mnk.CelestialRevolution or Mnk.PhantomRush
                => mnk.Beast.Count == 3 && BlitzFor(mnk) == action.Name,

            Mnk.ForbiddenChakra or Mnk.Enlightenment => mnk.Chakra >= MonkData.ChakraCost,

            Mnk.WindsReply => mnk.HasStatus(Mnk.WindRumination),
            Mnk.FiresReply => mnk.HasStatus(Mnk.FireRumination),

            _ when Family.TryGetValue(action.Name, out var family) => FormMet(mnk, family),

            _ => true,
        };
    }

    /// Whether the player is in the form a weaponskill demands.
    private static bool FormMet(MonkState state, BeastChakra family)
    {
        if (state.HasStatus(Mnk.PerfectBalance) || state.HasStatus(Mnk.FormlessFist))
            return true;

        return family switch
        {
            BeastChakra.OpoOpo => state.HasStatus(Mnk.OpoForm),
            BeastChakra.Raptor => state.HasStatus(Mnk.RaptorForm),
            BeastChakra.Coeurl => state.HasStatus(Mnk.CoeurlForm),
            _ => true,
        };
    }

    /// Which action Masterful Blitz becomes.
    public static string BlitzFor(MonkState state)
    {
        if (state.Beast.Count < 3)
            return Mnk.CelestialRevolution;

        var distinct = state.Beast.Distinct().Count();

        if (distinct == 2)
            return Mnk.CelestialRevolution;

        if (state.LunarNadi && state.SolarNadi)
            return Mnk.PhantomRush;

        return distinct == 1 ? Mnk.ElixirBurst : Mnk.RisingPhoenix;
    }

    /// Leaping Opo is a guaranteed critical hit while it has a Fury stack to spend.
    public bool IsAutoCrit(ActionDef action, SimState state)
        => action.Name == Mnk.LeapingOpo && FormMet((MonkState)state, BeastChakra.OpoOpo);

    /// Riddle of Wind halves the time between auto-attack swings.
    public double AutoAttackInterval(SimState state, PlayerStats stats)
        => state.HasStatus(Mnk.RiddleOfWind) ? stats.AutoAttackInterval * 0.5 : stats.AutoAttackInterval;

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var mnk = (MonkState)state;
        double potency = action.Potency;

        if (SpendsFury.TryGetValue(action.Name, out var fury) && mnk.HasStatus(fury))
            potency = action.ComboPotency;

        return potency;
    }

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var mnk = (MonkState)state;

        switch (action.Name)
        {
            case Mnk.PerfectBalanceAction:
                mnk.Beast.Clear();
                mnk.ApplyStatus(Mnk.PerfectBalance, MonkData.PerfectBalanceDuration, stacks: 3);
                return;

            case Mnk.RiddleOfFireAction:
                mnk.ApplyStatus(Mnk.RiddleOfFire, MonkData.RiddleOfFireDuration, damageMulti: MonkData.RiddleOfFireMulti);
                mnk.ApplyStatus(Mnk.FireRumination, 20.0);
                return;

            case Mnk.RiddleOfWindAction:
                mnk.ApplyStatus(Mnk.RiddleOfWind, MonkData.RiddleOfWindDuration);
                mnk.ApplyStatus(Mnk.WindRumination, 15.0);
                return;

            case Mnk.BrotherhoodAction:
                mnk.ApplyStatus(Mnk.Brotherhood, MonkData.BrotherhoodDuration, damageMulti: MonkData.BrotherhoodMulti);
                return;

            case Mnk.ForbiddenChakra:
            case Mnk.Enlightenment:
                mnk.SpendChakra(MonkData.ChakraCost);
                return;

            case Mnk.WindsReply:
                mnk.RemoveStatus(Mnk.WindRumination);
                AdvanceForm(mnk, action);
                return;

            case Mnk.FiresReply:
                mnk.RemoveStatus(Mnk.FireRumination);
                mnk.ApplyStatus(Mnk.FormlessFist, MonkData.FormDuration);
                return;

            case Mnk.ElixirBurst:
            case Mnk.RisingPhoenix:
            case Mnk.CelestialRevolution:
            case Mnk.PhantomRush:
                ResolveBlitz(mnk, action.Name);
                return;
        }

        if (Family.ContainsKey(action.Name))
        {
            mnk.GainChakra();
            AdvanceForm(mnk, action);
        }
    }

    /// Applies a weaponskill's effect on form, Fury stacks and Beast Chakra.
    private static void AdvanceForm(MonkState state, ActionDef action)
    {
        if (!Family.TryGetValue(action.Name, out var family))
            return;

        if (SpendsFury.TryGetValue(action.Name, out var spent))
            state.ConsumeStack(spent);

        switch (action.Name)
        {
            case Mnk.DragonKick:
                state.AddStack(Mnk.OpoFury, MonkData.FormDuration, 1);
                break;
            case Mnk.TwinSnakes:
                state.AddStack(Mnk.RaptorFury, MonkData.FormDuration, 1);
                break;
            case Mnk.Demolish:
                state.AddStack(Mnk.CoeurlFury, MonkData.FormDuration, 2);
                state.AddStack(Mnk.CoeurlFury, MonkData.FormDuration, 2);
                break;
        }

        if (state.HasStatus(Mnk.PerfectBalance))
        {
            state.Beast.Add(family);
            state.ConsumeStack(Mnk.PerfectBalance);

            if (state.StatusStacks(Mnk.PerfectBalance) <= 0)
                state.RemoveStatus(Mnk.PerfectBalance);

            return;
        }

        if (state.HasStatus(Mnk.FormlessFist))
            state.RemoveStatus(Mnk.FormlessFist);

        state.RemoveStatus(Mnk.OpoForm);
        state.RemoveStatus(Mnk.RaptorForm);
        state.RemoveStatus(Mnk.CoeurlForm);
        state.ApplyStatus(NextForm[family], MonkData.FormDuration);
    }

    /// Spends the banked Beast Chakra and lights whichever Nadi the Blitz opens.
    private static void ResolveBlitz(MonkState state, string blitz)
    {
        switch (blitz)
        {
            case Mnk.ElixirBurst:
                state.LunarNadi = true;
                break;

            case Mnk.RisingPhoenix:
                state.SolarNadi = true;
                break;

            case Mnk.PhantomRush:
                state.LunarNadi = false;
                state.SolarNadi = false;
                break;

            case Mnk.CelestialRevolution:
                state.Count("blitz.celestial", 1);
                if (!state.LunarNadi)
                    state.LunarNadi = true;
                else
                    state.SolarNadi = true;
                break;
        }

        state.Beast.Clear();
        state.ApplyStatus(Mnk.FormlessFist, MonkData.FormDuration);
    }
}
