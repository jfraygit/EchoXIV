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
    };

    public static Badge? Find(string id) =>
        Entries.TryGetValue(id, out var entry) ? entry : null;
}
