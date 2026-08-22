using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Dancer;

/// Dancer's standard single-target rotation.
public sealed class DancerStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int potionIndex;

    /// Standard Step, danced before the pull.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-15s", Dnc.StandardStep, "Dance the two steps before the pull so the finish lands at zero, "
                                   + "putting Standard Finish and Last Dance Ready up for free."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Dnc.StandardFinish,
        Buffs.PotionAction,
        Dnc.TechnicalStep,
        Dnc.Entrechat,
        Dnc.Emboite,
        Dnc.Pirouette,
        Dnc.Jete,
        Dnc.TechnicalFinish,
        Dnc.DevilmentAction,
        Dnc.Tillana,
        Dnc.Flourish,
        Dnc.FanDanceIV,
        Dnc.DanceOfTheDawn,
        Dnc.FanDanceIII,
        Dnc.LastDance,
        Dnc.FinishingMove,
        Dnc.SaberDance,
        Dnc.StarfallDance,
        Dnc.LastDance,
        Dnc.SaberDance,
    ];

    /// The opener's GCDs, in order, as observed on the reference parse.
    private static readonly string[] Opener =
    [
        Dnc.TechnicalStep,
    ];

    /// Whether the filler should be the area chain.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = 0;
        potionIndex = 0;

        areaChain = state.UseAreaRotation;

        var dnc = (DancerState)state;
        dnc.StepsRemaining = 0;
        dnc.DancingTechnical = false;
        dnc.FinishReady = true;
        dnc.Cooldown(Dnc.StandardStep).Use(-DancerData.PrePullStandardStep);
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var dnc = (DancerState)state;

        if (Weave(dnc, job, stats) is { } ability)
            return ability;

        if (dnc.Time + 1e-9 < dnc.NextGcdAt)
            return null;

        if (dnc.StepsRemaining > 0)
            return DancerData.Get(Dnc.DanceStep);

        if (dnc.FinishReady)
            return DancerData.Get(dnc.DancingTechnical ? Dnc.TechnicalFinish : Dnc.StandardFinish);

        if (openerIndex < Opener.Length)
        {
            var planned = DancerData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, dnc))
                return planned;

            dnc.Count("opener.skipped", 1);
            dnc.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(dnc, job);
    }

    /// The GCD priority.
    private ActionDef NextGcd(DancerState dnc, IJobSim job)
    {
        if (Ready(dnc, Dnc.TechnicalStep))
            return DancerData.Get(Dnc.TechnicalStep);

        if (!dnc.HasStatus(Dnc.FinishingMoveReady) && Ready(dnc, Dnc.StandardStep))
            return DancerData.Get(Dnc.StandardStep);

        if (dnc.Esprit >= DancerData.EspritSpendThreshold && !dnc.HasStatus(Dnc.DanceOfTheDawnReady)
            && job.CanUse(DancerData.Get(Dnc.SaberDance), dnc))
        {
            return DancerData.Get(Dnc.SaberDance);
        }

        foreach (var name in GcdPriority)
        {
            var action = DancerData.Get(name);

            if (action.Cooldown > 0 && !Ready(dnc, name))
                continue;

            if (name == Dnc.SaberDance && dnc.HasStatus(Dnc.DanceOfTheDawnReady))
                continue;

            if (job.CanUse(action, dnc))
            {
                return name switch
                {
                    Dnc.ReverseCascade =>
                        TargetChoice.Best(job, dnc, action, DancerData.Get(Dnc.RisingWindmill)),
                    Dnc.Fountainfall =>
                        TargetChoice.Best(job, dnc, action, DancerData.Get(Dnc.Bloodshower)),
                    _ => action,
                };
            }
        }

        if (areaChain)
        {
            var bladeshower = DancerData.Get(Dnc.Bladeshower);
            return job.CanUse(bladeshower, dnc) ? bladeshower : DancerData.Get(Dnc.Windmill);
        }

        var fountain = DancerData.Get(Dnc.Fountain);
        return job.CanUse(fountain, dnc) ? fountain : DancerData.Get(Dnc.Cascade);
    }

    /// Weaponskills worth pressing over filler, ordered by what expires soonest rather than by potency.
    private static readonly string[] GcdPriority =
    [
        Dnc.FinishingMove,
        Dnc.Tillana,
        Dnc.DanceOfTheDawn,

        Dnc.LastDance,
        Dnc.StarfallDance,
        Dnc.Fountainfall,
        Dnc.ReverseCascade,
        Dnc.SaberDance,
    ];

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(DancerState dnc, IJobSim job, PlayerStats stats)
    {
        if (!WeavePlanner.CanWeave(dnc, stats))
            return null;

        if (potionIndex < potionTimes.Count && dnc.Time + 1e-9 >= potionTimes[potionIndex]
            && dnc.Cooldown(Buffs.PotionAction).ChargesAt(dnc.Time) > 0)
        {
            potionIndex++;
            return DancerData.Get(Buffs.PotionAction);
        }

        if (Ready(dnc, Dnc.DevilmentAction) && dnc.HasStatus(Dnc.TechnicalFinishBuff))
            return DancerData.Get(Dnc.DevilmentAction);

        foreach (var name in new[] { Dnc.FanDanceIV, Dnc.FanDanceIII })
        {
            if (job.CanUse(DancerData.Get(name), dnc))
                return DancerData.Get(name);
        }

        if (Ready(dnc, Dnc.Flourish) && FlourishIsDue(dnc))
            return DancerData.Get(Dnc.Flourish);

        if (dnc.Feathers > 0
            && (dnc.Feathers >= DancerData.MaxFeathers - 1 || dnc.HasStatus(Dnc.TechnicalFinishBuff)))
        {
            return TargetChoice.Best(job, dnc, DancerData.Get(Dnc.FanDance), DancerData.Get(Dnc.FanDanceII));
        }

        return null;
    }

    /// Whether Flourish is worth pressing.
    private static bool FlourishIsDue(DancerState dnc)
    {
        if (dnc.HasStatus(Dnc.ThreefoldFanDance) || dnc.HasStatus(Dnc.FourfoldFanDance))
            return false;

        if (dnc.HasStatus(Dnc.TechnicalFinishBuff))
            return true;

        if (dnc.DancingTechnical && (dnc.StepsRemaining > 0 || dnc.FinishReady))
            return false;

        var untilTechnical = dnc.Cooldown(Dnc.TechnicalStep).ReadyAt(dnc.Time) - dnc.Time;
        if (untilTechnical <= DancerData.FlourishBurstHold)
            return false;

        return !dnc.HasStatus(Dnc.SilkenSymmetry)
               && !dnc.HasStatus(Dnc.SilkenFlow)
               && !dnc.HasStatus(Dnc.FlourishingSymmetry)
               && !dnc.HasStatus(Dnc.FlourishingFlow);
    }

    /// Whether an action's recast - and every recast it SHARES - has a charge.
    private static bool Ready(DancerState dnc, string name)
    {
        var action = DancerData.Get(name);
        var at = Math.Max(dnc.Time, dnc.AnimationLockUntil);

        if (dnc.HasCooldown(name) && dnc.Cooldown(name).ChargesAt(at) <= 0)
            return false;

        foreach (var shared in action.SharesCooldownWith)
        {
            if (dnc.HasCooldown(shared) && dnc.Cooldown(shared).ChargesAt(at) <= 0)
                return false;
        }

        return true;
    }
}
