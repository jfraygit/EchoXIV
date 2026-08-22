using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Gunbreaker;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Gnb
{
    public const string NoMercyStatus = "No Mercy";

    /// Bloodfest's own effect - what raises the gauge cap to six for thirty seconds.
    public const string BloodfestStatus = "Bloodfest";

    public const string ReadyToBreak = "Ready to Break";
    public const string ReadyToRip = "Ready to Rip";
    public const string ReadyToTear = "Ready to Tear";
    public const string ReadyToGouge = "Ready to Gouge";
    public const string ReadyToBlast = "Ready to Blast";
    public const string ReadyToRaze = "Ready to Raze";
    public const string ReadyToReign = "Ready to Reign";

    public const string DemonSlice = "Demon Slice";
    public const string DemonSlaughter = "Demon Slaughter";
    public const string FatedCircle = "Fated Circle";

    public const string LightningShot = "Lightning Shot";
    public const string KeenEdge = "Keen Edge";
    public const string BrutalShell = "Brutal Shell";
    public const string SolidBarrel = "Solid Barrel";
    public const string BurstStrike = "Burst Strike";
    public const string GnashingFang = "Gnashing Fang";
    public const string SavageClaw = "Savage Claw";
    public const string WickedTalon = "Wicked Talon";
    public const string SonicBreak = "Sonic Break";
    public const string SonicBreakDot = "Sonic Break (DOT)";
    public const string DoubleDown = "Double Down";
    public const string ReignOfBeasts = "Reign of Beasts";
    public const string NobleBlood = "Noble Blood";
    public const string LionHeart = "Lion Heart";

    public const string NoMercyAction = "No Mercy";
    public const string BloodfestAction = "Bloodfest";
    public const string BlastingZone = "Blasting Zone";
    public const string BowShock = "Bow Shock";
    public const string BowShockDot = "Bow Shock (DOT)";

    public const string JugularRip = "Jugular Rip";
    public const string AbdomenTear = "Abdomen Tear";
    public const string EyeGouge = "Eye Gouge";
    public const string Hypervelocity = "Hypervelocity";
    public const string FatedBrand = "Fated Brand";
}

/// Gunbreaker's action table, taken from the game's own sheets via tools/gamedata.
public static class GunbreakerData
{
    /// The falloff on the Reign chain: 60% off each enemy after the first.
    public const double GunbreakerFalloff = 0.60;

    /// Double Down loses only 15% - the most generous falloff figure anywhere in this project.
    public const double DoubleDownFalloff = 0.15;


    /// Three, from Cartridge Charge II at level 88.
    public const int MaxCartridges = 3;

    /// SIX WHILE BLOODFEST IS UP, and missing this is worth about two Burst Strikes a fight.
    public const int MaxCartridgesUnderBloodfest = 6;

    /// Bloodfest stores three outright, and its effect runs thirty seconds.
    public const int BloodfestCartridges = 3;

    public const double BloodfestDuration = 30.0;

    public const int BurstStrikeCost = 1;

    public const int GnashingFangCost = 1;

    public const int DoubleDownCost = 2;


    /// Twenty percent for twenty seconds, once a minute.
    public const double NoMercyMulti = 1.20;

    public const double NoMercyDuration = 20.0;

    /// How long the Ready-to-something grants last.
    public const double ContinuationWindow = 10.0;

    public const double ReadyToBreakDuration = 30.0;

    public const double ReadyToReignDuration = 30.0;


    /// 120 a tick for fifteen seconds - five ticks, emitted as a lump.
    public const int SonicBreakTickPotency = 120;

    public const int SonicBreakTicks = 5;

    /// 60 a tick for fifteen seconds.
    public const int BowShockTickPotency = 60;

