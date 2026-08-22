using System;
using System.Collections.Generic;

namespace EchoMix.Shared;

public sealed class DeckStatus
{
    public bool HasTrack { get; set; }
    public string? TrackTitle { get; set; }
    public bool IsPlaying { get; set; }
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public double CuePointSeconds { get; set; }
    public float Gain { get; set; }
    public float Trim { get; set; }
    public float LowGainDb { get; set; }
    public float MidGainDb { get; set; }
    public float HighGainDb { get; set; }
    public float FilterKnob { get; set; }
    public float PeakLevel { get; set; }

    /// Whether this deck auto-starts the next queued track on its own (see DeckQueueManager.Pump) - an
    /// explicit per-deck DJ toggle, defaulting on.
    public bool AutoplayEnabled { get; set; } = true;

    /// The currently loaded track's detected/overridden BPM, or null if unknown (still analyzing, analysis
    /// failed, no track loaded, or this deck is standing in for Spotify Mode/External Input Mode - see
    /// IpcServer.BuildDeckStatus).
    public float? Bpm { get; set; }

    /// True when this is the one deck currently being tempo-pulled to match the other's BPM (see
    /// MixerEngine.SetDeckSync) - mutually exclusive with the other deck's own SyncEnabled.
    public bool SyncEnabled { get; set; }

    /// The tempo ratio actually being applied right now (1.0 = unchanged) - lets the UI show e.g. a live
    /// "+3.2%" next to the Sync button.
    public float TempoRatio { get; set; } = 1f;
}

public sealed class MixerStatusMessage
{
    public DeckStatus DeckA { get; set; } = new();
    public DeckStatus DeckB { get; set; } = new();
    public float CrossfaderPosition { get; set; }

    /// Null means no curve button is toggled on (the original always-been-there feel, same math as
    /// CrossfaderCurve.Power) - see SetCrossfaderCurveCommand's own doc comment.
    public CrossfaderCurve? CrossfaderCurve { get; set; }

    /// See SetAutoDjCommand/MixerEngine.Tick - mirrored back here so the UI reflects ground truth (e.g.
    public bool AutoDjEnabled { get; set; }
    public float AutoDjFadeSeconds { get; set; } = 6f;
    public float MasterVolume { get; set; }
    public bool IsOutputMuted { get; set; }
    public float OutputPeak { get; set; }
    public BroadcastStatusMessage Broadcast { get; set; } = new();
    public SpotifyModeStatusMessage SpotifyMode { get; set; } = new();
    public ExternalInputModeStatusMessage ExternalInputMode { get; set; } = new();

    /// File path of the upcoming song currently being cue-previewed (see MixerEngine.StartPreview), or null
    /// when nothing's cueing - lets the UI highlight whichever queue row it belongs to and tells a
    /// middle-click on that same row to stop it instead.
    public string? PreviewingFilePath { get; set; }
}

/// Whether the deck mix is currently replaced by audio captured directly from Spotify's own process - see
/// MixerEngine.StartSpotifyModeAsync.
public sealed class SpotifyModeStatusMessage
{
    public bool IsActive { get; set; }
    public string? Error { get; set; }

    public string? NowPlayingTitle { get; set; }
    public string? NowPlayingArtist { get; set; }
    public double NowPlayingPositionSeconds { get; set; }
    public double NowPlayingDurationSeconds { get; set; }
    public bool NowPlayingIsPlaying { get; set; }
}

/// Whether the broadcast is currently replaced by audio captured from a DJ-selected Windows recording device
/// - see MixerEngine.StartExternalInputMode.
public sealed class ExternalInputModeStatusMessage
{
    public bool IsActive { get; set; }
    public string? Error { get; set; }
    public string? DeviceName { get; set; }

    /// True only when a second device was actually supplied and is still capturing - drives Deck B's own half
    /// of the UI reading as the second input (device name, Gain/Trim/EQ/ Filter redirect) instead of its
    /// normal idle deck state.
    public bool IsSecondActive { get; set; }
    public string? DeviceName2 { get; set; }
}

public sealed class AudioInputDeviceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class AudioInputDevicesSnapshotMessage
{
    public List<AudioInputDeviceDto> Devices { get; set; } = new();
}

public sealed class CapturableProcessDto
{
    /// The stable key - what's actually sent back in StartExternalInputModeCommand.
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class CapturableProcessesSnapshotMessage
{
    public List<CapturableProcessDto> Processes { get; set; } = new();
}

/// Phase 2 host/listener state, folded into the existing ~30Hz MixerStatus push rather than its own
/// dirty-flag snapshot (like PlaylistsSnapshot) - it's simple, small, and already changes frequently enough
/// (listener count, now-playing) that a fresh copy every tick is fine.
public sealed class BroadcastStatusMessage
{
    public bool IsLive { get; set; }
    public string? RoomCode { get; set; }
    public int ListenerCount { get; set; }
    public string? BroadcastError { get; set; }

    /// Null while the host hasn't opted into a Web Listen Link (the default) - populated with the full
    /// shareable URL (RelayConfig.WebListenBaseUrl + the relay-issued token) the moment
    /// SetWebListenLinkCommand{Enabled=true} is confirmed, and cleared again on disable or once the broadcast
    /// itself ends.
    public string? WebListenUrl { get; set; }

    /// When this lobby actually started (the room's own creation time, from the relay - see
    /// Room.CreatedAtUtc) - not when this particular host/listener connection joined it, so a listener
    /// joining mid-set sees the real elapsed time instead of a timer starting at 0:00.
    public DateTime? LiveSinceUtc { get; set; }

