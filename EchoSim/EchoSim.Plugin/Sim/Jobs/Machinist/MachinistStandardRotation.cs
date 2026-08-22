using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Machinist;

/// Machinist's standard single-target rotation.
public sealed class MachinistStandardRotation(
    IReadOnlyList<double>? potionTimes = null,
    bool prePullReassemble = true) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    /// Reassemble, and the honest reason for it.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-1.5s", Mch.ReassembleAction,
            "Puts a guaranteed critical direct hit on the Air Anchor that opens the fight. Worth "
            + "almost nothing measured - 0.00% on a dummy, +0.03% in a raid - because it starts the "
            + "recast early rather than creating an extra use. Free, so it may as well be pressed."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Mch.AirAnchor,
        Buffs.PotionAction,
        Mch.DoubleCheck,
        Mch.Checkmate,
        Mch.Drill,
        Mch.BarrelStabilizer,
        Mch.DoubleCheck,
        Mch.Checkmate,
        Mch.ChainSaw,
        Mch.Excavator,
        Mch.AutomatonQueen,
        Mch.WildfireAction,
        Mch.FullMetalField,
        Mch.HyperchargeAction,
        Mch.DoubleCheck,
        Mch.BlazingShot,
        Mch.Checkmate,
        Mch.BlazingShot,
        Mch.DoubleCheck,
        Mch.BlazingShot,
        Mch.Checkmate,
        Mch.BlazingShot,
        Mch.DoubleCheck,
        Mch.BlazingShot,
        Mch.Checkmate,
        Mch.ReassembleAction,
        Mch.Drill,
    ];

    /// The opener's GCDs, in order, as observed on the reference parse.
    private static readonly string[] Opener =
    [
        Mch.AirAnchor,
        Mch.Drill,
        Mch.ChainSaw,
        Mch.Excavator,
        Mch.FullMetalField,
    ];

    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's weaves, in order, each after the global it was recorded against.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(0, Mch.DoubleCheck),
        new(0, Mch.Checkmate),
        new(1, Mch.BarrelStabilizer),
        new(1, Mch.DoubleCheck),
        new(1, Mch.Checkmate),
        new(3, Mch.AutomatonQueen),
        new(3, Mch.WildfireAction),
        new(4, Mch.HyperchargeAction),
        new(4, Mch.DoubleCheck),
    ];

    /// Whether the filler should be Scattergun instead of the three-step combo.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = 0;
        potionIndex = 0;
        openerWeaveIndex = 0;

        areaChain = state.UseAreaRotation;

        if (!prePullReassemble)
            return;

        state.Cooldown(Mch.ReassembleAction).Use(-PrePullLead);
        state.ApplyStatus(Mch.Reassembled, MachinistData.ReassembleDuration - PrePullLead);
    }

    /// How far before the pull the Reassemble goes.
    private const double PrePullLead = 1.5;

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var mch = (MachinistState)state;

        if (Weave(mch, job, stats) is { } ability)
            return ability;

        if (mch.Time + 1e-9 < mch.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = MachinistData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, mch))
                return planned;

            mch.Count("opener.skipped", 1);
            mch.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(mch, job);
    }

    /// What the next weaponskill will be - the opener step if one is still pending, otherwise whatever the
    /// priority list picks.
    private ActionDef PlannedNextGcd(MachinistState mch, IJobSim job)
        => openerIndex < Opener.Length
            ? MachinistData.Get(Opener[openerIndex])
            : NextGcd(mch, job, Math.Max(mch.Time, mch.NextGcdAt));

    /// The GCD priority.
    private ActionDef NextGcd(MachinistState mch, IJobSim job) => NextGcd(mch, job, mch.Time);

    /// at: When to read recasts.
    private ActionDef NextGcd(MachinistState mch, IJobSim job, double at)
    {
        var blazing = MachinistData.Get(Mch.BlazingShot);
        if (job.CanUse(blazing, mch))
            return TargetChoice.Best(job, mch, blazing, MachinistData.Get(Mch.AutoCrossbow));

        foreach (var name in GcdPriority)
        {
            var action = MachinistData.Get(name);

            if (action.Cooldown > 0 && mch.Cooldown(name).ChargesAt(at) <= 0)
                continue;

            if (name == Mch.FullMetalField && !FullMetalFieldIsDue(mch))
                continue;

            if (job.CanUse(action, mch))
            {
                if (name == Mch.Drill && BioblasterWins(job, mch))
                    return MachinistData.Get(Mch.Bioblaster);

                return action;
            }
        }

        if (areaChain)
            return MachinistData.Get(Mch.Scattergun);

        foreach (var step in new[] { Mch.HeatedCleanShot, Mch.HeatedSlugShot })
        {
            var action = MachinistData.Get(step);
            if (job.CanUse(action, mch))
                return action;
        }

        return MachinistData.Get(Mch.HeatedSplitShot);
    }

    /// Whether Bioblaster beats Drill at the current target count, counting its damage-over-time.
    private static bool BioblasterWins(IJobSim job, MachinistState mch)
    {
        var bioblaster = MachinistData.Get(Mch.Bioblaster);

        var area = (bioblaster.Potency + MachinistData.BioblasterDotPotency)
                   * bioblaster.TargetMultiplier(mch.Targets);

        return area > TargetChoice.Value(job, mch, MachinistData.Get(Mch.Drill));
    }

    /// Whether Wildfire should go now.
    private static bool WildfireIsDue(MachinistState mch, IJobSim job)
    {
        if (!mch.HasStatus(Mch.FullMetalMachinist))
            return job.CanUse(MachinistData.Get(Mch.HyperchargeAction), mch);

        if (mch.StatusRemaining(Mch.FullMetalMachinist) <= 6.0)
            return true;

        foreach (var name in new[] { Mch.Excavator, Mch.ChainSaw })
        {
            var tool = MachinistData.Get(name);
            var offRecast = tool.Cooldown <= 0 || mch.Cooldown(name).ChargesAt(Math.Max(mch.Time, mch.AnimationLockUntil)) > 0;

            if (offRecast && job.CanUse(tool, mch))
                return false;
        }

        return true;
    }

    /// Whether Full Metal Field should go now, or wait for a Wildfire to put it under.
    private static bool FullMetalFieldIsDue(MachinistState mch)
        => mch.HasStatus(Mch.WildfireStatus)
           || mch.StatusRemaining(Mch.FullMetalMachinist) <= 5.0;

    /// The weaponskills worth pressing over filler, highest potency per GCD first.
    private static readonly string[] GcdPriority =
    [
        Mch.FullMetalField,
        Mch.Excavator,
        Mch.ChainSaw,
        Mch.AirAnchor,
        Mch.Drill,
    ];

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(MachinistState mch, IJobSim job, PlayerStats stats)
    {
        if (!WeavePlanner.CanWeave(mch, stats))
            return null;

        if (potionIndex < potionTimes.Count && mch.Time + 1e-9 >= potionTimes[potionIndex]
            && Ready(mch, Buffs.PotionAction))
        {
            potionIndex++;
            return MachinistData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd
                     && Ready(mch, planned.Action)
                     && job.CanUse(MachinistData.Get(planned.Action), mch))
            {
                openerWeaveIndex++;
                return MachinistData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (Ready(mch, Mch.BarrelStabilizer) && job.CanUse(MachinistData.Get(Mch.BarrelStabilizer), mch))
            return MachinistData.Get(Mch.BarrelStabilizer);

        var nextGcd = PlannedNextGcd(mch, job);

        if (Ready(mch, Mch.WildfireAction) && WildfireIsDue(mch, job))
            return MachinistData.Get(Mch.WildfireAction);

        if (!mch.HasStatus(Mch.FullMetalMachinist)
            && !mch.HasStatus(Mch.Overheated)
            && Ready(mch, Mch.HyperchargeAction)
            && job.CanUse(MachinistData.Get(Mch.HyperchargeAction), mch))
        {
            return MachinistData.Get(Mch.HyperchargeAction);
        }

        var inBurst = mch.HasStatus(Mch.FullMetalMachinist) || mch.HasStatus(Mch.WildfireStatus);
        if (Ready(mch, Mch.AutomatonQueen)
            && (mch.Battery >= MachinistData.QueenHoldBattery
                || (inBurst && mch.Battery >= MachinistData.QueenMinimumBattery)))
        {
            return MachinistData.Get(Mch.AutomatonQueen);
        }

        if (nextGcd.Name == Mch.Drill && !mch.HasStatus(Mch.Reassembled) && Ready(mch, Mch.ReassembleAction))
            return MachinistData.Get(Mch.ReassembleAction);

        foreach (var name in new[] { Mch.DoubleCheck, Mch.Checkmate })
        {
            if (!Ready(mch, name))
                continue;

            var capped = mch.Cooldown(name).ChargesAt(mch.Time) >= MachinistData.Get(name).MaxCharges;
            if (capped || WeavePlanner.FitsWithoutClipping(mch))
                return MachinistData.Get(name);
        }

        return null;
    }

    /// Whether an ability can be pressed in this weave window.
    private static bool Ready(MachinistState mch, string name)
        => mch.Cooldown(name).ChargesAt(Math.Max(mch.Time, mch.AnimationLockUntil)) > 0;
}
