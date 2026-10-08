using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using EchoMix.Shared;

namespace EchoMix.Plugin;

/// Which of the Welcome screen's two choice cards the user picked - see Configuration.LastChosenRole and
/// DjDeckWindow.ShowInitialView.
public enum UserRole
{
    Dj,
    Listener,
}

/// Where FollowNotificationToast anchors itself on screen - a fixed 9-point grid rather than a free-dragged
/// pixel position, so it stays sensible across different resolutions/UI scales instead of a saved absolute
/// coordinate drifting off-screen on a different setup.
public enum ToastAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleCenter,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public float MasterVolume { get; set; } = 0.8f;
    public float DeckAGain { get; set; } = 1f;
    public float DeckBGain { get; set; } = 1f;
    public float CrossfaderPosition { get; set; } = 0.5f;

    /// Null (the default) means no curve button is toggled on in the header - the original, always-been-there
    /// crossfade feel (equal-power under the hood) rather than a fourth named option - see
    /// SetCrossfaderCurveCommand's own doc comment.
    public CrossfaderCurve? CrossfaderCurve { get; set; }

    /// Off by default - see MixerEngine.Tick for what this actually does.
    public bool AutoDjEnabled { get; set; }
    public float AutoDjFadeSeconds { get; set; } = 6f;

    /// Where every toast (see ToastWindow) appears on screen - shared by every toast type despite the name
    /// (kept as-is rather than renamed to avoid silently resetting anyone's already-saved preference back to
    /// default; this originally only governed FollowNotificationToast, before ServerNoticeToast reused the
    /// same setting).
    public ToastAnchor FollowToastAnchor { get; set; } = ToastAnchor.TopCenter;

    /// Whether a host sees a toast (see ListenerJoinedToast) when someone joins their show - applies to both
    /// public and private shows alike.
    public bool NotifyOnListenerJoin { get; set; } = true;

    public string? LastPlaylistName { get; set; }
    public bool IsDjWindowOpen { get; set; } = true;
    public bool MuteWhenUnfocused { get; set; }

    /// Scales the whole main window (size + every layout constant in DjDeckWindow) - the window itself is
    /// fixed-size/non-resizable now (dragging a corner made the heavily hand-positioned layout look messy),
    /// so this is how a user on a different monitor/DPI gets a bigger or smaller window instead.
    public float UiScale { get; set; } = 1f;

    /// Switches the whole UI between the 1.0 look (DjDeckWindow) and the 2.0 redesign (EchoMixShellWindow).
    public bool UseNewDesign { get; set; } = true;

    /// Replaces the Browse grid with generated fixtures, for judging the layout without needing a dozen
    /// people live at once.
    public bool UseSampleBrowseData { get; set; }

    /// Whether the 2.0 nav rail is pinned open or collapsed to its thin edge affordance.
    public bool ShellRailExpanded { get; set; } = true;

    /// Pins the window in place (ImGuiWindowFlags.NoMove) so it can't be accidentally dragged around mid-set
    /// - toggled from the lock icon in the header.
    public bool IsWindowLocked { get; set; }

    /// Free toast placement, as an alternative to the nine-point FollowToastAnchor grid.
    public bool ToastUseCustomPosition { get; set; }
    public float ToastCustomX { get; set; } = 0.5f;
    public float ToastCustomY { get; set; } = 0.08f;

    /// Phase 2 host/listener settings.
    public string? HostDisplayName { get; set; }
    public bool IsProximityAudio { get; set; } = true;

    /// Remembered across reports so a DJ who volunteers this once doesn't have to retype it every time they
    /// hit Report a Bug - same "not a secret, just a preference" reasoning as HostDisplayName above.
    public string? LastReportBugDiscordName { get; set; }

    /// How close (in yalms) a listener needs to be to hear this DJ at all in Proximity mode - adjustable via
    /// right-click on the header's proximity icon.
    public float ProximityRange { get; set; } = 30f;

    public string? LastRoomCode { get; set; }
    public string? LastVanityRoomCode { get; set; }

    /// Whether the DJ's own show is listed publicly in the "View Live Shows" browse grid - see
    /// RelayProtocol.RegisterHostMessage.IsPubliclyListed's own doc comment for what this waives server-side.
    public bool IsPubliclyListed { get; set; }

    /// Cosmetic display name for the browse grid - separate from LastVanityRoomCode, which is still the only
    /// functional join key.
    public string? LastShowName { get; set; }

    /// Required when IsProximityAudio is true (a proximity show reads as "Venue" in the browse grid with
    /// these fields), optional when it's false ("Global" - these are shown to listeners if set, but a blank
    /// set is fine).
    public string? LastVenueName { get; set; }
    public string? LastVenueDataCenter { get; set; }
    public string? LastVenueWorld { get; set; }
    public string? LastVenueHousingArea { get; set; }
    public string? LastVenueWard { get; set; }

    /// Plot number for a house, or apartment number when LastVenueIsApartment is true - see
    /// RegisterHostMessage.VenuePlot's doc comment for why this one field serves both.
    public string? LastVenuePlot { get; set; }

    /// False (default) = house.
    public bool LastVenueIsApartment { get; set; }

    /// Only meaningful when LastVenueIsApartment is true - several housing areas have a second, separate
    /// apartment building added as a "subdivision" of the same area.
    public bool LastVenueSubdivision { get; set; }

    /// Which of the DJ's saved venues (see DjProfile.SavedVenues) was last picked in the Broadcast tab's
    /// venue selector - null means "Manual Entry" was selected.
    public string? LastSelectedSavedVenueId { get; set; }

    /// Listener-view visualizer style - stored as a plain int (cast to VisualizerWidget.Style at each use
    /// site) rather than referencing that UI-layer enum directly from this class, matching how
    /// ListenerAccentChoice used to store its own index.
    public int ListenerVisualizerStyle { get; set; }

    /// Multiplies VisualizerWidget's own per-band sensitivity for the Listener view's spectrum bars only (the
    /// DJ's own deck/minimized visualizers are unaffected) - see VisualizerWidget.Draw's reactivity
    /// parameter.
    public float ListenerVisualizerSensitivity { get; set; } = 1.6f;

    /// Same idea as ListenerVisualizerStyle/ListenerVisualizerSensitivity, but for the DJ's own deck screens
    /// and minimized dual-deck box (Settings > General > DJ Visualizer) - a separate pair of properties since
    /// a DJ's own visualizer and what a listener sees are independent.
    public int DeckVisualizerStyle { get; set; }
    public float DeckVisualizerSensitivity { get; set; } = 1f;

    /// The listener's own volume preference - always applied, multiplied together with the proximity falloff
    /// (which is 1.0 in Global mode) rather than being an alternative to it, so someone can still turn
    /// themselves down even while standing right next to the DJ in Proximity mode.
    public float ListenerVolume { get; set; } = 0.5f;

    /// Beta: automatically joins a live, public, Proximity-mode show the moment the listener is within its
    /// configured range, and leaves again once they walk out of it - see AutoJoinTracker for the actual
    /// join/leave/switch decision logic and its tunable thresholds.
    public bool ListenerAutoJoinNearbyShows { get; set; }

    /// Whether the "auto-joined a show"/"auto-left a show" toasts (AutoJoinedShowToast, AutoLeftShowToast)
    /// show at all - on by default since they're the only feedback a listener gets that
    /// ListenerAutoJoinNearbyShows just did something, short of noticing the audio itself change.
    public bool ListenerAutoJoinNotifications { get; set; } = true;

    /// The DJ's last-picked External Input Mode device (see MixerEngine.
    public string? ExternalInputDeviceId { get; set; }
    public string? ExternalInputDeviceName { get; set; }

    /// Optional second device, same remembered-ID-plus-display-name shape as ExternalInputDeviceId/Name above
    /// - null/empty means single-device mode, unchanged.
    public string? ExternalInputDeviceId2 { get; set; }
    public string? ExternalInputDeviceName2 { get; set; }

    /// The PRIMARY slot's alternative to a device - captures a whole application's own audio output instead
    /// (see MixerEngine.StartExternalInputModeAsync), for DJs who route their mixing software the way OBS's
    /// own Window Capture audio source does.
    public bool ExternalInputUseProcessCapture { get; set; }
    public string? ExternalInputProcessName { get; set; }
    public string? ExternalInputProcessDisplayName { get; set; }

    /// The newest ChangelogData entry's Version the player has actually opened the Changelog tab while
    /// showing - drives the "new update" badge on the Settings icon and the Changelog tab itself (see
    /// DjDeckWindow.HasUnseenChangelog), not tied to the plugin's own assembly version so the badge is driven
    /// purely by what's in ChangelogData, independent of exactly when a given build gets version-bumped for
    /// release.
    public string? LastSeenChangelogVersion { get; set; }

    /// How much the main mix quiets down (for the DJ only, never for listeners) while cue-previewing an
    /// upcoming song - see MixerEngine.StartPreview.
    public float PreviewDampenVolume { get; set; } = 0.5f;

    /// How loud the cue preview itself plays locally - see PreviewDampenVolume.
    public float PreviewVolume { get; set; } = 0.6f;

    /// Whether DjDeckWindow shows its Host/Listener Welcome screen the next time the plugin loads (i.e.
    public bool ShowWelcomeOnEnable { get; set; } = true;

    /// Whether the one-off 2.0 note has been read.
    public bool HasSeenTwoPointOhNote { get; set; }

    /// Whichever Welcome screen card the user last clicked - null means they've never picked one (still see
    /// Welcome regardless of ShowWelcomeOnEnable in that case).
    public UserRole? LastChosenRole { get; set; }

    /// Who's allowed to submit a Song Request while this DJ is live - see Settings > Broadcast.
    public SongRequestAccessMode SongRequestAccessMode { get; set; } = SongRequestAccessMode.Open;
    public List<string> SongRequestWhitelist { get; set; } = new();
    public List<string> SongRequestBlacklist { get; set; } = new();

    /// Deck A/Deck B/blend theme colors (Settings > General > Deck Colors) - stored as plain floats rather
    /// than a System.Numerics.Vector4, since Vector4's X/Y/Z/W are public fields (not properties), which
    /// Json.NET's default property-only serialization would silently drop.
    public float DeckAAccentR { get; set; } = 0.25f;
    public float DeckAAccentG { get; set; } = 0.85f;
    public float DeckAAccentB { get; set; } = 0.95f;
    public float DeckBAccentR { get; set; } = 1f;
    public float DeckBAccentG { get; set; } = 0.6f;
    public float DeckBAccentB { get; set; } = 0.15f;
    public float BlendAccentR { get; set; } = 0.625f;
    public float BlendAccentG { get; set; } = 0.725f;
    public float BlendAccentB { get; set; } = 0.55f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
