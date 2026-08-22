using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Machinist;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Mch
{
    public const string Overheated = "Overheated";
    public const string Reassembled = "Reassembled";
    public const string Hypercharged = "Hypercharged";
    public const string FullMetalMachinist = "Full Metal Machinist";
    public const string ExcavatorReady = "Excavator Ready";
    public const string WildfireStatus = "Wildfire";

    public const string HeatedSplitShot = "Heated Split Shot";
    public const string HeatedSlugShot = "Heated Slug Shot";
    public const string HeatedCleanShot = "Heated Clean Shot";

    public const string Drill = "Drill";
    public const string AirAnchor = "Air Anchor";
    public const string ChainSaw = "Chain Saw";
    public const string Excavator = "Excavator";
    public const string FullMetalField = "Full Metal Field";
    public const string BlazingShot = "Blazing Shot";

    public const string DoubleCheck = "Double Check";
    public const string Checkmate = "Checkmate";
    public const string HyperchargeAction = "Hypercharge";
    public const string BarrelStabilizer = "Barrel Stabilizer";
    public const string ReassembleAction = "Reassemble";
    public const string WildfireAction = "Wildfire";
    public const string AutomatonQueen = "Automaton Queen";

    /// Wildfire's delayed payout, emitted under its own name rather than the ability's.
    public const string WildfireDetonation = "Wildfire (detonation)";

    /// The Queen's whole deployment, emitted as one hit.
    public const string QueenDamage = "Automaton Queen (pet)";

    public const string Scattergun = "Scattergun";
    public const string AutoCrossbow = "Auto Crossbow";
    public const string Bioblaster = "Bioblaster";
    public const string BioblasterDot = "Bioblaster (DOT)";
}

/// Machinist's action table.
public static class MachinistData
{
    /// The falloff on Machinist's three big cleaving weaponskills: Chain Saw, Excavator and Full Metal Field
    /// all lose 25% per extra enemy.
    public const double MachinistFalloff = 0.25;

    /// Double Check and Checkmate lose 30% rather than 25%, which is why they carry their own figure.
    public const double CheckFalloff = 0.30;

    /// Bioblaster's damage-over-time, as a total: 50 potency a tick over 15 seconds is 5 ticks.
    public const double BioblasterDotPotency = 50 * 5;


    public const int MaxHeat = 100;
    public const int MaxBattery = 100;

    /// Heat spent by Hypercharge, unless Barrel Stabilizer has made one free.
    public const int HyperchargeHeatCost = 50;

    /// Battery the Queen needs before she can be deployed at all.
    public const int QueenMinimumBattery = 50;

    /// Battery the rotation holds for outside a burst window - ninety, not a hundred.
    public const int QueenHoldBattery = 90;

    /// Heat granted by each step of the combo.
    public const int HeatPerComboStep = 5;

    /// Battery granted by Heated Clean Shot's combo finish.
    public const int BatteryPerCombo = 10;

    /// Battery granted by Air Anchor, Chain Saw and Excavator alike.
    public const int BatteryPerTool = 20;


    /// Overheated stacks per Hypercharge - and so Blazing Shots per Hypercharge.
    public const int OverheatedStacks = 5;

    public const double OverheatedDuration = 10.0;

    /// Overheated's damage bonus, as FLAT POTENCY rather than a percentage.
    public const int OverheatedPotencyBonus = 20;

    /// Weaponskills the Overheated bonus applies to.
    public static readonly HashSet<string> SingleTargetWeaponskills =
    [
        Mch.HeatedSplitShot, Mch.HeatedSlugShot, Mch.HeatedCleanShot,
        Mch.Drill, Mch.AirAnchor, Mch.BlazingShot,
    ];


    /// Potency added to Wildfire for each of the Machinist's own weaponskills that lands.
    public const int WildfirePerWeaponskill = 240;

    public const int WildfireMaxStacks = 6;

    public const double WildfireDuration = 10.0;


    /// The Queen's entire deployment, priced per point of Battery spent.
    public const double QueenPotencyPerBattery = 26.6;

    /// What a pet's listed potency is actually worth - the same 0.92 Ninja's Bunshin uses.
    public const double PetPotencyMultiplier = 0.92;

    public const double QueenDuration = 12.0;


    public const double ReassembleDuration = 5.0;
    public const double BarrelStabilizerDuration = 30.0;
    public const double ExcavatorReadyDuration = 30.0;

