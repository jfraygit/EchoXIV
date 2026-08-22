using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Pictomancer;

/// Pictomancer's standard single-target rotation.
public sealed class PictomancerStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// Everything paintable goes down before the pull, which is free - motifs cast instantly out of combat,
    /// and Rainbow Drip is the only 4.0s cast the job ever wants to pay for.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-20s", Pct.CreatureMotif, "Free out of combat, and the creature canvas has to be full "
            + "before the first Living Muse charge is worth anything."),
        ("-16s", Pct.WeaponMotif, "Likewise for the hammer chain."),
        ("-12s", Pct.LandscapeMotif, "The two-minute window will not wait four seconds for this."),
        ("-4.5s", Pct.RainbowDrip, "1,000 potency and a White Paint, landing as the fight starts. "
            + "The only Rainbow Drip in the rotation that pays full price for its cast."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Pct.RainbowDrip,

        Buffs.PotionAction, Pct.LivingMuse, Pct.StrikingMuse,
        Pct.CreatureMotif, Pct.StarryMuse,
        Pct.HammerStamp, Pct.SubtractivePaletteAction,
        Pct.BlizzardInCyan,
        Pct.StoneInYellow,
        Pct.ThunderInMagenta,
        Pct.CometInBlack, Pct.LivingMuse, Pct.MogOfTheAges,
        Pct.StarPrism,
        Pct.HammerBrush,
        Pct.PolishingHammer,
        Pct.RainbowDrip,
        Pct.FireInRed,
        Pct.AeroInGreen,
    ];

    /// The opener's global cooldowns - the "2nd GCD Starry" opener both guides publish.
    private static readonly string[] Opener =
    [
        Pct.CreatureMotif,
        Pct.HammerStamp,
        Pct.BlizzardInCyan,
        Pct.StoneInYellow,
        Pct.ThunderInMagenta,
        Pct.CometInBlack,
        Pct.StarPrism,
        Pct.HammerBrush,
        Pct.PolishingHammer,
        Pct.RainbowDrip,
        Pct.FireInRed,
        Pct.AeroInGreen,
    ];

    /// The opener's weaves, in order, each after the global it was recorded against.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(-1, Pct.LivingMuse),
        new(-1, Pct.StrikingMuse),
        new(0, Pct.StarryMuse),
        new(1, Pct.SubtractivePaletteAction),
        new(5, Pct.LivingMuse),
        new(5, Pct.MogOfTheAges),
    ];

    /// How close Starry Muse has to be before a banked portrait waits for it.
    private const double PortraitBurstHold = 45.0;

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;

        var pct = (PictomancerState)state;

        pct.CreatureCanvas = true;
        pct.WeaponCanvas = true;
        pct.LandscapeCanvas = true;
        pct.GainWhitePaint();
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var pct = (PictomancerState)state;

        if (Weave(pct, job, stats) is { } ability)
            return ability;

        if (pct.Time + 1e-9 < pct.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = PictomancerData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, pct))
                return planned;

            pct.Count("opener.skipped", 1);
            pct.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(pct, job);
    }

    /// The global cooldown priority.
    private static ActionDef NextGcd(PictomancerState pct, IJobSim job)
    {
        if (First(pct, job, Pct.StarPrism) is { } prism)
            return prism;

        if (First(pct, job, Pct.HammerStamp, Pct.HammerBrush, Pct.PolishingHammer) is { } hammer)
            return hammer;


        if (pct.HasStatus(Pct.RainbowBright) && First(pct, job, Pct.RainbowDrip) is { } drip)
            return drip;


        if (First(pct, job, Pct.CometInBlack) is { } comet)
            return comet;

        if (First(pct, job, Pct.BlizzardInCyan, Pct.StoneInYellow, Pct.ThunderInMagenta) is { } cool)
            return cool;

        if (NeededMotif(pct, job) is { } motif)
            return motif;

        if (First(pct, job, Pct.FireInRed, Pct.AeroInGreen, Pct.WaterInBlue) is { } warm)
            return warm;

        return First(pct, job, Pct.HolyInWhite)
               ?? Paint(pct, job, PictomancerData.Get(Pct.FireInRed));
    }

    /// A motif for whichever canvas is empty and whose muse is nearly up.
    private static ActionDef? NeededMotif(PictomancerState pct, IJobSim job)
    {
        var checks = new (bool Painted, string Muse, string Motif, double Lead)[]
        {
            (pct.LandscapeCanvas, Pct.StarryMuse, Pct.LandscapeMotif, 15.0),
            (pct.WeaponCanvas, Pct.StrikingMuse, Pct.WeaponMotif, 10.0),
            (pct.CreatureCanvas, Pct.LivingMuse, Pct.CreatureMotif, 10.0),
        };

        foreach (var (painted, muse, motif, lead) in checks)
        {
            if (painted)
                continue;

            var cooldown = pct.Cooldown(muse);
            if (cooldown.ChargesAt(pct.Time) > 0 || cooldown.ReadyAt(pct.Time) - pct.Time <= lead)
                return PictomancerData.Get(motif);
        }

        return null;
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(PictomancerState pct, IJobSim job, PlayerStats stats)
    {
        if (openerIndex == 0
            && potionIndex < potionTimes.Count && pct.Time + 1e-9 >= potionTimes[potionIndex]
            && pct.Cooldown(Buffs.PotionAction).ChargesAt(pct.Time) > 0)
        {
            potionIndex++;
            return PictomancerData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length
            && OpenerWeaves[openerWeaveIndex] is { AfterGcd: < 0 } pull
            && openerIndex == 0)
        {
            openerWeaveIndex++;

            if (Ready(pct, job, pull.Action))
                return PictomancerData.Get(pull.Action);
        }

        if (!WeavePlanner.CanWeave(pct, stats))
            return null;

        if (potionIndex < potionTimes.Count && pct.Time + 1e-9 >= potionTimes[potionIndex]
            && pct.Cooldown(Buffs.PotionAction).ChargesAt(pct.Time) > 0)
        {
            potionIndex++;
            return PictomancerData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(pct, job, planned.Action))
            {
                openerWeaveIndex++;
                return PictomancerData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (Ready(pct, job, Pct.StarryMuse))
            return PictomancerData.Get(Pct.StarryMuse);

        if (!HoldPortraitForBurst(pct))
        {
            if (Ready(pct, job, Pct.RetributionOfTheMadeen))
                return PictomancerData.Get(Pct.RetributionOfTheMadeen);

            if (Ready(pct, job, Pct.MogOfTheAges))
                return PictomancerData.Get(Pct.MogOfTheAges);
        }

        if (Ready(pct, job, Pct.StrikingMuse) && !pct.HasStatus(Pct.HammerTime))
            return PictomancerData.Get(Pct.StrikingMuse);

        if (Ready(pct, job, Pct.LivingMuse))
            return PictomancerData.Get(Pct.LivingMuse);

        if (Ready(pct, job, Pct.SubtractivePaletteAction))
            return PictomancerData.Get(Pct.SubtractivePaletteAction);

        return null;
    }

    private static ActionDef? First(PictomancerState pct, IJobSim job, params string[] names)
    {
        foreach (var name in names)
        {
            var action = PictomancerData.Get(name);
            if (job.CanUse(action, pct))
                return Paint(pct, job, action);
        }

        return null;
    }

    /// The better of a paint and its area twin at the target count being fought.
    private static ActionDef Paint(PictomancerState pct, IJobSim job, ActionDef action)
        => PictomancerData.AreaTwin(action.Name) is { } twin
            ? TargetChoice.Best(job, pct, action, PictomancerData.Get(twin))
            : action;

    /// Whether a banked portrait should wait for the Starry Muse window rather than go now.
    private static bool HoldPortraitForBurst(PictomancerState pct)
    {
        var untilStarry = pct.Cooldown(Pct.StarryMuse).ReadyAt(pct.Time) - pct.Time;

        return untilStarry > 0 && untilStarry <= PortraitBurstHold;
    }

    /// Whether an ability is off recast and legal, asked at the moment it could actually be pressed.
    private static bool Ready(PictomancerState pct, IJobSim job, string name)
    {
        var action = PictomancerData.Get(name);
        var at = Math.Max(pct.Time, pct.AnimationLockUntil);

        return (!pct.HasCooldown(name) || pct.Cooldown(name).ChargesAt(at) > 0)
               && job.CanUse(action, pct);
    }
}
