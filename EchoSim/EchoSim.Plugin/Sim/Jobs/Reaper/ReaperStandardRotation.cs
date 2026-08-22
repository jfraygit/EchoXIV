using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Reaper;

/// Reaper's standard single-target rotation.
public sealed class ReaperStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    /// Set when the banked Enshroud goes in, cleared once Arcane Circle has been woven behind the Shadow of
    /// Death inside it.
    private bool burstShroudPending;

    /// Seconds of Death's Design left below which it gets refreshed ahead of anything else.
    private const double DesignRefreshWindow = 4.0;

    /// How long Arcane Circle is held back inside its weave window.
    private const double ArcaneCircleWeaveDelay = Simulator.StandardAnimationLock;

    /// Soulsow and Harpe genuinely are pressed before the pull, so unlike Monk's and Dragoon's this list is
    /// not empty.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("∞", "Soulsow", "Instant outside combat and free. It becomes Harvest Moon, which is 800 potency banked for whenever the fight next forces you out of melee range."),
        ("-1.7s", "Harpe", "A ranged 300-potency spell used purely to be already casting when the timer hits zero, so no time is lost walking in."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Rpr.ShadowOfDeath,
        Buffs.PotionAction,
        Rpr.SoulSlice,
        Rpr.ArcaneCircleAction,
        Rpr.Gluttony,
        Rpr.ExecutionersGibbet,
        Rpr.ExecutionersGallows,
        Rpr.SoulSlice,
        Rpr.PlentifulHarvest,
        Rpr.EnshroudAction,
        Rpr.Sacrificium,
        Rpr.VoidReaping,
        Rpr.CrossReaping,
        Rpr.LemuresSlice,
        Rpr.VoidReaping,
        Rpr.CrossReaping,
        Rpr.LemuresSlice,
        Rpr.Communio,
        Rpr.UnveiledGibbet,
        Rpr.Perfectio,
        Rpr.Gibbet,
        Rpr.ShadowOfDeath,
        Rpr.Slice,
        Rpr.SoulSlice,
        Rpr.UnveiledGallows,
        Rpr.Gallows,
    ];

    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's weaves, exactly as both guides print them.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(1, Rpr.ArcaneCircleAction),
        new(1, Rpr.Gluttony),
    ];

    /// The opener's GCDs, in order.
    private static readonly string[] Opener =
    [
        Rpr.ShadowOfDeath,
        Rpr.SoulSlice,
        Rpr.ExecutionersGibbet,
        Rpr.ExecutionersGallows,
        Rpr.SoulSlice,
        Rpr.PlentifulHarvest,
        Rpr.VoidReaping,
        Rpr.CrossReaping,
        Rpr.VoidReaping,
        Rpr.CrossReaping,
        Rpr.Communio,
        Rpr.Perfectio,
        Rpr.Gibbet,
        Rpr.ShadowOfDeath,
        Rpr.Slice,
        Rpr.SoulSlice,
        Rpr.Gallows,
    ];

    /// Whether the filler chain should be the two-step area one.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;
        burstShroudPending = false;

        areaChain = state.UseAreaRotation;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var rpr = (ReaperState)state;

        if (Weave(rpr, job, stats) is { } ability)
            return ability;

        if (rpr.Time + 1e-9 < rpr.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = ReaperData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, rpr))
                return planned;

            rpr.Count("opener.skipped", 1);
            rpr.Count($"opener.skipped:{planned.Name}@{openerIndex}", 1);
        }

        return NextGcd(rpr, job);
    }

    /// The GCD priority list.
    private ActionDef NextGcd(ReaperState rpr, IJobSim job)
    {
        if (rpr.HasStatus(Rpr.Enshrouded))
        {
            if (rpr.LemureShroud <= 1)
                return ReaperData.Get(Rpr.Communio);

            if (burstShroudPending && rpr.LemureShroud == ReaperData.LemureShroudStacks - 1)
            {
                burstShroudPending = false;

                if (rpr.StatusRemaining(Rpr.DeathsDesign) < BurstBridgeWindow)
                {
                    return TargetChoice.Best(
                        job, rpr, ReaperData.Get(Rpr.ShadowOfDeath), ReaperData.Get(Rpr.WhorlOfDeath));
                }
            }

            if (rpr.StatusRemaining(Rpr.DeathsDesign) < DesignRefreshWindow)
            {
                return TargetChoice.Best(
                    job, rpr, ReaperData.Get(Rpr.ShadowOfDeath), ReaperData.Get(Rpr.WhorlOfDeath));
            }

            var reaping = ReaperData.Get(rpr.HasStatus(Rpr.EnhancedCrossReaping) ? Rpr.CrossReaping : Rpr.VoidReaping);
            return TargetChoice.Best(job, rpr, reaping, ReaperData.Get(Rpr.GrimReaping));
        }

        var perfectio = ReaperData.Get(Rpr.Perfectio);
        if (job.CanUse(perfectio, rpr))
            return perfectio;

        var harvest = ReaperData.Get(Rpr.PlentifulHarvest);
        if (job.CanUse(harvest, rpr))
            return harvest;

        var executioner = ReaperData.Get(rpr.HasStatus(Rpr.EnhancedGallows)
            ? Rpr.ExecutionersGallows
            : Rpr.ExecutionersGibbet);

        if (job.CanUse(executioner, rpr))
            return TargetChoice.Best(job, rpr, executioner, ReaperData.Get(Rpr.ExecutionersGuillotine));

        var reaver = ReaperData.Get(rpr.HasStatus(Rpr.EnhancedGallows) ? Rpr.Gallows : Rpr.Gibbet);
        if (job.CanUse(reaver, rpr))
            return TargetChoice.Best(job, rpr, reaver, ReaperData.Get(Rpr.Guillotine));

        if (rpr.StatusRemaining(Rpr.DeathsDesign) < DesignRefreshWindow)
        {
            return TargetChoice.Best(
                job, rpr, ReaperData.Get(Rpr.ShadowOfDeath), ReaperData.Get(Rpr.WhorlOfDeath));
        }

        if (rpr.Soul <= ReaperData.MaxSoul - 50 && rpr.Cooldown(Rpr.SoulSlice).ChargesAt(Math.Max(rpr.Time, rpr.AnimationLockUntil)) > 0)
        {
            return TargetChoice.Best(
                job, rpr, ReaperData.Get(Rpr.SoulSlice), ReaperData.Get(Rpr.SoulScythe));
        }

        if (areaChain)
        {
            return rpr.IsComboReady(ReaperData.Get(Rpr.NightmareScythe))
                ? ReaperData.Get(Rpr.NightmareScythe)
                : ReaperData.Get(Rpr.SpinningScythe);
        }

        foreach (var step in new[] { Rpr.WaxingSlice, Rpr.InfernalSlice })
        {
            var action = ReaperData.Get(step);
            if (rpr.IsComboReady(action))
                return action;
        }

        return ReaperData.Get(Rpr.Slice);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(ReaperState rpr, IJobSim job, PlayerStats stats)
    {
        if (!WeaveFits(rpr, stats))
            return null;

        if (potionIndex < potionTimes.Count && rpr.Time + 1e-9 >= potionTimes[potionIndex]
            && Ready(rpr, job, Buffs.PotionAction))
        {
            potionIndex++;
            return ReaperData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(rpr, job, planned.Action))
            {
                openerWeaveIndex++;

                if (planned.Action == Rpr.ArcaneCircleAction)
                    rpr.WeaveDelay = ArcaneCircleWeaveDelay;

                return ReaperData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        foreach (var name in WeaveOrder)
        {
            if (name == Rpr.EnshroudAction && (rpr.HasStatus(Rpr.SoulReaver) || rpr.HasStatus(Rpr.Executioner)))
                continue;

            if (name == Rpr.EnshroudAction && HoldEnshroudForBurst(rpr))
                continue;

            if (name == Rpr.ArcaneCircleAction && burstShroudPending)
                continue;

            if (name == Rpr.Gluttony
                && (rpr.HasStatus(Rpr.ImmortalSacrifice) || rpr.HasStatus(Rpr.IdealHost)))
            {
                continue;
            }

            if (name == Rpr.Gluttony
                && !rpr.HasStatus(Rpr.Enshrouded)
                && UntilArcaneCircle(rpr) <= GluttonyClearWindow)
            {
                continue;
            }

            if (name is Rpr.UnveiledGibbet or Rpr.UnveiledGallows
                && rpr.Cooldown(Rpr.Gluttony).ChargesAt(Math.Max(rpr.Time, rpr.AnimationLockUntil)) > 0
                && rpr.Soul < ReaperData.SoulSpenderCost * 2)
            {
                continue;
            }

            if (name is Rpr.UnveiledGibbet or Rpr.UnveiledGallows
                && (rpr.HasStatus(Rpr.IdealHost)
                    || rpr.HasStatus(Rpr.ImmortalSacrifice)
                    || (rpr.Shroud >= ReaperData.EnshroudShroudCost && !HoldEnshroudForBurst(rpr))))
            {
                continue;
            }

            var action = ReaperData.Get(name);
            if (Ready(rpr, job, name))
            {
                if (name == Rpr.EnshroudAction && !rpr.HasStatus(Rpr.IdealHost) && IsBurstEnshroud(rpr))
                    burstShroudPending = true;

                return name is Rpr.UnveiledGibbet or Rpr.UnveiledGallows
                    ? TargetChoice.Best(job, rpr, action, ReaperData.Get(Rpr.GrimSwathe))
                    : action;
            }
        }

        return null;
    }

    /// Whether Enshroud is being banked for the two-minute rather than spent now.
    private static bool HoldEnshroudForBurst(ReaperState rpr)
    {
        if (rpr.HasStatus(Rpr.IdealHost))
            return false;

        var untilCircle = UntilArcaneCircle(rpr);
        return untilCircle > EnshroudLeadIn && untilCircle <= EnshroudBankWindow;
    }

    /// Whether this Enshroud is the banked one that opens a two-minute pair.
    private static bool IsBurstEnshroud(ReaperState rpr) => UntilArcaneCircle(rpr) <= EnshroudLeadIn;

    private static double UntilArcaneCircle(ReaperState rpr)
        => rpr.Cooldown(Rpr.ArcaneCircleAction).ReadyAt(rpr.Time) - rpr.Time;

    /// Seconds before Arcane Circle that the banked Enshroud goes in.
    private const double EnshroudLeadIn = 4.0;

    /// How near Arcane Circle has to be before Shroud is banked rather than spent.
    private const double EnshroudBankWindow = 30.0;

    /// How near Arcane Circle has to be before Gluttony is held rather than pressed.
    private const double GluttonyClearWindow = 10.0;

    /// Death's Design left, two globals into the banked window, below which the burst refresh happens.
    private const double BurstBridgeWindow = 24.0;

    /// Weave priority, in the order the reference parse presses them.
    private static readonly string[] WeaveOrder =
    [
        Rpr.ArcaneCircleAction,
        Rpr.Sacrificium,
        Rpr.LemuresSlice,
        Rpr.EnshroudAction,
        Rpr.Gluttony,
        Rpr.UnveiledGibbet,
        Rpr.UnveiledGallows,
    ];

    /// Whether an ability fits before the next GCD.
    private static bool WeaveFits(ReaperState rpr, PlayerStats stats)
        => WeavePlanner.CanWeave(rpr, stats);

    /// Whether an ability can be pressed in this weave window.
    private static bool Ready(ReaperState rpr, IJobSim job, string name)
    {
        var earliest = Math.Max(rpr.Time, rpr.AnimationLockUntil);

        return rpr.Cooldown(name).ChargesAt(earliest) > 0
               && job.CanUse(ReaperData.Get(name), rpr);
    }
}
