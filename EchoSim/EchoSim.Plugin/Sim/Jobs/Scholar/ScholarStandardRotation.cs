using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Scholar;

/// Scholar's standard single-target rotation.
public sealed class ScholarStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// One hard-cast Broil IV before the pull, and no potion.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-1.5s", Sch.Broil4,
            "The only pre-pull cast. Lands as the fight starts, so Biolysis goes up on the first "
            + "global cooldown rather than a hard cast being spent getting there."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Sch.Broil4,

        Buffs.PotionAction,
        Sch.Biolysis, Sch.AetherflowAction,
        Sch.Broil4, Sch.ChainStratagemAction,
        Sch.Broil4, Sch.EnergyDrain,
        Sch.Broil4, Sch.EnergyDrain,
        Sch.Broil4, Sch.EnergyDrain,
        Sch.Broil4, Sch.Dissipation,
        Sch.Broil4, Sch.BanefulImpaction,
        Sch.Broil4, Sch.EnergyDrain,
        Sch.Broil4, Sch.EnergyDrain,
        Sch.Broil4, Sch.EnergyDrain,
        Sch.Biolysis,
    ];

    /// The opener's global cooldowns: Biolysis, nine Broil IVs, and an EARLY Biolysis refresh.
    private static readonly string[] Opener =
    [
        Sch.Biolysis,
        Sch.Broil4,
        Sch.Broil4,
        Sch.Broil4,
        Sch.Broil4,
        Sch.Broil4,
        Sch.Broil4,
        Sch.Broil4,
        Sch.Broil4,
        Sch.Broil4,
        Sch.Biolysis,
    ];

    /// The opener's weaves, in order, each after the global it was recorded against.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(0, Sch.AetherflowAction),
        new(1, Sch.ChainStratagemAction),
        new(2, Sch.EnergyDrain),
        new(3, Sch.EnergyDrain),
        new(4, Sch.EnergyDrain),
        new(5, Sch.Dissipation),
        new(6, Sch.BanefulImpaction),
        new(7, Sch.EnergyDrain),
        new(8, Sch.EnergyDrain),
        new(9, Sch.EnergyDrain),
    ];

    /// Whether to run the area filler.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        areaChain = state.UseAreaRotation;

        openerIndex = 0;
        potionIndex = 0;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var sch = (ScholarState)state;

        if (Weave(sch, job, stats) is { } ability)
            return ability;

        if (sch.Time + 1e-9 < sch.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = ScholarData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, sch))
                return planned;

            sch.Count("opener.skipped", 1);
            sch.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(sch, job);
    }

    /// The global cooldown priority, which is two lines long.
    private ActionDef NextGcd(ScholarState sch, IJobSim job)
    {
        if (sch.DotExpiresAt <= sch.Time + ScholarData.BiolysisRefreshLead
            && First(sch, job, Sch.Biolysis) is { } biolysis)
            return biolysis;

        return ScholarData.Get(areaChain ? Sch.ArtOfWar2 : Sch.Broil4);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(ScholarState sch, IJobSim job, PlayerStats stats)
    {
        if (openerIndex == 0
            && potionIndex < potionTimes.Count && sch.Time + 1e-9 >= potionTimes[potionIndex]
            && sch.Cooldown(Buffs.PotionAction).ChargesAt(sch.Time) > 0)
        {
            potionIndex++;
            return ScholarData.Get(Buffs.PotionAction);
        }

        if (!WeavePlanner.CanWeave(sch, stats))
            return null;

        if (potionIndex < potionTimes.Count && sch.Time + 1e-9 >= potionTimes[potionIndex]
            && sch.Cooldown(Buffs.PotionAction).ChargesAt(sch.Time) > 0)
        {
            potionIndex++;
            return ScholarData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(sch, job, planned.Action))
            {
                openerWeaveIndex++;
                return ScholarData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (Ready(sch, job, Sch.ChainStratagemAction))
            return ScholarData.Get(Sch.ChainStratagemAction);

        if (Ready(sch, job, Sch.BanefulImpaction))
            return ScholarData.Get(Sch.BanefulImpaction);

        if (Ready(sch, job, Sch.AetherflowAction))
            return ScholarData.Get(Sch.AetherflowAction);

        if (Ready(sch, job, Sch.Dissipation))
            return ScholarData.Get(Sch.Dissipation);

        if (Ready(sch, job, Sch.EnergyDrain))
            return ScholarData.Get(Sch.EnergyDrain);

        return null;
    }

    private static ActionDef? First(ScholarState sch, IJobSim job, params string[] names)
    {
        foreach (var name in names)
        {
            var action = ScholarData.Get(name);
            if (job.CanUse(action, sch))
                return action;
        }

        return null;
    }

    /// Whether an ability is off recast and legal, asked at the moment it could actually be pressed.
    private static bool Ready(ScholarState sch, IJobSim job, string name)
    {
        var action = ScholarData.Get(name);
        var at = Math.Max(sch.Time, sch.AnimationLockUntil);

        return (!sch.HasCooldown(name) || sch.Cooldown(name).ChargesAt(at) > 0)
               && job.CanUse(action, sch);
    }
}
