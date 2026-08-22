using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Sage;

/// Sage's standard single-target rotation.
public sealed class SageStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// Eukrasia five seconds out, the potion, and one instant cast that lands as the fight starts.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-5.0s", Sge.EukrasiaAction,
            "Pressed before the pull so the first global of the fight is the damage-over-time rather "
            + "than the button that unlocks it. It deals nothing, so a player pays no global for it "
            + "and neither should the model."),

        ("-2.1s", Buffs.PotionAction,
            "Ahead of the first Eukrasian Dosis III, because a damage-over-time snapshots its buffs "
            + "when it is applied - Icy Veins: \"all 10 subsequent DoT ticks will receive these raid "
            + "buffs.\""),

        ("-1.5s", Sge.Toxikon2,
            "Instant, and its long travel time means the damage lands as the fight starts without "
            + "delaying the DoT. Costs an Addersting, which is regenerated pre-pull by shielding the "
            + "party with Eukrasian Diagnosis or Prognosis II. The Balance publishes a Pneuma variant "
            + "of this slot instead; the two openers are identical after the pull."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Sge.EukrasiaAction,
        Sge.Toxikon2,

        Buffs.PotionAction,
        Sge.EukrasianDosis3,
        Sge.Dosis3,
        Sge.Dosis3,
        Sge.Dosis3,
        Sge.Phlegma3, Sge.Psyche,
        Sge.Phlegma3,
        Sge.Dosis3,
        Sge.Dosis3,
        Sge.Dosis3,
        Sge.Dosis3,
        Sge.EukrasiaAction,
        Sge.EukrasianDosis3,
        Sge.Dosis3,
    ];

    /// The opener's global cooldowns: the damage-over-time, three fillers, both Phlegma charges, four more
    /// fillers, and an EARLY damage-over-time refresh.
    private static readonly string[] Opener =
    [
        Sge.EukrasianDosis3,
        Sge.Dosis3,
        Sge.Dosis3,
        Sge.Dosis3,
        Sge.Phlegma3,
        Sge.Phlegma3,
        Sge.Dosis3,
        Sge.Dosis3,
        Sge.Dosis3,
        Sge.Dosis3,
        Sge.EukrasiaAction,
        Sge.EukrasianDosis3,
    ];

    /// The opener's one weave.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(4, Sge.Psyche),
    ];

    /// Whether to run the area filler.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        areaChain = state.UseAreaRotation;

        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;

        var sge = (SageState)state;
        sge.ApplyStatus(Sge.EukrasiaStatus, SageData.EukrasiaStatusDuration);
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var sge = (SageState)state;

        if (Weave(sge, job, stats) is { } ability)
            return ability;

        if (sge.Time + 1e-9 < sge.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = SageData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, sge))
                return planned;

            sge.Count("opener.skipped", 1);
            sge.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(sge, job);
    }

    /// The global cooldown priority.
    private ActionDef NextGcd(SageState sge, IJobSim job)
    {
        if (First(sge, job, Sge.EukrasianDosis3) is { } dot)
            return dot;

        if (sge.DotExpiresAt <= sge.Time + SageData.DotRefreshLead
            && First(sge, job, Sge.EukrasiaAction) is { } eukrasia)
            return eukrasia;

        if (First(sge, job, Sge.Phlegma3) is { } phlegma
            && sge.Cooldown(Sge.Phlegma3).ChargesAt(Math.Max(sge.Time, sge.AnimationLockUntil)) > 0)
        {
            return phlegma;
        }

        if (Ready(sge, job, Sge.Pneuma))
            return SageData.Get(Sge.Pneuma);

        return SageData.Get(areaChain ? Sge.Dyskrasia2 : Sge.Dosis3);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(SageState sge, IJobSim job, PlayerStats stats)
    {
        if (openerIndex == 0
            && potionIndex < potionTimes.Count && sge.Time + 1e-9 >= potionTimes[potionIndex]
            && sge.Cooldown(Buffs.PotionAction).ChargesAt(sge.Time) > 0)
        {
            potionIndex++;
            return SageData.Get(Buffs.PotionAction);
        }

        if (!WeavePlanner.CanWeave(sge, stats))
            return null;

        if (potionIndex < potionTimes.Count && sge.Time + 1e-9 >= potionTimes[potionIndex]
            && sge.Cooldown(Buffs.PotionAction).ChargesAt(sge.Time) > 0)
        {
            potionIndex++;
            return SageData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(sge, job, planned.Action))
            {
                openerWeaveIndex++;
                return SageData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (Ready(sge, job, Sge.Psyche))
            return SageData.Get(Sge.Psyche);

        return null;
    }

    private static ActionDef? First(SageState sge, IJobSim job, params string[] names)
    {
        foreach (var name in names)
        {
            var action = SageData.Get(name);
            if (job.CanUse(action, sge))
                return action;
        }

        return null;
    }

    /// Whether an ability is off recast and legal, asked at the moment it could actually be pressed.
    private static bool Ready(SageState sge, IJobSim job, string name)
    {
        var action = SageData.Get(name);
        var at = Math.Max(sge.Time, sge.AnimationLockUntil);

        return (!sge.HasCooldown(name) || sge.Cooldown(name).ChargesAt(at) > 0)
               && job.CanUse(action, sge);
    }
}
