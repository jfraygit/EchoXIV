using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Summoner;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Smn
{
    public const string IfritsFavor = "Ifrit's Favor";
    public const string TitansFavor = "Titan's Favor";
    public const string GarudasFavor = "Garuda's Favor";
    public const string FurtherRuin = "Further Ruin";
    public const string RubysGlimmer = "Ruby's Glimmer";
    public const string RefulgentLux = "Refulgent Lux";
    public const string SearingLightStatus = "Searing Light";

    public const string Ruin3 = "Ruin III";
    public const string Ruin4 = "Ruin IV";

    public const string SummonSolarBahamut = "Summon Solar Bahamut";
    public const string SummonBahamut = "Summon Bahamut";
    public const string SummonPhoenix = "Summon Phoenix";
    public const string UmbralImpulse = "Umbral Impulse";
    public const string AstralImpulse = "Astral Impulse";
    public const string FountainOfFire = "Fountain of Fire";

    public const string Sunflare = "Sunflare";
    public const string Deathflare = "Deathflare";
    public const string Rekindle = "Rekindle";

    public const string EnkindleSolarBahamut = "Enkindle Solar Bahamut";
    public const string EnkindleBahamut = "Enkindle Bahamut";
    public const string EnkindlePhoenix = "Enkindle Phoenix";

    public const string SummonIfrit = "Summon Ifrit II";
    public const string SummonTitan = "Summon Titan II";
    public const string SummonGaruda = "Summon Garuda II";
    public const string RubyCatastrophe = "Ruby Catastrophe";
    public const string TopazCatastrophe = "Topaz Catastrophe";
    public const string EmeraldCatastrophe = "Emerald Catastrophe";
    public const string UmbralFlare = "Umbral Flare";
    public const string AstralFlare = "Astral Flare";
    public const string BrandOfPurgatory = "Brand of Purgatory";

    public const string RubyRite = "Ruby Rite";
    public const string TopazRite = "Topaz Rite";
    public const string EmeraldRite = "Emerald Rite";

    public const string CrimsonCyclone = "Crimson Cyclone";
    public const string CrimsonStrike = "Crimson Strike";
    public const string MountainBuster = "Mountain Buster";
    public const string Slipstream = "Slipstream";

    public const string EnergyDrain = "Energy Drain";
    public const string Necrotize = "Necrotize";

    public const string SearingLightAction = "Searing Light";
    public const string SearingFlash = "Searing Flash";
    public const string SwiftcastAction = "Swiftcast";
}

/// Summoner's action table, taken from the game's own sheets via tools/gamedata.
public static class SummonerData
{
    /// The falloff on Summoner's summon payoffs: 50% off each enemy after the first.
    public const double SummonerFalloff = 0.50;

    /// The four egi assault weaponskills lose 60% rather than 50%: Crimson Cyclone, Crimson Strike, Mountain
    /// Buster and Slipstream.
    public const double EgiAssaultFalloff = 0.60;

    /// Ruin IV's, which is the same 60% and a different clause on a different action.
    public const double Ruin4Falloff = 0.60;

    /// What a pet's listed potency is actually worth for THIS job - and it is not Ninja's 0.92.
    public const double PetPotencyMultiplier = 0.775;


    /// The order the demis are summoned in, and it repeats.
    public static readonly string[] DemiCycle =
    [
        Smn.SummonSolarBahamut,
        Smn.SummonBahamut,
        Smn.SummonSolarBahamut,
        Smn.SummonPhoenix,
    ];

    /// Filler casts inside one demi trance.
    public const int TranceCasts = 6;

    public const double TranceDuration = 15.0;

    /// The demi's automatic attack, and how many land per trance.
    public const int WavesPerTrance = 4;

    public const int LuxwavePotency = 180;

    public const int WyrmwavePotency = 150;

    public const int ScarletFlamePotency = 150;


    /// The egis in the order they are summoned after a demi window: TITAN, GARUDA, IFRIT.
    public static readonly string[] EgiCycle = [Smn.SummonTitan, Smn.SummonGaruda, Smn.SummonIfrit];

    /// Gemshine casts each egi grants.
    public const int GemshineCasts = 4;


    /// Stacks one Energy Drain grants.
    public const int AetherflowStacks = 2;

    /// Energy Drain's recast, which is what actually paces the Aetherflow economy.
    public const double EnergyDrainCooldown = 60.0;


    public const double FavorDuration = 30.0;
    public const double FurtherRuinDuration = 60.0;
    public const double RubysGlimmerDuration = 30.0;
    public const double SearingLightDuration = 20.0;

