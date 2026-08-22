using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.RedMage;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Rdm
{
    public const string Dualcast = "Dualcast";
    public const string VerfireReady = "Verfire Ready";
    public const string VerstoneReady = "Verstone Ready";
    public const string AccelerationStatus = "Acceleration";
    public const string GrandImpactReady = "Grand Impact Ready";
    public const string MagickedSwordplay = "Magicked Swordplay";
    public const string EmboldenStatus = "Embolden";
    public const string ThornedFlourish = "Thorned Flourish";
    public const string PrefulgenceReady = "Prefulgence Ready";
    public const string SwiftcastStatus = "Swiftcast";

    public const string Jolt3 = "Jolt III";
    public const string Verthunder3 = "Verthunder III";
    public const string Veraero3 = "Veraero III";
    public const string Verfire = "Verfire";
    public const string Verstone = "Verstone";
    public const string GrandImpact = "Grand Impact";
    public const string Impact = "Impact";

    public const string EnchantedRiposte = "Enchanted Riposte";
    public const string EnchantedZwerchhau = "Enchanted Zwerchhau";
    public const string EnchantedRedoublement = "Enchanted Redoublement";
    public const string Verflare = "Verflare";
    public const string Verholy = "Verholy";
    public const string Scorch = "Scorch";
    public const string Resolution = "Resolution";

    public const string Fleche = "Fleche";
    public const string ContreSixte = "Contre Sixte";
    public const string CorpsACorps = "Corps-a-Corps";
    public const string Engagement = "Engagement";
    public const string AccelerationAction = "Acceleration";
    public const string ManaficationAction = "Manafication";
    public const string EmboldenAction = "Embolden";
    public const string ViceOfThorns = "Vice of Thorns";
    public const string Prefulgence = "Prefulgence";
    public const string SwiftcastAction = "Swiftcast";

    public const string Verthunder2 = "Verthunder II";
    public const string Veraero2 = "Veraero II";

    public const string EnchantedMoulinet = "Enchanted Moulinet";
    public const string EnchantedMoulinetDeux = "Enchanted Moulinet Deux";
    public const string EnchantedMoulinetTrois = "Enchanted Moulinet Trois";
}

/// Red Mage's action table.
public static class RedMageData
{
    /// The falloff on Red Mage's cleaving actions: 55% off each enemy after the first.
    public const double RedMageFalloff = 0.55;


    public const int MaxMana = 100;

    /// Black and White mana the melee combo needs before it can start.
    public const int MeleeComboCost = 50;

    /// What Enchanted Riposte spends of each colour.
    public const int RiposteCost = 20;

    /// What Zwerchhau and Redoublement each spend of each colour.
    public const int ContinuationCost = 15;

    /// Mana from a Verthunder III or a Verfire (black) and a Veraero III or Verstone (white).
    public const int ManaPerVerspell = 6;

    /// Jolt III feeds both colours at once, which is what makes it the balancing filler.
    public const int ManaPerJolt = 2;

    /// Grand Impact, likewise, but larger.
    public const int ManaPerGrandImpact = 3;

    /// Seven, not the Verspells' six - the area builders are the one place in the casting half where the
    /// multi-target option is strictly better per cast rather than merely wider.
    public const int ManaPerAreaVerspell = 7;

    /// Acceleration raises Impact from 210 to 260, which is the only conditional potency in the casting half.
    public const int ImpactAccelerationPotency = 260;

    /// Verflare and Verholy hand back a chunk of their own colour.
    public const int ManaPerFinisher = 11;

    /// Scorch and Resolution both feed both colours.
    public const int ManaPerScorch = 4;


    /// Chance a Verthunder III grants Verfire Ready, and a Veraero III grants Verstone Ready.
    public const double ProcChance = 0.50;

    /// Chance a Verspell cast off an ACCELERATION charge procs, which is one.
    public const double AcceleratedProcChance = 1.0;

    /// Chance a Verflare grants Verfire Ready, and a Verholy grants Verstone Ready.
    public const double FinisherProcChance = 0.20;

    public const double ProcDuration = 30.0;
    public const double DualcastDuration = 15.0;
    public const double AccelerationDuration = 20.0;
    public const double GrandImpactDuration = 30.0;
    public const double EmboldenDuration = 20.0;
    public const double ThornedFlourishDuration = 30.0;
    public const double PrefulgenceDuration = 30.0;

    /// Embolden's damage bonus to the Red Mage's own spells.
    public const double EmboldenSelfBonus = 1.05;

