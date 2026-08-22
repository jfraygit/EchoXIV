using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.DarkKnight;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Drk
{
    public const string Darkside = "Darkside";
    public const string DeliriumStatus = "Delirium";
    public const string BloodWeapon = "Blood Weapon";
    public const string Scorn = "Scorn";
    public const string SaltedEarthActive = "Salted Earth";

    public const string Unleash = "Unleash";
    public const string StalwartSoul = "Stalwart Soul";
    public const string Quietus = "Quietus";

    /// Quietus under Delirium, the way Scarlet Delirium is Bloodspiller under Delirium.
    public const string Impalement = "Impalement";
    public const string FloodOfShadow = "Flood of Shadow";

    public const string Unmend = "Unmend";
    public const string HardSlash = "Hard Slash";
    public const string SyphonStrike = "Syphon Strike";
    public const string Souleater = "Souleater";
    public const string Bloodspiller = "Bloodspiller";
    public const string ScarletDelirium = "Scarlet Delirium";
    public const string Comeuppance = "Comeuppance";
    public const string Torcleaver = "Torcleaver";
    public const string Disesteem = "Disesteem";

    public const string EdgeOfShadow = "Edge of Shadow";
    public const string DeliriumAction = "Delirium";
    public const string CarveAndSpit = "Carve and Spit";

    /// Carve and Spit's area twin.
    public const string AbyssalDrain = "Abyssal Drain";
    public const string Shadowbringer = "Shadowbringer";
    public const string LivingShadow = "Living Shadow";
    public const string SaltedEarth = "Salted Earth";
    public const string SaltedEarthDot = "Salted Earth (DOT)";
    public const string SaltAndDarkness = "Salt and Darkness";

    public const string SimulacrumAbyssalDrain = "Living Shadow: Abyssal Drain";
    public const string SimulacrumShadowbringer = "Living Shadow: Shadowbringer";
    public const string SimulacrumEdgeOfShadow = "Living Shadow: Edge of Shadow";
    public const string SimulacrumBloodspiller = "Living Shadow: Bloodspiller";
    public const string SimulacrumDisesteem = "Living Shadow: Disesteem";
}

/// Dark Knight's action table, taken from the game's own sheets via tools/gamedata.
public static class DarkKnightData
{
    /// The falloff on Shadowbringer and Disesteem: 25% off each enemy after the first.
    public const double DarkKnightFalloff = 0.25;


    public const int MaxMp = 10_000;

    /// Edge of Shadow's cost, and the only thing this rotation spends MP on.
    public const int EdgeOfShadowCost = 3000;

    /// Syphon Strike's combo bonus, and the largest single source across a fight.
    public const int SyphonStrikeMp = 600;

    /// Stalwart Soul's, which is the SAME 600 - and it was missing entirely.
    public const int StalwartSoulMp = 600;

    /// Carve and Spit's, on its sixty-second recast.
    public const int CarveAndSpitMp = 600;

    /// Blood Weapon's, per stack.
    public const int BloodWeaponMp = 600;

    /// Comeuppance and Torcleaver each restore this.
    public const int DeliriumComboMp = 200;

    /// Passive MP regeneration, in MP per second.
    public const double MpPerSecond = 200.0 / 3.0;


    public const int MaxBlood = 100;

    /// Bloodspiller's cost.
    public const int BloodspillerCost = 50;

    /// Souleater's combo bonus.
    public const int SouleaterBlood = 20;

    /// Blood Weapon's, per stack - three per Delirium.
    public const int BloodWeaponBlood = 10;


    /// Ten percent, stated in plain text on Edge of Shadow's own row: "Grants Darkside, increasing damage
    /// dealt by 10%." Confirmed off the parse the same way Warrior's Surging Tempest was - Dark Knight's
    /// damage per point of potency measures 70.3, and 70.3 / 1.10 is 63.9 against Paladin's unbuffed 64.9.
    public const double DarksideMulti = 1.10;

    public const double DarksideDuration = 30.0;

    public const double DarksideMax = 60.0;


    /// Three free Scarlet Delirium casts, and three Blood Weapon stacks alongside them.
    public const int DeliriumStacks = 3;

    public const double DeliriumDuration = 15.0;


    /// 50 a tick for fifteen seconds - five ticks, emitted as a lump.
    public const int SaltedEarthTickPotency = 50;

    public const int SaltedEarthTicks = 5;

    public const double SaltedEarthDuration = 15.0;


