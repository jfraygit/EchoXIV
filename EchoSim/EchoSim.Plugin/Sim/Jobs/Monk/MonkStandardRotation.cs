using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Monk;

/// Monk's standard single-target rotation.
public sealed class MonkStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    /// An opener ability and the opener global cooldown it is woven after, counted from zero.
    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// How many Blitzes have been spent, which decides what the next Perfect Balance window banks.
    private int blitzCount;

    /// Opo-opo windows banked since the solar Nadi was last lit, which is what paces the mixed windows.
    private int lunarWindowsSinceSolar;

    /// What to press before the pull, for the Opener tab.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-1.0s", Mnk.Meditation,
            "Meditated to five chakra before the pull, so the opener can spend the Forbidden Chakra "
            + "on the second global rather than the sixth. It deals nothing and costs no global "
            + "cooldown a player has to pay for."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Mnk.DragonKick,
        Mnk.PerfectBalanceAction,
        Buffs.PotionAction,
        Mnk.LeapingOpo,
        Mnk.DragonKick,
        Mnk.BrotherhoodAction,
        Mnk.RiddleOfFireAction,
        Mnk.LeapingOpo,
        Mnk.RiddleOfWindAction,
        Mnk.ElixirBurst,
        Mnk.DragonKick,
        Mnk.ForbiddenChakra,
        Mnk.WindsReply,
        Mnk.FiresReply,
        Mnk.LeapingOpo,
        Mnk.PerfectBalanceAction,
        Mnk.DragonKick,
        Mnk.LeapingOpo,
        Mnk.DragonKick,
        Mnk.ElixirBurst,
        Mnk.LeapingOpo,
    ];

    /// The opener's GCDs, in order: the Double Lunar DK opener, which both guides print and all five
    /// reference parses press.
    private static readonly string[] Opener =
    [
        Mnk.DragonKick,
        Mnk.LeapingOpo,
        Mnk.DragonKick,
        Mnk.LeapingOpo,
        Mnk.ElixirBurst,
        Mnk.DragonKick,
        Mnk.WindsReply,
        Mnk.FiresReply,
        Mnk.LeapingOpo,
        Mnk.DragonKick,
        Mnk.LeapingOpo,
        Mnk.DragonKick,
        Mnk.ElixirBurst,
        Mnk.LeapingOpo,
    ];

    /// The opener's weaves, each named by the opener global cooldown it follows, counted from zero.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(0, Mnk.PerfectBalanceAction),
        new(0, Buffs.PotionAction),

        new(1, Mnk.ForbiddenChakra),

        new(2, Mnk.BrotherhoodAction),
        new(2, Mnk.RiddleOfFireAction),
        new(3, Mnk.RiddleOfWindAction),
        new(8, Mnk.PerfectBalanceAction),
    ];

    /// Whether to run the area chain.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;

        areaChain = state.UseAreaRotation;
        blitzCount = 0;

        var mnk = (MonkState)state;
        mnk.Chakra = MonkData.ChakraCost;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var mnk = (MonkState)state;

        if (Weave(mnk, job, stats) is { } ability)
            return ability;

        if (mnk.Time + 1e-9 < mnk.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = MonkData.Get(Opener[openerIndex]);
            if (job.CanUse(planned, mnk))
            {
                openerIndex++;
                return planned;
            }

            openerIndex++;
            mnk.Count("opener.skipped", 1);
        }

        return NextGcd(mnk, job);
    }

    /// The filler loop.
    private ActionDef? NextGcd(MonkState mnk, IJobSim job)
    {
        if (mnk.Beast.Count == 3)
        {
            var blitz = MonkData.Get(MonkSim.BlitzFor(mnk));
            if (job.CanUse(blitz, mnk))
            {
                blitzCount++;

                if (blitz.Name == Mnk.RisingPhoenix)
                    lunarWindowsSinceSolar = 0;
                else
                    lunarWindowsSinceSolar++;

                return blitz;
            }
        }

        var fire = MonkData.Get(Mnk.FiresReply);
        if (job.CanUse(fire, mnk)
            && (mnk.StatusRemaining(Mnk.FireRumination) < 6.0
                || (mnk.HasStatus(Mnk.RaptorForm) && !WastesFormless(mnk))))
        {
            return fire;
        }

        var wind = MonkData.Get(Mnk.WindsReply);
        if (job.CanUse(wind, mnk))
            return wind;

        return FormAction(mnk, job);
    }

    /// Whether pressing Fire's Reply right now would throw away a Formless Fist.
    private static bool WastesFormless(MonkState mnk)
        => mnk.HasStatus(Mnk.FormlessFist) || mnk.HasStatus(Mnk.PerfectBalance);

    /// The right weaponskill for whichever form is up.
    private ActionDef? FormAction(MonkState mnk, IJobSim job)
    {
        if (mnk.HasStatus(Mnk.PerfectBalance))
            return MonkData.Get(PerfectBalanceChoice(mnk));

        if (mnk.HasStatus(Mnk.OpoForm) || mnk.HasStatus(Mnk.FormlessFist))
        {
            return Choose(job, mnk,
                mnk.HasStatus(Mnk.OpoFury) ? Mnk.LeapingOpo : Mnk.DragonKick,
                Mnk.ShadowOfTheDestroyer);
        }

        if (mnk.HasStatus(Mnk.RaptorForm))
        {
            return Choose(job, mnk,
                mnk.HasStatus(Mnk.RaptorFury) ? Mnk.RisingRaptor : Mnk.TwinSnakes,
                Mnk.FourPointFury);
        }

        if (mnk.HasStatus(Mnk.CoeurlForm))
        {
            return Choose(job, mnk,
                mnk.HasStatus(Mnk.CoeurlFury) ? Mnk.PouncingCoeurl : Mnk.Demolish,
                Mnk.Rockbreaker);
        }

        mnk.Count("form.missing", 1);
        return MonkData.Get(Mnk.DragonKick);
    }

    /// The better of a form's two actions at the current target count.
    private ActionDef Choose(IJobSim job, MonkState mnk, string single, string area)
        => MonkData.Get(areaChain ? area : single);

    /// What to bank during a Perfect Balance window.
    private string PerfectBalanceChoice(MonkState mnk)
    {
        var wantsSolar = mnk.LunarNadi && !mnk.SolarNadi;

        if (!wantsSolar)
            return mnk.HasStatus(Mnk.OpoFury) ? Mnk.LeapingOpo : Mnk.DragonKick;

        if (!mnk.Beast.Contains(BeastChakra.OpoOpo))
            return mnk.HasStatus(Mnk.OpoFury) ? Mnk.LeapingOpo : Mnk.DragonKick;

        if (!mnk.Beast.Contains(BeastChakra.Raptor))
            return mnk.HasStatus(Mnk.RaptorFury) ? Mnk.RisingRaptor : Mnk.TwinSnakes;

        return mnk.HasStatus(Mnk.CoeurlFury) ? Mnk.PouncingCoeurl : Mnk.Demolish;
    }

    /// The next ability worth weaving, or null.
    private ActionDef? Weave(MonkState mnk, IJobSim job, PlayerStats stats)
    {
        if (!WeaveFits(mnk, stats))
            return null;


        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(mnk, job, planned.Action)
                     && (planned.Action != Buffs.PotionAction || PotionDue(mnk)))
            {
                openerWeaveIndex++;

                if (planned.Action == Buffs.PotionAction)
                    potionIndex++;

                return MonkData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return planned.Action == Mnk.ForbiddenChakra ? null : ChakraDump(mnk, job);
            }
        }

        if (PotionDue(mnk))
        {
            potionIndex++;
            return MonkData.Get(Buffs.PotionAction);
        }

        foreach (var name in BurstOrder)
        {
            var action = MonkData.Get(name);
            if (Ready(mnk, job, name) && ShouldPress(mnk, name))
                return action;
        }

        return ChakraDump(mnk, job);
    }

    /// The chakra spender, if there are five banked and its one-second recast is up.
    private static ActionDef? ChakraDump(MonkState mnk, IJobSim job)
    {
        if (mnk.Chakra < MonkData.ChakraCost || !Ready(mnk, job, Mnk.ForbiddenChakra))
            return null;

        return TargetChoice.Best(
            job, mnk, MonkData.Get(Mnk.ForbiddenChakra), MonkData.Get(Mnk.Enlightenment));
    }

    /// Whether an ability can actually be pressed at the moment it would be pressed.
    private bool PotionDue(MonkState mnk)
        => potionIndex < potionTimes.Count
           && mnk.Time + 1e-9 >= potionTimes[potionIndex]
           && mnk.Cooldown(Buffs.PotionAction).ChargesAt(mnk.Time) > 0;

    private static bool Ready(MonkState mnk, IJobSim job, string name)
    {
        var earliest = System.Math.Max(mnk.Time, mnk.AnimationLockUntil);

        return mnk.Cooldown(name).ChargesAt(earliest) > 0
               && job.CanUse(MonkData.Get(name), mnk);
    }

    /// Burst cooldowns, in the order the reference parses press them.
    private static readonly string[] BurstOrder =
    [
        Mnk.PerfectBalanceAction,
        Mnk.BrotherhoodAction,
        Mnk.RiddleOfFireAction,
        Mnk.RiddleOfWindAction,
    ];

    /// Whether a burst cooldown should go now.
    private static bool ShouldPress(MonkState mnk, string name)
    {
        if (name != Mnk.PerfectBalanceAction)
            return true;

        if (mnk.HasStatus(Mnk.PerfectBalance) || mnk.Beast.Count > 0)
            return false;

        if (!mnk.HasStatus(Mnk.RaptorForm))
            return false;

        if (mnk.HasStatus(Mnk.FormlessFist))
            return false;

        var gcd = mnk.CurrentGcdLength > 0 ? mnk.CurrentGcdLength : 2.0;

        if (mnk.HasStatus(Mnk.RiddleOfFire))
            return mnk.StatusRemaining(Mnk.RiddleOfFire) >= 4.0 * gcd;

        return mnk.Cooldown(Mnk.RiddleOfFireAction).ReadyAt(mnk.Time) <= mnk.Time + (3.0 * gcd);
    }

    /// Whether an ability fits before the next GCD.
    private static bool WeaveFits(MonkState mnk, PlayerStats stats)
        => WeavePlanner.CanWeave(mnk, stats);
}
