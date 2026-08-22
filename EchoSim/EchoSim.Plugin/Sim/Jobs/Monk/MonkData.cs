using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Monk;

/// Status, form and action names, kept in one place so typos surface at compile time.
public static class Mnk
{
    public const string OpoForm = "Opo-opo Form";
    public const string RaptorForm = "Raptor Form";
    public const string CoeurlForm = "Coeurl Form";
    public const string FormlessFist = "Formless Fist";

    public const string OpoFury = "Opo-opo's Fury";
    public const string RaptorFury = "Raptor's Fury";
    public const string CoeurlFury = "Coeurl's Fury";

    public const string PerfectBalance = "Perfect Balance";
    public const string RiddleOfFire = "Riddle of Fire";
    public const string RiddleOfWind = "Riddle of Wind";
    public const string Brotherhood = "Brotherhood";
    public const string FireRumination = "Fire's Rumination";
    public const string WindRumination = "Wind's Rumination";

    public const string LeapingOpo = "Leaping Opo";
    public const string RisingRaptor = "Rising Raptor";
    public const string PouncingCoeurl = "Pouncing Coeurl";
    public const string DragonKick = "Dragon Kick";
    public const string TwinSnakes = "Twin Snakes";
    public const string Demolish = "Demolish";
    public const string SixSidedStar = "Six-sided Star";

    public const string ElixirBurst = "Elixir Burst";
    public const string RisingPhoenix = "Rising Phoenix";
    public const string CelestialRevolution = "Celestial Revolution";
    public const string PhantomRush = "Phantom Rush";

    public const string WindsReply = "Wind's Reply";
    public const string FiresReply = "Fire's Reply";

    public const string ShadowOfTheDestroyer = "Shadow of the Destroyer";
    public const string FourPointFury = "Four-point Fury";
    public const string Rockbreaker = "Rockbreaker";
    public const string Enlightenment = "Enlightenment";

    public const string PerfectBalanceAction = "Perfect Balance";
    public const string RiddleOfFireAction = "Riddle of Fire";
    public const string RiddleOfWindAction = "Riddle of Wind";
    public const string BrotherhoodAction = "Brotherhood";
    public const string ForbiddenChakra = "The Forbidden Chakra";

    /// Pre-pull only, and display-only: it never enters the action table because it deals nothing and is
    /// never pressed in combat.
    public const string Meditation = "Meditation";
}

/// Monk's action table.
public static class MonkData
{
    /// Riddle of Fire's damage bonus - the window the whole burst is built around.
    public const double RiddleOfFireMulti = 1.15;

    /// Brotherhood's party-wide bonus, which the Monk also receives.
    public const double BrotherhoodMulti = 1.05;

    public const double RiddleOfFireDuration = 20.0;
    public const double RiddleOfWindDuration = 15.0;
    public const double BrotherhoodDuration = 20.0;
    public const double PerfectBalanceDuration = 20.0;
    public const double FormDuration = 30.0;

    /// Chakra needed before The Forbidden Chakra can be spent.
    public const int ChakraCost = 5;

    /// The falloff every one of Monk's cleaving actions carries: 35% off each enemy after the first.
    public const double MonkFalloff = 0.35;

    /// Monk's permanent haste, as a percentage.
    public const int HastePercent = 20;

