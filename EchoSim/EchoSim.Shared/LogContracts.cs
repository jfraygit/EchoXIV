namespace EchoSim.Shared;

/// The wire contract between the plugin and the log relay.
public sealed class LogFightSummary
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// FFLogs' own id for WHICH BOSS this was, which is the only thing that says so unambiguously.
    public int EncounterId { get; set; }

    public string Difficulty { get; set; } = string.Empty;

    public bool Kill { get; set; }

    /// Fight length in seconds.
    public double Duration { get; set; }

    /// When the pull started, in the report's own milliseconds.
    public double StartTime { get; set; }

    /// Best-effort percentage of the pull completed, for wipes.
    public double? BossPercentage { get; set; }

    public List<LogPlayer> Players { get; set; } = [];
}

public sealed class LogPlayer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// Job abbreviation as FFLogs reports it, e.g. "Ninja".
    public string Job { get; set; } = string.Empty;
}

public sealed class LogReportInfo
{
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// When the report began, as a Unix epoch in milliseconds.
    public double StartTime { get; set; }

    public List<LogFightSummary> Fights { get; set; } = [];
}

/// One action the player used.
public sealed class LogCast
{
    /// Seconds from the start of the fight.
    public double Time { get; set; }

    /// FFLogs ability id, kept so the plugin can map unknown actions and report them.
    public int AbilityId { get; set; }

    public string Ability { get; set; } = string.Empty;
}

/// A stretch where nobody could damage the boss.
public sealed class LogDowntime
{
    public double Start { get; set; }

    public double End { get; set; }
}

/// A death, and when the player got going again.
public sealed class LogDeath
{
    /// Seconds from the start of the fight.
    public double Time { get; set; }

    /// When the player next acted, or the end of the fight if they never did.
    public double ResumedAt { get; set; }

    /// When the resurrection actually landed, which is where the Weakness clock starts.
    public double ResurrectedAt { get; set; }

    /// Whether a healer's level 3 limit break did the resurrecting.
    public bool ByLimitBreak { get; set; }
}

/// FFLogs' percentile bands, and the colour each is shown in.
public static class ParseBands
{
    /// Lower bound, label, and FFLogs' colour as 0-255 RGB.
    public static readonly (double Min, string Name, byte R, byte G, byte B)[] Bands =
    [
        (100, "Gold",   0xE5, 0xCC, 0x80),
        (99,  "Pink",   0xE2, 0x68, 0xA8),
        (95,  "Orange", 0xFF, 0x80, 0x00),
        (75,  "Purple", 0xA3, 0x35, 0xEE),
        (50,  "Blue",   0x00, 0x70, 0xDD),
        (25,  "Green",  0x1E, 0xFF, 0x00),
        (0,   "Grey",   0x66, 0x66, 0x66),
    ];

    public static (double Min, string Name, byte R, byte G, byte B) For(double percent)
    {
        foreach (var band in Bands)
        {
            if (percent >= band.Min)
                return band;
        }

        return Bands[^1];
    }
}

/// One player's fight, ready to analyse.
public sealed class LogFightDetail
{
    public string ReportCode { get; set; } = string.Empty;

    public int FightId { get; set; }

    public string FightName { get; set; } = string.Empty;

    public string PlayerName { get; set; } = string.Empty;

    public string Job { get; set; } = string.Empty;

    public double Duration { get; set; }

    /// The player's damage in this fight, for comparison against the simulated ceiling.
    public double TotalDamage { get; set; }

    public List<LogCast> Casts { get; set; } = [];

    public List<LogDowntime> Downtime { get; set; } = [];

    /// Every time this player died, with when they next acted.
    public List<LogDeath> Deaths { get; set; } = [];

    /// True when downtime was inferred rather than known, so the UI can say so.
    public bool DowntimeIsEstimated { get; set; } = true;

    /// The player's FFLogs percentile for this fight, 0-100, or null when it isn't ranked.
    public double? ParsePercent { get; set; }

    /// The ranked DPS behind that percentile, which is rDPS rather than raw damage.
    public double? RankedDps { get; set; }
}

/// Error shape, so the plugin can show something useful rather than a status code.
public sealed class LogError
{
    public string Message { get; set; } = string.Empty;

    public static LogError From(string message) => new() { Message = message };
}
