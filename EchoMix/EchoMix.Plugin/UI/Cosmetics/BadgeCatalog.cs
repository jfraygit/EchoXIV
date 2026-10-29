using System.Collections.Generic;

namespace EchoMix.Plugin.UI.Cosmetics;

/// What a badge id actually means: its name, the sentence explaining what it was for, and which embedded
/// image to draw for it.
public static class BadgeCatalog
{
    public readonly record struct Badge(string Title, string Description, string ResourceName);

    private static readonly Dictionary<string, Badge> Entries = new()
    {
        [EchoMix.Shared.DjBadgeDto.FounderOneOh] = new Badge(
            "EchoMix 1.0",
            "Had a DJ listing before EchoMix 2.0. Here for the first one.",
            "EchoMix.Plugin.Badges.founder-1.0.png"),

        ["monthly-2026-10-gold"] = new Badge(
            "October 2026 Gold",
            "Finished first in October.",
            "EchoMix.Plugin.Badges.monthly-2026-10-gold.png"),

        ["monthly-2026-10-silver"] = new Badge(
            "October 2026 Silver",
            "Finished second in October.",
            "EchoMix.Plugin.Badges.monthly-2026-10-silver.png"),

        ["monthly-2026-10-bronze"] = new Badge(
            "October 2026 Bronze",
            "Finished third in October.",
            "EchoMix.Plugin.Badges.monthly-2026-10-bronze.png"),
    };

    /// The wording for a board key, for the category list on a monthly badge's tooltip.
    private static readonly Dictionary<string, string> CategoryTitles = new()
    {
        ["ShowsPlayed"] = "Shows Played",
        ["OnAirSeconds"] = "Time On Air",
        ["ListenerSeconds"] = "Listener Hours",
        ["PeakListeners"] = "Biggest Room",
        ["DaysActive"] = "Days Active",
        ["UniqueListeners"] = "People Reached",
    };

    public static Badge? Find(string id) =>
        Entries.TryGetValue(id, out var entry) ? entry : null;

    public static string? CategoryTitle(string key) =>
        CategoryTitles.TryGetValue(key, out var title) ? title : null;
}
