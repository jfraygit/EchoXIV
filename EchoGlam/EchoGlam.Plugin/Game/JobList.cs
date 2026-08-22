using System.Collections.Generic;

namespace EchoGlam.Game;

/// How the job filter row is grouped.
public enum JobGroup
{
    Tank,
    Healer,
    Melee,
    Ranged,
    Caster,

    /// Disciples of the Hand - the eight crafters.
    Hand,

    /// Disciples of the Land - the three gatherers.
    Land,
}

public readonly record struct JobInfo(uint Id, string Abbreviation, string Name, JobGroup Group)
{
    /// Job icons sit at a fixed offset from the ClassJob row id - the game's own framed icon set, which
    /// covers the crafters and gatherers as well as the combat jobs.
    public uint IconId => 62100 + Id;
}

public static class JobList
{
    /// Every job worth filtering by, in character-sheet order.
    public static readonly JobInfo[] All =
    [
        new(19, "PLD", "Paladin", JobGroup.Tank),
        new(21, "WAR", "Warrior", JobGroup.Tank),
        new(32, "DRK", "Dark Knight", JobGroup.Tank),
        new(37, "GNB", "Gunbreaker", JobGroup.Tank),

        new(24, "WHM", "White Mage", JobGroup.Healer),
        new(28, "SCH", "Scholar", JobGroup.Healer),
        new(33, "AST", "Astrologian", JobGroup.Healer),
        new(40, "SGE", "Sage", JobGroup.Healer),

        new(20, "MNK", "Monk", JobGroup.Melee),
        new(22, "DRG", "Dragoon", JobGroup.Melee),
        new(30, "NIN", "Ninja", JobGroup.Melee),
        new(34, "SAM", "Samurai", JobGroup.Melee),
        new(39, "RPR", "Reaper", JobGroup.Melee),
        new(41, "VPR", "Viper", JobGroup.Melee),

        new(23, "BRD", "Bard", JobGroup.Ranged),
        new(31, "MCH", "Machinist", JobGroup.Ranged),
        new(38, "DNC", "Dancer", JobGroup.Ranged),

        new(25, "BLM", "Black Mage", JobGroup.Caster),
        new(27, "SMN", "Summoner", JobGroup.Caster),
        new(35, "RDM", "Red Mage", JobGroup.Caster),
        new(42, "PCT", "Pictomancer", JobGroup.Caster),
        new(36, "BLU", "Blue Mage", JobGroup.Caster),

        new(8, "CRP", "Carpenter", JobGroup.Hand),
        new(9, "BSM", "Blacksmith", JobGroup.Hand),
        new(10, "ARM", "Armorer", JobGroup.Hand),
        new(11, "GSM", "Goldsmith", JobGroup.Hand),
        new(12, "LTW", "Leatherworker", JobGroup.Hand),
        new(13, "WVR", "Weaver", JobGroup.Hand),
        new(14, "ALC", "Alchemist", JobGroup.Hand),
        new(15, "CUL", "Culinarian", JobGroup.Hand),

        new(16, "MIN", "Miner", JobGroup.Land),
        new(17, "BTN", "Botanist", JobGroup.Land),
        new(18, "FSH", "Fisher", JobGroup.Land),
    ];

    public static string GroupLabel(JobGroup group) => group switch
    {
        JobGroup.Tank => "Tank",
        JobGroup.Healer => "Healer",
        JobGroup.Melee => "Melee",
        JobGroup.Ranged => "Ranged",
        JobGroup.Caster => "Caster",
        JobGroup.Hand => "Disciples of the Hand",
        JobGroup.Land => "Disciples of the Land",
        _ => group.ToString(),
    };

    public static JobInfo? Find(uint id)
    {
        foreach (var job in All)
        {
            if (job.Id == id)
                return job;
        }

        return null;
    }

    /// The largest group, which is what the popup's grid is sized to so every group lays out on one row.
    public static int LargestGroupSize
    {
        get
        {
            var largest = 0;

            foreach (var group in Groups)
            {
                var count = 0;
                foreach (var _ in InGroup(group))
                    count++;

                if (count > largest)
                    largest = count;
            }

            return largest;
        }
    }

    public static IEnumerable<JobInfo> InGroup(JobGroup group)
    {
        foreach (var job in All)
        {
            if (job.Group == group)
                yield return job;
        }
    }

    public static readonly JobGroup[] Groups =
    [
        JobGroup.Tank, JobGroup.Healer, JobGroup.Melee, JobGroup.Ranged,
        JobGroup.Caster, JobGroup.Hand, JobGroup.Land,
    ];
}
