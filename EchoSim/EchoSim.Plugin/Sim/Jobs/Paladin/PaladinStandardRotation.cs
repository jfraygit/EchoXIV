using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Paladin;

/// Paladin's standard single-target rotation.
public sealed class PaladinStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    /// One hard-cast Holy Spirit before the pull, and no potion.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-1.75s", Pld.HolySpirit,
            "The one hard cast in the rotation, and it costs nothing before the pull. Lands as the "
            + "fight starts so the first global cooldown is a Fast Blade rather than a cast bar. "
            + "Both guides say 1.75 rather than the bare 1.5s cast time - Icy Veins: \"start casting "
            + "Holy Spirit around 1.75 seconds before the pull to get the global cooldown running "
            + "early\", and The Balance's graphic labels the same slot -1.75."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Pld.HolySpirit,
        Pld.FastBlade,
        Buffs.PotionAction,
        Pld.RiotBlade,
        Pld.RoyalAuthority,
        Pld.FightOrFlightAction,
        Pld.Imperator,
        Pld.Expiacion,
        Pld.Confiteor,
        Pld.CircleOfScorn,
        Pld.Intervene,
        Pld.Intervene,
        Pld.BladeOfFaith,
        Pld.BladeOfTruth,
        Pld.BladeOfValor,
        Pld.BladeOfHonor,
        Pld.GoringBlade,
        Pld.Atonement,
        Pld.Supplication,
        Pld.Sepulchre,
        Pld.HolySpirit,
    ];

    /// The opener's global cooldowns, deliberately short.
    private static readonly string[] Opener =
    [
        Pld.FastBlade,
        Pld.RiotBlade,
        Pld.RoyalAuthority,
    ];

    /// An ability the opener presses at a planned point rather than when a condition trips.
    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's weaves, in order, each named by the opener global cooldown it follows.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(2, Pld.FightOrFlightAction),
        new(2, Pld.Imperator),
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
        var pld = (PaladinState)state;

        if (Weave(pld, job, stats) is { } ability)
            return ability;

        if (pld.Time + 1e-9 < pld.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = PaladinData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, pld))
                return planned;

            pld.Count("opener.skipped", 1);
            pld.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(pld, job);
    }

    /// The global cooldown priority.
    private ActionDef NextGcd(PaladinState pld, IJobSim job)
    {
        if (First(pld, job, Pld.BladeOfValor, Pld.BladeOfTruth, Pld.BladeOfFaith) is { } chain)
            return chain;

        if (First(pld, job, Pld.Confiteor) is { } confiteor)
            return confiteor;

        if (First(pld, job, Pld.GoringBlade) is { } goring)
            return goring;


        var procsOutstanding = pld.HasStatus(Pld.AtonementReady)
                               || pld.HasStatus(Pld.SupplicationReady)
                               || pld.HasStatus(Pld.SepulchreReady)
                               || pld.HasStatus(Pld.DivineMight);

        if (pld.HasStatus(Pld.FightOrFlight))
        {
            if (First(pld, job, Pld.Sepulchre, Pld.Supplication, Pld.Atonement) is { } buffed)
                return buffed;

            if (First(pld, job, Pld.HolySpirit) is { } buffedHoly)
                return TargetChoice.Best(job, pld, buffedHoly, PaladinData.Get(Pld.HolyCircle));
        }

        if (First(pld, job, Pld.RiotBlade) is { } riot)
            return riot;

        if (!procsOutstanding && First(pld, job, Pld.RoyalAuthority) is { } royal)
            return royal;

        if (!areaChain && procsOutstanding
            && First(pld, job, Pld.RiotBlade, Pld.RoyalAuthority) is null)
        {
            return PaladinData.Get(Pld.FastBlade);
        }

        if (First(pld, job, Pld.Sepulchre, Pld.Supplication, Pld.Atonement) is { } atonement)
            return atonement;

        if (First(pld, job, Pld.HolySpirit) is { } holy)
            return TargetChoice.Best(job, pld, holy, PaladinData.Get(Pld.HolyCircle));

        if (First(pld, job, Pld.RoyalAuthority) is { } stalled)
            return stalled;

        if (areaChain)
        {
            return PaladinData.Get(
                pld.IsComboReady(PaladinData.Get(Pld.Prominence)) ? Pld.Prominence : Pld.TotalEclipse);
        }

        return PaladinData.Get(Pld.FastBlade);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(PaladinState pld, IJobSim job, PlayerStats stats)
    {
        if (!WeavePlanner.CanWeave(pld, stats))
            return null;

        if (potionIndex < potionTimes.Count && pld.Time + 1e-9 >= potionTimes[potionIndex]
            && pld.Cooldown(Buffs.PotionAction).ChargesAt(pld.Time) > 0)
        {
            potionIndex++;
            return PaladinData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd && Ready(pld, job, planned.Action))
            {
                openerWeaveIndex++;
                return PaladinData.Get(planned.Action);
            }

            if (openerIndex <= planned.AfterGcd)
                return null;
        }

        if (Ready(pld, job, Pld.FightOrFlightAction))
            return PaladinData.Get(Pld.FightOrFlightAction);

        if (pld.HasStatus(Pld.FightOrFlight) && Ready(pld, job, Pld.Imperator))
            return PaladinData.Get(Pld.Imperator);

        if (Ready(pld, job, Pld.BladeOfHonor))
            return PaladinData.Get(Pld.BladeOfHonor);

        if (WeavePlanner.FitsWithoutClipping(pld))
        {
            if (Ready(pld, job, Pld.Expiacion))
                return PaladinData.Get(Pld.Expiacion);

            if (Ready(pld, job, Pld.CircleOfScorn))
                return PaladinData.Get(Pld.CircleOfScorn);
        }

        if (pld.HasStatus(Pld.FightOrFlight)
            && WeavePlanner.FitsWithoutClipping(pld)
            && Ready(pld, job, Pld.Intervene))
        {
            return PaladinData.Get(Pld.Intervene);
        }

        return null;
    }

    private static ActionDef? First(PaladinState pld, IJobSim job, params string[] names)
    {
        foreach (var name in names)
        {
            var action = PaladinData.Get(name);
            if (job.CanUse(action, pld))
                return action;
        }

        return null;
    }

    /// Whether an ability can be pressed in the weave slot that is opening, rather than at this exact
    /// instant.
    private static bool Ready(PaladinState pld, IJobSim job, string name)
    {
        var action = PaladinData.Get(name);
        var earliest = System.Math.Max(pld.Time, pld.AnimationLockUntil);

        return (!pld.HasCooldown(name) || pld.Cooldown(name).ChargesAt(earliest) > 0)
               && job.CanUse(action, pld);
    }
}
