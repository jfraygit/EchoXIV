namespace EchoSim.Sim;

/// A resolved set of player stats, with every derived multiplier computed once up front.
public sealed class PlayerStats
{
    public LevelStats Level { get; }

    public int WeaponDamage { get; }
    public double WeaponDelay { get; }
    public int MainStat { get; }
    public int Crit { get; }
    public int Determination { get; }
    public int DirectHit { get; }
    public int SkillSpeed { get; }
    public int Tenacity { get; }

    /// Healer MP regeneration stat.
    public int Piety { get; }

    /// Whether this job's Piety does anything - see CombatRoles.UsesPiety.
    public bool UsesPiety { get; }

    /// MP restored per three-second server tick, Piety included.
    public int MpPerTick { get; }

    /// The job's main-stat modifier from the ClassJob sheet - 110 for Ninja.
    public int JobMainStatModifier { get; }

    /// Permanent job trait haste, as a percentage.
    public int JobHastePercent { get; }

    /// Potency of a single auto-attack swing.
    public int AutoAttackPotency { get; }

    /// The job's flat damage trait - the Trait term in the formula, not part of potency.
    public double JobTraitMultiplier { get; }

    public double CritChance { get; }
    public double CritMulti { get; }
    public double DirectHitChance { get; }
    public double AutoDirectHitBonus { get; }
    public double DetMulti { get; }
    public double TenacityMulti { get; }
    public double WeaponDamageMulti { get; }
    public double MainStatMulti { get; }
    public double AutoAttackMulti { get; }
    public double SksDotMulti { get; }

    /// Actual GCD recast in seconds after Skill Speed and the job's haste trait.
    public double Gcd { get; }

    /// Seconds between auto-attack swings, after Skill Speed.
    public double AutoAttackInterval { get; }

    /// Main stat multiplier while a Gemdraught is up.
    public double PottedMainStatMulti { get; }

    /// Flat main stat the potion is actually granting, after the cap bites.
    public int PotionMainStatBonus { get; }

    public PotionDef Potion { get; } = PotionDef.Grade4;

    public const double PotionDuration = 30.0;

    /// Gemdraughts share a 270-second recast.
    public const double PotionRecast = 270.0;

    public PlayerStats(
        LevelStats level,
        int weaponDamage,
        double weaponDelay,
        int mainStat,
        int crit,
        int determination,
        int directHit,
        int skillSpeed,
        int tenacity = 420,
        int jobMainStatModifier = 110,
        int jobHastePercent = 0,
        int autoAttackPotency = 90,
        double jobTraitMultiplier = 1.0,
        bool applyPartyBonus = false,
        PotionDef? potion = null,
        bool usesTenacity = false,
        int piety = 440,
        bool usesPiety = false)
    {
        potion ??= PotionDef.Grade4;
        Level = level;
        WeaponDamage = weaponDamage;
        WeaponDelay = weaponDelay;
        MainStat = applyPartyBonus ? (int)System.Math.Floor(mainStat * 1.05) : mainStat;
        Crit = crit;
        Determination = determination;
        DirectHit = directHit;
        SkillSpeed = skillSpeed;
        Tenacity = tenacity;
        JobMainStatModifier = jobMainStatModifier;
        JobHastePercent = jobHastePercent;
        AutoAttackPotency = autoAttackPotency;
        JobTraitMultiplier = jobTraitMultiplier;

        CritChance = XivMath.CritChance(level, crit);
        CritMulti = XivMath.CritMulti(level, crit);
        DirectHitChance = XivMath.DirectHitChance(level, directHit);
        AutoDirectHitBonus = XivMath.AutoDirectHitBonus(level, directHit);
        DetMulti = XivMath.DetMulti(level, determination);

        TenacityMulti = usesTenacity ? XivMath.TenacityMulti(level, tenacity) : 1.0;

        Piety = piety;
        UsesPiety = usesPiety;
        MpPerTick = usesPiety ? XivMath.MpPerTick(level, piety) : XivMath.BaseMpPerTick;
        WeaponDamageMulti = XivMath.WeaponDamageMulti(level, weaponDamage, jobMainStatModifier);
        MainStatMulti = XivMath.MainStatMulti(level, MainStat);
        AutoAttackMulti = XivMath.AutoAttackMulti(level, weaponDamage, jobMainStatModifier, weaponDelay);
        SksDotMulti = XivMath.SksDotMulti(level, skillSpeed);

        Potion = potion;
        PotionMainStatBonus = potion.BonusFor(MainStat);
        PottedMainStatMulti = XivMath.MainStatMulti(level, MainStat + PotionMainStatBonus);

        Gcd = XivMath.GcdRecast(level, 2.5, skillSpeed, jobHastePercent);

        AutoAttackInterval = XivMath.GcdRecast(level, weaponDelay, skillSpeed, jobHastePercent);
    }

    /// Actual recast of a GCD whose tooltip base is not the standard 2.5s, after Skill Speed and the job's
    /// haste trait.
    public double RecastFor(double baseRecast, int extraHastePercent = 0)
        => XivMath.GcdRecast(Level, baseRecast, SkillSpeed, JobHastePercent + extraHastePercent);

    /// Seconds between auto-attack swings with extra haste applied.
    public double AutoAttackIntervalWith(int extraHastePercent)
        => XivMath.GcdRecast(Level, WeaponDelay, SkillSpeed, JobHastePercent + extraHastePercent);

    /// Expected damage of a single hit at the given potency, averaged over the crit and direct-hit rolls.
    public double ExpectedDamage(
        double potency,
        double buffMulti = 1.0,
        double bonusCritChance = 0,
        double bonusDirectHitChance = 0,
        bool isAutoAttack = false,
        bool autoCrit = false,
        bool autoDirectHit = false,
        bool potted = false)
    {
        var wd = isAutoAttack ? AutoAttackMulti : WeaponDamageMulti;

        var baseDamage = XivMath.BaseDamage(
            potency: potency,
            mainStatMulti: potted ? PottedMainStatMulti : MainStatMulti,
            detMulti: DetMulti,
            tenacityMulti: TenacityMulti,
            wdMulti: wd,
            traitMulti: JobTraitMultiplier,
            autoDirectHitBonus: AutoDirectHitBonus,
            autoDirectHit: autoDirectHit,
            applySkillSpeed: isAutoAttack,
            sksDotMulti: SksDotMulti);

        var critChance = autoCrit ? 1.0 : System.Math.Min(1.0, CritChance + bonusCritChance);
        var dhChance = autoDirectHit ? 1.0 : System.Math.Min(1.0, DirectHitChance + bonusDirectHitChance);

        var expected = XivMath.ExpectedCritDh(baseDamage, critChance, CritMulti, dhChance, XivMath.DirectHitMulti);

        if (autoCrit && bonusCritChance > 0)
            expected *= 1 + (bonusCritChance * (CritMulti - 1));

        if (autoDirectHit && bonusDirectHitChance > 0)
            expected *= 1 + (bonusDirectHitChance * (XivMath.DirectHitMulti - 1));

        return expected * buffMulti;
    }
}