    /// Searing Light's damage bonus to the Summoner and everyone nearby.
    public const double SearingLightBonus = 1.05;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Smn.Ruin3] = new ActionDef
        {
            Name = Smn.Ruin3,
            Kind = ActionKind.Gcd,
            Potency = 400,
            CastTime = 1.5,
        },

        [Smn.Ruin4] = new ActionDef
        {
            Name = Smn.Ruin4,
            MaxTargets = ActionDef.AllNearby,
            Falloff = Ruin4Falloff,
            Kind = ActionKind.Gcd,
            Potency = 520,
        },

        [Smn.SummonSolarBahamut] = new ActionDef
        {
            Name = Smn.SummonSolarBahamut,
            Kind = ActionKind.Gcd,
            Cooldown = 60.0,
            SharesCooldownWith = [Smn.SummonBahamut, Smn.SummonPhoenix],
        },

        [Smn.SummonBahamut] = new ActionDef
        {
            Name = Smn.SummonBahamut,
            Kind = ActionKind.Gcd,
            Cooldown = 60.0,
            SharesCooldownWith = [Smn.SummonSolarBahamut, Smn.SummonPhoenix],
        },

        [Smn.SummonPhoenix] = new ActionDef
        {
            Name = Smn.SummonPhoenix,
            Kind = ActionKind.Gcd,
            Cooldown = 60.0,
            SharesCooldownWith = [Smn.SummonSolarBahamut, Smn.SummonBahamut],
        },

        [Smn.UmbralImpulse] = new ActionDef
        {
            Name = Smn.UmbralImpulse,
            Kind = ActionKind.Gcd,
            Potency = 640,
        },

        [Smn.AstralImpulse] = new ActionDef
        {
            Name = Smn.AstralImpulse,
            Kind = ActionKind.Gcd,
            Potency = 500,
        },

        [Smn.FountainOfFire] = new ActionDef
        {
            Name = Smn.FountainOfFire,
            Kind = ActionKind.Gcd,
            Potency = 580,
        },

        [Smn.Sunflare] = new ActionDef
        {
            Name = Smn.Sunflare,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SummonerFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 1000,
        },

        [Smn.Deathflare] = new ActionDef
        {
            Name = Smn.Deathflare,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SummonerFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 500,
        },

        [Smn.Rekindle] = new ActionDef
        {
            Name = Smn.Rekindle,
            Kind = ActionKind.OffGcd,
            Potency = 0,
        },

        [Smn.EnkindleSolarBahamut] = new ActionDef
        {
            Name = Smn.EnkindleSolarBahamut,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SummonerFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 1500,
            Cooldown = 20.0,
        },

        [Smn.EnkindleBahamut] = new ActionDef
        {
            Name = Smn.EnkindleBahamut,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SummonerFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 1300,
            Cooldown = 20.0,
        },

        [Smn.EnkindlePhoenix] = new ActionDef
        {
            Name = Smn.EnkindlePhoenix,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SummonerFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 1300,
            Cooldown = 20.0,
        },

        [Smn.SummonIfrit] = new ActionDef
        {
            Name = Smn.SummonIfrit,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SummonerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 800,
        },

        [Smn.SummonTitan] = new ActionDef
        {
            Name = Smn.SummonTitan,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SummonerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 800,
        },

        [Smn.SummonGaruda] = new ActionDef
        {
            Name = Smn.SummonGaruda,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SummonerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 800,
        },

        [Smn.RubyCatastrophe] = new ActionDef
        {
            Name = Smn.RubyCatastrophe,
            Kind = ActionKind.Gcd,
            Potency = 210,
            CastTime = 3.0,
            MaxTargets = ActionDef.AllNearby,
        },

        [Smn.TopazCatastrophe] = new ActionDef
        {
            Name = Smn.TopazCatastrophe,
            Kind = ActionKind.Gcd,
            Potency = 140,
            MaxTargets = ActionDef.AllNearby,
        },

        [Smn.EmeraldCatastrophe] = new ActionDef
        {
            Name = Smn.EmeraldCatastrophe,
            Kind = ActionKind.Gcd,
            Potency = 100,
            MaxTargets = ActionDef.AllNearby,
        },

        [Smn.UmbralFlare] = new ActionDef
        {
            Name = Smn.UmbralFlare,
            Kind = ActionKind.Gcd,
            Potency = 300,
            MaxTargets = ActionDef.AllNearby,
        },

        [Smn.AstralFlare] = new ActionDef
        {
            Name = Smn.AstralFlare,
            Kind = ActionKind.Gcd,
            Potency = 180,
            MaxTargets = ActionDef.AllNearby,
        },

        [Smn.BrandOfPurgatory] = new ActionDef
        {
            Name = Smn.BrandOfPurgatory,
            Kind = ActionKind.Gcd,
            Potency = 240,
            MaxTargets = ActionDef.AllNearby,
        },

        [Smn.RubyRite] = new ActionDef
        {
            Name = Smn.RubyRite,
            Kind = ActionKind.Gcd,
            Potency = 620,
            CastTime = 2.8,
            BaseRecast = 3.0,
        },

        [Smn.TopazRite] = new ActionDef
        {
            Name = Smn.TopazRite,
            Kind = ActionKind.Gcd,
            Potency = 340,
        },

        [Smn.EmeraldRite] = new ActionDef
        {
            Name = Smn.EmeraldRite,
            Kind = ActionKind.Gcd,
            Potency = 280,
            BaseRecast = 1.5,
        },

        [Smn.CrimsonCyclone] = new ActionDef
        {
            Name = Smn.CrimsonCyclone,
            MaxTargets = ActionDef.AllNearby,
            Falloff = EgiAssaultFalloff,
            Kind = ActionKind.Gcd,
            Potency = 560,
        },

        [Smn.CrimsonStrike] = new ActionDef
        {
            Name = Smn.CrimsonStrike,
            MaxTargets = ActionDef.AllNearby,
            Falloff = EgiAssaultFalloff,
            Kind = ActionKind.Gcd,
            Potency = 560,
        },

        [Smn.Slipstream] = new ActionDef
        {
            Name = Smn.Slipstream,
            MaxTargets = ActionDef.AllNearby,
            Falloff = EgiAssaultFalloff,
            Kind = ActionKind.Gcd,
            Potency = 520,
            CastTime = 3.0,
            BaseRecast = 3.5,
        },

        [Smn.MountainBuster] = new ActionDef
        {
            Name = Smn.MountainBuster,
            MaxTargets = ActionDef.AllNearby,
            Falloff = EgiAssaultFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 160,
        },

        [Smn.EnergyDrain] = new ActionDef
        {
            Name = Smn.EnergyDrain,
            Kind = ActionKind.OffGcd,
            Potency = 200,
            Cooldown = EnergyDrainCooldown,
        },

        [Smn.Necrotize] = new ActionDef
        {
            Name = Smn.Necrotize,
            Kind = ActionKind.OffGcd,
            Potency = 500,
        },

        [Smn.SearingLightAction] = new ActionDef
        {
            Name = Smn.SearingLightAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Smn.SearingFlash] = new ActionDef
        {
            Name = Smn.SearingFlash,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.OffGcd,
            Potency = 700,
        },

        [Smn.SwiftcastAction] = new ActionDef
        {
            Name = Smn.SwiftcastAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 60.0,
        },

        [Buffs.PotionAction] = new ActionDef
        {
            Name = Buffs.PotionAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = PlayerStats.PotionRecast,
        },
    };

    /// Whether an action's damage comes from a pet rather than the Summoner.
    public static bool IsPetAction(string name)
        => name is Smn.SummonIfrit or Smn.SummonTitan or Smn.SummonGaruda
            or Smn.EnkindleSolarBahamut or Smn.EnkindleBahamut or Smn.EnkindlePhoenix;

    /// The trance filler each demi grants.
    public static string AreaFillerFor(string demi) => demi switch
    {
        Smn.SummonSolarBahamut => Smn.UmbralFlare,
        Smn.SummonBahamut => Smn.AstralFlare,
        _ => Smn.BrandOfPurgatory,
    };

    public static string FillerFor(string demi) => demi switch
    {
        Smn.SummonSolarBahamut => Smn.UmbralImpulse,
        Smn.SummonBahamut => Smn.AstralImpulse,
        _ => Smn.FountainOfFire,
    };

    /// The enkindle each demi grants.
    public static string EnkindleFor(string demi) => demi switch
    {
        Smn.SummonSolarBahamut => Smn.EnkindleSolarBahamut,
        Smn.SummonBahamut => Smn.EnkindleBahamut,
        _ => Smn.EnkindlePhoenix,
    };

    /// The Astral Flow each demi grants.
    public static string AstralFlowFor(string demi) => demi switch
    {
        Smn.SummonSolarBahamut => Smn.Sunflare,
        Smn.SummonBahamut => Smn.Deathflare,
        _ => Smn.Rekindle,
    };

    /// The automatic attack each demi makes, and what it is worth.
    public static (string Name, int Potency) WaveFor(string demi) => demi switch
    {
        Smn.SummonSolarBahamut => ("Luxwave", LuxwavePotency),
        Smn.SummonBahamut => ("Wyrmwave", WyrmwavePotency),
        _ => ("Scarlet Flame", ScarletFlamePotency),
    };

    /// The gemshine each egi grants.
    public static string AreaGemshineFor(string egi) => egi switch
    {
        Smn.SummonIfrit => Smn.RubyCatastrophe,
        Smn.SummonTitan => Smn.TopazCatastrophe,
        _ => Smn.EmeraldCatastrophe,
    };

    public static string GemshineFor(string egi) => egi switch
    {
        Smn.SummonIfrit => Smn.RubyRite,
        Smn.SummonTitan => Smn.TopazRite,
        _ => Smn.EmeraldRite,
    };
}
