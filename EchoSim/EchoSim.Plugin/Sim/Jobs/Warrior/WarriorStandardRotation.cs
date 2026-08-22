using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Warrior;

/// Warrior's standard single-target rotation.
public sealed class WarriorStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    /// The potion at -2.0s, which every instant-cast job here uses.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-0.7s", War.Tomahawk,
            "The pull, and what the Infuriate behind it weaves under. Both guides open on it and so "
            + "does the reference parse. It is a ranged 150 rather than a damage decision - the "
            + "simulation starts its clock on Heavy Swing, because a global cooldown spent before "
            + "the timer is free to a player and is not free to a fixed-length model."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        War.InfuriateAction,
        War.HeavySwing,
        War.Maim,
        War.StormsEye,
        War.InnerReleaseAction,
        Buffs.PotionAction,
        War.InnerChaos,
        War.Upheaval,
        War.Onslaught,
        War.FellCleave,
        War.Onslaught,
        War.FellCleave,
        War.Onslaught,
        War.FellCleave,
        War.PrimalWrath,
        War.InfuriateAction,
        War.PrimalRend,
        War.PrimalRuination,
        War.InnerChaos,
    ];

    /// The opener's global cooldowns.
    private static readonly string[] Opener =
    [
        War.HeavySwing,
        War.Maim,
        War.StormsEye,
        War.InnerChaos,
        War.FellCleave,
        War.FellCleave,
        War.FellCleave,
        War.PrimalRend,
        War.PrimalRuination,
        War.InnerChaos,
    ];

    /// An ability the opener presses at a planned point rather than when a condition trips.
    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's weaves, in order, each named by the opener global cooldown it follows.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(-1, War.InfuriateAction),
        new(2, War.InnerReleaseAction),
        new(2, Buffs.PotionAction),
        new(3, War.Upheaval),
        new(3, War.Onslaught),
        new(4, War.Onslaught),
        new(5, War.Onslaught),
        new(6, War.PrimalWrath),
        new(6, War.InfuriateAction),
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
        var war = (WarriorState)state;

        if (Weave(war, job, stats) is { } ability)
            return ability;

        if (war.Time + 1e-9 < war.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = WarriorData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, war))
                return planned;

            war.Count("opener.skipped", 1);
            war.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(war, job);
    }

    /// The global cooldown priority.
    private ActionDef NextGcd(WarriorState war, IJobSim job)
    {
        if (war.IsComboReady(WarriorData.Get(War.StormsEye)))
        {
            var remaining = war.StatusRemaining(War.SurgingTempest);
            var ender = remaining <= WarriorData.SurgingTempestMax - WarriorData.SurgingTempestDuration
                ? War.StormsEye
                : War.StormsPath;

            return WarriorData.Get(ender);
        }

        if (First(war, job, War.Maim) is { } maim)
            return maim;

        if (First(war, job, War.InnerChaos) is { } chaos)
            return TargetChoice.Best(job, war, chaos, WarriorData.Get(War.ChaoticCyclone));

        if (war.HasStatus(War.InnerReleaseStatus) && First(war, job, War.FellCleave) is { } free)
            return TargetChoice.Best(job, war, free, WarriorData.Get(War.Decimate));

        if (First(war, job, War.PrimalRuination, War.PrimalRend) is { } primal)
            return primal;

        if (war.Beast >= WarriorData.FellCleaveCost && First(war, job, War.FellCleave) is { } cleave)
            return TargetChoice.Best(job, war, cleave, WarriorData.Get(War.Decimate));

        if (areaChain)
        {
            return WarriorData.Get(
                war.IsComboReady(WarriorData.Get(War.MythrilTempest)) ? War.MythrilTempest : War.Overpower);
        }

        return WarriorData.Get(War.HeavySwing);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(WarriorState war, IJobSim job, PlayerStats stats)
    {
        if (openerWeaveIndex < OpenerWeaves.Length
            && OpenerWeaves[openerWeaveIndex] is { AfterGcd: < 0 } pull
            && openerIndex == 0)
        {
            if (Ready(war, job, pull.Action))
            {
                openerWeaveIndex++;
                return WarriorData.Get(pull.Action);
            }

            openerWeaveIndex++;
        }

        if (!WeavePlanner.CanWeave(war, stats))
            return null;

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(war, job, planned.Action)
                     && (planned.Action != Buffs.PotionAction || PotionDue(war)))
            {
                openerWeaveIndex++;

                if (planned.Action == Buffs.PotionAction)
                    potionIndex++;

                return WarriorData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (PotionDue(war))
        {
            potionIndex++;
            return WarriorData.Get(Buffs.PotionAction);
        }

        if (war.HasStatus(War.SurgingTempest) && Ready(war, job, War.InnerReleaseAction))
            return WarriorData.Get(War.InnerReleaseAction);

        if (Ready(war, job, War.PrimalWrath))
            return WarriorData.Get(War.PrimalWrath);

        if (Ready(war, job, War.Upheaval))
            return TargetChoice.Best(job, war, WarriorData.Get(War.Upheaval), WarriorData.Get(War.Orogeny));

        if (!war.HasStatus(War.NascentChaos)
            && war.Beast <= WarriorData.MaxBeastGauge - WarriorData.InfuriateGauge
            && Ready(war, job, War.InfuriateAction))
        {
            return WarriorData.Get(War.InfuriateAction);
        }

        if (war.HasStatus(War.InnerReleaseStatus) && Ready(war, job, War.Onslaught))
            return WarriorData.Get(War.Onslaught);

        if (war.Cooldown(War.Onslaught).ChargesAt(war.Time) >= WarriorData.Get(War.Onslaught).MaxCharges
            && Ready(war, job, War.Onslaught))
        {
            return WarriorData.Get(War.Onslaught);
        }

        return null;
    }

    private static ActionDef? First(WarriorState war, IJobSim job, params string[] names)
    {
        foreach (var name in names)
        {
            var action = WarriorData.Get(name);
            if (job.CanUse(action, war))
                return action;
        }

        return null;
    }

    /// Whether an ability can be pressed in the weave slot that is opening, rather than at this exact
    /// instant.
    private bool PotionDue(WarriorState war)
        => potionIndex < potionTimes.Count
           && war.Time + 1e-9 >= potionTimes[potionIndex]
           && war.Cooldown(Buffs.PotionAction).ChargesAt(war.Time) > 0;

    private static bool Ready(WarriorState war, IJobSim job, string name)
    {
        var action = WarriorData.Get(name);
        var earliest = System.Math.Max(war.Time, war.AnimationLockUntil);

        return (!war.HasCooldown(name) || war.Cooldown(name).ChargesAt(earliest) > 0)
               && job.CanUse(action, war);
    }
}
