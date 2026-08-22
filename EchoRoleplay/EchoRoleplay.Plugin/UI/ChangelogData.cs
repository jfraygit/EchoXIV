namespace EchoRoleplay.UI;

/// One release's worth of user-facing highlights, shown in Settings > Changelog.
public sealed record ChangelogEntry(string Version, string[] Highlights);

/// The release notes shown inside the plugin.
public static class ChangelogData
{
    /// Empty until there is a release.
    public static readonly ChangelogEntry[] Entries =
    [
        new("1.0.0.4",
        [
            "You can now open the plugin with /erp (haha) as well as /echorp.",
            "Fixed the window opening as a small box with unreadable text after an update, with no "
            + "way to expand it.",
        ]),

        new("1.0.0.3",
        [
            "New setting - Hide In Duties turns off statuses and hover profiles while you are in a duty.",
            "Some status icons were drawn smaller than the rest, inside a black square. They now "
            + "match the others.",
        ]),

        new("1.0.0.2",
        [
            "The hover card has been redesigned. It now shows a portrait, and marks characters "
            + "verified through the Lodestone.",
            "Theme songs no longer silence ambient sound while they play.",
        ]),
    ];

    /// The newest version with notes.
    public static string LatestVersion => Entries.Length > 0 ? Entries[0].Version : string.Empty;
}
