using EchoSim.Sim.Jobs;

namespace EchoSim.UI;

/// The name a podium place carries, per job.
public static class PodiumTitles
{
    /// The title for a job at a rank, or a plain one for a job with no entry.
    public static string For(uint jobId, int rank)
    {
        var index = Math.Clamp(rank, 1, 3) - 1;

        return Titles.TryGetValue(jobId, out var set)
            ? set[index]
            : Fallback[index];
    }

    /// Carries the same three tier words, so an unlisted job still reads as a rank.
    private static readonly string[] Fallback =
        ["Undisputed Champion", "Prime Contender", "Rising Challenger"];

    private static readonly Dictionary<uint, string[]> Titles = new()
    {
        [JobRegistry.PaladinId] = ["Undisputed Oath", "Bulwark Contender", "Rising Squire"],
        [JobRegistry.WarriorId] = ["Undisputed Fury", "Savage Contender", "Rising Warbeast"],
        [JobRegistry.DarkKnightId] = ["Undisputed Darkness", "Shadowed Contender", "Rising Abyss"],
        [JobRegistry.GunbreakerId] = ["Undisputed Gunblade", "Powder Contender", "Rising Gunhand"],

        [JobRegistry.MonkId] = ["Undisputed Fist", "Ironfist Contender", "Rising Chakra"],
        [JobRegistry.DragoonId] = ["Undisputed Lance", "Azure Contender", "Rising Wyrm"],
        [JobRegistry.NinjaId] = ["Undisputed Shinobi", "Silent Contender", "Rising Shadow"],
        [JobRegistry.SamuraiId] = ["Undisputed Blade", "Bushido Contender", "Rising Ronin"],
        [JobRegistry.ReaperId] = ["Undisputed Scythe", "Voidbound Contender", "Rising Wraith"],
        [JobRegistry.ViperId] = ["Undisputed Fangs", "Venomous Contender", "Rising Serpent"],

        [JobRegistry.BardId] = ["Undisputed Muse", "Ringside Contender", "Rising Minstrel"],
        [JobRegistry.MachinistId] = ["Undisputed Deadeye", "Brass Contender", "Rising Gunsmith"],
        [JobRegistry.DancerId] = ["Undisputed Footwork", "Feather Contender", "Rising Encore"],

        [JobRegistry.BlackMageId] = ["Undisputed Elements", "Umbral Contender", "Rising Ember"],
        [JobRegistry.SummonerId] = ["Undisputed Primal", "Titan Contender", "Rising Carbuncle"],
        [JobRegistry.RedMageId] = ["Undisputed Balance", "Duelist Contender", "Rising Rapier"],
        [JobRegistry.PictomancerId] = ["Undisputed Canvas", "Palette Contender", "Rising Brush"],

        [JobRegistry.WhiteMageId] = ["Undisputed Grace", "Sacred Contender", "Rising Lily"],
        [JobRegistry.ScholarId] = ["Undisputed Tactics", "Nymian Contender", "Rising Grimoire"],
        [JobRegistry.AstrologianId] = ["Undisputed Stars", "Fortune Contender", "Rising Diviner"],
        [JobRegistry.SageId] = ["Undisputed Noulith", "Aether Contender", "Rising Physician"],
    };
}