    public const int BowShockTicks = 5;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {

        [Gnb.DemonSlice] = new ActionDef
        {
            Name = Gnb.DemonSlice,
            Kind = ActionKind.Gcd,
            Potency = 100,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Gnb.DemonSlaughter] = new ActionDef
        {
            Name = Gnb.DemonSlaughter,
            Kind = ActionKind.Gcd,
            Potency = 100,
            ComboPotency = 160,
            ComboFrom = Gnb.DemonSlice,
            MaxTargets = ActionDef.AllNearby,
        },

        [Gnb.FatedCircle] = new ActionDef
        {
            Name = Gnb.FatedCircle,
            Kind = ActionKind.Gcd,
            Potency = 300,
            MaxTargets = ActionDef.AllNearby,
        },

        [Gnb.LightningShot] = new ActionDef
        {
            Name = Gnb.LightningShot,
            Kind = ActionKind.Gcd,
            Potency = 150,
        },

        [Gnb.KeenEdge] = new ActionDef
        {
            Name = Gnb.KeenEdge,
            Kind = ActionKind.Gcd,
            Potency = 300,
            IsComboStarter = true,
        },

        [Gnb.BrutalShell] = new ActionDef
        {
            Name = Gnb.BrutalShell,
            Kind = ActionKind.Gcd,
            Potency = 240,
            ComboPotency = 380,
            ComboFrom = Gnb.KeenEdge,
        },

        [Gnb.SolidBarrel] = new ActionDef
        {
            Name = Gnb.SolidBarrel,
            Kind = ActionKind.Gcd,
            Potency = 240,
            ComboPotency = 460,
            ComboFrom = Gnb.BrutalShell,
        },


        [Gnb.BurstStrike] = new ActionDef
        {
            Name = Gnb.BurstStrike,
            Kind = ActionKind.Gcd,
            Potency = 420,
        },

        [Gnb.GnashingFang] = new ActionDef
        {
            Name = Gnb.GnashingFang,
            Kind = ActionKind.Gcd,
            Potency = 440,
            Cooldown = 30.0,
            MaxCharges = 2,
        },

        [Gnb.SavageClaw] = new ActionDef
        {
            Name = Gnb.SavageClaw,
            Kind = ActionKind.Gcd,
            Potency = 500,
            ComboFrom = Gnb.GnashingFang,
            ComboPotency = 500,
        },

        [Gnb.WickedTalon] = new ActionDef
        {
            Name = Gnb.WickedTalon,
            Kind = ActionKind.Gcd,
            Potency = 560,
            ComboFrom = Gnb.SavageClaw,
            ComboPotency = 560,
        },

        [Gnb.DoubleDown] = new ActionDef
        {
            Name = Gnb.DoubleDown,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DoubleDownFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1000,
            Cooldown = 60.0,
        },


        [Gnb.SonicBreak] = new ActionDef
        {
            Name = Gnb.SonicBreak,
            Kind = ActionKind.Gcd,
            Potency = 340,
        },

        [Gnb.ReignOfBeasts] = new ActionDef
        {
            Name = Gnb.ReignOfBeasts,
            MaxTargets = ActionDef.AllNearby,
            Falloff = GunbreakerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 800,
        },

        [Gnb.NobleBlood] = new ActionDef
        {
            Name = Gnb.NobleBlood,
            MaxTargets = ActionDef.AllNearby,
            Falloff = GunbreakerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 900,
            ComboFrom = Gnb.ReignOfBeasts,
            ComboPotency = 900,
        },

        [Gnb.LionHeart] = new ActionDef
        {
            Name = Gnb.LionHeart,
            MaxTargets = ActionDef.AllNearby,
            Falloff = GunbreakerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1000,
            ComboFrom = Gnb.NobleBlood,
            ComboPotency = 1000,
        },


        [Gnb.NoMercyAction] = new ActionDef
        {
            Name = Gnb.NoMercyAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 60.0,
        },

        [Gnb.BloodfestAction] = new ActionDef
        {
            Name = Gnb.BloodfestAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 60.0,
        },

        [Gnb.BlastingZone] = new ActionDef
        {
            Name = Gnb.BlastingZone,
            Kind = ActionKind.OffGcd,
            Potency = 800,
            Cooldown = 30.0,
        },

        [Gnb.BowShock] = new ActionDef
        {
            Name = Gnb.BowShock,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.OffGcd,
            Potency = 150,
            Cooldown = 60.0,
        },


        [Gnb.JugularRip] = new ActionDef
        {
            Name = Gnb.JugularRip,
            Kind = ActionKind.OffGcd,
            Potency = 220,
        },

        [Gnb.AbdomenTear] = new ActionDef
        {
            Name = Gnb.AbdomenTear,
            Kind = ActionKind.OffGcd,
            Potency = 260,
        },

        [Gnb.EyeGouge] = new ActionDef
        {
            Name = Gnb.EyeGouge,
            Kind = ActionKind.OffGcd,
            Potency = 300,
        },

        [Gnb.Hypervelocity] = new ActionDef
        {
            Name = Gnb.Hypervelocity,
            Kind = ActionKind.OffGcd,
            Potency = 180,
        },

        [Gnb.FatedBrand] = new ActionDef
        {
            Name = Gnb.FatedBrand,
            Kind = ActionKind.OffGcd,
            Potency = 120,
            MaxTargets = ActionDef.AllNearby,
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
