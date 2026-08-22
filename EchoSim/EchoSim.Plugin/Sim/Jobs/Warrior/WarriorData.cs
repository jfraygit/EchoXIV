using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Warrior;

/// Status and action names, kept in one place so typos surface at compile time.
public static class War
{
    public const string SurgingTempest = "Surging Tempest";
    public const string InnerReleaseStatus = "Inner Release";
    public const string NascentChaos = "Nascent Chaos";
    public const string BurgeoningFury = "Burgeoning Fury";
    public const string PrimalRendReady = "Primal Rend Ready";
    public const string PrimalRuinationReady = "Primal Ruination Ready";

    public const string Overpower = "Overpower";
    public const string MythrilTempest = "Mythril Tempest";
    public const string Decimate = "Decimate";
    public const string ChaoticCyclone = "Chaotic Cyclone";

    public const string Tomahawk = "Tomahawk";
    public const string HeavySwing = "Heavy Swing";
    public const string Maim = "Maim";
    public const string StormsPath = "Storm's Path";
    public const string StormsEye = "Storm's Eye";
    public const string FellCleave = "Fell Cleave";
    public const string InnerChaos = "Inner Chaos";
    public const string PrimalRend = "Primal Rend";
    public const string PrimalRuination = "Primal Ruination";

    public const string InnerReleaseAction = "Inner Release";
    public const string PrimalWrath = "Primal Wrath";
    public const string InfuriateAction = "Infuriate";
    public const string Upheaval = "Upheaval";

    /// Upheaval's area twin.
    public const string Orogeny = "Orogeny";
    public const string Onslaught = "Onslaught";
}

/// Warrior's action table, taken from the game's own sheets via tools/gamedata.
public static class WarriorData
{
    /// The falloff on Warrior's three Primal weaponskills: 50% off each enemy after the first.
    public const double WarriorFalloff = 0.50;


    public const int MaxBeastGauge = 100;

    /// Fell Cleave and Inner Chaos both cost fifty, which is what makes the gauge worth two.
    public const int FellCleaveCost = 50;

    /// Infuriate's grant, and the only number in this block the game states outright.
    public const int InfuriateGauge = 50;

    /// What the combo pays: ten for Maim, twenty for Storm's Path, ten for Storm's Eye.
    public const int MaimGauge = 10;

    public const int StormsPathGauge = 20;

    public const int StormsEyeGauge = 10;

    /// TWENTY, and the area combo is therefore gauge-equivalent to the single-target one per global - 20
    /// across two steps against 30 across three.
    public const int MythrilTempestGauge = 20;


    /// Ten percent, and this one IS in the game data - Mythril Tempest and Storm's Eye both say "Grants
    /// Surging Tempest, increasing damage dealt by 10%" in plain text.
    public const double SurgingTempestMulti = 1.10;

    public const double SurgingTempestDuration = 30.0;

    /// The ceiling refreshing runs into.
    public const double SurgingTempestMax = 60.0;

    /// Inner Release tops it up by ten seconds, which is worth about one extra Storm's Eye a fight.
    public const double InnerReleaseTempestExtension = 10.0;


    /// Three free Fell Cleaves, each a guaranteed critical direct hit.
    public const int InnerReleaseStacks = 3;

    public const double InnerReleaseDuration = 15.0;

    /// Three Burgeoning Fury make the Warrior Wrathful, which is what Primal Wrath needs.
    public const int WrathfulStacks = 3;


    /// Five seconds off Infuriate for every Fell Cleave AND every Inner Chaos.
    public const double InfuriateReduction = 5.0;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {

        [War.Overpower] = new ActionDef
        {
            Name = War.Overpower,
            Kind = ActionKind.Gcd,
            Potency = 110,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [War.MythrilTempest] = new ActionDef
        {
            Name = War.MythrilTempest,
            Kind = ActionKind.Gcd,
            Potency = 100,
            ComboPotency = 140,
            ComboFrom = War.Overpower,
            MaxTargets = ActionDef.AllNearby,
        },

        [War.Decimate] = new ActionDef
        {
            Name = War.Decimate,
            Kind = ActionKind.Gcd,
            Potency = 180,
            MaxTargets = ActionDef.AllNearby,
        },

        [War.ChaoticCyclone] = new ActionDef
        {
            Name = War.ChaoticCyclone,
            Kind = ActionKind.Gcd,
            Potency = 200,
            MaxTargets = ActionDef.AllNearby,
        },

        [War.Tomahawk] = new ActionDef
        {
            Name = War.Tomahawk,
            Kind = ActionKind.Gcd,
            Potency = 150,
        },

        [War.HeavySwing] = new ActionDef
        {
            Name = War.HeavySwing,
            Kind = ActionKind.Gcd,
            Potency = 240,
            IsComboStarter = true,
        },

        [War.Maim] = new ActionDef
        {
            Name = War.Maim,
            Kind = ActionKind.Gcd,
            Potency = 190,
            ComboPotency = 340,
            ComboFrom = War.HeavySwing,
        },

        [War.StormsPath] = new ActionDef
        {
            Name = War.StormsPath,
            Kind = ActionKind.Gcd,
            Potency = 220,
            ComboPotency = 500,
            ComboFrom = War.Maim,
        },

        [War.StormsEye] = new ActionDef
        {
            Name = War.StormsEye,
            Kind = ActionKind.Gcd,
            Potency = 220,
            ComboPotency = 500,
            ComboFrom = War.Maim,
        },


        [War.FellCleave] = new ActionDef
        {
            Name = War.FellCleave,
            Kind = ActionKind.Gcd,
            Potency = 580,
        },

        [War.InnerChaos] = new ActionDef
        {
            Name = War.InnerChaos,
            Kind = ActionKind.Gcd,
            Potency = 700,
        },


        [War.PrimalRend] = new ActionDef
        {
            Name = War.PrimalRend,
            MaxTargets = ActionDef.AllNearby,
            Falloff = WarriorFalloff,
            Kind = ActionKind.Gcd,
            Potency = 720,
        },

        [War.PrimalRuination] = new ActionDef
        {
            Name = War.PrimalRuination,
            MaxTargets = ActionDef.AllNearby,
            Falloff = WarriorFalloff,
            Kind = ActionKind.Gcd,
            Potency = 800,
        },


        [War.InnerReleaseAction] = new ActionDef
        {
            Name = War.InnerReleaseAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 60.0,
        },

        [War.PrimalWrath] = new ActionDef
        {
            Name = War.PrimalWrath,
            MaxTargets = ActionDef.AllNearby,
            Falloff = WarriorFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 700,
        },

        [War.InfuriateAction] = new ActionDef
        {
            Name = War.InfuriateAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 60.0,
            MaxCharges = 2,
        },

        [War.Upheaval] = new ActionDef
        {
            Name = War.Upheaval,
            Kind = ActionKind.OffGcd,
            Potency = 420,
            Cooldown = 30.0,
        },

        [War.Orogeny] = new ActionDef
        {
            Name = War.Orogeny,
            Kind = ActionKind.OffGcd,
            Potency = 150,
            SharesCooldownWith = [War.Upheaval],
            MaxTargets = ActionDef.AllNearby,
        },

        [War.Onslaught] = new ActionDef
        {
            Name = War.Onslaught,
            Kind = ActionKind.OffGcd,
            Potency = 150,
            Cooldown = 30.0,
            MaxCharges = 3,
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
