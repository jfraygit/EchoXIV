using EchoSim.Sim.Engine;
using EchoSim.Sim.Jobs.Ninja;

namespace EchoSim.Sim.Analysis;

/// One thing that went wrong, written to be read by a player rather than a developer.
public sealed record AnalysisFinding(
    string Title,
    string Explanation,
    double PotencyLost,
    double? Time = null,
    int UsesMissed = 0)
{
    /// Whether this finding carries a damage estimate, as opposed to a count of missed uses.
    public bool IsPriced => PotencyLost > 0;

    /// Roughly what this cost in DPS over the fight, given a potency-to-damage rate.
    public double DpsCost(double damagePerPotency, double duration)
        => duration <= 0 ? 0 : PotencyLost * damagePerPotency / duration;
}

/// Reads a fight and explains where damage was lost.
public static class RotationAnalysis
{
    /// The shortest recast worth auditing.
    public const double MinAuditedRecast = 30.0;

    /// The cooldowns this job's fights are audited on.
    public static IEnumerable<ActionDef> AuditedCooldowns(IJobSim job)
        => job.Actions.Values.Where(a =>
            a.Kind == ActionKind.OffGcd
            && a.Cooldown >= MinAuditedRecast
            && a.Name != Buffs.PotionAction);

    /// How far short of the modelled rotation is allowed before it is worth mentioning.
    private const int Tolerance = 1;

    public static List<AnalysisFinding> Analyse(
        CombatTimeline fight,
        double gcdRecast,
        IJobSim? job = null,
        SimResult? reference = null,
        PlayerStats? stats = null)
    {
        var findings = new List<AnalysisFinding>();

        if (job is not null && reference is not null)
            CheckCooldownPacing(fight, job, reference, findings);

        CheckDeaths(fight, gcdRecast, job, stats, findings);

        if (job is NinjaSim)
        {
            CheckUnspentProcs(fight, findings);
            CheckTenChiJin(fight, findings);
        }

        CheckDroppedGcds(fight, gcdRecast, job, findings);

        return
        [
            .. findings.OrderByDescending(f => f.IsPriced)
                .ThenByDescending(f => f.PotencyLost)
                .ThenByDescending(f => f.UsesMissed),
        ];
    }

