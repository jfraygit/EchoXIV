using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Viper;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Vpr
{
    public const string HuntersInstinct = "Hunter's Instinct";
    public const string Swiftscaled = "Swiftscaled";
    public const string ReadyToReawaken = "Ready to Reawaken";
    public const string Reawakened = "Reawakened";
    public const string HuntersVenom = "Hunter's Venom";
    public const string SwiftskinsVenom = "Swiftskin's Venom";
    public const string PoisedForTwinfang = "Poised for Twinfang";
    public const string PoisedForTwinblood = "Poised for Twinblood";

    public const string SteelFangs = "Steel Fangs";
    public const string ReavingFangs = "Reaving Fangs";

    public const string HuntersSting = "Hunter's Sting";
    public const string SwiftskinsSting = "Swiftskin's Sting";

    public const string FlankstingStrike = "Flanksting Strike";
    public const string HindstingStrike = "Hindsting Strike";
    public const string FlanksbaneFang = "Flanksbane Fang";
    public const string HindsbaneFang = "Hindsbane Fang";

    public const string Vicewinder = "Vicewinder";
    public const string HuntersCoil = "Hunter's Coil";
    public const string SwiftskinsCoil = "Swiftskin's Coil";

    public const string UncoiledFury = "Uncoiled Fury";

    public const string ReawakenAction = "Reawaken";
    public const string FirstGeneration = "First Generation";
    public const string SecondGeneration = "Second Generation";
    public const string ThirdGeneration = "Third Generation";
    public const string FourthGeneration = "Fourth Generation";
    public const string Ouroboros = "Ouroboros";

    public const string DeathRattle = "Death Rattle";
    public const string TwinfangBite = "Twinfang Bite";
    public const string TwinbloodBite = "Twinblood Bite";
    public const string UncoiledTwinfang = "Uncoiled Twinfang";
    public const string UncoiledTwinblood = "Uncoiled Twinblood";
    public const string FirstLegacy = "First Legacy";
    public const string SecondLegacy = "Second Legacy";
    public const string ThirdLegacy = "Third Legacy";
    public const string FourthLegacy = "Fourth Legacy";
    public const string SerpentsIre = "Serpent's Ire";

    public const string SteelMaw = "Steel Maw";
    public const string ReavingMaw = "Reaving Maw";
    public const string HuntersBite = "Hunter's Bite";
    public const string SwiftskinsBite = "Swiftskin's Bite";
    public const string JaggedMaw = "Jagged Maw";
    public const string BloodiedMaw = "Bloodied Maw";
    public const string Vicepit = "Vicepit";
    public const string HuntersDen = "Hunter's Den";
    public const string SwiftskinsDen = "Swiftskin's Den";
}

/// Viper's action table.
public static class ViperData
{
    /// The falloff on every cleaving thing Viper has: 75% off each enemy after the first.
    public const double ViperFalloff = 0.75;

    public const double HuntersInstinctMulti = 1.10;

    public const double BuffDuration = 40.0;
    public const double VenomDuration = 30.0;
    public const double ReawakenDuration = 30.0;
    public const double ReadyToReawakenDuration = 30.0;

    public const int MaxSerpentOfferings = 100;
    public const int ReawakenCost = 50;
    public const int MaxRattlingCoil = 3;
    public const int AnguineTributeStacks = 5;

    /// Measured, not derived.
    public const double GenerationRecast = 1.69;

    /// Ouroboros, measured - and the measurement disagrees with the formula by more than Generations does.
    public const double OuroborosRecast = 2.54;

    /// Uncoiled Fury's own recast, read straight off the action row: 3.5s where every other weaponskill in
    /// this job takes the standard global.
    public const double UncoiledFuryRecast = 3.5;

