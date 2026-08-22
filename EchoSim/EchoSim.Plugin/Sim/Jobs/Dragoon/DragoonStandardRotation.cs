using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Dragoon;

/// Dragoon's standard single-target rotation.
public sealed class DragoonStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    /// The Piercing Talon opening, which is what both guides lead with.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-15s", Drg.ElusiveJump,
            "Not for the backflip. It grants Enhanced Piercing Talon for fifteen seconds, which is "
            + "worth 150 potency on the pull below - so it is pressed exactly late enough to still be "
            + "running when the timer hits zero."),
        ("-0.85s", Drg.PiercingTalon,
            "The pull, from outside melee range, which is how most fights start. It gets the global "
            + "cooldown rolling before the boss is reachable and delays True Thrust just enough to "
            + "line Battle Litany up better with the party's own buffs."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Drg.TrueThrust,
        Buffs.PotionAction,
        Drg.SpiralBlow,
        Drg.LanceChargeAction,
        Drg.BattleLitanyAction,
        Drg.ChaoticSpring,
        Drg.Geirskogul,
        Drg.WheelingThrust,
        Drg.HighJump,
        Drg.LifeSurgeAction,
        Drg.Drakesbane,
        Drg.DragonfireDive,
        Drg.Nastrond,
        Drg.RaidenThrust,
        Drg.Stardiver,
        Drg.LanceBarrage,
        Drg.Starcross,
        Drg.LifeSurgeAction,
        Drg.HeavensThrust,
        Drg.RiseOfTheDragon,
        Drg.MirageDive,
        Drg.FangAndClaw,
        Drg.Drakesbane,
        Drg.RaidenThrust,
        Drg.WyrmwindThrust,
    ];

    /// The opener's GCDs, in order.
    private static readonly string[] Opener =
    [
        Drg.TrueThrust,
        Drg.SpiralBlow,
        Drg.ChaoticSpring,
        Drg.WheelingThrust,
        Drg.Drakesbane,
        Drg.RaidenThrust,
        Drg.LanceBarrage,
        Drg.HeavensThrust,
        Drg.FangAndClaw,
        Drg.Drakesbane,
        Drg.RaidenThrust,
    ];

    /// An opener ability and the opener global cooldown it follows, counted from zero.
    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's weaves, exactly as both guides print them.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(1, Drg.LanceChargeAction),
        new(1, Drg.BattleLitanyAction),
        new(2, Drg.Geirskogul),
        new(3, Drg.HighJump),
        new(3, Drg.LifeSurgeAction),
        new(4, Drg.DragonfireDive),
        new(4, Drg.Nastrond),
        new(5, Drg.Stardiver),
        new(6, Drg.Starcross),
        new(6, Drg.LifeSurgeAction),
        new(7, Drg.RiseOfTheDragon),
        new(7, Drg.MirageDive),
        new(10, Drg.WyrmwindThrust),
    ];

    /// Whether to run the three-global area chain instead of the five-global single-target one.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;

        areaChain = state.UseAreaRotation;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var drg = (DragoonState)state;

        if (Weave(drg, job, stats) is { } ability)
            return ability;

        if (drg.Time + 1e-9 < drg.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = DragoonData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, drg))
                return planned;

            drg.Count("opener.skipped", 1);
        }

        return NextGcd(drg, job);
    }

    /// The GCD loop: finish whichever line is running, otherwise start the next one.
    private ActionDef NextGcd(DragoonState drg, IJobSim job)
    {
        if (areaChain)
        {
            if (drg.LastComboAction == Drg.SonicThrust && drg.ComboExpiresAt > drg.Time)
                return DragoonData.Get(Drg.CoerthanTorment);

            if (drg.LastComboAction is Drg.DoomSpike or Drg.DraconianFury && drg.ComboExpiresAt > drg.Time)
                return DragoonData.Get(Drg.SonicThrust);

            return DragoonData.Get(drg.HasStatus(Drg.DraconianFire) ? Drg.DraconianFury : Drg.DoomSpike);
        }

        var preferred = drg.NextLineIsBuff
            ? new[] { Drg.SpiralBlow, Drg.ChaoticSpring, Drg.WheelingThrust }
            : new[] { Drg.LanceBarrage, Drg.HeavensThrust, Drg.FangAndClaw };

        var other = drg.NextLineIsBuff
            ? new[] { Drg.LanceBarrage, Drg.HeavensThrust, Drg.FangAndClaw }
            : new[] { Drg.SpiralBlow, Drg.ChaoticSpring, Drg.WheelingThrust };

        foreach (var step in preferred.Concat(other))
        {
            var action = DragoonData.Get(step);
            if (job.CanUse(action, drg))
                return action;
        }

        if (drg.LastComboAction is Drg.FangAndClaw or Drg.WheelingThrust)
            return DragoonData.Get(Drg.Drakesbane);

        var starter = DragoonData.Get(drg.HasStatus(Drg.DraconianFire) ? Drg.RaidenThrust : Drg.TrueThrust);
        return starter;
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(DragoonState drg, IJobSim job, PlayerStats stats)
    {
        if (!WeaveFits(drg, stats))
            return null;

        if (potionIndex < potionTimes.Count && drg.Time + 1e-9 >= potionTimes[potionIndex]
            && Ready(drg, job, Buffs.PotionAction))
        {
            potionIndex++;
            return DragoonData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(drg, job, planned.Action))
            {
                openerWeaveIndex++;
                return DragoonData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (NextIsBigWeaponskill(drg)
            && !drg.HasStatus(Drg.LifeSurge)
            && Ready(drg, job, Drg.LifeSurgeAction))
        {
            return DragoonData.Get(Drg.LifeSurgeAction);
        }

        foreach (var name in WeaveOrder)
        {
            if (!Ready(drg, job, name))
                continue;

            if (name is Drg.MirageDive or Drg.RiseOfTheDragon && !WeavePlanner.FitsWithoutClipping(drg))
                continue;

            return DragoonData.Get(name);
        }

        return null;
    }

    /// Whether the next weaponskill is one of the two worth a guaranteed critical hit.
    private static bool NextIsBigWeaponskill(DragoonState drg)
        => drg.ComboExpiresAt > drg.Time
           && drg.LastComboAction is Drg.FangAndClaw or Drg.WheelingThrust or Drg.LanceBarrage;

    /// Whether an ability can actually be pressed at the moment it would be pressed.
    private static bool Ready(DragoonState drg, IJobSim job, string name)
    {
        var earliest = System.Math.Max(drg.Time, drg.AnimationLockUntil);

        return drg.Cooldown(name).ChargesAt(earliest) > 0
               && job.CanUse(DragoonData.Get(name), drg);
    }

    /// Weave priority, in the order the reference parse presses them.
    private static readonly string[] WeaveOrder =
    [
        Drg.LanceChargeAction,
        Drg.BattleLitanyAction,
        Drg.Geirskogul,

        Drg.Nastrond,

        Drg.HighJump,
        Drg.DragonfireDive,
        Drg.Stardiver,
        Drg.Starcross,
        Drg.RiseOfTheDragon,
        Drg.MirageDive,
        Drg.WyrmwindThrust,
    ];

    /// Whether an ability fits before the next GCD.
    private static bool WeaveFits(DragoonState drg, PlayerStats stats)
        => WeavePlanner.CanWeave(drg, stats);
}
