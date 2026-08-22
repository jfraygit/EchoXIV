using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.WhiteMage;

/// White Mage's standard single-target rotation.
public sealed class WhiteMageStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// One hard-cast Glare III before the pull, and no potion.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-1.5s", Whm.Glare3,
            "The only pre-pull cast. Lands as the fight starts, so Dia goes up on the first global "
            + "cooldown rather than a hard cast being spent getting there."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Whm.Glare3,

        Buffs.PotionAction,
        Whm.Dia,
        Whm.Glare3,
        Whm.Glare3, Whm.PresenceOfMindAction,
        Whm.AfflatusMisery, Whm.Assize,
        Whm.Glare4,
        Whm.Glare4,
        Whm.Glare4,
        Whm.Glare3,
        Whm.Glare3,
    ];

    /// The opener's global cooldowns.
    private static readonly string[] Opener =
    [
        Whm.Dia,
        Whm.Glare3,
        Whm.Glare3,
        Whm.AfflatusMisery,
        Whm.Glare4,
        Whm.Glare4,
        Whm.Glare4,
    ];

    /// The opener's weaves.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(2, Whm.PresenceOfMindAction),
        new(3, Whm.Assize),
    ];

    /// Whether to run the area filler.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        areaChain = state.UseAreaRotation;

        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;

        var whm = (WhiteMageState)state;
        whm.Lilies = WhiteMageData.MaxLilies;
        whm.BloodLily = WhiteMageData.LiliesPerBloom;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var whm = (WhiteMageState)state;

        if (Weave(whm, job, stats) is { } ability)
            return ability;

        if (whm.Time + 1e-9 < whm.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = WhiteMageData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, whm))
                return planned;

            whm.Count("opener.skipped", 1);
            whm.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(whm, job);
    }

    /// The global cooldown priority.
    private ActionDef NextGcd(WhiteMageState whm, IJobSim job)
    {
        if (First(whm, job, Whm.AfflatusMisery) is { } misery)
            return misery;

        if (First(whm, job, Whm.Glare4) is { } glare4)
            return glare4;

        if (whm.DotExpiresAt <= whm.Time + WhiteMageData.DiaRefreshLead
            && First(whm, job, Whm.Dia) is { } dia)
        {
            return dia;
        }

        if (whm.Lilies >= WhiteMageData.MaxLilies && First(whm, job, Whm.AfflatusRapture) is { } rapture)
            return rapture;

        return WhiteMageData.Get(areaChain ? Whm.Holy3 : Whm.Glare3);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(WhiteMageState whm, IJobSim job, PlayerStats stats)
    {
        if (openerIndex == 0
            && potionIndex < potionTimes.Count && whm.Time + 1e-9 >= potionTimes[potionIndex]
            && whm.Cooldown(Buffs.PotionAction).ChargesAt(whm.Time) > 0)
        {
            potionIndex++;
            return WhiteMageData.Get(Buffs.PotionAction);
        }

        if (!WeavePlanner.CanWeave(whm, stats))
            return null;

        if (potionIndex < potionTimes.Count && whm.Time + 1e-9 >= potionTimes[potionIndex]
            && whm.Cooldown(Buffs.PotionAction).ChargesAt(whm.Time) > 0)
        {
            potionIndex++;
            return WhiteMageData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(whm, job, planned.Action))
            {
                openerWeaveIndex++;
                return WhiteMageData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (Ready(whm, job, Whm.PresenceOfMindAction))
            return WhiteMageData.Get(Whm.PresenceOfMindAction);

        if (Ready(whm, job, Whm.Assize))
            return WhiteMageData.Get(Whm.Assize);

        return null;
    }

    private static ActionDef? First(WhiteMageState whm, IJobSim job, params string[] names)
    {
        foreach (var name in names)
        {
            var action = WhiteMageData.Get(name);
            if (job.CanUse(action, whm))
                return action;
        }

        return null;
    }

    /// Whether an ability is off recast and legal, asked at the moment it could actually be pressed.
    private static bool Ready(WhiteMageState whm, IJobSim job, string name)
    {
        var action = WhiteMageData.Get(name);
        var at = Math.Max(whm.Time, whm.AnimationLockUntil);

        return (!whm.HasCooldown(name) || whm.Cooldown(name).ChargesAt(at) > 0)
               && job.CanUse(action, whm);
    }
}
