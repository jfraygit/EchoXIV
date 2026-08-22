using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Reaper;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Rpr
{
    public const string DeathsDesign = "Death's Design";
    public const string SoulReaver = "Soul Reaver";
    public const string Executioner = "Executioner";
    public const string EnhancedGibbet = "Enhanced Gibbet";
    public const string EnhancedGallows = "Enhanced Gallows";
    public const string EnhancedVoidReaping = "Enhanced Void Reaping";
    public const string EnhancedCrossReaping = "Enhanced Cross Reaping";
    public const string Enshrouded = "Enshrouded";
    public const string Oblatio = "Oblatio";
    public const string IdealHost = "Ideal Host";
    public const string PerfectioOcculta = "Perfectio Occulta";
    public const string PerfectioParata = "Perfectio Parata";
    public const string ImmortalSacrifice = "Immortal Sacrifice";
    public const string ArcaneCircleBuff = "Arcane Circle";

    public const string Slice = "Slice";
    public const string WaxingSlice = "Waxing Slice";
    public const string InfernalSlice = "Infernal Slice";

    public const string ShadowOfDeath = "Shadow of Death";
    public const string SoulSlice = "Soul Slice";

    public const string Gibbet = "Gibbet";
    public const string Gallows = "Gallows";
    public const string ExecutionersGibbet = "Executioner's Gibbet";
    public const string ExecutionersGallows = "Executioner's Gallows";

    public const string PlentifulHarvest = "Plentiful Harvest";

    public const string VoidReaping = "Void Reaping";
    public const string CrossReaping = "Cross Reaping";
    public const string Communio = "Communio";
    public const string Perfectio = "Perfectio";

    public const string UnveiledGibbet = "Unveiled Gibbet";
    public const string UnveiledGallows = "Unveiled Gallows";
    public const string Gluttony = "Gluttony";
    public const string EnshroudAction = "Enshroud";
    public const string LemuresSlice = "Lemure's Slice";
    public const string Sacrificium = "Sacrificium";
    public const string ArcaneCircleAction = "Arcane Circle";

    public const string SpinningScythe = "Spinning Scythe";
    public const string NightmareScythe = "Nightmare Scythe";
    public const string WhorlOfDeath = "Whorl of Death";
    public const string SoulScythe = "Soul Scythe";
    public const string Guillotine = "Guillotine";
    public const string ExecutionersGuillotine = "Executioner's Guillotine";
    public const string GrimReaping = "Grim Reaping";
    public const string LemuresScythe = "Lemure's Scythe";

    /// The area Soul spender - Unveiled Gibbet and Gallows' counterpart, paying from four.
    public const string GrimSwathe = "Grim Swathe";
}

/// Reaper's action table.
public static class ReaperData
{
    /// The falloff on most of Reaper's cleaving actions: 20% off each enemy after the first.
    public const double ReaperFalloff = 0.20;

    /// Gluttony alone loses 25%, which is why it does not share the constant above.
    public const double GluttonyFalloff = 0.25;

    /// Death's Design.
    public const int ReapingBasePotency = 580;

    public const double DeathsDesignMulti = 1.10;

    public const double DeathsDesignDuration = 30.0;

    /// Arcane Circle: 3% to the party, and the Reaper is in the party.
    public const double ArcaneCircleMulti = 1.03;

    public const double ArcaneCircleDuration = 20.0;

    public const double EnshroudDuration = 30.0;
    public const double BuffDuration = 30.0;

    /// Lemure Shroud granted by Enshroud: four reapings and the Communio that ends it.
    public const int LemureShroudStacks = 5;

    /// Void Shroud per reaping, and what Lemure's Slice costs.
    public const int LemuresSliceCost = 2;

    public const int MaxSoul = 100;
    public const int MaxShroud = 100;

