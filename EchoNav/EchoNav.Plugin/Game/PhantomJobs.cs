namespace EchoNav.Game;

/// One of the four buffs a phantom job can hand out at a knowledge crystal.
public sealed record PhantomBuff(
    string Name, byte JobId, string JobName, byte JobLevel, uint ActionId, string ActionName, uint StatusId);

/// Phantom jobs, and the buffs four of them can give out.
public static class PhantomJobs
{
    /// In sheet order, so the index is the job id and also the index into the character's level array.
    public static readonly string[] Names =
    [
        "Freelancer", "Knight", "Berserker", "Monk", "Ranger", "Samurai", "Bard", "Geomancer",
        "Time Mage", "Cannoneer", "Chemist", "Oracle", "Thief", "Mystic Knight", "Gladiator",
        "Dancer", "Ninja", "White Mage", "Black Mage", "Dragoon", "Summoner", "Blue Mage",
        "Red Mage", "Necromancer",
    ];

    public const byte Freelancer = 0;

    /// Inquiring Mind grants every buff the character's own job levels qualify for, in one cast.
    public const uint InquiringMind = 46606;

    public const byte InquiringMindLevel = 15;

    public static readonly PhantomBuff[] Buffs =
    [
        new("Enduring Fortitude", 1, "Knight", 2, 41589, "Pray", 4233),
        new("Fleetfooted", 3, "Monk", 3, 41597, "Counterstance", 4239),
        new("Romeo's Ballad", 6, "Bard", 2, 41609, "Romeo's Ballad", 4244),
        new("Quicker Step", 15, "Dancer", 2, 46603, "Quickstep", 4799),
    ];

    public static string NameOf(int jobId) =>
        jobId >= 0 && jobId < Names.Length ? Names[jobId] : $"job {jobId}";
}
