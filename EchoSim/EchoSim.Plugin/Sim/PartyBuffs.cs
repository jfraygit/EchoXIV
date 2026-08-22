namespace EchoSim.Sim;

/// A raid buff, described by what it does rather than who casts it.
public sealed record RaidBuff(
    string Name,
    double DamageMulti = 1.0,
    double CritBonus = 0,
    double DirectHitBonus = 0,
    double Duration = 20,
    double Cooldown = 120,
    double FirstUse = 0);

/// One buff landing at a point in time.
public readonly record struct ScheduledBuff(double Time, RaidBuff Buff);

/// Which jobs bring which raid buffs, at patch 7.55.
public static class PartyBuffs
{
    public static readonly IReadOnlyDictionary<uint, RaidBuff[]> ByJob = new Dictionary<uint, RaidBuff[]>
    {
        [20] = [new RaidBuff("Brotherhood", DamageMulti: 1.05)],        [22] = [new RaidBuff("Battle Litany", CritBonus: 0.10)],
        [30] = [new RaidBuff("Dokumori", DamageMulti: 1.05)],
        [39] = [new RaidBuff("Arcane Circle", DamageMulti: 1.03, Duration: 20)],
        [23] =        [
            new RaidBuff("Battle Voice", DirectHitBonus: 0.20),
            new RaidBuff("Radiant Finale", DamageMulti: 1.06, Cooldown: 110),
        ],
        [38] = [new RaidBuff("Technical Finish", DamageMulti: 1.05)],
        [27] = [new RaidBuff("Searing Light", DamageMulti: 1.05)],        [35] = [new RaidBuff("Embolden", DamageMulti: 1.05)],        [42] = [new RaidBuff("Starry Muse", DamageMulti: 1.05)],
        [28] = [new RaidBuff("Chain Stratagem", CritBonus: 0.10)],        [33] = [new RaidBuff("Divination", DamageMulti: 1.06)],    };

    /// Dancer's partner-only buffs.
    public static readonly RaidBuff StandardFinish = new("Standard Finish", DamageMulti: 1.05, Duration: 60, Cooldown: 30);

    public static readonly RaidBuff Devilment = new("Devilment", CritBonus: 0.20, DirectHitBonus: 0.20);

    /// Astrologian's damage card, for the one player receiving it.
    public static readonly RaidBuff CardTarget =
        new("Astrologian Card", DamageMulti: 1.06, Duration: 15, Cooldown: 110);

    /// True when this job brings anything to the party at all.
    public static bool Contributes(uint jobId) => ByJob.ContainsKey(jobId);

    /// Builds the timeline of buffs for a party.
    public static List<ScheduledBuff> Schedule(
        IEnumerable<uint> partyJobs,
        bool dancePartner,
        bool aligned,
        double fightDuration,
        bool cardTarget = false)
    {
        var schedule = new List<ScheduledBuff>();

        void Add(RaidBuff buff)
        {
            var period = aligned ? Math.Max(buff.Cooldown, 120) : buff.Cooldown;
            for (var t = buff.FirstUse; t < fightDuration; t += period)
                schedule.Add(new ScheduledBuff(t, buff));
        }

        foreach (var job in partyJobs)
        {
            if (job == 0 || !ByJob.TryGetValue(job, out var buffs))
                continue;

            foreach (var buff in buffs)
                Add(buff);
        }

        if (dancePartner)
        {
            Add(Devilment);

            for (var t = 0.0; t < fightDuration; t += StandardFinish.Cooldown)
                schedule.Add(new ScheduledBuff(t, StandardFinish));
        }

        if (cardTarget)
        {
            for (var t = 0.0; t < fightDuration; t += CardTarget.Cooldown)
                schedule.Add(new ScheduledBuff(t, CardTarget));
        }

        schedule.Sort((a, b) => a.Time.CompareTo(b.Time));
        return schedule;
    }
}
