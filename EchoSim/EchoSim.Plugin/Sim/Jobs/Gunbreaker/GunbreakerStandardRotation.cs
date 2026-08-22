using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Gunbreaker;

/// Gunbreaker's standard single-target rotation.
public sealed class GunbreakerStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    /// How far ahead the rotation looks when deciding whether Double Down's two cartridges are already spoken
    /// for.
    private const double CartridgeHold = 10.0;

    /// How long a Gnashing Fang chain occupies the global cooldown once started.
    private const double GnashingChainSeconds = 2 * 2.5;

    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-0.7s", Gnb.LightningShot,
            "The pull, and what the Bloodfest behind it weaves under. Both guides open on it and so "
            + "does the reference parse; the Balance calls it \"heavily suggested for MT threat "
            + "generation and buff alignment\". The simulation starts its clock on Keen Edge, because "
            + "a global cooldown spent before the timer is free to a player and is not free to a "
            + "fixed-length model."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Gnb.BloodfestAction,
        Gnb.KeenEdge,
        Gnb.BrutalShell,
        Gnb.NoMercyAction,
        Buffs.PotionAction,
        Gnb.GnashingFang,
        Gnb.JugularRip,
        Gnb.BowShock,
        Gnb.DoubleDown,
        Gnb.BlastingZone,
        Gnb.SonicBreak,
        Gnb.SavageClaw,
        Gnb.AbdomenTear,
        Gnb.WickedTalon,
        Gnb.EyeGouge,
        Gnb.ReignOfBeasts,
        Gnb.NobleBlood,
        Gnb.LionHeart,
        Gnb.SolidBarrel,
        Gnb.GnashingFang,
        Gnb.JugularRip,
    ];

    /// The opener's global cooldowns.
    private static readonly string[] Opener =
    [
        Gnb.KeenEdge,
        Gnb.BrutalShell,
        Gnb.GnashingFang,
        Gnb.DoubleDown,
        Gnb.SonicBreak,
        Gnb.SavageClaw,
        Gnb.WickedTalon,
        Gnb.ReignOfBeasts,
        Gnb.NobleBlood,
        Gnb.LionHeart,
        Gnb.SolidBarrel,
        Gnb.GnashingFang,
    ];

    /// An ability the opener presses at a planned point rather than when a condition trips.
    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's weaves, in order, each named by the opener global cooldown it follows.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(-1, Gnb.BloodfestAction),
        new(1, Gnb.NoMercyAction),
        new(1, Buffs.PotionAction),
        new(2, Gnb.BowShock),
        new(3, Gnb.BlastingZone),
    ];

    /// Whether to run the area chain.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        areaChain = state.UseAreaRotation;

        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var gnb = (GunbreakerState)state;

        if (Weave(gnb, job, stats) is { } ability)
            return ability;

        if (gnb.Time + 1e-9 < gnb.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = GunbreakerData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, gnb))
                return planned;

            gnb.Count("opener.skipped", 1);
            gnb.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(gnb, job);
    }

    /// The global cooldown priority.
    private ActionDef NextGcd(GunbreakerState gnb, IJobSim job)
    {
        if (First(gnb, job, Gnb.WickedTalon, Gnb.SavageClaw) is { } fang)
            return fang;

        if (First(gnb, job, Gnb.LionHeart, Gnb.NobleBlood) is { } reign)
            return reign;

        if (First(gnb, job, Gnb.SolidBarrel, Gnb.BrutalShell) is { } pending)
            return pending;

        if (gnb.HasStatus(Gnb.NoMercyStatus) && First(gnb, job, Gnb.SonicBreak) is { } sonic)
            return sonic;

        if (Ready(gnb, job, Gnb.DoubleDown))
            return GunbreakerData.Get(Gnb.DoubleDown);

        if (First(gnb, job, Gnb.ReignOfBeasts) is { } reignStart)
            return reignStart;

        var chainWouldOutlastDoubleDown =
            gnb.Cooldown(Gnb.DoubleDown).ReadyAt(gnb.Time) - gnb.Time < GnashingChainSeconds;

        if (!chainWouldOutlastDoubleDown && Ready(gnb, job, Gnb.GnashingFang))
            return GunbreakerData.Get(Gnb.GnashingFang);

        if (First(gnb, job, Gnb.SonicBreak) is { } lateSonic)
            return lateSonic;

        var reserve = gnb.Cooldown(Gnb.DoubleDown).ReadyAt(gnb.Time) - gnb.Time < CartridgeHold
            ? GunbreakerData.DoubleDownCost
            : 0;

        var full = gnb.Cartridges >= gnb.CartridgeCap;

        if ((full || gnb.Cartridges > reserve) && First(gnb, job, Gnb.BurstStrike) is { } burst)
            return TargetChoice.Best(job, gnb, burst, GunbreakerData.Get(Gnb.FatedCircle));

        if (areaChain)
        {
            return GunbreakerData.Get(
                gnb.IsComboReady(GunbreakerData.Get(Gnb.DemonSlaughter)) ? Gnb.DemonSlaughter : Gnb.DemonSlice);
        }

        return GunbreakerData.Get(Gnb.KeenEdge);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(GunbreakerState gnb, IJobSim job, PlayerStats stats)
    {
        if (openerWeaveIndex < OpenerWeaves.Length
            && OpenerWeaves[openerWeaveIndex] is { AfterGcd: < 0 } pull
            && openerIndex == 0)
        {
            openerWeaveIndex++;

            if (Ready(gnb, job, pull.Action))
                return GunbreakerData.Get(pull.Action);
        }

        if (!WeavePlanner.CanWeave(gnb, stats))
            return null;

        if (First(gnb, job, Gnb.EyeGouge, Gnb.AbdomenTear, Gnb.JugularRip, Gnb.Hypervelocity, Gnb.FatedBrand)
            is { } continuation)
        {
            return continuation;
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(gnb, job, planned.Action)
                     && (planned.Action != Buffs.PotionAction || PotionDue(gnb)))
            {
                openerWeaveIndex++;

                if (planned.Action == Buffs.PotionAction)
                    potionIndex++;

                return GunbreakerData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (PotionDue(gnb))
        {
            potionIndex++;
            return GunbreakerData.Get(Buffs.PotionAction);
        }

        if (Ready(gnb, job, Gnb.NoMercyAction))
            return GunbreakerData.Get(Gnb.NoMercyAction);

        if (Ready(gnb, job, Gnb.BloodfestAction))
            return GunbreakerData.Get(Gnb.BloodfestAction);

        if (Ready(gnb, job, Gnb.BlastingZone))
            return GunbreakerData.Get(Gnb.BlastingZone);

        if (Ready(gnb, job, Gnb.BowShock))
            return GunbreakerData.Get(Gnb.BowShock);

        return null;
    }

    private static ActionDef? First(GunbreakerState gnb, IJobSim job, params string[] names)
    {
        foreach (var name in names)
        {
            var action = GunbreakerData.Get(name);
            if (job.CanUse(action, gnb) && (!gnb.HasCooldown(name) || gnb.Cooldown(name).ChargesAt(System.Math.Max(gnb.Time, gnb.AnimationLockUntil)) > 0))
                return action;
        }

        return null;
    }

    /// Whether an ability can be pressed in the weave slot that is opening, rather than at this exact
    /// instant.
    private bool PotionDue(GunbreakerState gnb)
        => potionIndex < potionTimes.Count
           && gnb.Time + 1e-9 >= potionTimes[potionIndex]
           && gnb.Cooldown(Buffs.PotionAction).ChargesAt(gnb.Time) > 0;

    private static bool Ready(GunbreakerState gnb, IJobSim job, string name)
    {
        var action = GunbreakerData.Get(name);
        var earliest = System.Math.Max(gnb.Time, gnb.AnimationLockUntil);

        return (!gnb.HasCooldown(name) || gnb.Cooldown(name).ChargesAt(earliest) > 0)
               && job.CanUse(action, gnb);
    }
}