    /// Seconds Blazing Shot takes off both Double Check and Checkmate, per cast.
    public const double BlazingShotRecastReduction = 15.0;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Mch.Scattergun] = new ActionDef
        {
            Name = Mch.Scattergun,
            Kind = ActionKind.Gcd,
            Potency = 130,
            MaxTargets = ActionDef.AllNearby,
        },

        [Mch.AutoCrossbow] = new ActionDef
        {
            Name = Mch.AutoCrossbow,
            BaseRecast = 1.5,            Kind = ActionKind.Gcd,
            Potency = 180,
            MaxTargets = ActionDef.AllNearby,
        },

        [Mch.Bioblaster] = new ActionDef
        {
            Name = Mch.Bioblaster,
            Kind = ActionKind.Gcd,
            Potency = 50,
            Cooldown = 20.0,
            MaxCharges = 2,
            SharesCooldownWith = [Mch.Drill],
            MaxTargets = ActionDef.AllNearby,
        },

        [Mch.HeatedSplitShot] = new ActionDef
        {
            Name = Mch.HeatedSplitShot,
            Kind = ActionKind.Gcd,
            Potency = 220,
            IsComboStarter = true,
        },

        [Mch.HeatedSlugShot] = new ActionDef
        {
            Name = Mch.HeatedSlugShot,
            Kind = ActionKind.Gcd,
            Potency = 140,
            ComboPotency = 320,
            ComboFrom = Mch.HeatedSplitShot,
        },

        [Mch.HeatedCleanShot] = new ActionDef
        {
            Name = Mch.HeatedCleanShot,
            Kind = ActionKind.Gcd,
            Potency = 160,
            ComboPotency = 420,
            ComboFrom = Mch.HeatedSlugShot,
        },

        [Mch.Drill] = new ActionDef
        {
            Name = Mch.Drill,
            Kind = ActionKind.Gcd,
            Potency = 660,
            Cooldown = 20.0,
            MaxCharges = 2,
            PreservesCombo = true,
        },

        [Mch.AirAnchor] = new ActionDef
        {
            Name = Mch.AirAnchor,
            Kind = ActionKind.Gcd,
            Potency = 660,
            Cooldown = 40.0,
            PreservesCombo = true,
        },

        [Mch.ChainSaw] = new ActionDef
        {
            Name = Mch.ChainSaw,
            MaxTargets = ActionDef.AllNearby,
            Falloff = MachinistFalloff,
            Kind = ActionKind.Gcd,
            Potency = 660,
            Cooldown = 60.0,
            PreservesCombo = true,
        },

        [Mch.Excavator] = new ActionDef
        {
            Name = Mch.Excavator,
            MaxTargets = ActionDef.AllNearby,
            Falloff = MachinistFalloff,
            Kind = ActionKind.Gcd,
            Potency = 660,
            PreservesCombo = true,
        },

        [Mch.FullMetalField] = new ActionDef
        {
            Name = Mch.FullMetalField,
            MaxTargets = ActionDef.AllNearby,
            Falloff = MachinistFalloff,
            Kind = ActionKind.Gcd,
            Potency = 900,
            PreservesCombo = true,
        },

        [Mch.BlazingShot] = new ActionDef
        {
            Name = Mch.BlazingShot,
            Kind = ActionKind.Gcd,
            Potency = 240,
            BaseRecast = 1.5,
            PreservesCombo = true,
        },

        [Mch.DoubleCheck] = new ActionDef
        {
            Name = Mch.DoubleCheck,
            MaxTargets = ActionDef.AllNearby,
            Falloff = CheckFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 180,
            Cooldown = 30.0,
            MaxCharges = 3,
        },

        [Mch.Checkmate] = new ActionDef
        {
            Name = Mch.Checkmate,
            MaxTargets = ActionDef.AllNearby,
            Falloff = CheckFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 180,
            Cooldown = 30.0,
            MaxCharges = 3,
        },

        [Mch.HyperchargeAction] = new ActionDef
        {
            Name = Mch.HyperchargeAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 10.0,
        },

        [Mch.BarrelStabilizer] = new ActionDef
        {
            Name = Mch.BarrelStabilizer,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Mch.ReassembleAction] = new ActionDef
        {
            Name = Mch.ReassembleAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 55.0,
            MaxCharges = 2,
        },

        [Mch.WildfireAction] = new ActionDef
        {
            Name = Mch.WildfireAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Mch.AutomatonQueen] = new ActionDef
        {
            Name = Mch.AutomatonQueen,
            Kind = ActionKind.OffGcd,
            Cooldown = 6.0,
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
