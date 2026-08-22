using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Samurai;

/// Samurai's standard single-target rotation.
public sealed class SamuraiStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    /// How long a buff may have left before the rotation goes out of its way to refresh it.
    private const double RefreshThreshold = 15.0;

    /// Seconds of Higanbana left below which the next single Sen is spent re-applying it.
    private const double HiganbanaRefreshWindow = 10.0;

    /// The same threshold while Meikyo Shisui is running, where the single Sen is free.
    private const double HiganbanaBurstWindow = 20.0;

    /// How close Senei has to be to coming off cooldown before Shinten stops taking its Kenki.
    private const double SeneiReserveWindow = 16.0;

    /// Meikyo Shisui really is pressed before the pull, so unlike Monk's and Dragoon's this list is not empty
    /// - but it holds only the two actions a player actually presses.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-14s", Sam.MeikyoAction, "Off the global cooldown and pressed early so it is charging again by the time the opener needs the second one. Its three free weaponskills are what let Gekko and Kasha open the fight directly, putting Fugetsu and Fuka up by the second GCD instead of the fourth. Fourteen seconds is the latest it can be, not a rough figure: the window lasts twenty, the third free weaponskill is the fifth-second Yukikaze, and the reference parse lands that Yukikaze at 5.66s with 0.34s to spare."),
        ("-5s", "True North", "Gekko wants the rear and Kasha the flank, and the opener presses both before there is any time to move. Every potency figure this simulation uses for them assumes the positional lands."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Sam.Gekko,
        Buffs.PotionAction,
        Sam.Kasha,
        Sam.Ikishoten,
        Sam.Yukikaze,
        Sam.TendoSetsugekka,
        Sam.Senei,
        Sam.TendoKaeshiSetsugekka,
        Sam.MeikyoAction,
        Sam.Gekko,
        Sam.Zanshin,
        Sam.Higanbana,
        Sam.OgiNamikiri,
        Sam.Shoha,
        Sam.KaeshiNamikiri,
        Sam.Kasha,
        Sam.Shinten,
        Sam.Gekko,
        Sam.Gyoten,
        Sam.Gyofu,
        Sam.Yukikaze,
        Sam.Shinten,
        Sam.TendoSetsugekka,
        Sam.TendoKaeshiSetsugekka,
    ];

    /// An opener ability and the opener global cooldown it follows, counted from zero.
    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's weaves, exactly as both guides print them and as the reference log presses them.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(1, Sam.Ikishoten),
        new(3, Sam.Senei),
        new(4, Sam.MeikyoAction),
        new(5, Sam.Zanshin),
        new(7, Sam.Shoha),
        new(9, Sam.Shinten),
        new(10, Sam.Gyoten),
        new(12, Sam.Shinten),
    ];

    /// The opener's GCDs, in order.
    private static readonly string[] Opener =
    [
        Sam.Gekko,
        Sam.Kasha,
        Sam.Yukikaze,
        Sam.TendoSetsugekka,
        Sam.TendoKaeshiSetsugekka,
        Sam.Gekko,
        Sam.Higanbana,
        Sam.OgiNamikiri,
        Sam.KaeshiNamikiri,
        Sam.Kasha,
        Sam.Gekko,
        Sam.Gyofu,
        Sam.Yukikaze,
        Sam.TendoSetsugekka,
        Sam.TendoKaeshiSetsugekka,
    ];

    /// Whether to run the area combo and the area iaijutsu instead of the single-target ones.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;

        areaChain = state.UseAreaRotation;

        var sam = (SamuraiState)state;

        sam.ApplyStatus(Sam.Meikyo, SamuraiData.MeikyoDuration - 14.0, stacks: SamuraiData.MeikyoStacks);
        sam.ApplyStatus(Sam.Tendo, SamuraiData.TendoDuration - 14.0);
        sam.Cooldown(Sam.MeikyoAction).Use(-14.0);
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var sam = (SamuraiState)state;

        if (Weave(sam, job, stats) is { } ability)
            return ability;

        if (sam.Time + 1e-9 < sam.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = SamuraiData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, sam))
                return planned;

            sam.Count("opener.skipped", 1);
            sam.Count($"opener.skipped:{planned.Name}@{openerIndex}", 1);
        }

        return NextGcd(sam, job);
    }

    /// The GCD priority list.
    private ActionDef NextGcd(SamuraiState sam, IJobSim job)
    {
        var repeats = areaChain
            ? new[] { Sam.TendoKaeshiGoken, Sam.KaeshiGoken, Sam.KaeshiNamikiri, Sam.OgiNamikiri }
            : new[] { Sam.TendoKaeshiSetsugekka, Sam.KaeshiSetsugekka, Sam.KaeshiNamikiri, Sam.OgiNamikiri };

        foreach (var name in repeats)
        {
            var repeat = SamuraiData.Get(name);
            if (job.CanUse(repeat, sam))
                return repeat;
        }

        if (areaChain)
        {
            if (sam.SenCount == 2)
            {
                var goken = SamuraiData.Get(sam.HasStatus(Sam.Tendo) ? Sam.TendoGoken : Sam.TenkaGoken);
                if (job.CanUse(goken, sam))
                    return goken;
            }

            if (sam.HasStatus(Sam.Meikyo) || sam.LastComboAction == Sam.Fuko)
                return SamuraiData.Get(!sam.Getsu ? Sam.Mangetsu : Sam.Oka);

            return SamuraiData.Get(Sam.Fuko);
        }

        var higanbanaLeft = sam.HiganbanaExpiresAt - sam.Time;
        var refreshWindow = sam.HasStatus(Sam.Meikyo) ? HiganbanaBurstWindow : HiganbanaRefreshWindow;

        if (sam.SenCount == 1 && higanbanaLeft < refreshWindow)
            return SamuraiData.Get(Sam.Higanbana);

        if (sam.SenCount == 3)
        {
            var iaijutsu = SamuraiData.Get(sam.HasStatus(Sam.Tendo) ? Sam.TendoSetsugekka : Sam.MidareSetsugekka);
            if (job.CanUse(iaijutsu, sam))
                return iaijutsu;
        }

        if (sam.HasStatus(Sam.Meikyo))
        {
            if (!sam.Getsu)
                return SamuraiData.Get(Sam.Gekko);

            if (!sam.Ka)
                return SamuraiData.Get(Sam.Kasha);

            if (!sam.Setsu)
                return SamuraiData.Get(Sam.Yukikaze);

            return SamuraiData.Get(Sam.Gekko);
        }

        if (sam.LastComboAction == Sam.Jinpu)
            return SamuraiData.Get(Sam.Gekko);

        if (sam.LastComboAction == Sam.Shifu)
            return SamuraiData.Get(Sam.Kasha);

        if (sam.LastComboAction == Sam.Gyofu)
            return SamuraiData.Get(ChooseLine(sam));

        return SamuraiData.Get(Sam.Gyofu);
    }

    /// Which of the three lines to run out of Gyofu.
    private static string ChooseLine(SamuraiState sam)
    {
        if (sam.StatusRemaining(Sam.Fugetsu) < RefreshThreshold)
            return Sam.Jinpu;

        if (sam.StatusRemaining(Sam.Fuka) < RefreshThreshold)
            return Sam.Shifu;

        if (!sam.Setsu)
            return Sam.Yukikaze;

        if (!sam.Getsu)
            return Sam.Jinpu;

        if (!sam.Ka)
            return Sam.Shifu;

        return Sam.Yukikaze;
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(SamuraiState sam, IJobSim job, PlayerStats stats)
    {
        if (!WeaveFits(sam, stats))
            return null;

        if (potionIndex < potionTimes.Count && sam.Time + 1e-9 >= potionTimes[potionIndex]
            && Ready(sam, job, Buffs.PotionAction))
        {
            potionIndex++;
            return SamuraiData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(sam, job, planned.Action))
            {
                openerWeaveIndex++;
                return SamuraiData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        foreach (var name in WeaveOrder)
        {
            if (name == Sam.MeikyoAction && (sam.HasStatus(Sam.Meikyo) || sam.HasStatus(Sam.Tendo)))
                continue;


            if (name is Sam.Shinten or Sam.Gyoten
                && sam.HasStatus(Sam.ZanshinReady) && sam.Kenki < SamuraiData.ZanshinCost)
            {
                continue;
            }

            if (name == Sam.Shinten && sam.Kenki < SamuraiData.ShintenCost + SamuraiData.SeneiCost)
            {
                var earliest = Math.Max(sam.Time, sam.AnimationLockUntil);
                if (sam.Cooldown(Sam.Senei).ReadyAt(earliest) - earliest <= SeneiReserveWindow)
                    continue;
            }

            var action = name switch
            {
                Sam.Shinten => TargetChoice.Best(job, sam, SamuraiData.Get(Sam.Shinten), SamuraiData.Get(Sam.Kyuten)),
                Sam.Senei => TargetChoice.Best(job, sam, SamuraiData.Get(Sam.Senei), SamuraiData.Get(Sam.Guren)),
                _ => SamuraiData.Get(name),
            };

            if (Ready(sam, job, name) && job.CanUse(action, sam))
                return action;
        }

        return null;
    }

    /// Whether an ability can be pressed in the weave window that is about to open.
    private static bool Ready(SamuraiState sam, IJobSim job, string name)
    {
        var earliest = Math.Max(sam.Time, sam.AnimationLockUntil);

        return sam.Cooldown(name).ChargesAt(earliest) > 0
               && job.CanUse(SamuraiData.Get(name), sam);
    }

    /// Weave priority, in the order the reference parse presses them.
    private static readonly string[] WeaveOrder =
    [
        Sam.Ikishoten,
        Sam.Zanshin,
        Sam.Senei,
        Sam.Shoha,
        Sam.MeikyoAction,
        Sam.Shinten,
    ];

    /// Whether an ability fits before the next GCD.
    private static bool WeaveFits(SamuraiState sam, PlayerStats stats)
        => WeavePlanner.CanWeave(sam, stats);
}
