using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Dragoon;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Drg
{
    public const string PowerSurge = "Power Surge";
    public const string LanceCharge = "Lance Charge";
    public const string LifeOfTheDragon = "Life of the Dragon";
    public const string BattleLitany = "Battle Litany";
    public const string DraconianFire = "Draconian Fire";
    public const string DiveReady = "Dive Ready";
    public const string NastrondReady = "Nastrond Ready";
    public const string StarcrossReady = "Starcross Ready";
    public const string DragonsFlight = "Dragon's Flight";
    public const string LifeSurge = "Life Surge";

    public const string PiercingTalon = "Piercing Talon";
    public const string ElusiveJump = "Elusive Jump";

    public const string TrueThrust = "True Thrust";
    public const string RaidenThrust = "Raiden Thrust";

    public const string LanceBarrage = "Lance Barrage";
    public const string HeavensThrust = "Heavens' Thrust";
    public const string FangAndClaw = "Fang and Claw";

    public const string SpiralBlow = "Spiral Blow";
    public const string ChaoticSpring = "Chaotic Spring";
    public const string WheelingThrust = "Wheeling Thrust";

    public const string Drakesbane = "Drakesbane";
    public const string Nastrond = "Nastrond";

    public const string HighJump = "High Jump";
    public const string MirageDive = "Mirage Dive";
    public const string DragonfireDive = "Dragonfire Dive";
    public const string Geirskogul = "Geirskogul";
    public const string Stardiver = "Stardiver";
    public const string Starcross = "Starcross";
    public const string RiseOfTheDragon = "Rise of the Dragon";
    public const string WyrmwindThrust = "Wyrmwind Thrust";
    public const string LifeSurgeAction = "Life Surge";
    public const string LanceChargeAction = "Lance Charge";
    public const string BattleLitanyAction = "Battle Litany";

    public const string ChaoticSpringDot = "Chaotic Spring (DOT)";

    public const string DoomSpike = "Doom Spike";
    public const string DraconianFury = "Draconian Fury";
    public const string SonicThrust = "Sonic Thrust";
    public const string CoerthanTorment = "Coerthan Torment";
}

/// Dragoon's action table.
public static class DragoonData
{
    public const double PowerSurgeMulti = 1.10;
    public const double LanceChargeMulti = 1.10;

    /// Battle Litany raises critical hit RATE by 10%, and is deliberately not expressed as a damage
    /// multiplier beside the two above.
    public const double BattleLitanyCritBonus = 0.10;

    /// The falloff on Dragoon's cleaving abilities: half potency on every enemy after the first.
    public const double DragoonFalloff = 0.50;

    /// Starcross alone loses 40% rather than 50%, and it is the reason there are two constants here instead
    /// of one.
    public const double StarcrossFalloff = 0.40;

    /// Chaotic Spring's damage-over-time, as a total: a 45-potency tick over 24 seconds is 8 ticks.
    public const double ChaoticSpringDotPotency = 45 * 8;

    /// Life of the Dragon, 15%, and it is the one of the three that is worth confirming.
    public const double LifeOfTheDragonMulti = 1.15;

    public const double PowerSurgeDuration = 30.0;
    public const double LanceChargeDuration = 20.0;
    public const double BattleLitanyDuration = 20.0;
    public const double LifeOfTheDragonDuration = 20.0;
    public const double DraconianFireDuration = 30.0;
    public const double ChaoticSpringDotDuration = 24.0;

    /// Firstminds' Focus needed for Wyrmwind Thrust, and the cap it builds to.
    public const int FocusCost = 2;

