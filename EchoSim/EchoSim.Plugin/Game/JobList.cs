using LuminaClassJob = Lumina.Excel.Sheets.ClassJob;

namespace EchoSim.Game;

public enum JobRole
{
    Tank,
    Healer,
    Dps,
}

/// One combat job, for the selector row.
public readonly record struct JobInfo(uint Id, string Abbreviation, string Name, uint IconId, JobRole Role)
{
    /// Whether EchoSim has a simulation for this job yet.
    public bool Implemented => Sim.Jobs.JobRegistry.IsImplemented(Id);
}

public static class JobList
{
    public const uint Ninja = 30;

    /// ClassJob row ids of the combat jobs, in the order the game groups them: tanks, healers, melee,
    /// physical ranged, magical ranged.
    private static readonly uint[] CombatJobs =
    [
        19, 21, 32, 37,        24, 28, 33, 40,        20, 22, 30, 34, 39, 41,        23, 31, 38,        25, 27, 35, 42,    ];

    private static readonly HashSet<uint> Tanks = [19, 21, 32, 37];

    private static readonly HashSet<uint> Healers = [24, 28, 33, 40];

    public static JobRole RoleOf(uint jobId)
        => Tanks.Contains(jobId) ? JobRole.Tank
            : Healers.Contains(jobId) ? JobRole.Healer
            : JobRole.Dps;

    /// The seven slots alongside a player of the given job, in a standard full party of two tanks, two
    /// healers and four DPS.
    public static JobRole[] PartyLayoutFor(uint playerJobId)
    {
        var role = RoleOf(playerJobId);

        var tanks = role == JobRole.Tank ? 1 : 2;
        var healers = role == JobRole.Healer ? 1 : 2;
        var dps = role == JobRole.Dps ? 3 : 4;

        return
        [
            .. Enumerable.Repeat(JobRole.Tank, tanks),
            .. Enumerable.Repeat(JobRole.Healer, healers),
            .. Enumerable.Repeat(JobRole.Dps, dps),
        ];
    }

    /// Job icons live at a fixed offset from the ClassJob row id.
    private const uint JobIconBase = 62100;

    private static List<JobInfo>? cache;

    public static IReadOnlyList<JobInfo> All()
    {
        if (cache is not null)
            return cache;

        var jobs = new List<JobInfo>();

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaClassJob>();

            foreach (var id in CombatJobs)
            {
                if (!sheet.TryGetRow(id, out var row))
                    continue;

                jobs.Add(new JobInfo(
                    id,
                    row.Abbreviation.ExtractText(),
                    TitleCase(row.Name.ExtractText()),
                    JobIconBase + id,
                    RoleOf(id)));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not read the job list");
        }

        cache = jobs;
        return cache;
    }

    public static JobInfo? ById(uint id) => All().FirstOrDefault(j => j.Id == id) is { Id: not 0 } job ? job : null;

    /// Job names come out of the sheet lower-cased ("black mage"), which reads as a typo in a tooltip.
    private static string TitleCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var chars = value.ToCharArray();
        var startOfWord = true;

        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                if (startOfWord)
                    chars[i] = char.ToUpperInvariant(chars[i]);

                startOfWord = false;
            }
            else
            {
                startOfWord = chars[i] is ' ' or '-';
            }
        }

        return new string(chars);
    }
}
