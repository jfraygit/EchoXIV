using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.DarkKnight;

/// Dark Knight's standard single-target rotation.
public sealed class DarkKnightStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    /// How little MP the rotation will let itself hold, outside a Darkside emergency.
    private const int MpReserve = DarkKnightData.EdgeOfShadowCost;

    /// Above this the pool is close enough to full that regeneration is at risk, so Edge of Shadow goes
    /// regardless of the reserve.
    private const int MpSpendFreelyAbove = DarkKnightData.MaxMp - DarkKnightData.EdgeOfShadowCost;

    /// Darkside remaining below which a top-up outranks every other weave.
    private const double DarksideFloor = 10.0;

    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-1.0s", Drk.Unmend,
            "The pull. The Balance: \"Unmend is used to delay the opener slightly to account for raid "
            + "buff application time, as well as to start our GCD rolling as early as possible.\" The "
            + "simulation starts its clock on Hard Slash, because a global cooldown spent before the "
            + "timer is free to a player and is not free to a fixed-length model."),
        ("-0.5s", Buffs.PotionAction,
            "Behind Unmend, which is where both guides put it - Icy Veins: use the Gemdraught "
            + "\"shortly before your Hard Slash\". A player pays nothing for it there. THE MODEL "
            + "PRESSES IT AT 1.20s INSTEAD, in the first weave slot after Hard Slash, and that is "
            + "deliberate: the pull slot already belongs to Edge of Shadow, so taking it for the "
            + "potion as well would push Hard Slash and the entire fight back six tenths of a second "
            + "to buy 1.2 seconds of a thirty-second buff over the weakest weaponskill in the job. "
            + "The slot it does take is empty either way, so the model pays nothing for it."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Drk.EdgeOfShadow,
        Drk.HardSlash,
        Buffs.PotionAction,
        Drk.LivingShadow,
        Drk.SyphonStrike,
        Drk.Souleater,
        Drk.DeliriumAction,
        Drk.Disesteem,
        Drk.SaltedEarth,
        Drk.EdgeOfShadow,
        Drk.ScarletDelirium,
        Drk.Shadowbringer,
        Drk.EdgeOfShadow,
        Drk.Comeuppance,
        Drk.CarveAndSpit,
        Drk.EdgeOfShadow,
        Drk.Torcleaver,
        Drk.Shadowbringer,
        Drk.Bloodspiller,
        Drk.SaltAndDarkness,
    ];

    /// The opener's global cooldowns.
    private static readonly string[] Opener =
    [
        Drk.HardSlash,
        Drk.SyphonStrike,
        Drk.Souleater,
        Drk.Disesteem,
        Drk.ScarletDelirium,
        Drk.Comeuppance,
        Drk.Torcleaver,
        Drk.Bloodspiller,
    ];

    /// An ability the opener presses at a planned point rather than when a condition trips.
    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's weaves, in order, each named by the opener global cooldown it follows.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(-1, Drk.EdgeOfShadow),
        new(0, Drk.LivingShadow),
        new(2, Drk.DeliriumAction),
        new(3, Drk.SaltedEarth),
        new(3, Drk.EdgeOfShadow),
        new(4, Drk.Shadowbringer),
        new(4, Drk.EdgeOfShadow),
        new(5, Drk.CarveAndSpit),
        new(5, Drk.EdgeOfShadow),
        new(6, Drk.Shadowbringer),
        new(7, Drk.SaltAndDarkness),
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
        var drk = (DarkKnightState)state;

        if (Weave(drk, job, stats) is { } ability)
            return ability;

        if (drk.Time + 1e-9 < drk.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = DarkKnightData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, drk))
                return planned;

            drk.Count("opener.skipped", 1);
            drk.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(drk, job);
    }

    /// The global cooldown priority.
    private ActionDef NextGcd(DarkKnightState drk, IJobSim job)
    {
        if (First(drk, job, Drk.Souleater, Drk.SyphonStrike) is { } pending)
            return pending;

        if (First(drk, job, Drk.Torcleaver, Drk.Comeuppance, Drk.ScarletDelirium) is { } delirium)
            return TargetChoice.Best(job, drk, delirium, DarkKnightData.Get(Drk.Impalement));

        if (First(drk, job, Drk.Disesteem) is { } disesteem)
            return disesteem;

        if (drk.Blood >= DarkKnightData.BloodspillerCost && First(drk, job, Drk.Bloodspiller) is { } spiller)
            return TargetChoice.Best(job, drk, spiller, DarkKnightData.Get(Drk.Quietus));

        if (areaChain)
        {
            return DarkKnightData.Get(
                drk.IsComboReady(DarkKnightData.Get(Drk.StalwartSoul)) ? Drk.StalwartSoul : Drk.Unleash);
        }

        return DarkKnightData.Get(Drk.HardSlash);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(DarkKnightState drk, IJobSim job, PlayerStats stats)
    {
        if (openerWeaveIndex < OpenerWeaves.Length
            && OpenerWeaves[openerWeaveIndex] is { AfterGcd: < 0 } pull
            && openerIndex == 0)
        {
            openerWeaveIndex++;

            if (Ready(drk, job, pull.Action))
                return DarkKnightData.Get(pull.Action);
        }

        if (!WeavePlanner.CanWeave(drk, stats))
            return null;

        if (potionIndex < potionTimes.Count && drk.Time + 1e-9 >= potionTimes[potionIndex]
            && drk.Cooldown(Buffs.PotionAction).ChargesAt(drk.Time) > 0)
        {
            potionIndex++;
            return DarkKnightData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(drk, job, planned.Action))
            {
                openerWeaveIndex++;
                return DarkKnightData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (drk.HasStatus(Drk.Darkside)
            && drk.StatusRemaining(Drk.Darkside) < DarksideFloor
            && Ready(drk, job, Drk.EdgeOfShadow))
        {
            return EdgeOrFlood(drk, job);
        }

        if (Ready(drk, job, Drk.LivingShadow))
            return DarkKnightData.Get(Drk.LivingShadow);

        if (Ready(drk, job, Drk.DeliriumAction))
            return DarkKnightData.Get(Drk.DeliriumAction);

        if (Ready(drk, job, Drk.Shadowbringer))
            return DarkKnightData.Get(Drk.Shadowbringer);

        if (Ready(drk, job, Drk.CarveAndSpit))
        {
            return TargetChoice.Best(
                job, drk, DarkKnightData.Get(Drk.CarveAndSpit), DarkKnightData.Get(Drk.AbyssalDrain));
        }

        if (Ready(drk, job, Drk.SaltAndDarkness))
            return DarkKnightData.Get(Drk.SaltAndDarkness);

        if (Ready(drk, job, Drk.SaltedEarth))
            return DarkKnightData.Get(Drk.SaltedEarth);

        var mp = drk.MpAt(drk.Time);

        if (Ready(drk, job, Drk.EdgeOfShadow)
            && (mp >= MpSpendFreelyAbove || mp >= MpReserve + DarkKnightData.EdgeOfShadowCost))
        {
            return EdgeOrFlood(drk, job);
        }

        return null;
    }

    /// The mana spender, chosen on its own arithmetic rather than with the combo.
    private static ActionDef EdgeOrFlood(DarkKnightState drk, IJobSim job)
        => TargetChoice.Best(
            job, drk, DarkKnightData.Get(Drk.EdgeOfShadow), DarkKnightData.Get(Drk.FloodOfShadow));

    private static ActionDef? First(DarkKnightState drk, IJobSim job, params string[] names)
    {
        foreach (var name in names)
        {
            var action = DarkKnightData.Get(name);
            if (job.CanUse(action, drk))
                return action;
        }

        return null;
    }

    /// Whether an ability can be pressed in the weave slot that is opening, rather than at this exact
    /// instant.
    private static bool Ready(DarkKnightState drk, IJobSim job, string name)
    {
        var action = DarkKnightData.Get(name);
        var earliest = System.Math.Max(drk.Time, drk.AnimationLockUntil);

        return (!drk.HasCooldown(name) || drk.Cooldown(name).ChargesAt(earliest) > 0)
               && job.CanUse(action, drk);
    }
}
