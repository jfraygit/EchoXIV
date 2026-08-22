using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Viper;

/// Viper's standard single-target rotation.
public sealed class ViperStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int potionIndex;

    /// Set when Serpent's Ire is woven, cleared by the next global.
    private bool burstComboPending;

    /// Nothing.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-0.5s", "Slither",
            "Closes to melee range so the first weaponskill lands the instant the timer hits zero. "
            + "Deals nothing and costs no global cooldown."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Vpr.ReavingFangs,
        Vpr.SerpentsIre,
        Vpr.SwiftskinsSting,
        Vpr.Vicewinder,
        Buffs.PotionAction,
        Vpr.HuntersCoil,
        Vpr.TwinfangBite,
        Vpr.TwinbloodBite,
        Vpr.SwiftskinsCoil,

        Vpr.TwinbloodBite,
        Vpr.TwinfangBite,
        Vpr.ReawakenAction,
        Vpr.FirstGeneration,
        Vpr.FirstLegacy,
        Vpr.SecondGeneration,
        Vpr.SecondLegacy,
        Vpr.ThirdGeneration,
        Vpr.ThirdLegacy,
        Vpr.FourthGeneration,
        Vpr.FourthLegacy,
        Vpr.Ouroboros,

        Vpr.UncoiledFury,
        Vpr.UncoiledTwinfang,
        Vpr.UncoiledTwinblood,
        Vpr.UncoiledFury,
        Vpr.UncoiledTwinfang,
        Vpr.UncoiledTwinblood,
        Vpr.HindsbaneFang,
        Vpr.DeathRattle,
    ];

    /// The opener's GCDs, in order.
    private static readonly string[] Opener =
    [
        Vpr.ReavingFangs,
        Vpr.SwiftskinsSting,
        Vpr.Vicewinder,
        Vpr.HuntersCoil,
        Vpr.SwiftskinsCoil,
        Vpr.ReawakenAction,
        Vpr.FirstGeneration,
        Vpr.SecondGeneration,
        Vpr.ThirdGeneration,
        Vpr.FourthGeneration,
        Vpr.Ouroboros,
        Vpr.UncoiledFury,
        Vpr.UncoiledFury,
        Vpr.HindsbaneFang,
    ];

    /// The four finisher branches, in the order the Venom cycle produces them, with the sting each one needs
    /// in front of it.
    private static readonly (string Sting, string Finisher)[] FinisherCycle =
    [
        (Vpr.HuntersSting, Vpr.FlankstingStrike),
        (Vpr.HuntersSting, Vpr.HindstingStrike),
        (Vpr.SwiftskinsSting, Vpr.FlanksbaneFang),
        (Vpr.SwiftskinsSting, Vpr.HindsbaneFang),
    ];

    /// Whether to run the area three-step chain instead of the single-target one.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = 0;
        potionIndex = 0;
        burstComboPending = false;

        areaChain = state.UseAreaRotation;

        ((ViperState)state).FinisherCycle = 3;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var vpr = (ViperState)state;

        if (Weave(vpr, job, stats) is { } ability)
            return ability;

        if (vpr.Time + 1e-9 < vpr.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = ViperData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, vpr))
                return planned;

            vpr.Count("opener.skipped", 1);
            vpr.Count($"opener.skipped:{planned.Name}@{openerIndex}", 1);
        }

        return NextGcd(vpr, job);
    }

    /// The GCD priority list.
    private ActionDef NextGcd(ViperState vpr, IJobSim job)
    {
        if (vpr.HasStatus(Vpr.Reawakened))
        {
            var inside = vpr.AnguineTribute switch
            {
                5 => Vpr.FirstGeneration,
                4 => Vpr.SecondGeneration,
                3 => Vpr.ThirdGeneration,
                2 => Vpr.FourthGeneration,
                _ => Vpr.Ouroboros,
            };

            return ViperData.Get(inside);
        }

        var burstFiller = burstComboPending;
        burstComboPending = false;

        var reawaken = ViperData.Get(Vpr.ReawakenAction);
        if (job.CanUse(reawaken, vpr) && !burstFiller && !HoldReawakenForBurst(vpr))
            return reawaken;

        foreach (var (coil, den) in new[]
                 {
                     (Vpr.HuntersCoil, Vpr.HuntersDen),
                     (Vpr.SwiftskinsCoil, Vpr.SwiftskinsDen),
                 })
        {
            var action = ViperData.Get(coil);
            if (job.CanUse(action, vpr))
                return TargetChoice.Best(job, vpr, action, ViperData.Get(den));
        }

        if (!burstFiller && vpr.Cooldown(Vpr.Vicewinder).ChargesAt(Math.Max(vpr.Time, vpr.AnimationLockUntil)) > 0)
        {
            return TargetChoice.Best(
                job, vpr, ViperData.Get(Vpr.Vicewinder), ViperData.Get(Vpr.Vicepit));
        }

        if (!burstFiller && vpr.RattlingCoil > 0)
            return ViperData.Get(Vpr.UncoiledFury);

        if (areaChain)
        {
            var bite = vpr.FinisherCycle < 2 ? Vpr.HuntersBite : Vpr.SwiftskinsBite;

            if (vpr.LastComboAction == bite)
                return ViperData.Get(vpr.FinisherCycle % 2 == 0 ? Vpr.JaggedMaw : Vpr.BloodiedMaw);

            if (vpr.LastComboAction is Vpr.SteelMaw or Vpr.ReavingMaw)
                return ViperData.Get(bite);

            return ViperData.Get(vpr.FinisherCycle < 2 ? Vpr.SteelMaw : Vpr.ReavingMaw);
        }

        var (sting, finisher) = FinisherCycle[vpr.FinisherCycle];

        if (vpr.LastComboAction == sting)
            return ViperData.Get(finisher);

        if (vpr.LastComboAction is Vpr.SteelFangs or Vpr.ReavingFangs)
            return ViperData.Get(sting);

        return ViperData.Get(vpr.FinisherCycle < 2 ? Vpr.SteelFangs : Vpr.ReavingFangs);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(ViperState vpr, IJobSim job, PlayerStats stats)
    {
        if (!WeaveFits(vpr, stats))
            return null;

        if (vpr.PendingLegacy is { } pending && Ready(vpr, job, pending))
            return ViperData.Get(pending);

        var potionHeld = openerIndex < Opener.Length && openerIndex <= OpenerPotionAfterGcd;

        if (!potionHeld && potionIndex < potionTimes.Count && vpr.Time + 1e-9 >= potionTimes[potionIndex]
            && Ready(vpr, job, Buffs.PotionAction))
        {
            potionIndex++;
            return ViperData.Get(Buffs.PotionAction);
        }

        if (Ready(vpr, job, Vpr.SerpentsIre) && !vpr.HasStatus(Vpr.ReadyToReawaken))
        {
            burstComboPending = openerIndex >= Opener.Length;
            return ViperData.Get(Vpr.SerpentsIre);
        }

        return null;
    }

    /// Whether a paid Reawaken is being banked for the two-minute rather than spent now.
    private static bool HoldReawakenForBurst(ViperState vpr)
    {
        if (vpr.HasStatus(Vpr.ReadyToReawaken))
            return false;

        var untilIre = vpr.Cooldown(Vpr.SerpentsIre).ReadyAt(vpr.Time) - vpr.Time;
        return untilIre <= ReawakenBankWindow;
    }

    /// How near Serpent's Ire has to be before Offerings are banked rather than spent.
    private const double ReawakenBankWindow = 30.0;

    /// Index into Opener of Vicewinder, which the opener's potion weaves behind.
    private const int OpenerPotionAfterGcd = 2;

    /// Whether an ability can be pressed in this weave window.
    private static bool Ready(ViperState vpr, IJobSim job, string name)
    {
        var earliest = Math.Max(vpr.Time, vpr.AnimationLockUntil);

        return vpr.Cooldown(name).ChargesAt(earliest) > 0
               && job.CanUse(ViperData.Get(name), vpr);
    }

    /// Whether an ability fits before the next GCD.
    private static bool WeaveFits(ViperState vpr, PlayerStats stats)
        => WeavePlanner.CanWeave(vpr, stats);
}
