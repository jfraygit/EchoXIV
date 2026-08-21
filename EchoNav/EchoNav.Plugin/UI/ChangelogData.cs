namespace EchoNav.UI;

/// One release's worth of user-facing highlights, shown in Settings > Changelog.
public sealed record ChangelogEntry(string Version, string[] Highlights);

/// The release notes shown inside the plugin.
public static class ChangelogData
{
    public static readonly ChangelogEntry[] Entries =
    [
        new("1.0.0.5",
        [
            "The aetheryte network now works whatever language you play in. Playing in anything but " +
            "English, no shard was ever found and EchoNav quietly walked everywhere instead.",

            "Travelling to a shard you have never stood at yourself now works. It used to need you to " +
            "have visited a shard before it could take you there.",

            "The window no longer appears on the title and character select screens.",
        ]),

        new("1.0.0.4",
        [
            "The bug report box now wraps as you type instead of running off the edge.",
        ]),

        new("1.0.0.1",
        [
            "EchoNav now follows Dalamud's UI scale. At anything above 100% the window kept its " +
            "original size while the text inside it grew, so labels were cut off mid-word, the " +
            "distance on a card printed over the encounter's name, and the settings ran out through " +
            "the side of their own panels. Every part of the plugin is measured against your scale " +
            "now - the window, the cards, the tabs, the sliders and the bug report box.",

            "Buttons take their size from what is written on them rather than from a fixed number, " +
            "so a label can no longer be wider than the button holding it.",

            "Icons sitting next to a label were very slightly right of centre. They are centred now, " +
            "which is worth a line here only because it is the sort of thing that reads as sloppy " +
            "without ever being obvious enough to report.",
        ]),
    ];

    /// The newest version with notes.
    public static string LatestVersion => Entries.Length > 0 ? Entries[0].Version : string.Empty;
}