    /// Reawaken's own recast, derived from its 2.2s tooltip and NOT measured.
    public const double ReawakenBaseRecast = 2.2;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Vpr.SteelMaw] = new ActionDef
        {
            Name = Vpr.SteelMaw,
            Kind = ActionKind.Gcd,
            Potency = 140,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Vpr.ReavingMaw] = new ActionDef
        {
            Name = Vpr.ReavingMaw,
            Kind = ActionKind.Gcd,
            Potency = 140,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Vpr.HuntersBite] = new ActionDef
        {
            Name = Vpr.HuntersBite,
            Kind = ActionKind.Gcd,
            Potency = 180,
            PreservesCombo = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Vpr.SwiftskinsBite] = new ActionDef
        {
            Name = Vpr.SwiftskinsBite,
            Kind = ActionKind.Gcd,
            Potency = 180,
            PreservesCombo = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Vpr.JaggedMaw] = new ActionDef
        {
            Name = Vpr.JaggedMaw,
            Kind = ActionKind.Gcd,
            Potency = 220,
            PreservesCombo = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Vpr.BloodiedMaw] = new ActionDef
        {
            Name = Vpr.BloodiedMaw,
            Kind = ActionKind.Gcd,
            Potency = 220,
            PreservesCombo = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Vpr.Vicepit] = new ActionDef
        {
            Name = Vpr.Vicepit,
            Kind = ActionKind.Gcd,
            Potency = 250,
            Cooldown = 40.0,
            MaxCharges = 2,
            SharesCooldownWith = [Vpr.Vicewinder],
            MaxTargets = ActionDef.AllNearby,
        },

        [Vpr.HuntersDen] = new ActionDef
        {
            Name = Vpr.HuntersDen,
            Kind = ActionKind.Gcd,
            Potency = 300,
            PreservesCombo = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Vpr.SwiftskinsDen] = new ActionDef
        {
            Name = Vpr.SwiftskinsDen,
            Kind = ActionKind.Gcd,
            Potency = 300,
            PreservesCombo = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Vpr.SteelFangs] = new ActionDef
        {
            Name = Vpr.SteelFangs,
            Kind = ActionKind.Gcd,
            Potency = 300,
            IsComboStarter = true,
        },

        [Vpr.ReavingFangs] = new ActionDef
        {
            Name = Vpr.ReavingFangs,
            Kind = ActionKind.Gcd,
            Potency = 300,
            IsComboStarter = true,
        },

        [Vpr.HuntersSting] = new ActionDef
        {
            Name = Vpr.HuntersSting,
            Kind = ActionKind.Gcd,
            Potency = 300,
            PreservesCombo = true,
        },

        [Vpr.SwiftskinsSting] = new ActionDef
        {
            Name = Vpr.SwiftskinsSting,
            Kind = ActionKind.Gcd,
            Potency = 300,
            PreservesCombo = true,
        },

        [Vpr.FlankstingStrike] = new ActionDef
        {
            Name = Vpr.FlankstingStrike,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 500,
            PreservesCombo = true,
        },

        [Vpr.HindstingStrike] = new ActionDef
        {
            Name = Vpr.HindstingStrike,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 500,
            PreservesCombo = true,
        },

        [Vpr.FlanksbaneFang] = new ActionDef
        {
            Name = Vpr.FlanksbaneFang,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 500,
            PreservesCombo = true,
        },

        [Vpr.HindsbaneFang] = new ActionDef
        {
            Name = Vpr.HindsbaneFang,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 500,
            PreservesCombo = true,
        },

        [Vpr.Vicewinder] = new ActionDef
        {
            Name = Vpr.Vicewinder,
            Kind = ActionKind.Gcd,
            Potency = 540,
            Cooldown = 40.0,
            MaxCharges = 2,
            PreservesCombo = true,
        },

        [Vpr.HuntersCoil] = new ActionDef
        {
            Name = Vpr.HuntersCoil,
            PositionalBonus = 50,            Kind = ActionKind.Gcd,
            Potency = 680,
            PreservesCombo = true,
        },

        [Vpr.SwiftskinsCoil] = new ActionDef
        {
            Name = Vpr.SwiftskinsCoil,
            PositionalBonus = 50,            Kind = ActionKind.Gcd,
            Potency = 680,
            PreservesCombo = true,
        },

        [Vpr.UncoiledFury] = new ActionDef
        {
            Name = Vpr.UncoiledFury,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.Gcd,
            Potency = 680,
            BaseRecast = UncoiledFuryRecast,
            PreservesCombo = true,
        },

        [Vpr.ReawakenAction] = new ActionDef
        {
            Name = Vpr.ReawakenAction,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.Gcd,
            Potency = 750,
            BaseRecast = ReawakenBaseRecast,
            PreservesCombo = true,
        },

        [Vpr.FirstGeneration] = new ActionDef
        {
            Name = Vpr.FirstGeneration,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.Gcd,
            Potency = 680,
            FixedRecast = GenerationRecast,
            PreservesCombo = true,
        },

        [Vpr.SecondGeneration] = new ActionDef
        {
            Name = Vpr.SecondGeneration,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.Gcd,
            Potency = 680,
            FixedRecast = GenerationRecast,
            PreservesCombo = true,
        },

        [Vpr.ThirdGeneration] = new ActionDef
        {
            Name = Vpr.ThirdGeneration,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.Gcd,
            Potency = 680,
            FixedRecast = GenerationRecast,
            PreservesCombo = true,
        },

        [Vpr.FourthGeneration] = new ActionDef
        {
            Name = Vpr.FourthGeneration,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.Gcd,
            Potency = 680,
            FixedRecast = GenerationRecast,
            PreservesCombo = true,
        },

        [Vpr.Ouroboros] = new ActionDef
        {
            Name = Vpr.Ouroboros,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1150,
            FixedRecast = OuroborosRecast,
            PreservesCombo = true,
        },

        [Vpr.DeathRattle] = new ActionDef
        {
            Name = Vpr.DeathRattle,
            Kind = ActionKind.OffGcd,
            Potency = 280,
            Cooldown = 1.0,
        },

        [Vpr.TwinfangBite] = new ActionDef
        {
            Name = Vpr.TwinfangBite,
            Kind = ActionKind.OffGcd,
            Potency = 170,
            Cooldown = 1.0,
        },

        [Vpr.TwinbloodBite] = new ActionDef
        {
            Name = Vpr.TwinbloodBite,
            Kind = ActionKind.OffGcd,
            Potency = 170,
            Cooldown = 1.0,
        },

        [Vpr.UncoiledTwinfang] = new ActionDef
        {
            Name = Vpr.UncoiledTwinfang,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 170,
            Cooldown = 1.0,
        },

        [Vpr.UncoiledTwinblood] = new ActionDef
        {
            Name = Vpr.UncoiledTwinblood,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 170,
            Cooldown = 1.0,
        },

        [Vpr.FirstLegacy] = new ActionDef
        {
            Name = Vpr.FirstLegacy,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 320,
            Cooldown = 1.0,
        },

        [Vpr.SecondLegacy] = new ActionDef
        {
            Name = Vpr.SecondLegacy,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 320,
            Cooldown = 1.0,
        },

        [Vpr.ThirdLegacy] = new ActionDef
        {
            Name = Vpr.ThirdLegacy,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 320,
            Cooldown = 1.0,
        },

        [Vpr.FourthLegacy] = new ActionDef
        {
            Name = Vpr.FourthLegacy,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ViperFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 320,
            Cooldown = 1.0,
        },

        [Vpr.SerpentsIre] = new ActionDef
        {
            Name = Vpr.SerpentsIre,
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
