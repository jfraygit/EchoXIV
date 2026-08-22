using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.BlackMage;

/// Black Mage's standard single-target rotation.
public sealed class BlackMageStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;

    /// How far through OpenerWeaves the opening is.
    private int openerWeaveIndex;

    /// Whether the scripted opening is still running, and so whether the reactive cooldown rules should stand
    /// aside for the plan.
    private bool InOpener => !areaChain && openerIndex < Opener.Length;
    private int potionIndex;

    /// Whether to run the area cycle.
    private bool areaChain;

    /// Fire III before the pull, which for this job is not an optimisation but the only way to start.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-4.0s", Blm.Fire3,
            "The only hard cast in the rotation. Puts the fight's first global cooldown in Astral "
            + "Fire III with a full 10,000 MP behind it, which is six Fire IV and a Flare Star."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Blm.Fire3,
        Blm.HighThunder,
        Blm.TriplecastAction,
        Blm.Amplifier,
        Blm.Fire4,
        Buffs.PotionAction,
        Blm.LeyLinesAction,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Xenoglossy,
        Blm.Manafont,
        Blm.Fire4,
        Blm.FlareStar,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.HighThunder,
        Blm.Fire4,
        Blm.FlareStar,
        Blm.Despair,
    ];

    /// The opener's global cooldowns, in order.
    private static readonly string[] Opener =
    [
        Blm.HighThunder,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Xenoglossy,
        Blm.Fire4,
        Blm.FlareStar,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.Fire4,
        Blm.HighThunder,
        Blm.Fire4,
        Blm.FlareStar,
        Blm.Despair,
    ];

    /// An ability the opener presses at a planned point rather than when a condition trips.
    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's weaves, in order, each named by the opener GCD it follows.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(0, Blm.TriplecastAction),
        new(0, Blm.Amplifier),

        new(1, Buffs.PotionAction),
        new(1, Blm.LeyLinesAction),

        new(6, Blm.Manafont),
    ];

    /// How far before the pull the Fire III goes, so it lands as the fight starts.
    private const double PrePullLead = 4.0;

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;
        areaChain = state.UseAreaRotation;

        var blm = (BlackMageState)state;

        blm.EnterAstralFire(BlackMageData.MaxElementStacks);
        blm.Mp = BlackMageData.MaxMp;
        blm.UmbralHearts = BlackMageData.MaxUmbralHearts;
        blm.ApplyStatus(Blm.Thunderhead, BlackMageData.ThunderheadDuration - PrePullLead);
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var blm = (BlackMageState)state;

        if (Weave(blm, job, stats) is { } ability)
            return ability;

        if (blm.Time + 1e-9 < blm.NextGcdAt)
            return null;

        if (!areaChain && openerIndex < Opener.Length)
        {
            var planned = BlackMageData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, blm))
                return planned;

            blm.Count("opener.skipped", 1);
            blm.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(blm, job);
    }

    /// The global cooldown priority, which is really two priorities with a phase test in front.
    private ActionDef NextGcd(BlackMageState blm, IJobSim job)
    {
        if (blm.HasStatus(Blm.Thunderhead)
            && blm.DotExpiresAt <= blm.Time + (blm.CurrentGcdLength > 0 ? blm.CurrentGcdLength : 0))
            return TargetChoice.Best(job, blm, BlackMageData.Get(Blm.HighThunder), BlackMageData.Get(Blm.HighThunder2));

        if (blm.Polyglot >= BlackMageData.MaxPolyglot - 1)
            return TargetChoice.Best(job, blm, BlackMageData.Get(Blm.Xenoglossy), BlackMageData.Get(Blm.Foul));

        if (blm.InAstralFire)
            return areaChain ? AreaFirePhase(blm, job) : FirePhase(blm, job);

        if (blm.InUmbralIce)
            return areaChain ? AreaIcePhase(blm, job) : IcePhase(blm, job);

        return BlackMageData.Get(Blm.Fire3);
    }

    /// The fire phase: spend the bar, in the order that gets the most out of it.
    private static ActionDef FirePhase(BlackMageState blm, IJobSim job)
    {
        if (blm.AstralFire < BlackMageData.MaxElementStacks)
        {
            var opener = BlackMageData.Get(Blm.Paradox);
            if (job.CanUse(opener, blm))
                return opener;

            var climb = BlackMageData.Get(Blm.Fire3);
            if (job.CanUse(climb, blm))
                return climb;
        }

        foreach (var name in FirePriority)
        {
            var action = BlackMageData.Get(name);
            if (job.CanUse(action, blm))
                return action;
        }

        var xeno = BlackMageData.Get(Blm.Xenoglossy);
        return job.CanUse(xeno, blm)
            ? TargetChoice.Best(job, blm, xeno, BlackMageData.Get(Blm.Foul))
            : BlackMageData.Get(Blm.Blizzard3);
    }

    /// The ice phase: get to three stacks, take the three hearts, and leave as soon as the bar is full.
    private static ActionDef IcePhase(BlackMageState blm, IJobSim job)
    {
        if (blm.UmbralIce < BlackMageData.MaxElementStacks)
            return BlackMageData.Get(Blm.Blizzard3);

        var blizzard4 = BlackMageData.Get(Blm.Blizzard4);
        if (blm.UmbralHearts < BlackMageData.MaxUmbralHearts && job.CanUse(blizzard4, blm))
            return blizzard4;

        var paradox = BlackMageData.Get(Blm.Paradox);
        if (job.CanUse(paradox, blm))
            return paradox;

        if (blm.Mp >= BlackMageData.MaxMp)
            return BlackMageData.Get(Blm.Fire3);

        foreach (var name in IceFiller)
        {
            var action = BlackMageData.Get(name);
            if (job.CanUse(action, blm))
                return action;
        }

        var blizzard4Again = BlackMageData.Get(Blm.Blizzard4);
        return job.CanUse(blizzard4Again, blm) ? blizzard4Again : BlackMageData.Get(Blm.Fire3);
    }

    /// The area fire phase: two Flares and a Flare Star, and that is the whole of it.
    private static ActionDef AreaFirePhase(BlackMageState blm, IJobSim job)
    {
        var flareStar = BlackMageData.Get(Blm.FlareStar);
        if (job.CanUse(flareStar, blm))
            return flareStar;

        var flare = BlackMageData.Get(Blm.Flare);
        if (job.CanUse(flare, blm))
            return flare;

        return AreaFiller(blm, job);
    }

    /// The area ice phase: one Freeze for the three hearts, then out.
    private static ActionDef AreaIcePhase(BlackMageState blm, IJobSim job)
    {
        var freeze = BlackMageData.Get(Blm.Freeze);
        if (blm.UmbralHearts < BlackMageData.MaxUmbralHearts && job.CanUse(freeze, blm))
            return freeze;

        return AreaFiller(blm, job);
    }

    /// What to press while waiting for the Transpose that changes phase.
    private static ActionDef AreaFiller(BlackMageState blm, IJobSim job)
    {
        foreach (var name in AreaFillers)
        {
            var action = BlackMageData.Get(name);
            if (job.CanUse(action, blm))
                return action;
        }

        return BlackMageData.Get(blm.InUmbralIce ? Blm.Freeze : Blm.Flare);
    }

    /// Spent in either phase, highest first.
    private static readonly string[] AreaFillers =
    [
        Blm.Foul,
        Blm.Paradox,
    ];

    private static readonly string[] FirePriority =
    [
        Blm.FlareStar,
        Blm.Paradox,
        Blm.Fire4,
        Blm.Despair,
    ];

    /// Spare ice-phase globals, in preference order.
    private static readonly string[] IceFiller =
    [
        Blm.Xenoglossy,
        Blm.HighThunder,
    ];

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(BlackMageState blm, IJobSim job, PlayerStats stats)
    {
        if (!WeavePlanner.CanWeave(blm, stats))
            return null;

        if (openerWeaveIndex >= OpenerWeaves.Length && PotionDue(blm))
        {
            potionIndex++;
            return BlackMageData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(blm, planned.Action)
                     && (planned.Action != Buffs.PotionAction || PotionDue(blm)))
            {
                openerWeaveIndex++;

                if (planned.Action == Buffs.PotionAction)
                    potionIndex++;

                return BlackMageData.Get(planned.Action);
            }
            else if (!areaChain && openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (!InOpener && blm.InAstralFire && blm.Mp < BlackMageData.DespairMinimumMp && Ready(blm, Blm.Manafont))
            return BlackMageData.Get(Blm.Manafont);

        if (blm.InAstralFire && blm.Mp < BlackMageData.DespairMinimumMp
            && blm.AstralSoul < BlackMageData.AstralSoulsForFlareStar && Ready(blm, Blm.Transpose))
        {
            return BlackMageData.Get(Blm.Transpose);
        }

        if (areaChain)
        {
            if (blm.InUmbralIce
                && blm.UmbralHearts >= BlackMageData.MaxUmbralHearts
                && blm.Mp >= BlackMageData.AreaFirePhaseMp
                && Ready(blm, Blm.Transpose))
            {
                return BlackMageData.Get(Blm.Transpose);
            }
        }
        else if (blm.InUmbralIce
                 && blm.UmbralIce >= BlackMageData.MaxElementStacks
                 && blm.UmbralHearts >= BlackMageData.MaxUmbralHearts
                 && blm.Mp >= BlackMageData.MaxMp
                 && !blm.ParadoxReady
                 && Ready(blm, Blm.Transpose))
        {
            return BlackMageData.Get(Blm.Transpose);
        }

        if (!WeavePlanner.FitsWithoutClipping(blm))
            return null;

        if (Ready(blm, Blm.LeyLinesAction) && !blm.HasStatus(Blm.LeyLinesStatus))
            return BlackMageData.Get(Blm.LeyLinesAction);

        if ((blm.InAstralFire || blm.InUmbralIce)
            && blm.Polyglot < BlackMageData.MaxPolyglot
            && Ready(blm, Blm.Amplifier))
        {
            return BlackMageData.Get(Blm.Amplifier);
        }

        var upcoming = NextGcd(blm, job);
        var needsCover = upcoming.CastTime > 3.0 && job.CastTimeOf(upcoming, blm) > 0;

        if (needsCover && !blm.HasStatus(Blm.TriplecastStatus) && !blm.HasStatus(Blm.SwiftcastStatus))
        {
            if (Ready(blm, Blm.SwiftcastAction))
                return BlackMageData.Get(Blm.SwiftcastAction);

            if (Ready(blm, Blm.TriplecastAction))
                return BlackMageData.Get(Blm.TriplecastAction);
        }

        if (!blm.HasStatus(Blm.TriplecastStatus)
            && blm.Cooldown(Blm.TriplecastAction).ChargesAt(blm.Time) >= BlackMageData.Get(Blm.TriplecastAction).MaxCharges)
        {
            return BlackMageData.Get(Blm.TriplecastAction);
        }

        return null;
    }

    /// Whether an ability is off recast, asked at the moment it could actually be pressed.
    private bool PotionDue(BlackMageState blm)
        => potionIndex < potionTimes.Count
           && blm.Time + 1e-9 >= potionTimes[potionIndex]
           && blm.Cooldown(Buffs.PotionAction).ChargesAt(blm.Time) > 0;

    private static bool Ready(BlackMageState blm, string name)
        => blm.Cooldown(name).ChargesAt(Math.Max(blm.Time, blm.AnimationLockUntil)) > 0;

}