    public const int SoulSpenderCost = 50;
    public const int EnshroudShroudCost = 50;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Rpr.Slice] = new ActionDef
        {
            Name = Rpr.Slice,
            Kind = ActionKind.Gcd,
            Potency = 420,
            IsComboStarter = true,
        },

        [Rpr.WaxingSlice] = new ActionDef
        {
            Name = Rpr.WaxingSlice,
            Kind = ActionKind.Gcd,
            Potency = 260,
            ComboPotency = 500,
            ComboFrom = Rpr.Slice,
        },

        [Rpr.InfernalSlice] = new ActionDef
        {
            Name = Rpr.InfernalSlice,
            Kind = ActionKind.Gcd,
            Potency = 280,
            ComboPotency = 600,
            ComboFrom = Rpr.WaxingSlice,
        },

        [Rpr.ShadowOfDeath] = new ActionDef
        {
            Name = Rpr.ShadowOfDeath,
            Kind = ActionKind.Gcd,
            Potency = 300,
            PreservesCombo = true,
        },

        [Rpr.SoulSlice] = new ActionDef
        {
            Name = Rpr.SoulSlice,
            Kind = ActionKind.Gcd,
            Potency = 520,
            Cooldown = 30.0,
            MaxCharges = 2,
            PreservesCombo = true,
        },

        [Rpr.SpinningScythe] = new ActionDef
        {
            Name = Rpr.SpinningScythe,
            Kind = ActionKind.Gcd,
            Potency = 140,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rpr.NightmareScythe] = new ActionDef
        {
            Name = Rpr.NightmareScythe,
            Kind = ActionKind.Gcd,
            Potency = 120,
            ComboPotency = 180,
            ComboFrom = Rpr.SpinningScythe,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rpr.WhorlOfDeath] = new ActionDef
        {
            Name = Rpr.WhorlOfDeath,
            Kind = ActionKind.Gcd,
            Potency = 100,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rpr.SoulScythe] = new ActionDef
        {
            Name = Rpr.SoulScythe,
            Kind = ActionKind.Gcd,
            Potency = 180,
            Cooldown = 30.0,

            MaxCharges = 2,
            SharesCooldownWith = [Rpr.SoulSlice],
            MaxTargets = ActionDef.AllNearby,
        },

        [Rpr.Guillotine] = new ActionDef
        {
            Name = Rpr.Guillotine,
            Kind = ActionKind.Gcd,
            Potency = 200,
            PreservesCombo = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rpr.ExecutionersGuillotine] = new ActionDef
        {
            Name = Rpr.ExecutionersGuillotine,
            Kind = ActionKind.Gcd,
            Potency = 260,
            PreservesCombo = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rpr.GrimReaping] = new ActionDef
        {
            Name = Rpr.GrimReaping,
            Kind = ActionKind.Gcd,
            Potency = 220,
            PreservesCombo = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rpr.LemuresScythe] = new ActionDef
        {
            Name = Rpr.LemuresScythe,
            Kind = ActionKind.OffGcd,
            Potency = 100,
            Cooldown = 1.0,
            SharesCooldownWith = [Rpr.LemuresSlice],
            MaxTargets = ActionDef.AllNearby,
        },

        [Rpr.Gibbet] = new ActionDef
        {
            Name = Rpr.Gibbet,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 620,
            PreservesCombo = true,
        },

        [Rpr.Gallows] = new ActionDef
        {
            Name = Rpr.Gallows,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 620,
            PreservesCombo = true,
        },

        [Rpr.ExecutionersGibbet] = new ActionDef
        {
            Name = Rpr.ExecutionersGibbet,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 820,
            PreservesCombo = true,
        },

        [Rpr.ExecutionersGallows] = new ActionDef
        {
            Name = Rpr.ExecutionersGallows,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 820,
            PreservesCombo = true,
        },

        [Rpr.PlentifulHarvest] = new ActionDef
        {
            Name = Rpr.PlentifulHarvest,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ReaperFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1000,
            PreservesCombo = true,
        },

        [Rpr.VoidReaping] = new ActionDef
        {
            Name = Rpr.VoidReaping,
            Kind = ActionKind.Gcd,
            Potency = 640,
            BaseRecast = 1.5,
            PreservesCombo = true,
        },

        [Rpr.CrossReaping] = new ActionDef
        {
            Name = Rpr.CrossReaping,
            Kind = ActionKind.Gcd,
            Potency = 640,
            BaseRecast = 1.5,
            PreservesCombo = true,
        },

        [Rpr.Communio] = new ActionDef
        {
            Name = Rpr.Communio,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ReaperFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1100,
            PreservesCombo = true,
        },

        [Rpr.Perfectio] = new ActionDef
        {
            Name = Rpr.Perfectio,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ReaperFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1300,
            PreservesCombo = true,
        },

        [Rpr.UnveiledGibbet] = new ActionDef
        {
            Name = Rpr.UnveiledGibbet,
            Kind = ActionKind.OffGcd,
            Potency = 440,
            Cooldown = 1.0,
        },

        [Rpr.UnveiledGallows] = new ActionDef
        {
            Name = Rpr.UnveiledGallows,
            Kind = ActionKind.OffGcd,
            Potency = 440,
            Cooldown = 1.0,
        },

        [Rpr.GrimSwathe] = new ActionDef
        {
            Name = Rpr.GrimSwathe,
            Kind = ActionKind.OffGcd,
            Potency = 140,
            Cooldown = 1.0,
            MaxTargets = ActionDef.AllNearby,
        },

        [Rpr.Gluttony] = new ActionDef
        {
            Name = Rpr.Gluttony,
            MaxTargets = ActionDef.AllNearby,
            Falloff = GluttonyFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 560,
            Cooldown = 60.0,
        },

        [Rpr.EnshroudAction] = new ActionDef
        {
            Name = Rpr.EnshroudAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 5.0,
        },

        [Rpr.LemuresSlice] = new ActionDef
        {
            Name = Rpr.LemuresSlice,
            Kind = ActionKind.OffGcd,
            Potency = 280,
            Cooldown = 1.0,
        },

        [Rpr.Sacrificium] = new ActionDef
        {
            Name = Rpr.Sacrificium,
            MaxTargets = ActionDef.AllNearby,
            Falloff = ReaperFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 700,
            Cooldown = 1.0,
        },

        [Rpr.ArcaneCircleAction] = new ActionDef
        {
            Name = Rpr.ArcaneCircleAction,
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