    /// What the simulacrum does, in the order the reference records it.
    public static readonly (string Source, int Potency)[] SimulacrumAttacks =
    [
        (Drk.SimulacrumAbyssalDrain, 420),
        (Drk.SimulacrumShadowbringer, 570),
        (Drk.SimulacrumEdgeOfShadow, 420),
        (Drk.SimulacrumBloodspiller, 420),
        (Drk.SimulacrumDisesteem, 620),
    ];

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {

        [Drk.Unleash] = new ActionDef
        {
            Name = Drk.Unleash,
            Kind = ActionKind.Gcd,
            Potency = 120,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Drk.StalwartSoul] = new ActionDef
        {
            Name = Drk.StalwartSoul,
            Kind = ActionKind.Gcd,
            Potency = 120,
            ComboPotency = 160,
            ComboFrom = Drk.Unleash,
            MaxTargets = ActionDef.AllNearby,
        },

        [Drk.Quietus] = new ActionDef
        {
            Name = Drk.Quietus,
            Kind = ActionKind.Gcd,
            Potency = 240,
            MaxTargets = ActionDef.AllNearby,
        },

        [Drk.Impalement] = new ActionDef
        {
            Name = Drk.Impalement,
            Kind = ActionKind.Gcd,
            Potency = 300,
            MaxTargets = ActionDef.AllNearby,
        },

        [Drk.FloodOfShadow] = new ActionDef
        {
            Name = Drk.FloodOfShadow,
            Kind = ActionKind.OffGcd,
            Potency = 160,
            MaxTargets = ActionDef.AllNearby,
        },

        [Drk.Unmend] = new ActionDef
        {
            Name = Drk.Unmend,
            Kind = ActionKind.Gcd,
            Potency = 150,
        },

        [Drk.HardSlash] = new ActionDef
        {
            Name = Drk.HardSlash,
            Kind = ActionKind.Gcd,
            Potency = 300,
            IsComboStarter = true,
        },

        [Drk.SyphonStrike] = new ActionDef
        {
            Name = Drk.SyphonStrike,
            Kind = ActionKind.Gcd,
            Potency = 240,
            ComboPotency = 380,
            ComboFrom = Drk.HardSlash,
        },

        [Drk.Souleater] = new ActionDef
        {
            Name = Drk.Souleater,
            Kind = ActionKind.Gcd,
            Potency = 260,
            ComboPotency = 480,
            ComboFrom = Drk.SyphonStrike,
        },


        [Drk.Bloodspiller] = new ActionDef
        {
            Name = Drk.Bloodspiller,
            Kind = ActionKind.Gcd,
            Potency = 600,
        },

        [Drk.ScarletDelirium] = new ActionDef
        {
            Name = Drk.ScarletDelirium,
            Kind = ActionKind.Gcd,
            Potency = 620,
        },

        [Drk.Comeuppance] = new ActionDef
        {
            Name = Drk.Comeuppance,
            Kind = ActionKind.Gcd,
            Potency = 720,
            ComboFrom = Drk.ScarletDelirium,
            ComboPotency = 720,
        },

        [Drk.Torcleaver] = new ActionDef
        {
            Name = Drk.Torcleaver,
            Kind = ActionKind.Gcd,
            Potency = 820,
            ComboFrom = Drk.Comeuppance,
            ComboPotency = 820,
        },

        [Drk.Disesteem] = new ActionDef
        {
            Name = Drk.Disesteem,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DarkKnightFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1000,
        },


        [Drk.EdgeOfShadow] = new ActionDef
        {
            Name = Drk.EdgeOfShadow,
            Kind = ActionKind.OffGcd,
            Potency = 460,
            Cooldown = 1.0,
        },

        [Drk.DeliriumAction] = new ActionDef
        {
            Name = Drk.DeliriumAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 60.0,
        },

        [Drk.CarveAndSpit] = new ActionDef
        {
            Name = Drk.CarveAndSpit,
            Kind = ActionKind.OffGcd,
            Potency = 540,
            Cooldown = 60.0,
        },

        [Drk.AbyssalDrain] = new ActionDef
        {
            Name = Drk.AbyssalDrain,
            Kind = ActionKind.OffGcd,
            Potency = 240,
            SharesCooldownWith = [Drk.CarveAndSpit],
            MaxTargets = ActionDef.AllNearby,
        },

        [Drk.Shadowbringer] = new ActionDef
        {
            Name = Drk.Shadowbringer,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DarkKnightFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 600,
            Cooldown = 60.0,
            MaxCharges = 2,
        },

        [Drk.LivingShadow] = new ActionDef
        {
            Name = Drk.LivingShadow,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 120.0,
        },

        [Drk.SaltedEarth] = new ActionDef
        {
            Name = Drk.SaltedEarth,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 90.0,
        },

        [Drk.SaltAndDarkness] = new ActionDef
        {
            Name = Drk.SaltAndDarkness,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DarkKnightFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 500,
            Cooldown = 20.0,
        },

        [Buffs.PotionAction] = new ActionDef
        {
            Name = Buffs.PotionAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 270.0,
        },
    };
}
