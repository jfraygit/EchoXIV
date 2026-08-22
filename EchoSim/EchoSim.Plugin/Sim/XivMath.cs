namespace EchoSim.Sim;

/// The level-dependent constants every stat formula divides by.
public sealed record LevelStats(
    int Level,
    int BaseMainStat,
    int BaseSubStat,
    int LevelDiv,
    int MainStatPowerMod)
{
    /// Dawntrail level cap, for every job that is not a tank.
    public static readonly LevelStats Lv100 = new(Level: 100, BaseMainStat: 440, BaseSubStat: 420, LevelDiv: 2780, MainStatPowerMod: 237);

    /// The same level, with the TANK attack-power modifier: 190 against everyone else's 237.
    public static readonly LevelStats Lv100Tank = Lv100 with { MainStatPowerMod = 190 };
}

/// The game's damage and stat formulas, ported from the community-reverse-engineered versions (AkhMorning's
/// Allagan Studies, cross-checked against the xivgear planner's xivmath).
public static class XivMath
{
    /// Floor, with a correction for binary floating point error.
    public static double Fl(double input)
    {
        var floored = System.Math.Floor(input);
        return input - floored >= 0.99999995 ? floored + 1 : floored;
    }

    /// Floor to a fixed number of decimal places.
    public static double Flp(int places, double input)
    {
        var scale = System.Math.Pow(10, places);
        return Fl(input * scale) / scale;
    }


    /// Probability of a critical hit, 0..1.
    public static double CritChance(LevelStats lv, int crit)
        => Fl(200.0 * (crit - lv.BaseSubStat) / lv.LevelDiv + 50) / 1000.0;

    /// Damage multiplier applied when a hit crits.
    public static double CritMulti(LevelStats lv, int crit)
        => (1400 + Fl(200.0 * (crit - lv.BaseSubStat) / lv.LevelDiv)) / 1000.0;

    /// Probability of a direct hit, 0..1.
    public static double DirectHitChance(LevelStats lv, int dhit)
        => Fl(550.0 * (dhit - lv.BaseSubStat) / lv.LevelDiv) / 1000.0;

    /// Direct hits are a flat 25% in every expansion so far.
    public const double DirectHitMulti = 1.25;

    /// Bonus damage that guaranteed-direct-hit actions get from Direct Hit stat.
    public static double AutoDirectHitBonus(LevelStats lv, int dhit)
        => Fl(140.0 * ((dhit - lv.BaseSubStat) / (double)lv.LevelDiv)) / 1000.0;

    /// Determination damage multiplier.
    public static double DetMulti(LevelStats lv, int det)
        => (1000 + Fl(140.0 * (det - lv.BaseMainStat) / lv.LevelDiv)) / 1000.0;

    /// Tenacity damage multiplier.
    public static double TenacityMulti(LevelStats lv, int tenacity)
        => (1000 + Fl(112.0 * (tenacity - lv.BaseSubStat) / lv.LevelDiv)) / 1000.0;

    /// Maximum MP, which is the same 10,000 for every job at every level since Endwalker.
    public const int MaxMp = 10_000;

    /// The server's regeneration tick, in seconds.
    public const double MpTickInterval = 3.0;

    /// MP restored per tick with no Piety invested - 2% of the pool.
    public const int BaseMpPerTick = 200;

    /// MP restored per server tick.
    public static int MpPerTick(LevelStats lv, int piety)
        => (int)Fl(150.0 * (piety - lv.BaseMainStat) / lv.LevelDiv) + BaseMpPerTick;

    /// Skill Speed's damage multiplier.
    public static double SksDotMulti(LevelStats lv, int sks)
        => (1000 + Fl(130.0 * (sks - lv.BaseSubStat) / lv.LevelDiv)) / 1000.0;


    /// Main stat (DEX for Ninja) damage multiplier.
    public static double MainStatMulti(LevelStats lv, int mainStat)
        => System.Math.Max(0, (Fl(lv.MainStatPowerMod * (double)(mainStat - lv.BaseMainStat) / lv.BaseMainStat) + 100) / 100.0);

    /// Weapon damage multiplier.
    public static double WeaponDamageMulti(LevelStats lv, int weaponDamage, int jobMainStatModifier)
        => Fl(lv.BaseMainStat * jobMainStatModifier / 1000.0 + weaponDamage) / 100.0;

    /// Auto-attack equivalent of f(WD).
    public static double AutoAttackMulti(LevelStats lv, int weaponDamage, int jobMainStatModifier, double weaponDelay)
        => Fl(Fl(lv.BaseMainStat * jobMainStatModifier / 1000.0 + weaponDamage) * (weaponDelay / 3.0)) / 100.0;


    /// Actual recast time of a GCD after Skill Speed and any haste effect.
    public static double GcdRecast(LevelStats lv, double baseRecast, int sks, int hastePercent = 0)
        => System.Math.Max(0, Fl(Fl((1000 - Fl(130.0 * (sks - lv.BaseSubStat) / lv.LevelDiv)) * baseRecast) * (100 - hastePercent) / 1000.0) / 100.0);


    /// Maim and Mend II: +30% base action damage, carried by every job that deals magic damage.
    public const double MaimAndMend = 1.30;

    /// Base damage of a hit before crit/direct-hit rolls, damage variance, and outgoing buffs.
    public static double BaseDamage(
        double potency,
        double mainStatMulti,
        double detMulti,
        double tenacityMulti,
        double wdMulti,
        double traitMulti,
        double autoDirectHitBonus = 0,
        bool autoDirectHit = false,
        bool applySkillSpeed = false,
        double sksDotMulti = 1.0)
    {
        var effectiveDet = autoDirectHit ? Flp(3, detMulti + autoDirectHitBonus) : detMulti;
        var spdMulti = applySkillSpeed ? sksDotMulti : 1.0;

        var d = Fl(potency * mainStatMulti);
        d = Fl(d * effectiveDet);
        d = Fl(d * tenacityMulti);
        d = Fl(d * wdMulti);
        d = Fl(d * spdMulti);
        d = Fl(d * traitMulti) + (potency < 100 ? 1 : 0);

        return d <= 1 ? 1 : d;
    }

    /// Scales a base damage figure by the *expected* value of the crit and direct-hit rolls.
    public static double ExpectedCritDh(double baseDamage, double critChance, double critMulti, double dhChance, double dhMulti)
        => baseDamage * (1 + critChance * (critMulti - 1)) * (1 + dhChance * (dhMulti - 1));
}