    /// Enchanted melee steps Manafication makes free.
    public const int MagickedSwordplayStacks = 3;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Rdm.Jolt3] = new ActionDef
        {
            Name = Rdm.Jolt3,
            Kind = ActionKind.Gcd,
            Potency = 360,
            CastTime = 2.0,
        },

        [Rdm.Verfire] = new ActionDef
        {
            Name = Rdm.Verfire,
            Kind = ActionKind.Gcd,
            Potency = 380,
            CastTime = 2.0,
        },

        [Rdm.Verstone] = new ActionDef
        {
            Name = Rdm.Verstone,
            Kind = ActionKind.Gcd,
            Potency = 380,
            CastTime = 2.0,
        },

        [Rdm.Verthunder2] = new ActionDef
        {
            Name = Rdm.Verthunder2,
            Kind = ActionKind.Gcd,
            Potency = 140,
            CastTime = 2.0,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rdm.Veraero2] = new ActionDef
        {
            Name = Rdm.Veraero2,
            Kind = ActionKind.Gcd,
            Potency = 140,
            CastTime = 2.0,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rdm.Impact] = new ActionDef
        {
            Name = Rdm.Impact,
            Kind = ActionKind.Gcd,
            Potency = 210,
            CastTime = 5.0,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rdm.Verthunder3] = new ActionDef
        {
            Name = Rdm.Verthunder3,
            Kind = ActionKind.Gcd,
            Potency = 440,
            CastTime = 5.0,
        },

        [Rdm.Veraero3] = new ActionDef
        {
            Name = Rdm.Veraero3,
            Kind = ActionKind.Gcd,
            Potency = 440,
            CastTime = 5.0,
        },

        [Rdm.GrandImpact] = new ActionDef
        {
            Name = Rdm.GrandImpact,
            MaxTargets = ActionDef.AllNearby,
            Falloff = RedMageFalloff,
            Kind = ActionKind.Gcd,
            Potency = 600,
        },

        [Rdm.EnchantedMoulinet] = new ActionDef
        {
            Name = Rdm.EnchantedMoulinet,
            Kind = ActionKind.Gcd,
            Potency = 130,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rdm.EnchantedMoulinetDeux] = new ActionDef
        {
            Name = Rdm.EnchantedMoulinetDeux,
            Kind = ActionKind.Gcd,
            Potency = 140,
            ComboFrom = Rdm.EnchantedMoulinet,
            ComboPotency = 140,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rdm.EnchantedMoulinetTrois] = new ActionDef
        {
            Name = Rdm.EnchantedMoulinetTrois,
            Kind = ActionKind.Gcd,
            Potency = 150,
            ComboFrom = Rdm.EnchantedMoulinetDeux,
            ComboPotency = 150,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rdm.EnchantedRiposte] = new ActionDef
        {
            Name = Rdm.EnchantedRiposte,
            Kind = ActionKind.Gcd,
            Potency = 340,
            BaseRecast = 1.5,
        },

        [Rdm.EnchantedZwerchhau] = new ActionDef
        {
            Name = Rdm.EnchantedZwerchhau,
            Kind = ActionKind.Gcd,
            Potency = 380,
            BaseRecast = 1.5,
            ComboFrom = Rdm.EnchantedRiposte,
            ComboPotency = 380,
        },

        [Rdm.EnchantedRedoublement] = new ActionDef
        {
            Name = Rdm.EnchantedRedoublement,
            Kind = ActionKind.Gcd,
            Potency = 560,
            BaseRecast = 2.2,
            ComboFrom = Rdm.EnchantedZwerchhau,
            ComboPotency = 560,
        },

        [Rdm.Verflare] = new ActionDef
        {
            Name = Rdm.Verflare,
            MaxTargets = ActionDef.AllNearby,
            Falloff = RedMageFalloff,
            Kind = ActionKind.Gcd,
            Potency = 650,
        },

        [Rdm.Verholy] = new ActionDef
        {
            Name = Rdm.Verholy,
            MaxTargets = ActionDef.AllNearby,
            Falloff = RedMageFalloff,
            Kind = ActionKind.Gcd,
            Potency = 650,
        },

        [Rdm.Scorch] = new ActionDef
        {
            Name = Rdm.Scorch,
            MaxTargets = ActionDef.AllNearby,
            Falloff = RedMageFalloff,
            Kind = ActionKind.Gcd,
            Potency = 750,
        },

        [Rdm.Resolution] = new ActionDef
        {
            Name = Rdm.Resolution,
            MaxTargets = ActionDef.AllNearby,
            Falloff = RedMageFalloff,
            Kind = ActionKind.Gcd,
            Potency = 850,
        },

        [Rdm.Fleche] = new ActionDef
        {
            Name = Rdm.Fleche,
            Kind = ActionKind.OffGcd,
            Potency = 480,
            Cooldown = 25.0,
        },

        [Rdm.ContreSixte] = new ActionDef
        {
            Name = Rdm.ContreSixte,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.OffGcd,
            Potency = 420,
            Cooldown = 35.0,
        },

        [Rdm.CorpsACorps] = new ActionDef
        {
            Name = Rdm.CorpsACorps,
            Kind = ActionKind.OffGcd,
            Potency = 130,
            Cooldown = 35.0,
            MaxCharges = 2,
        },

        [Rdm.Engagement] = new ActionDef
        {
            Name = Rdm.Engagement,
            Kind = ActionKind.OffGcd,
            Potency = 180,
            Cooldown = 35.0,
            MaxCharges = 2,
        },

        [Rdm.AccelerationAction] = new ActionDef
        {
            Name = Rdm.AccelerationAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 55.0,
            MaxCharges = 2,
        },

        [Rdm.ManaficationAction] = new ActionDef
        {
            Name = Rdm.ManaficationAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 110.0,
        },

        [Rdm.EmboldenAction] = new ActionDef
        {
            Name = Rdm.EmboldenAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Rdm.ViceOfThorns] = new ActionDef
        {
            Name = Rdm.ViceOfThorns,
            MaxTargets = ActionDef.AllNearby,
            Falloff = RedMageFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 950,
        },

        [Rdm.Prefulgence] = new ActionDef
        {
            Name = Rdm.Prefulgence,
            MaxTargets = ActionDef.AllNearby,
            Falloff = RedMageFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 1200,
        },

        [Rdm.SwiftcastAction] = new ActionDef
        {
            Name = Rdm.SwiftcastAction,
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
}
