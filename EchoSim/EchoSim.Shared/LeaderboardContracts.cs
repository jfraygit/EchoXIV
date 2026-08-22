namespace EchoSim.Shared;

/// One board's worth of content: a fight the plugin will score and rank people on.
public sealed record EncounterDef(
    string Key,
    string Name,
    string Short,
    uint TerritoryId,
    string Category,
    int LogEncounterId,
    int Phase = 0);

/// The fights with leaderboards, taken from the game's own ContentFinderCondition sheet.
public static class Encounters
{
    public static readonly EncounterDef[] All =
    [
        new("fru", "Futures Rewritten (Ultimate)", "FRU", 1238, "Ultimate", LogEncounterId: 1079),
        new("dm", "Dancing Mad (Ultimate)", "UMAD", 1363, "Ultimate", LogEncounterId: 1085),
        new("unmaking", "The Unmaking (Extreme)", "Unmaking EX", 1362, "Extreme", LogEncounterId: 1084),

        new("m9s", "AAC Heavyweight M1 (Savage)", "M9S", 1321, "Savage", LogEncounterId: 101),
        new("m10s", "AAC Heavyweight M2 (Savage)", "M10S", 1323, "Savage", LogEncounterId: 102),
        new("m11s", "AAC Heavyweight M3 (Savage)", "M11S", 1325, "Savage", LogEncounterId: 103),

        new("m12s-p1", "AAC Heavyweight M4 (Savage) - Phase 1", "M12S P1", 1327, "Savage", LogEncounterId: 104, Phase: 1),
        new("m12s-p2", "AAC Heavyweight M4 (Savage) - Phase 2", "M12S P2", 1327, "Savage", LogEncounterId: 105, Phase: 2),
    ];

    /// Every board in a territory, in phase order.
    public static EncounterDef[] InTerritory(uint territory)
        => [.. All.Where(e => e.TerritoryId == territory).OrderBy(e => e.Phase)];

    public static EncounterDef? ByKey(string? key)
        => key is null ? null : All.FirstOrDefault(e => e.Key == key);
}

/// One button press, as submitted.
public sealed class LeaderboardCast
{
    public double Time { get; set; }

    public string Action { get; set; } = string.Empty;

    public bool IsGcd { get; set; }
}

/// One stretch of the fight with nothing to hit.
public sealed class LeaderboardDowntime
{
    public double Start { get; set; }

    public double End { get; set; }
}

/// A finished kill, offered to a board.
public sealed class LeaderboardSubmission
{
    /// Read from the character, never typed.
    public string Character { get; set; } = string.Empty;

    public string World { get; set; } = string.Empty;

    /// FFLogs region slug - NA, EU, JP, OC.
    public string Region { get; set; } = string.Empty;

    /// ClassJob row id.
    public uint JobId { get; set; }

    public string EncounterKey { get; set; } = string.Empty;

    /// Seconds there was something to hit, which is what the score is measured against.
    public double ActiveSeconds { get; set; }

    /// Wall-clock length of the fight, kept for sanity checks the score itself cannot make.
    public double Duration { get; set; }

    public List<LeaderboardCast> Casts { get; set; } = [];

    /// Where the fight had nothing to hit, so the ceiling can be given the same gaps.
    public List<LeaderboardDowntime> Downtime { get; set; } = [];

    /// A random value generated once per installation, and THE ONE FIELD HERE THAT IS NEVER SHOWN.
    public string OwnerKey { get; set; } = string.Empty;

    public string PluginVersion { get; set; } = string.Empty;

    /// When the kill happened, by the client's clock.
    public DateTime KilledAt { get; set; }

    /// An FFLogs report for this same kill, when there is one.
    public string? ReportCode { get; set; }

    public int? FightId { get; set; }
}

/// One row on a board.
public sealed class LeaderboardEntry
{
    /// 1-based, and assigned by the server against the whole board rather than the page.
    public int Rank { get; set; }

    public string Character { get; set; } = string.Empty;

    public string World { get; set; } = string.Empty;

    public uint JobId { get; set; }

    public double Score { get; set; }

    /// Whether an FFLogs report was fetched and rescored to this figure.
    public bool Verified { get; set; }

    public DateTime AchievedAt { get; set; }

    public double ActiveSeconds { get; set; }

    /// Set per request, so the caller's own row can be picked out of the list it appears in.
    public bool IsYou { get; set; }
}

/// A board as the plugin draws it: the ranked page, plus wherever the caller landed.
public sealed class LeaderboardBoard
{
    public string EncounterKey { get; set; } = string.Empty;

    public uint JobId { get; set; }

    public List<LeaderboardEntry> Top { get; set; } = [];

    /// The caller's own row when it is NOT in Top, with its true rank against the whole board.
    public LeaderboardEntry? You { get; set; }

    /// Every player with a score on this board, which is what a rank is out of.
    public int TotalEntries { get; set; }
}

/// Which run of the boards a score belongs to.
public static class Seasons
{
    public const int Current = 1;

    public static string Label(int season) => $"Season {season}";
}

/// One score on a player's profile: their best on a given fight, as a given job.
public sealed class LeaderboardProfileEntry
{
    public string EncounterKey { get; set; } = string.Empty;

    public uint JobId { get; set; }

    public double Score { get; set; }

    /// Where this sits on that fight's board for that job.
    public int Rank { get; set; }

    /// How many ranked players that rank is out of.
    public int TotalEntries { get; set; }

    public bool Verified { get; set; }

    public DateTime AchievedAt { get; set; }

    public double ActiveSeconds { get; set; }
}

/// One player's scores across every fight and job, as their profile page shows them.
public sealed class LeaderboardProfile
{
    public string Character { get; set; } = string.Empty;

    public string World { get; set; } = string.Empty;


    public int Season { get; set; } = Seasons.Current;

    /// Every fight this player has a score on, best score first.
    public List<LeaderboardProfileEntry> Entries { get; set; } = [];

    /// Whether this is the caller's own profile, which is the fuller view.
    public bool IsYou { get; set; }
}

/// What came of an attempt to post a score.
public sealed class LeaderboardSubmitResult
{
    public bool Accepted { get; set; }

    /// Written for a player.
    public string Reason { get; set; } = string.Empty;

    /// The score the SERVER computed.
    public double Score { get; set; }

    public int Rank { get; set; }

    public int TotalEntries { get; set; }
}