    /// Every cooldown the job's own rotation paces, checked against how often the player pressed it.
    private static void CheckCooldownPacing(
        CombatTimeline fight,
        IJobSim job,
        SimResult reference,
        List<AnalysisFinding> findings)
    {
        if (reference.Duration <= 0 || fight.UptimeSeconds <= 0)
            return;

        var scale = fight.UptimeSeconds / reference.Duration;

        var modelled = reference.Timeline
            .GroupBy(t => t.Action)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var action in AuditedCooldowns(job).OrderBy(a => a.Name))
        {
            if (!modelled.TryGetValue(action.Name, out var count))
                continue;

            var name = action.Name;
            var expected = (int)Math.Floor(count * scale);

            var used = fight.CastCount(action);
            var missed = expected - used - Tolerance;

            if (missed <= 0)
                continue;

            var priced = action.Potency > 0;

            var damage = priced
                ? $"That is about {missed * action.Potency:N0} potency of its own damage, before whatever it enables."
                : "It does no damage itself, so this is not in the potency total above - but a cooldown the " +
                  "rotation paces is paced for a reason, and it is usually a buff or a resource that pays out elsewhere.";

            findings.Add(new AnalysisFinding(
                $"{name} used {used} {(used == 1 ? "time" : "times")}, the rotation gets {expected}",
                $"The modelled rotation presses {name} {expected} times over a fight this long and you got {used}. " +
                $"{damage} Press it on cooldown unless you are holding it for a burst window - holding it past " +
                "that is the same as losing it.",
                priced ? missed * action.Potency : 0,
                UsesMissed: missed));
        }
    }

    /// How long Weakness and Brink of Death last, in seconds.
    private const double WeaknessDuration = 100.0;

    /// Main stat remaining under Weakness, and under Brink of Death.
    private const double WeakenedMainStat = 0.75;

    private const double BrinkMainStat = 0.50;

    /// The debuff windows a run of deaths leaves behind, as the fraction of main stat that survives.
    public static List<(double Start, double End, double MainStat)> PenaltyWindows(
        IEnumerable<DeathWindow> deaths)
    {
        var windows = new List<(double Start, double End, double MainStat)>();

        var active = 1.0;
        var activeUntil = double.NegativeInfinity;

        foreach (var death in deaths.OrderBy(d => d.Time))
        {
            var carried = death.Time < activeUntil ? active : 1.0;

            var remaining = death.ByLimitBreak
                ? carried
                : carried >= 1.0 ? WeakenedMainStat : BrinkMainStat;

            var start = death.PenaltyStartsAt;

            active = remaining;
            activeUntil = remaining >= 1.0 ? double.NegativeInfinity : start + WeaknessDuration;

            if (remaining < 1.0)
                windows.Add((start, start + WeaknessDuration, remaining));
        }

        return windows;
    }

    /// Deaths, the time they cost on the floor, and the penalty that follows getting up.
    private static void CheckDeaths(
        CombatTimeline fight,
        double gcdRecast,
        IJobSim? job,
        PlayerStats? stats,
        List<AnalysisFinding> findings)
    {
        if (fight.Deaths.Count == 0 || gcdRecast <= 0)
            return;

        var filler = FillerPotency(fight, job);
        var onTheFloor = fight.Deaths.Sum(d => d.Duration);
        var lostGcds = onTheFloor / gcdRecast;

        var penalties = PenaltyWindows(fight.Deaths);
        var weakenedPotency = 0.0;
        var brink = penalties.Any(p => p.MainStat == BrinkMainStat);

        if (stats is not null)
        {
            foreach (var (start, end, remaining) in penalties)
            {
                var kept = XivMath.MainStatMulti(stats.Level, (int)(stats.MainStat * remaining))
                           / stats.MainStatMulti;

                var pressed = fight.Casts
                    .Where(c => c.IsGcd && c.Time >= start && c.Time < end)
                    .Sum(c => Lookup(job, c.Action) is { Potency: > 0 } def ? def.Potency : filler);

                weakenedPotency += pressed * (1 - kept);
            }
        }

        var penalty = brink
            ? "Weakness, then Brink of Death for dying again while still weakened - half your main stat"
            : "Weakness - a quarter off your main stat";

        var weakened = penalties.Count == 0
            ? " A healer's limit break picked you up, which raises outright with no Weakness at all."
            : weakenedPotency > 0
                ? $" The {WeaknessDuration:F0}s after each raise ran under {penalty}, costing about " +
                  $"{weakenedPotency:N0} potency on top."
                : $" Each raise is followed by {WeaknessDuration:F0}s of {penalty}.";

        findings.Add(new AnalysisFinding(
            $"Died {fight.Deaths.Count} time{(fight.Deaths.Count == 1 ? string.Empty : "s")}, " +
            $"{onTheFloor:F0}s spent down",
            $"About {lostGcds:F1} GCDs you couldn't press, counted here rather than as dropped globals so " +
            $"the two don't get confused.{weakened} Up to five seconds of each gap may be a deliberate " +
            "Transcendent hold rather than a loss.",
            (lostGcds * filler) + weakenedPotency,
            fight.Deaths[0].Time));
    }

    private static void CheckUnspentProcs(CombatTimeline fight, List<AnalysisFinding> findings)
    {
        var raitons = fight.CastCount(Nin.Raiton) + fight.CastCount(Nin.TcjRaiton);
        var raijus = fight.CastCount(Nin.FleetingRaiju);
        var unspentRaiju = raitons - raijus;

        if (unspentRaiju > 1)
        {
            findings.Add(new AnalysisFinding(
                $"{unspentRaiju} Raiju charges expired unused",
                "Every Raiton gives you a Fleeting Raiju worth 700 potency, and it's stronger than any filler " +
                "you'd otherwise press. They cap at three and fall off after 30 seconds, so spend them rather " +
                "than banking them.",
                unspentRaiju * 700));
        }

        var bunshin = fight.CastCount(Nin.BunshinAction);
        var phantom = fight.CastCount(Nin.PhantomKamaitachi);
        var unspentPhantom = bunshin - phantom;

        if (unspentPhantom > 0)
        {
            findings.Add(new AnalysisFinding(
                $"{unspentPhantom} Phantom Kamaitachi went unused",
                "Bunshin grants a free Phantom Kamaitachi worth 700 potency. It lasts 45 seconds and doesn't " +
                "break your combo, so there's no reason to let it expire.",
                unspentPhantom * 700));
        }

        var kassatsu = fight.CastCount(Nin.KassatsuAction);
        var hyosho = fight.CastCount(Nin.HyoshoRanryu);

        if (kassatsu > hyosho)
        {
            findings.Add(new AnalysisFinding(
                $"{kassatsu - hyosho} Kassatsu expired without a Hyosho Ranryu",
                "Kassatsu's only job is to make Hyosho Ranryu available. Letting it fall off wastes the whole " +
                "cooldown - press Hyosho while it's up, even if the burst window has already passed.",
                (kassatsu - hyosho) * 1690));
        }
    }

    private static void CheckTenChiJin(CombatTimeline fight, List<AnalysisFinding> findings)
    {
        var tcj = fight.CastCount(Nin.TenChiJinAction);
        if (tcj == 0)
            return;

        var steps = fight.CastCount(Nin.TcjFuma) + fight.CastCount(Nin.TcjRaiton) + fight.CastCount(Nin.TcjSuiton);
        var missing = (tcj * 3) - steps;

        if (missing > 0)
        {
            findings.Add(new AnalysisFinding(
                $"Ten Chi Jin cut short {missing} time(s)",
                "Ten Chi Jin gives three free ninjutsu, but the window is only six seconds and moving cancels it. " +
                "If a mechanic keeps interrupting it, use it slightly earlier or later rather than losing the casts.",
                missing * 600));
        }

        var tenri = fight.CastCount(Nin.TenriJindo);
        if (tenri < tcj)
        {
            findings.Add(new AnalysisFinding(
                $"{tcj - tenri} Tenri Jindo went unused",
                "Ten Chi Jin leaves you a Tenri Jindo worth 1100 potency for 30 seconds. It's easy to forget " +
                "because it isn't part of the ninjutsu sequence itself.",
                (tcj - tenri) * 1100));
        }
    }

    /// Time the global cooldown spent idle that downtime doesn't account for.
    private static void CheckDroppedGcds(
        CombatTimeline fight,
        double gcdRecast,
        IJobSim? job,
        List<AnalysisFinding> findings)
    {
        if (gcdRecast <= 0)
            return;

        var gcds = fight.Casts.Where(c => c.IsGcd).OrderBy(c => c.Time).ToList();
        if (gcds.Count < 2)
            return;

        var lostSeconds = 0.0;
        var worstGap = 0.0;
        var worstAt = 0.0;

        for (var i = 1; i < gcds.Count; i++)
        {
            var gap = gcds[i].Time - gcds[i - 1].Time;

            var leadIn = Lookup(job, gcds[i].Action)?.MudraCost * 0.5 ?? 0;

            var idle = gap - ExpectedGap(Lookup(job, gcds[i - 1].Action), gcdRecast) - leadIn;

            if (idle <= 0.6)
                continue;

            var overlap = fight.Downtime
                .Select(d => Math.Max(0, Math.Min(d.End, gcds[i].Time) - Math.Max(d.Start, gcds[i - 1].Time)))
                .Sum();

            overlap += fight.Deaths
                .Select(d => Math.Max(0, Math.Min(d.ResumedAt, gcds[i].Time) - Math.Max(d.Time, gcds[i - 1].Time)))
                .Sum();

            var realIdle = idle - overlap;
            if (realIdle <= 0.25)
                continue;

            lostSeconds += realIdle;

            if (realIdle > worstGap)
            {
                worstGap = realIdle;
                worstAt = gcds[i - 1].Time;
            }
        }

        if (lostSeconds < gcdRecast)
            return;

        var lostGcds = lostSeconds / gcdRecast;

        findings.Add(new AnalysisFinding(
            $"About {lostGcds:F1} GCDs dropped ({lostSeconds:F1}s idle)",
            "This is time the global cooldown was up and nothing was pressed, with downtime already excluded. " +
            $"The longest single gap was {worstGap:F1}s. Some of this is unavoidable movement, but it's usually " +
            "the largest single source of lost damage and the easiest to improve.",
            lostGcds * FillerPotency(fight, job),
            worstAt));
    }

    private static ActionDef? Lookup(IJobSim? job, string action)
        => job is not null && job.Actions.TryGetValue(action, out var def) ? def : null;

    /// How long the global cooldown was legitimately occupied by the action that started it.
    private static double ExpectedGap(ActionDef? action, double gcdRecast)
    {
        if (action is null)
            return gcdRecast;

        if (action.FixedRecast > 0)
            return Math.Max(action.FixedRecast, action.CastTime);

        var scale = gcdRecast / Simulator.StandardRecast;
        var recast = action.BaseRecast > 0 ? action.BaseRecast * scale : gcdRecast;

        return Math.Max(recast, action.CastTime * scale);
    }

    /// What one dropped GCD was worth, taken from the job the player actually played.
    private static double FillerPotency(CombatTimeline fight, IJobSim? job)
    {
        const double Fallback = 450;

        if (job is null)
            return Fallback;

        var potencies = fight.Casts
            .Where(c => c.IsGcd)
            .Select(c => Lookup(job, c.Action))
            .Where(a => a is { Potency: > 0 })
            .Select(a => (double)a!.Potency)
            .OrderBy(p => p)
            .ToList();

        return potencies.Count == 0 ? Fallback : potencies[potencies.Count / 2];
    }

    /// Converts a simulation result into the shared shape, so both paths analyse identically.
    public static CombatTimeline FromSimulation(SimResult result)
        => new()
        {
            JobName = result.JobName,
            Duration = result.Duration,
            Source = "simulation",
            Casts = [.. result.Timeline.Select(t => new TimelineCast(t.Time, t.Action, t.Kind != ActionKind.OffGcd, t.Damage))],
        };
}
