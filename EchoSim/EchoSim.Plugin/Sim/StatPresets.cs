namespace EchoSim.Sim;

/// A named, editable set of raw stats to simulate with.
public sealed class StatPreset
{
    public string Name = string.Empty;
    public int WeaponDamage;
    public double WeaponDelay = 2.56;
    public int MainStat;
    public int Crit;
    public int Determination;
    public int DirectHit;
    public int SkillSpeed;
    public int Tenacity = 420;

    /// Healer MP regeneration.
    public int Piety = 440;

    public StatPreset Clone() => (StatPreset)MemberwiseClone();

    public int StatFor(SubStat stat) => stat switch
    {
        SubStat.Crit => Crit,
        SubStat.Determination => Determination,
        SubStat.DirectHit => DirectHit,
        SubStat.Speed => SkillSpeed,
        SubStat.Piety => Piety,
        _ => Tenacity,
    };

    /// Resolves these raw stats against a job.
    public PlayerStats ToPlayerStats(
        Engine.IJobSim job,
        bool partyBonus,
        FoodDef? food = null,
        PotionDef? potion = null)
        => ToPlayerStats(job.MainStatModifier, job.HastePercent, job.AutoAttackPotency,
            partyBonus, food, potion, job.TraitMultiplier,
            Engine.CombatRoles.LevelStats(job.Role), Engine.CombatRoles.UsesTenacity(job.Role),
            Engine.CombatRoles.UsesPiety(job.Role));

    /// level: The level constants to resolve against.
    public PlayerStats ToPlayerStats(
        int jobMainStatModifier,
        int jobHastePercent,
        int autoAttackPotency,
        bool partyBonus,
        FoodDef? food = null,
        PotionDef? potion = null,
        double jobTraitMultiplier = 1.0,
        LevelStats? level = null,
        bool usesTenacity = false,
        bool usesPiety = false)
    {
        food ??= FoodDef.None;
        level ??= LevelStats.Lv100;

        return new PlayerStats(
            level,
            WeaponDamage,
            WeaponDelay,
            MainStat,
            Crit + food.BonusFor(SubStat.Crit, Crit),
            Determination + food.BonusFor(SubStat.Determination, Determination),
            DirectHit + food.BonusFor(SubStat.DirectHit, DirectHit),
            SkillSpeed + food.BonusFor(SubStat.Speed, SkillSpeed),
            Tenacity + food.BonusFor(SubStat.Tenacity, Tenacity),
            jobMainStatModifier,
            jobHastePercent,
            autoAttackPotency,
            jobTraitMultiplier,
            partyBonus,
            potion ?? PotionDef.Grade4,
            usesTenacity,
            Piety + food.BonusFor(SubStat.Piety, Piety),
            usesPiety);
    }
}

public static class StatPresets
{
    /// A patch 7.55 Ninja Best-in-Slot set.
    public static StatPreset NinjaBis() => new()
    {
        Name = "NIN 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 2.56,
        MainStat = 6491,
        Crit = 3460,
        Determination = 2132,
        DirectHit = 2123,

        SkillSpeed = 690,
    };

    /// A Monk set at the same item level as the Ninja preset.
    public static StatPreset MonkBis() => new()
    {
        Name = "MNK 7.55 BiS",
        WeaponDamage = 158,
        WeaponDelay = 2.56,
        MainStat = 6491,
        Crit = 3298,
        DirectHit = 1955,
        Determination = 2192,

        SkillSpeed = 960,
    };

    /// A Dragoon set at the same item level as the others.
    public static StatPreset DragoonBis() => new()
    {
        Name = "DRG 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 2.80,
        MainStat = 6513,
        Crit = 3514,
        Determination = 2327,
        DirectHit = 2144,
        SkillSpeed = 420,
    };

    /// A Samurai set at the same item level as the others.
    public static StatPreset SamuraiBis() => new()
    {
        Name = "SAM 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 2.64,
        MainStat = 6499,
        Crit = 3460,
        Determination = 2408,
        DirectHit = 1847,

        SkillSpeed = 690,
    };

    /// A Reaper set at the same item level as the others.
    public static StatPreset ReaperBis() => new()
    {
        Name = "RPR 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 3.20,
        MainStat = 6513,
        Crit = 3514,
        Determination = 2327,
        DirectHit = 2144,
        SkillSpeed = 420,
    };

    /// A Viper set at the same item level as the others.
    public static StatPreset ViperBis() => new()
    {
        Name = "VPR 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 2.64,
        MainStat = 6491,
        Crit = 3460,
        Determination = 2132,
        DirectHit = 2123,

        SkillSpeed = 690,
    };

