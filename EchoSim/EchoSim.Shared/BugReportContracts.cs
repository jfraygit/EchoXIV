namespace EchoSim.Shared;

/// A bug report on its way from the plugin to the relay.
public sealed class BugReport
{
    /// What the user typed.
    public string Description { get; set; } = string.Empty;

    /// Optional, and free text rather than anything read off the character.
    public string Name { get; set; } = string.Empty;

    /// Plugin version the report came from, which is the first thing worth knowing.
    public string PluginVersion { get; set; } = string.Empty;

    /// Whether it came from a Debug build - a report from one is a developer, not a user.
    public bool DevBuild { get; set; }

    /// The job selected in the plugin, which is what the sim was configured for.
    public string SelectedJob { get; set; } = string.Empty;

    /// The job actually being played, which is often not the one above - and when they differ, that is
    /// frequently the bug being reported.
    public string CurrentJob { get; set; } = string.Empty;

    /// The gear the sim was running on, as a one-line summary rather than an item list.
    public string Stats { get; set; } = string.Empty;

    /// Food, potion and party-bonus settings, which change every number on screen.
    public string Consumables { get; set; } = string.Empty;

    /// Which tab was open, so a UI report points at the right window.
    public string Screen { get; set; } = string.Empty;

    /// Game and Dalamud versions, for the class of bug that is really an API change.
    public string GameVersion { get; set; } = string.Empty;

    /// Dalamud's own version string.
    public string DalamudVersion { get; set; } = string.Empty;
}

/// What the relay says back.
public sealed class BugReportAck
{
    public bool Ok { get; set; }

    /// Whether it reached Discord as well as disk.
    public bool Delivered { get; set; }

    public string Error { get; set; } = string.Empty;
}