    /// Everyone currently listening, for the Host Lobby's Listeners section - mirrors HostRoster below, just
    /// for listeners instead of co-hosts.
    public List<ListenerRosterEntryDto> ListenerRoster { get; set; } = new();

    public bool IsListening { get; set; }
    public bool IsListenerReconnecting { get; set; }

    public bool IsHostReconnecting { get; set; }
    public string? HostDjName { get; set; }
    public string? HostCharacterName { get; set; }
    public bool IsProximityAudio { get; set; }
    public float ProximityRange { get; set; } = 30f;
    public string? NowPlayingTitleA { get; set; }
    public double NowPlayingPositionSecondsA { get; set; }
    public double NowPlayingDurationSecondsA { get; set; }
    public string? NowPlayingTitleB { get; set; }
    public double NowPlayingPositionSecondsB { get; set; }
    public double NowPlayingDurationSecondsB { get; set; }

    /// Deck B can never be used while the host's Spotify Mode is active - listeners use this to hide Deck B's
    /// now-useless half entirely rather than showing it sitting frozen.
    public bool IsHostSpotifyModeActive { get; set; }
    public string? ListenError { get; set; }

    /// The lead's own per-deck spectrum bands, mirrored to every other connected co-host the same way
    /// NowPlayingTitleA/B is (see BroadcastHostConnection.LatestLeadSpectrum) - lets a waiting co-host's own
    /// deck displays show the lead's actual visualizer instead of their own idle one.
    public float[]? LeadSpectrumBandsA { get; set; }
    public float[]? LeadSpectrumBandsB { get; set; }

    public bool IsLead { get; set; } = true;
    public Guid HostId { get; set; }
    public Guid LeadHostId { get; set; }
    public List<HostRosterEntryDto> HostRoster { get; set; } = new();

    /// How loud the current lead's incoming audio plays back locally while monitoring (not lead yourself) -
    /// see MixerEngine.AttachMonitorSource.
    public float MonitorVolume { get; set; } = 1f;

    /// The lead's own upcoming queues, mirrored to every other connected co-host - see
    /// BroadcastHostConnection.LatestLeadQueues and RelayMessageType.DeckQueuesUpdate.
    public DeckQueueDto? LeadQueueA { get; set; }
    public DeckQueueDto? LeadQueueB { get; set; }

    /// Seconds remaining before this listener may submit another song request - see
    /// BroadcastListenClient.SongRequestCooldownRemaining.
    public float SongRequestCooldownSecondsRemaining { get; set; }

    /// Feedback from this listener's own most recent song-request attempt (rejected format/size, still on
    /// cooldown, etc.) - cleared the next time a request is actually sent.
    public string? SongRequestError { get; set; }

    /// A short-lived maintenance notice from the relay (e.g.
    public string? ServerNotice { get; set; }
    public DateTime? ServerNoticeReceivedUtc { get; set; }
}

/// One song a listener has uploaded that's waiting for the DJ to accept or decline it - see IpcServer's
/// pending-request list and DjDeckWindow's Requests tab.
public sealed class PendingSongRequestDto
{
    public Guid RequestId { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    /// True when this came from a co-host waiting in the lobby rather than a listener - see
    /// DjDeckWindow.DrawSongRequestsList for the visual tag this drives.
    public bool IsFromCoHost { get; set; }
}

public sealed class PendingSongRequestsSnapshotMessage
{
    public List<PendingSongRequestDto> Requests { get; set; } = new();
}

public sealed class SpectrumMessage
{
    public DeckId Deck { get; set; }
    public float[] Bands { get; set; } = System.Array.Empty<float>();
}

/// The Listener view's spectrum - fed from BroadcastListenClient's own FftAnalyzer over the decoded remote
/// stream, not from a deck (no decks run locally while listening).
public sealed class ListenSpectrumMessage
{
    /// The old single blended spectrum, computed locally from the decoded stream
    /// (BroadcastListenClient.Analyzer) - kept as a fallback, but the UI now prefers BandsA/B below (real
    /// per-deck data pushed by the host) once those have arrived.
    public float[] Bands { get; set; } = System.Array.Empty<float>();
    public float[] BandsA { get; set; } = System.Array.Empty<float>();
    public float[] BandsB { get; set; } = System.Array.Empty<float>();
}

public sealed class TrackDto
{
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public float Gain { get; set; } = 1f;

    /// Detected (or DJ-corrected) beats-per-minute, or null if unknown - see BpmAnalyzer and Track.Bpm.
    public float? Bpm { get; set; }

    /// Beatgrid anchor, in seconds - see Track.BeatGridOffsetSeconds.
    public float? BeatGridOffsetSeconds { get; set; }
}

public sealed class PlaylistDto
{
    public string Name { get; set; } = string.Empty;
    public List<TrackDto> Tracks { get; set; } = new();
}

public sealed class PlaylistsSnapshotMessage
{
    public List<PlaylistDto> Playlists { get; set; } = new();
}

public sealed class DeckQueueDto
{
    public List<TrackDto> Tracks { get; set; } = new();
}

public sealed class DeckQueuesSnapshotMessage
{
    public DeckQueueDto QueueA { get; set; } = new();
    public DeckQueueDto QueueB { get; set; } = new();
}

public sealed class SoundPadDto
{
    public string Label { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public bool Looping { get; set; }
    public float LoopIntervalSeconds { get; set; } = 1f;
    public float Volume { get; set; } = 1f;
}

public sealed class SoundPadsSnapshotMessage
{
    public List<SoundPadDto> Pads { get; set; } = new();
}