    /// A Machinist set at the same item level as the others - the first physical ranged job here.
    public static StatPreset MachinistBis() => new()
    {
        Name = "MCH 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 2.64,

        MainStat = 6513,
        Crit = 3494,
        Determination = 2506,
        DirectHit = 1985,

        SkillSpeed = 420,
    };

    /// A Bard set at the same item level as the others.
    public static StatPreset BardBis() => new()
    {
        Name = "BRD 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 3.04,

        MainStat = 6513,
        Crit = 3494,
        Determination = 2506,
        DirectHit = 1985,

        SkillSpeed = 420,
    };

    /// A Dancer set at the same item level as the others.
    public static StatPreset DancerBis() => new()
    {
        Name = "DNC 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 3.12,

        MainStat = 6513,
        Crit = 3494,
        Determination = 2506,
        DirectHit = 1985,

        SkillSpeed = 420,
    };

    /// A Black Mage set at the same item level as the others - the first caster here.
    public static StatPreset BlackMageBis() => new()
    {
        Name = "BLM 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 3.28,
        MainStat = 6513,
        Crit = 3403,
        Determination = 2367,
        DirectHit = 1837,

        SkillSpeed = 798,
    };

    /// A Red Mage set at the same item level as the others.
    public static StatPreset RedMageBis() => new()
    {
        Name = "RDM 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 3.44,
        MainStat = 6513,
        Crit = 3511,
        Determination = 2367,
        DirectHit = 2107,

        SkillSpeed = 420,
    };

    /// A Pictomancer set, on the same Casting gear as the other two.
    public static StatPreset PictomancerBis() => new()
    {
        Name = "PCT 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 2.96,
        MainStat = 6513,
        Crit = 3511,
        Determination = 2367,
        DirectHit = 2107,
        SkillSpeed = 420,
    };

    /// A Summoner set, on the same Casting gear as the other three casters.
    public static StatPreset SummonerBis() => new()
    {
        Name = "SMN 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 3.12,
        MainStat = 6513,
        Crit = 3511,
        Determination = 2367,
        DirectHit = 2107,
        SkillSpeed = 420,
    };


    /// The four healer sets, REAL - read from game item data with melds solved by "/echosim bis".
    public static StatPreset WhiteMageBis() => new()
    {
        Name = "WHM 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 3.44,
        MainStat = 6513,
        Crit = 3514,
        Determination = 2649,
        DirectHit = 1230,
        SkillSpeed = 528,
        Piety = 924,
    };

    public static StatPreset ScholarBis() => new()
    {
        Name = "SCH 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 3.12,
        MainStat = 6513,
        Crit = 3514,
        Determination = 2649,
        DirectHit = 1230,
        SkillSpeed = 528,
        Piety = 924,
    };

    public static StatPreset AstrologianBis() => new()
    {
        Name = "AST 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 3.20,
        MainStat = 6513,
        Crit = 3514,
        Determination = 2649,
        DirectHit = 1230,
        SkillSpeed = 528,
        Piety = 924,
    };

    public static StatPreset SageBis() => new()
    {
        Name = "SGE 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 2.80,
        MainStat = 6513,
        Crit = 3514,
        Determination = 2649,
        DirectHit = 1230,
        SkillSpeed = 528,
        Piety = 924,
    };


    /// The four tank sets.
    public static StatPreset PaladinBis() => new()
    {
        Name = "PLD 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 2.24,
        MainStat = 6447,
        Crit = 3396,
        Determination = 3103,
        DirectHit = 1230,
        SkillSpeed = 420,
        Tenacity = 676,
    };

    public static StatPreset WarriorBis() => new()
    {
        Name = "WAR 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 3.36,
        MainStat = 6469,
        Crit = 3396,
        Determination = 3103,
        DirectHit = 1230,
        SkillSpeed = 420,
        Tenacity = 676,
    };

    public static StatPreset DarkKnightBis() => new()
    {
        Name = "DRK 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 2.96,
        MainStat = 6469,
        Crit = 3396,
        Determination = 3103,
        DirectHit = 1230,
        SkillSpeed = 420,
        Tenacity = 676,
    };

    public static StatPreset GunbreakerBis() => new()
    {
        Name = "GNB 7.55 BiS",
        WeaponDamage = 158,

        WeaponDelay = 2.80,
        MainStat = 6447,
        Crit = 3396,
        Determination = 3103,
        DirectHit = 1230,
        SkillSpeed = 420,
        Tenacity = 676,
    };
}
