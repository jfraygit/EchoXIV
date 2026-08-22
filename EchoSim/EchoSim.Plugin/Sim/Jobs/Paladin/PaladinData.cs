using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Paladin;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Pld
{
    public const string FightOrFlight = "Fight or Flight";
    public const string RequiescatStatus = "Requiescat";
    public const string ConfiteorReady = "Confiteor Ready";
    public const string DivineMight = "Divine Might";
    public const string GoringBladeReady = "Goring Blade Ready";
    public const string AtonementReady = "Atonement Ready";
    public const string SupplicationReady = "Supplication Ready";
    public const string SepulchreReady = "Sepulchre Ready";
    public const string BladeOfHonorReady = "Blade of Honor Ready";

    public const string TotalEclipse = "Total Eclipse";
    public const string Prominence = "Prominence";
    public const string HolyCircle = "Holy Circle";

    public const string FastBlade = "Fast Blade";
    public const string RiotBlade = "Riot Blade";
    public const string RoyalAuthority = "Royal Authority";
    public const string GoringBlade = "Goring Blade";
    public const string Atonement = "Atonement";
    public const string Supplication = "Supplication";
    public const string Sepulchre = "Sepulchre";
    public const string HolySpirit = "Holy Spirit";
    public const string Confiteor = "Confiteor";
    public const string BladeOfFaith = "Blade of Faith";
    public const string BladeOfTruth = "Blade of Truth";
    public const string BladeOfValor = "Blade of Valor";

    public const string FightOrFlightAction = "Fight or Flight";
    public const string Imperator = "Imperator";
    public const string BladeOfHonor = "Blade of Honor";
    public const string CircleOfScorn = "Circle of Scorn";
    public const string CircleOfScornDot = "Circle of Scorn (DOT)";
    public const string Expiacion = "Expiacion";
    public const string Intervene = "Intervene";
}

/// Paladin's action table, taken from the game's own sheets via tools/gamedata.
public static class PaladinData
{
    /// The falloff on Paladin's cleaving weaponskills: 60% off each enemy after the first.
    public const double PaladinFalloff = 0.60;


    /// Twenty-five percent for twenty seconds, once a minute.
    public const double FightOrFlightMulti = 1.25;

    public const double FightOrFlightDuration = 20.0;


    /// Four stacks, thirty seconds, and exactly enough for the Confiteor chain.
    public const int RequiescatStacks = 4;

    public const double RequiescatDuration = 30.0;

    /// How long Divine Might, Confiteor Ready and the Atonement chain's grants last.
    public const double GrantDuration = 30.0;


    /// 30 a tick for fifteen seconds - five ticks, emitted as a lump like every other DoT here.
    public const int CircleOfScornTickPotency = 30;

    public const int CircleOfScornTicks = 5;


    /// 500 under Divine Might, against 400 plain - measured off the reference, not read off a sheet.
    public const int HolySpiritDivineMight = 500;

    /// 250, and this one IS read off the sheet: "Divine Might Potency: 250 Requiescat Potency: 350".
    public const int HolyCircleDivineMight = 250;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {

        [Pld.TotalEclipse] = new ActionDef
        {
            Name = Pld.TotalEclipse,
            Kind = ActionKind.Gcd,
            Potency = 120,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Pld.Prominence] = new ActionDef
        {
            Name = Pld.Prominence,
            Kind = ActionKind.Gcd,
            Potency = 100,
            ComboPotency = 220,
            ComboFrom = Pld.TotalEclipse,
            MaxTargets = ActionDef.AllNearby,
        },

        [Pld.HolyCircle] = new ActionDef
        {
            Name = Pld.HolyCircle,
            Kind = ActionKind.Gcd,
            Potency = 100,
            MaxTargets = ActionDef.AllNearby,
        },

        [Pld.FastBlade] = new ActionDef
        {
            Name = Pld.FastBlade,
            Kind = ActionKind.Gcd,
            Potency = 220,
            IsComboStarter = true,
        },

        [Pld.RiotBlade] = new ActionDef
        {
            Name = Pld.RiotBlade,
            Kind = ActionKind.Gcd,
            Potency = 170,
            ComboPotency = 330,
            ComboFrom = Pld.FastBlade,
        },

        [Pld.RoyalAuthority] = new ActionDef
        {
            Name = Pld.RoyalAuthority,
            Kind = ActionKind.Gcd,
            Potency = 200,
            ComboPotency = 460,
            ComboFrom = Pld.RiotBlade,
        },


        [Pld.Atonement] = new ActionDef
        {
            Name = Pld.Atonement,
            Kind = ActionKind.Gcd,
            Potency = 460,
        },

        [Pld.Supplication] = new ActionDef
        {
            Name = Pld.Supplication,
            Kind = ActionKind.Gcd,
            Potency = 500,
        },

        [Pld.Sepulchre] = new ActionDef
        {
            Name = Pld.Sepulchre,
            Kind = ActionKind.Gcd,
            Potency = 540,
        },


        [Pld.HolySpirit] = new ActionDef
        {
            Name = Pld.HolySpirit,
            Kind = ActionKind.Gcd,
            Potency = 400,
            CastTime = 1.5,
        },

        [Pld.GoringBlade] = new ActionDef
        {
            Name = Pld.GoringBlade,
            Kind = ActionKind.Gcd,
            Potency = 700,
        },


        [Pld.Confiteor] = new ActionDef
        {
            Name = Pld.Confiteor,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PaladinFalloff,
            Kind = ActionKind.Gcd,
            Potency = 500,
            ComboPotency = 920,
        },

        [Pld.BladeOfFaith] = new ActionDef
        {
            Name = Pld.BladeOfFaith,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PaladinFalloff,
            Kind = ActionKind.Gcd,
            Potency = 260,
            ComboPotency = 760,
            ComboFrom = Pld.Confiteor,
        },

        [Pld.BladeOfTruth] = new ActionDef
        {
            Name = Pld.BladeOfTruth,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PaladinFalloff,
            Kind = ActionKind.Gcd,
            Potency = 380,
            ComboPotency = 880,
            ComboFrom = Pld.BladeOfFaith,
        },

        [Pld.BladeOfValor] = new ActionDef
        {
            Name = Pld.BladeOfValor,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PaladinFalloff,
            Kind = ActionKind.Gcd,
            Potency = 500,
            ComboPotency = 920,
            ComboFrom = Pld.BladeOfTruth,
        },


        [Pld.FightOrFlightAction] = new ActionDef
        {
            Name = Pld.FightOrFlightAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 60.0,
        },

        [Pld.Imperator] = new ActionDef
        {
            Name = Pld.Imperator,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PaladinFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 580,
            Cooldown = 60.0,
        },

        [Pld.BladeOfHonor] = new ActionDef
        {
            Name = Pld.BladeOfHonor,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PaladinFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 1000,
        },

        [Pld.CircleOfScorn] = new ActionDef
        {
            Name = Pld.CircleOfScorn,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.OffGcd,
            Potency = 140,
            Cooldown = 30.0,
        },

        [Pld.Expiacion] = new ActionDef
        {
            Name = Pld.Expiacion,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PaladinFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 450,
            Cooldown = 30.0,
        },

        [Pld.Intervene] = new ActionDef
        {
            Name = Pld.Intervene,
            Kind = ActionKind.OffGcd,
            Potency = 150,
            Cooldown = 30.0,
            MaxCharges = 2,
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