    public const int MaxFocus = 2;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Drg.PiercingTalon] = new ActionDef
        {
            Name = Drg.PiercingTalon,
            Kind = ActionKind.Gcd,
            Potency = 200,
            ComboPotency = 350,
        },

        [Drg.ElusiveJump] = new ActionDef
        {
            Name = Drg.ElusiveJump,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 30.0,
        },

        [Drg.TrueThrust] = new ActionDef
        {
            Name = Drg.TrueThrust,
            Kind = ActionKind.Gcd,
            Potency = 230,
            IsComboStarter = true,
        },

        [Drg.RaidenThrust] = new ActionDef
        {
            Name = Drg.RaidenThrust,
            Kind = ActionKind.Gcd,
            Potency = 320,
            IsComboStarter = true,
        },

        [Drg.LanceBarrage] = new ActionDef
        {
            Name = Drg.LanceBarrage,
            Kind = ActionKind.Gcd,
            Potency = 130,
            ComboPotency = 340,
            ComboFrom = Drg.RaidenThrust,
        },

        [Drg.HeavensThrust] = new ActionDef
        {
            Name = Drg.HeavensThrust,
            Kind = ActionKind.Gcd,
            Potency = 160,
            ComboPotency = 460,
            ComboFrom = Drg.LanceBarrage,
        },

        [Drg.FangAndClaw] = new ActionDef
        {
            Name = Drg.FangAndClaw,
            PositionalBonus = 40,            Kind = ActionKind.Gcd,
            Potency = 180,
            ComboPotency = 340,
            ComboFrom = Drg.HeavensThrust,
        },

        [Drg.SpiralBlow] = new ActionDef
        {
            Name = Drg.SpiralBlow,
            Kind = ActionKind.Gcd,
            Potency = 140,
            ComboPotency = 300,
            ComboFrom = Drg.RaidenThrust,
        },

        [Drg.ChaoticSpring] = new ActionDef
        {
            Name = Drg.ChaoticSpring,
            PositionalBonus = 40,            Kind = ActionKind.Gcd,
            Potency = 180,
            ComboPotency = 340,
            ComboFrom = Drg.SpiralBlow,
        },

        [Drg.WheelingThrust] = new ActionDef
        {
            Name = Drg.WheelingThrust,
            PositionalBonus = 40,            Kind = ActionKind.Gcd,
            Potency = 180,
            ComboPotency = 340,
            ComboFrom = Drg.ChaoticSpring,
        },

        [Drg.Drakesbane] = new ActionDef
        {
            Name = Drg.Drakesbane,
            Kind = ActionKind.Gcd,
            Potency = 460,
            PreservesCombo = false,
        },

        [Drg.DoomSpike] = new ActionDef
        {
            Name = Drg.DoomSpike,
            Kind = ActionKind.Gcd,
            Potency = 110,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Drg.DraconianFury] = new ActionDef
        {
            Name = Drg.DraconianFury,
            Kind = ActionKind.Gcd,
            Potency = 130,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Drg.SonicThrust] = new ActionDef
        {
            Name = Drg.SonicThrust,
            Kind = ActionKind.Gcd,
            Potency = 100,
            ComboPotency = 120,
            ComboFrom = Drg.DraconianFury,
            MaxTargets = ActionDef.AllNearby,
        },

        [Drg.CoerthanTorment] = new ActionDef
        {
            Name = Drg.CoerthanTorment,
            Kind = ActionKind.Gcd,
            Potency = 100,
            ComboPotency = 150,
            ComboFrom = Drg.SonicThrust,
            MaxTargets = ActionDef.AllNearby,
        },

        [Drg.Nastrond] = new ActionDef
        {
            Name = Drg.Nastrond,
            Kind = ActionKind.OffGcd,
            Potency = 720,

            Cooldown = 2.0,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DragoonFalloff,
        },

        [Drg.HighJump] = new ActionDef
        {
            Name = Drg.HighJump,
            Kind = ActionKind.OffGcd,
            Potency = 400,
            Cooldown = 30.0,
        },

        [Drg.MirageDive] = new ActionDef
        {
            Name = Drg.MirageDive,
            Kind = ActionKind.OffGcd,
            Potency = 380,
            Cooldown = 1.0,
        },

        [Drg.DragonfireDive] = new ActionDef
        {
            Name = Drg.DragonfireDive,
            Kind = ActionKind.OffGcd,
            Potency = 500,
            Cooldown = 120.0,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DragoonFalloff,
        },

        [Drg.Geirskogul] = new ActionDef
        {
            Name = Drg.Geirskogul,
            Kind = ActionKind.OffGcd,
            Potency = 280,
            Cooldown = 60.0,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DragoonFalloff,
        },

        [Drg.Stardiver] = new ActionDef
        {
            Name = Drg.Stardiver,
            Kind = ActionKind.OffGcd,
            Potency = 840,
            Cooldown = 30.0,
            MaxTargets = ActionDef.AllNearby,
            Falloff = StarcrossFalloff,
        },

        [Drg.Starcross] = new ActionDef
        {
            Name = Drg.Starcross,
            Kind = ActionKind.OffGcd,
            Potency = 1000,
            Cooldown = 1.0,
            MaxTargets = ActionDef.AllNearby,
            Falloff = StarcrossFalloff,
        },

        [Drg.RiseOfTheDragon] = new ActionDef
        {
            Name = Drg.RiseOfTheDragon,
            Kind = ActionKind.OffGcd,
            Potency = 550,
            Cooldown = 1.0,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DragoonFalloff,
        },

        [Drg.WyrmwindThrust] = new ActionDef
        {
            Name = Drg.WyrmwindThrust,
            Kind = ActionKind.OffGcd,
            Potency = 440,
            Cooldown = 10.0,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DragoonFalloff,
        },

        [Drg.LifeSurgeAction] = new ActionDef
        {
            Name = Drg.LifeSurgeAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 40.0,
            MaxCharges = 2,
        },

        [Drg.LanceChargeAction] = new ActionDef
        {
            Name = Drg.LanceChargeAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 60.0,
        },

        [Drg.BattleLitanyAction] = new ActionDef
        {
            Name = Drg.BattleLitanyAction,
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