    /// Six-sided Star occupies more than one GCD.
    public const double SixSidedStarRecast = 4.0;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Mnk.LeapingOpo] = new ActionDef
        {
            Name = Mnk.LeapingOpo,
            Kind = ActionKind.Gcd,
            Potency = 260,
            ComboPotency = 460,
        },

        [Mnk.DragonKick] = new ActionDef
        {
            Name = Mnk.DragonKick,
            Kind = ActionKind.Gcd,
            Potency = 320,
        },

        [Mnk.RisingRaptor] = new ActionDef
        {
            Name = Mnk.RisingRaptor,
            Kind = ActionKind.Gcd,
            Potency = 340,
            ComboPotency = 540,
        },

        [Mnk.TwinSnakes] = new ActionDef
        {
            Name = Mnk.TwinSnakes,
            Kind = ActionKind.Gcd,
            Potency = 420,
        },

        [Mnk.PouncingCoeurl] = new ActionDef
        {
            Name = Mnk.PouncingCoeurl,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 370,
            ComboPotency = 520,
        },

        [Mnk.Demolish] = new ActionDef
        {
            Name = Mnk.Demolish,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 420,
        },

        [Mnk.ElixirBurst] = new ActionDef
        {
            Name = Mnk.ElixirBurst,
            Kind = ActionKind.Gcd,
            Potency = 900,
            MaxTargets = ActionDef.AllNearby,
            Falloff = MonkFalloff,
        },

        [Mnk.RisingPhoenix] = new ActionDef
        {
            Name = Mnk.RisingPhoenix,
            Kind = ActionKind.Gcd,
            Potency = 900,
            MaxTargets = ActionDef.AllNearby,
            Falloff = MonkFalloff,
        },

        [Mnk.CelestialRevolution] = new ActionDef
        {
            Name = Mnk.CelestialRevolution,
            Kind = ActionKind.Gcd,
            Potency = 600,
        },

        [Mnk.PhantomRush] = new ActionDef
        {
            Name = Mnk.PhantomRush,
            Kind = ActionKind.Gcd,
            Potency = 1500,
            MaxTargets = ActionDef.AllNearby,
            Falloff = MonkFalloff,
        },

        [Mnk.WindsReply] = new ActionDef
        {
            Name = Mnk.WindsReply,
            Kind = ActionKind.Gcd,
            Potency = 1040,
            MaxTargets = ActionDef.AllNearby,
            Falloff = MonkFalloff,
        },

        [Mnk.FiresReply] = new ActionDef
        {
            Name = Mnk.FiresReply,
            Kind = ActionKind.Gcd,
            Potency = 1400,
            MaxTargets = ActionDef.AllNearby,
            Falloff = MonkFalloff,
        },

        [Mnk.ShadowOfTheDestroyer] = new ActionDef
        {
            Name = Mnk.ShadowOfTheDestroyer,
            Kind = ActionKind.Gcd,
            Potency = 120,
            MaxTargets = ActionDef.AllNearby,
        },

        [Mnk.FourPointFury] = new ActionDef
        {
            Name = Mnk.FourPointFury,
            Kind = ActionKind.Gcd,
            Potency = 140,
            MaxTargets = ActionDef.AllNearby,
        },

        [Mnk.Rockbreaker] = new ActionDef
        {
            Name = Mnk.Rockbreaker,
            Kind = ActionKind.Gcd,
            Potency = 150,
            MaxTargets = ActionDef.AllNearby,
        },

        [Mnk.Enlightenment] = new ActionDef
        {
            Name = Mnk.Enlightenment,
            Kind = ActionKind.OffGcd,
            Potency = 160,
            Cooldown = 1.0,
            SharesCooldownWith = [Mnk.ForbiddenChakra],
            MaxTargets = ActionDef.AllNearby,
        },

        [Mnk.SixSidedStar] = new ActionDef
        {
            Name = Mnk.SixSidedStar,
            Kind = ActionKind.Gcd,
            Potency = 780,
            BaseRecast = SixSidedStarRecast,
        },

        [Mnk.ForbiddenChakra] = new ActionDef
        {
            Name = Mnk.ForbiddenChakra,
            Kind = ActionKind.OffGcd,
            Potency = 400,
            Cooldown = 1.0,
        },

        [Mnk.PerfectBalanceAction] = new ActionDef
        {
            Name = Mnk.PerfectBalanceAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 40.0,
            MaxCharges = 2,
        },

        [Mnk.RiddleOfFireAction] = new ActionDef
        {
            Name = Mnk.RiddleOfFireAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 60.0,
        },

        [Mnk.RiddleOfWindAction] = new ActionDef
        {
            Name = Mnk.RiddleOfWindAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 90.0,
        },

        [Mnk.BrotherhoodAction] = new ActionDef
        {
            Name = Mnk.BrotherhoodAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Buffs.PotionAction] = new ActionDef
        {
            Name = Buffs.PotionAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = PlayerStats.PotionRecast,
        },
    };
}
