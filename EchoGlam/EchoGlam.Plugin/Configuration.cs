using System;
using System.Numerics;
using Dalamud.Configuration;

namespace EchoGlam;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// Bumped when a stored value needs correcting rather than merely defaulting.
    public const int CurrentVersion = 1;

    /// Brings a saved configuration up to date.
    public void Migrate()
    {
        if (Version >= CurrentVersion)
            return;

        Version = CurrentVersion;
        Save();
    }

    public bool IsMainWindowOpen { get; set; }

    /// The diagnostics window, remembered separately and off by default.
    public bool IsDebugWindowOpen { get; set; }

    /// Which tab was open when the session ended.
    public int LastTab { get; set; }

    /// Window size, remembered across sessions.
    public float WindowWidth { get; set; } = DefaultWindowWidth;
    public float WindowHeight { get; set; } = DefaultWindowHeight;

    /// Three gallery cards across, plus the window's own padding and scrollbar.
    public const float DefaultWindowWidth = 900f;

    /// Two rows of cards and the header above them, which is enough to show that the grid continues below
    /// without dominating the screen.
    public const float DefaultWindowHeight = 700f;

    /// Pins the window in place, so working in it can't nudge it around.
    public bool WindowLocked { get; set; }

    /// Whether the window was collapsed to its box when the session ended.
    public bool StartMinimised { get; set; }

    /// Where each mode was last left.
    public float FullWindowX { get; set; } = UnsetPosition;
    public float FullWindowY { get; set; } = UnsetPosition;
    public float MinimisedX { get; set; } = UnsetPosition;
    public float MinimisedY { get; set; } = UnsetPosition;

    public const float UnsetPosition = -99999f;

    /// The one colour every other colour in the plugin derives from.
    public Vector4? AccentColour { get; set; }


    /// The newest changelog entry the player has actually had open.
    public string? LastSeenChangelogVersion { get; set; }

    /// This installation's identity on the relay.
    public string OwnerKey { get; set; } = string.Empty;

    /// Whether this installation takes part in glamour sharing.
    public bool ShareGlamours { get; set; }

    /// Returns the owner key, minting and saving one the first time it is asked for.
    public string EnsureOwnerKey()
    {
        if (!string.IsNullOrEmpty(OwnerKey))
            return OwnerKey;

        OwnerKey = Guid.NewGuid().ToString("N");
        Save();
        return OwnerKey;
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
