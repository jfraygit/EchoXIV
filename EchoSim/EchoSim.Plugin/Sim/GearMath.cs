namespace EchoSim.Sim;

using EchoSim.Sim.Engine;

/// The arithmetic behind gear and melds, kept free of game-data types so the harness can test it.
public readonly record struct AutoCritProfile(double CritShare, double DirectHitShare)
{
    public bool IsEmpty => CritShare <= 0 && DirectHitShare <= 0;
}

public static class GearMath
{
    /// The character's starting value for a substat, before any gear.
    public static int BaseFor(SubStat stat, LevelStats level)
        => stat is SubStat.Determination or SubStat.Piety ? level.BaseMainStat : level.BaseSubStat;

    /// The most of one substat a piece of gear can carry, base and melds combined.
    public static int SubstatCap(int itemLevelBudget, int slotPercentPerMille)
        => itemLevelBudget <= 0 || slotPercentPerMille <= 0
            ? 0
            : (int)Math.Round(itemLevelBudget * slotPercentPerMille / 1000.0, MidpointRounding.AwayFromZero);

    /// What a piece actually contributes for one substat once melds are added and the cap applied.
    public static int EffectiveSubstat(int baseStat, int meldTotal, int cap)
    {
        var combined = baseStat + meldTotal;
        return cap <= 0 ? combined : Math.Min(combined, cap);
    }

    /// How much of a meld is thrown away against a cap.
    public static int WastedMeld(int baseStat, int meldTotal, int cap)
        => cap <= 0 ? 0 : Math.Max(0, baseStat + meldTotal - cap);

    /// Expected damage per point of potency for a stat line.
    public static double DamageIndex(PlayerStats stats, AutoCritProfile profile = default)
    {
        var a = Math.Clamp(profile.CritShare, 0, 1);

        var b = Math.Clamp(profile.DirectHitShare, 0, a);

        var critNormal = 1 + (stats.CritChance * (stats.CritMulti - 1));
        var dhNormal = 1 + (stats.DirectHitChance * (XivMath.DirectHitMulti - 1));

        var ordinary = critNormal * dhNormal * stats.DetMulti;
        var critOnly = stats.CritMulti * dhNormal * stats.DetMulti;
        var both = stats.CritMulti * XivMath.DirectHitMulti * (stats.DetMulti + stats.AutoDirectHitBonus);

        var blended = ((1 - a) * ordinary) + ((a - b) * critOnly) + (b * both);

        return stats.MainStatMulti
               * stats.TenacityMulti
               * stats.WeaponDamageMulti
               * blended;
    }

    /// The share of a finished run's damage that came through guaranteed critical hits, and through
    /// guaranteed direct hits.
    public static AutoCritProfile AutoCritShares(SimResult result)
    {
        var total = result.Damage.Sum(d => d.Damage);
        if (total <= 0)
            return default;

        return new AutoCritProfile(
            result.Damage.Where(d => d.AutoCrit).Sum(d => d.Damage) / total,
            result.Damage.Where(d => d.AutoDirectHit).Sum(d => d.Damage) / total);
    }
}
