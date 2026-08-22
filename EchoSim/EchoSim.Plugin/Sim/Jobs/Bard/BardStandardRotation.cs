using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Bard;

/// Bard's standard single-target rotation.
public sealed class BardStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's weaves, in order, each after the global it was recorded against.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(-1, Brd.HeartbreakShot),
        new(0, Brd.WanderersMinuet),

        new(1, Buffs.PotionAction),
        new(1, Brd.BattleVoiceAction),
        new(2, Brd.RadiantFinaleAction),
        new(2, Brd.RagingStrikesAction),
        new(3, Brd.BarrageAction),
        new(3, Brd.Sidewinder),
        new(4, Brd.EmpyrealArrow),
        new(4, Brd.HeartbreakShot),
        new(5, Brd.PitchPerfect),
        new(5, Brd.HeartbreakShot),
        new(6, Brd.HeartbreakShot),
    ];

    /// THE OPENER'S POTION IS NOW A PLANNED WEAVE, ahead of Battle Voice in the same window, and this
    /// constant is gone with the branch that used it.
    private const bool OpenerPotionIsPlanned = true;

    /// Nothing pressed before the pull.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps = [];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Brd.HeartbreakShot,
        Brd.Stormbite,
        Brd.WanderersMinuet,
        Brd.CausticBite,
        Buffs.PotionAction,
        Brd.BattleVoiceAction,
        Brd.BurstShot,
        Brd.RadiantFinaleAction,
        Brd.RagingStrikesAction,
        Brd.BurstShot,
        Brd.BarrageAction,
        Brd.Sidewinder,
        Brd.RefulgentArrow,
        Brd.EmpyrealArrow,
        Brd.HeartbreakShot,
        Brd.RadiantEncore,
        Brd.PitchPerfect,
        Brd.HeartbreakShot,
        Brd.ResonantArrow,
        Brd.HeartbreakShot,
        Brd.RefulgentArrow,
        Brd.BurstShot,
        Brd.IronJaws,
    ];

    /// The opener's GCDs, in order, as observed on the reference parse.
    private static readonly string[] Opener =
    [
        Brd.Stormbite,
        Brd.CausticBite,
        Brd.BurstShot,
        Brd.BurstShot,
        Brd.RefulgentArrow,
        Brd.RadiantEncore,
        Brd.ResonantArrow,
    ];

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var brd = (BardState)state;

        if (Weave(brd, job, stats) is { } ability)
            return ability;

        if (brd.Time + 1e-9 < brd.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = BardData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, brd))
                return planned;

            brd.Count("opener.skipped", 1);
            brd.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(brd, job);
    }

    /// The GCD priority.
    private ActionDef NextGcd(BardState brd, IJobSim job)
    {
        if (DotsNeedRefreshing(brd))
            return BardData.Get(Brd.IronJaws);

        if (brd.CausticExpiresAt <= 0)
            return BardData.Get(Brd.CausticBite);

        if (brd.StormExpiresAt <= 0)
            return BardData.Get(Brd.Stormbite);

        foreach (var name in GcdPriority)
        {
            var action = BardData.Get(name);

            if (name == Brd.ApexArrow && !ApexIsDue(brd))
                continue;

            if (name == Brd.RadiantEncore
                && !brd.HasStatus(Brd.RagingStrikes)
                && brd.StatusRemaining(Brd.RadiantEncoreReady) > BardData.EncoreHoldFloor)
            {
                continue;
            }

            if (job.CanUse(action, brd))
            {
                return name == Brd.RefulgentArrow
                    ? TargetChoice.Best(job, brd, action, BardData.Get(Brd.Shadowbite))
                    : action;
            }
        }

        return TargetChoice.Best(job, brd, BardData.Get(Brd.BurstShot), BardData.Get(Brd.Ladonsbite));
    }

    /// Whether Iron Jaws is due.
    private static bool DotsNeedRefreshing(BardState brd)
    {
        if (brd.CausticExpiresAt <= 0 || brd.StormExpiresAt <= 0)
            return false;

        var soonest = System.Math.Min(brd.CausticExpiresAt, brd.StormExpiresAt);
        return soonest - brd.Time <= 2.5;
    }

    private static bool ApexIsDue(BardState brd)
        => brd.SoulVoice >= BardData.MaxSoulVoice
           || (brd.SoulVoice >= BardData.BlastArrowThreshold && brd.HasStatus(Brd.RagingStrikes));

    /// Weaponskills worth pressing over filler, highest first.
    private static readonly string[] GcdPriority =
    [
        Brd.BlastArrow,
        Brd.RadiantEncore,
        Brd.ResonantArrow,
        Brd.ApexArrow,
        Brd.RefulgentArrow,
    ];

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(BardState brd, IJobSim job, PlayerStats stats)
    {
        if (openerWeaveIndex < OpenerWeaves.Length
            && OpenerWeaves[openerWeaveIndex] is { AfterGcd: < 0 } pull
            && openerIndex == 0)
        {
            openerWeaveIndex++;

            if (Ready(brd, pull.Action) && job.CanUse(BardData.Get(pull.Action), brd))
                return BardData.Get(pull.Action);
        }

        if (!WeavePlanner.CanWeave(brd, stats))
            return null;

        if (OpenerPotionIsPlanned && openerWeaveIndex >= OpenerWeaves.Length && PotionDue(brd))
        {
            potionIndex++;
            return BardData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd
                     && Ready(brd, planned.Action)
                     && job.CanUse(BardData.Get(planned.Action), brd)
                     && (planned.Action != Buffs.PotionAction || PotionDue(brd)))
            {
                openerWeaveIndex++;

                if (planned.Action == Buffs.PotionAction)
                    potionIndex++;

                return BardData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (NextSong(brd) is { } song)
            return song;

        foreach (var name in new[] { Brd.BattleVoiceAction, Brd.RadiantFinaleAction, Brd.RagingStrikesAction, Brd.BarrageAction })
        {
            if (name == Brd.RadiantFinaleAction && brd.CurrentSong != Song.Minuet)
                continue;

            if (Ready(brd, name) && job.CanUse(BardData.Get(name), brd))
                return BardData.Get(name);
        }

        if (brd.PitchPerfectStacks >= BardData.MaxPitchPerfect
            && Ready(brd, Brd.PitchPerfect) && job.CanUse(BardData.Get(Brd.PitchPerfect), brd))
        {
            return BardData.Get(Brd.PitchPerfect);
        }

        if (Ready(brd, Brd.EmpyrealArrow))
            return BardData.Get(Brd.EmpyrealArrow);

        if (Ready(brd, Brd.Sidewinder))
            return BardData.Get(Brd.Sidewinder);

        if (Ready(brd, Brd.PitchPerfect) && job.CanUse(BardData.Get(Brd.PitchPerfect), brd)
            && (brd.PitchPerfectStacks >= BardData.MaxPitchPerfect || MinuetIsEnding(brd)))
        {
            return BardData.Get(Brd.PitchPerfect);
        }

        if (Ready(brd, Brd.HeartbreakShot))
        {
            return TargetChoice.Best(
                job, brd, BardData.Get(Brd.HeartbreakShot), BardData.Get(Brd.RainOfDeath));
        }

        return null;
    }

    /// The song cycle: Minuet, then Ballad, then Paeon, on the parse's own timings.
    private static ActionDef? NextSong(BardState brd)
    {
        var elapsed = brd.Time - brd.SongStartedAt;

        switch (brd.CurrentSong)
        {
            case Song.None:
                foreach (var name in new[] { Brd.WanderersMinuet, Brd.MagesBallad, Brd.ArmysPaeon })
                {
                    if (Ready(brd, name))
                        return BardData.Get(name);
                }

                return null;

            case Song.Minuet when elapsed >= BardData.MinuetHold && Ready(brd, Brd.MagesBallad):
                return BardData.Get(Brd.MagesBallad);

            case Song.Ballad when elapsed >= BardData.BalladHold && Ready(brd, Brd.ArmysPaeon):
                return BardData.Get(Brd.ArmysPaeon);

            case Song.Paeon when Ready(brd, Brd.WanderersMinuet):
                return BardData.Get(Brd.WanderersMinuet);

            default:
                return null;
        }
    }

    /// Whether Minuet is close enough to being swapped that banked stacks would be lost.
    private static bool MinuetIsEnding(BardState brd)
        => brd.CurrentSong == Song.Minuet && brd.Time - brd.SongStartedAt >= BardData.MinuetHold - 3.0;

    /// Whether an ability can be pressed in this weave window.
    private bool PotionDue(BardState brd)
        => potionIndex < potionTimes.Count
           && brd.Time + 1e-9 >= potionTimes[potionIndex]
           && Ready(brd, Buffs.PotionAction);

    private static bool Ready(BardState brd, string name)
        => brd.Cooldown(name).ChargesAt(Math.Max(brd.Time, brd.AnimationLockUntil)) > 0;
}
