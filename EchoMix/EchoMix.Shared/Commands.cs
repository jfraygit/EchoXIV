using System;
using System.Collections.Generic;

namespace EchoMix.Shared;

/// Type string constants for IpcEnvelope.Type, shared by both ends so a typo can't silently produce two
/// different strings on each side.
public static class MessageType
{
    public const string LoadTrack = nameof(LoadTrack);
    public const string TogglePlay = nameof(TogglePlay);
    public const string SetCue = nameof(SetCue);
    public const string JumpToCue = nameof(JumpToCue);
    public const string SetPosition = nameof(SetPosition);
    public const string SetGain = nameof(SetGain);
    public const string SetTrim = nameof(SetTrim);
    public const string SetEq = nameof(SetEq);
    public const string SetFilter = nameof(SetFilter);
    public const string SetCrossfader = nameof(SetCrossfader);
    public const string SetCrossfaderCurve = nameof(SetCrossfaderCurve);
    public const string SetAutoDj = nameof(SetAutoDj);

    public const string SetLocalIdentity = nameof(SetLocalIdentity);
    public const string SetMasterVolume = nameof(SetMasterVolume);
    public const string CreatePlaylist = nameof(CreatePlaylist);
    public const string DeletePlaylist = nameof(DeletePlaylist);
    public const string RenamePlaylist = nameof(RenamePlaylist);
    public const string UploadTrack = nameof(UploadTrack);
    public const string RemoveTrack = nameof(RemoveTrack);
    public const string RequestPlaylists = nameof(RequestPlaylists);
    public const string SetOutputMuted = nameof(SetOutputMuted);
    public const string Shutdown = nameof(Shutdown);

    public const string AssignTrackToDeck = nameof(AssignTrackToDeck);
    public const string RemoveFromDeckQueue = nameof(RemoveFromDeckQueue);
    public const string SetTrackGain = nameof(SetTrackGain);
    public const string DeckQueuesSnapshot = nameof(DeckQueuesSnapshot);
    public const string UnloadDeck = nameof(UnloadDeck);

    public const string SetDeckAutoplay = nameof(SetDeckAutoplay);

    public const string SetDeckSync = nameof(SetDeckSync);

    public const string SetDeckTempo = nameof(SetDeckTempo);

    public const string SetTrackBpm = nameof(SetTrackBpm);

    public const string PreviewTrack = nameof(PreviewTrack);
    public const string StopPreviewTrack = nameof(StopPreviewTrack);
    public const string SetPreviewVolume = nameof(SetPreviewVolume);
    public const string SetPreviewDampenVolume = nameof(SetPreviewDampenVolume);

    public const string UploadSoundPad = nameof(UploadSoundPad);
    public const string SetSoundPadLabel = nameof(SetSoundPadLabel);
    public const string RemoveSoundPad = nameof(RemoveSoundPad);
    public const string PlaySoundPad = nameof(PlaySoundPad);
    public const string RequestSoundPads = nameof(RequestSoundPads);
    public const string SetSoundPadLoop = nameof(SetSoundPadLoop);
    public const string SetSoundPadLoopInterval = nameof(SetSoundPadLoopInterval);
    public const string SetSoundPadVolume = nameof(SetSoundPadVolume);

    public const string MixerStatus = nameof(MixerStatus);
    public const string Spectrum = nameof(Spectrum);
    public const string ListenSpectrum = nameof(ListenSpectrum);
    public const string PlaylistsSnapshot = nameof(PlaylistsSnapshot);
    public const string SoundPadsSnapshot = nameof(SoundPadsSnapshot);

    public const string StartBroadcast = nameof(StartBroadcast);
    public const string StopBroadcast = nameof(StopBroadcast);
    public const string SetProximityMode = nameof(SetProximityMode);
    public const string ConnectToRemote = nameof(ConnectToRemote);
    public const string DisconnectFromRemote = nameof(DisconnectFromRemote);
    public const string SetListenVolume = nameof(SetListenVolume);

    public const string JoinAsHost = nameof(JoinAsHost);
    public const string PromoteHost = nameof(PromoteHost);
    public const string SetMonitorVolume = nameof(SetMonitorVolume);

    public const string StartSpotifyMode = nameof(StartSpotifyMode);
    public const string StopSpotifyMode = nameof(StopSpotifyMode);

    public const string SpotifySkipNext = nameof(SpotifySkipNext);
    public const string SpotifySkipPrevious = nameof(SpotifySkipPrevious);
    public const string SpotifyTogglePlayPause = nameof(SpotifyTogglePlayPause);

    public const string RequestAudioInputDevices = nameof(RequestAudioInputDevices);
    public const string AudioInputDevicesSnapshot = nameof(AudioInputDevicesSnapshot);
    public const string StartExternalInputMode = nameof(StartExternalInputMode);
    public const string StopExternalInputMode = nameof(StopExternalInputMode);

    public const string RequestCapturableProcesses = nameof(RequestCapturableProcesses);
    public const string CapturableProcessesSnapshot = nameof(CapturableProcessesSnapshot);

    public const string RequestSong = nameof(RequestSong);
    public const string AcceptSongRequest = nameof(AcceptSongRequest);
    public const string DeclineSongRequest = nameof(DeclineSongRequest);
    public const string PendingSongRequestsSnapshot = nameof(PendingSongRequestsSnapshot);

    public const string SetSongRequestAccessControl = nameof(SetSongRequestAccessControl);

    public const string SubmitBugReport = nameof(SubmitBugReport);
    public const string BugReportResult = nameof(BugReportResult);

    public const string RequestPublicShows = nameof(RequestPublicShows);
    public const string PublicShowsSnapshot = nameof(PublicShowsSnapshot);

    public const string ReportShow = nameof(ReportShow);
    public const string ReportShowResult = nameof(ReportShowResult);

    public const string RequestDjProfiles = nameof(RequestDjProfiles);
    public const string DjProfilesSnapshot = nameof(DjProfilesSnapshot);
    public const string GetDjProfileDetail = nameof(GetDjProfileDetail);
    public const string DjProfileDetailSnapshot = nameof(DjProfileDetailSnapshot);
    public const string SaveDjProfile = nameof(SaveDjProfile);
    public const string DjProfileSaveResult = nameof(DjProfileSaveResult);
    public const string DeleteDjProfile = nameof(DeleteDjProfile);
    public const string DjProfileDeleteResult = nameof(DjProfileDeleteResult);
    public const string ReportDjProfile = nameof(ReportDjProfile);
    public const string DjProfileReportResult = nameof(DjProfileReportResult);
    public const string ToggleDjProfileLike = nameof(ToggleDjProfileLike);
    public const string DjProfileLikeResult = nameof(DjProfileLikeResult);
    public const string ToggleDjProfileFollow = nameof(ToggleDjProfileFollow);
    public const string DjProfileFollowResult = nameof(DjProfileFollowResult);
    public const string GenerateProfileLinkCode = nameof(GenerateProfileLinkCode);
    public const string ProfileLinkCodeResult = nameof(ProfileLinkCodeResult);
    public const string RedeemProfileLinkCode = nameof(RedeemProfileLinkCode);
    public const string ProfileLinkRedeemResult = nameof(ProfileLinkRedeemResult);
    public const string UnlinkProfileCharacter = nameof(UnlinkProfileCharacter);
    public const string ProfileUnlinkResult = nameof(ProfileUnlinkResult);

    public const string FollowedDjWentLive = nameof(FollowedDjWentLive);

    public const string SetDjProfileImage = nameof(SetDjProfileImage);
    public const string DjProfileImageResult = nameof(DjProfileImageResult);

    public const string SetShowImage = nameof(SetShowImage);

    public const string SetShowName = nameof(SetShowName);

    public const string SetWebListenLink = nameof(SetWebListenLink);
}

public sealed class StartExternalInputModeCommand
{
    /// The PRIMARY slot's Windows recording device - ignored if ProcessName is set instead (see ProcessName's
    /// own doc comment).
    public string DeviceId { get; set; } = string.Empty;

    /// The PRIMARY slot's alternative to DeviceId - captures one application's own audio output instead of a
    /// Windows recording device (see MixerEngine.StartExternalInputModeAsync).
    public string? ProcessName { get; set; }

    /// Optional second Windows recording device - when set, Deck B's own Gain/Trim/EQ/ Filter dials
    /// (otherwise idle while External Input Mode is active) control this second input, and both are summed
    /// together into the single broadcast feed.
    public string? DeviceId2 { get; set; }
}

public sealed class StartBroadcastCommand
{
    /// Null/empty asks the relay to assign a short random code; a caller-supplied value is a vanity code
    /// request.
    public string? RoomCode { get; set; }
    public string Password { get; set; } = string.Empty;

    /// Separate from Password - required to go live even for a solo DJ.
    public string HostPassword { get; set; } = string.Empty;

    public string DjName { get; set; } = string.Empty;

    /// The DJ's real character name - AudioHost has no Dalamud access of its own, so the plugin (which does)
    /// reads this from IClientState and passes it along here, purely so listeners' ProximityTracker has
    /// something to search IObjectTable for.
    public string CharacterName { get; set; } = string.Empty;
    public bool IsProximityAudio { get; set; } = true;

    /// How close (in yalms) a listener needs to be to the host's character to hear anything at all in
    /// Proximity mode - see ProximityTracker.
    public float ProximityRange { get; set; } = 30f;

    /// Opts this show into the "View Live Shows" browse grid - see
    /// RelayProtocol.RegisterHostMessage.IsPubliclyListed's own doc comment for what this waives server-side
    /// (the otherwise-mandatory listener password).
    public bool IsPubliclyListed { get; set; }

    /// Cosmetic display name for the browse grid - separate from RoomCode/vanity code, which stays the only
    /// functional join key.
    public string? ShowName { get; set; }

    /// Only meaningful (and only shown to listeners) when IsProximityAudio is true - see RelayProtocol's
    /// Room-side doc comment for the Venue/Global split.
    public string? VenueName { get; set; }
    public string? VenueDataCenter { get; set; }
    public string? VenueWorld { get; set; }
    public string? VenueHousingArea { get; set; }
    public string? VenueWard { get; set; }
    public string? VenuePlot { get; set; }
    public bool VenueIsApartment { get; set; }
    public bool VenueSubdivision { get; set; }
}

/// See MessageType.SetShowImage's own doc comment - SourceFilePath points at an already cropped/resized temp
/// file the plugin wrote, not the DJ's original picked file.
public sealed class SetShowImageCommand
{
    public string SourceFilePath { get; set; } = string.Empty;
}

/// See MessageType.SetShowName's own doc comment.
public sealed class SetShowNameCommand
{
    public string? ShowName { get; set; }
}

/// See MessageType.SetWebListenLink's own doc comment.
public sealed class SetWebListenLinkCommand
{
    public bool Enabled { get; set; }
}

/// See MessageType.SetDjProfileImage's own doc comment - SourceFilePath points at an already cropped/resized
/// (square for avatar, 3:1 for banner) temp file the plugin wrote.
public sealed class SetDjProfileImageCommand
{
    public string ProfileId { get; set; } = string.Empty;

    /// Filled in by the plugin, not AudioHost - same reasoning as StartBroadcastCommand.CharacterName
    /// (AudioHost has no Dalamud access of its own), and required here since the relay verifies this matches
    /// the profile's own owner before writing anything to disk (see
    /// RelayServer.HandleDjProfileImageUploadAsync).
    public string CharacterName { get; set; } = string.Empty;

    /// "avatar" or "banner" - see DjProfileImageChunkMessage.Slot.
    public string Slot { get; set; } = string.Empty;
    public string SourceFilePath { get; set; } = string.Empty;
}

/// Joins an already-live room as an additional co-host DJ rather than a plain listener - the counterpart to
/// StartBroadcastCommand for a DJ who isn't the one who went live first.
public sealed class JoinAsHostCommand
{
    public string RoomCode { get; set; } = string.Empty;
    public string HostPassword { get; set; } = string.Empty;
    public string DjName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
}

/// Sent by whoever currently holds lead to hand it to another connected co-host - see
/// BroadcastStatusMessage.HostRoster for the Guid identifying each connected DJ.
public sealed class PromoteHostCommand
{
    public Guid TargetHostId { get; set; }
}

/// How loud the current lead's incoming live audio plays back locally while a co-host is monitoring (not lead
/// themselves) - independent of the deck fader, like a DJ's own cue/ headphone mix versus the master fader.
public sealed class SetMonitorVolumeCommand
{
    public float Volume { get; set; }
}

/// Sent whenever either the Proximity/Global toggle or the range slider changes - always carries both current
/// values together rather than one at a time, so the relay/listeners never need to reconcile a partial
/// update.
public sealed class SetProximityModeCommand
{
    public bool IsProximityAudio { get; set; }
    public float ProximityRange { get; set; } = 30f;
}

public sealed class ConnectToRemoteCommand
{
    public string RoomCode { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// The listener's own real character name - same reasoning as StartBroadcastCommand's own CharacterName
    /// (AudioHost has no Dalamud access of its own, so the plugin reads this from IClientState and passes it
    /// along) - lets the Host Lobby's Listeners section show who's actually tuned in instead of just a bare
    /// count.
    public string CharacterName { get; set; } = string.Empty;
}

public sealed class SetListenVolumeCommand
{
    public float Volume { get; set; }
}

public sealed class LoadTrackCommand
{
    public DeckId Deck { get; set; }
    public string FilePath { get; set; } = string.Empty;
}

public sealed class DeckCommand
{
    public DeckId Deck { get; set; }
}

public sealed class SetDeckAutoplayCommand
{
    public DeckId Deck { get; set; }
    public bool Enabled { get; set; }
}

/// Sync pulls one deck's tempo (pitch-preserving) to match the other's BPM - mutually exclusive per deck (see
/// MixerEngine.SetDeckSync), the same "only one at a time" shape as a mixer's solo buttons.
public sealed class SetDeckSyncCommand
{
    public DeckId Deck { get; set; }
    public bool Enabled { get; set; }
}

/// Manual tempo override (the BPM slider under each deck's display) - see MixerEngine.SetDeckTempo.
public sealed class SetDeckTempoCommand
{
    public DeckId Deck { get; set; }
    public float Ratio { get; set; }
}

public sealed class SetPositionCommand
{
    public DeckId Deck { get; set; }
    public double PositionSeconds { get; set; }
}

public sealed class SetGainCommand
{
    public DeckId Deck { get; set; }
    public float Gain { get; set; }
}

/// Input trim - a separate gain stage from the channel fader (SetGainCommand), applied earlier in the signal
/// chain (before EQ) rather than at the end of it - the same gain/trim-knob-vs-channel-fader split a real
/// mixer has.
public sealed class SetTrimCommand
{
    public DeckId Deck { get; set; }
    public float Trim { get; set; }
}

public sealed class SetEqCommand
{
    public DeckId Deck { get; set; }
    public EqBand Band { get; set; }
    public float GainDb { get; set; }
}

public sealed class SetFilterCommand
{
    public DeckId Deck { get; set; }
    public float Knob { get; set; }
}

public sealed class SetCrossfaderCommand
{
    public float Position { get; set; }
}

/// The shape of the volume ramp between Deck A and Deck B as the crossfader moves - see
/// CrossfadeSampleProvider for the actual per-curve gain math.
public enum CrossfaderCurve
{
    Linear,
    Cut,
    Power,
}

/// Null means no curve button is toggled on - the original, always-been-there crossfade feel (which happens
/// to be the same math as CrossfaderCurve.Power, just not visually pinned to that button) rather than a
/// fourth named option.
public sealed class SetCrossfaderCurveCommand
{
    public CrossfaderCurve? Curve { get; set; }
}

/// Toggles Auto-DJ (see MixerEngine.Tick) - while enabled, the crossfader animates itself across to the other
/// deck as the currently-favored one nears the end of its track, using whatever CrossfaderCurve is already
/// set.
public sealed class SetAutoDjCommand
{
    public bool Enabled { get; set; }
    public float FadeSeconds { get; set; } = 6f;
}

public sealed class SetLocalIdentityCommand
{
    public string CharacterName { get; set; } = string.Empty;
}

public sealed class SetMasterVolumeCommand
{
    public float Volume { get; set; }
}

public sealed class PlaylistNameCommand
{
    public string PlaylistName { get; set; } = string.Empty;
}

public sealed class RenamePlaylistCommand
{
    public string PlaylistName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}

public sealed class UploadTrackCommand
{
    public string PlaylistName { get; set; } = string.Empty;
    public string SourceFilePath { get; set; } = string.Empty;
}

public sealed class RemoveTrackCommand
{
    public string PlaylistName { get; set; } = string.Empty;
    public string TrackFilePath { get; set; } = string.Empty;
}

/// Assigns a track to a deck's upcoming-songs queue.
public sealed class AssignTrackToDeckCommand
{
    public DeckId Deck { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public float Gain { get; set; } = 1f;
    public double DurationSeconds { get; set; }
    public float? Bpm { get; set; }
    public float? BeatGridOffsetSeconds { get; set; }
}

public sealed class RemoveFromDeckQueueCommand
{
    public DeckId Deck { get; set; }
    public int Index { get; set; }
}

/// Identifies the queued track to cue up by file path, same as SetTrackGainCommand/ RemoveTrackCommand
/// elsewhere - a queue's own index shifts around too easily (auto-advance, removals) to be a stable identity
/// for something a preview can stay attached to.
public sealed class PreviewTrackCommand
{
    public string FilePath { get; set; } = string.Empty;
}

/// How loud a cue preview plays locally - user-configurable (Settings > General > Song Preview) rather than a
/// guessed constant, since how loud a cue channel should be relative to the main mix comes down to a given
/// DJ's own headphone taste.
public sealed class SetPreviewVolumeCommand
{
    public float Volume { get; set; }
}

/// How much the main mix quiets down (for the DJ only) while a cue preview plays - same reasoning as
/// SetPreviewVolumeCommand.
public sealed class SetPreviewDampenVolumeCommand
{
    public float Volume { get; set; }
}

/// Sets a playlist track's saved gain - applied automatically to the deck's fader whenever that track is
/// loaded, so songs that are mixed quieter/louder than the rest of the playlist don't need manual fader
/// riding every time they come up.
public sealed class SetTrackGainCommand
{
    public string PlaylistName { get; set; } = string.Empty;
    public string TrackFilePath { get; set; } = string.Empty;
    public float Gain { get; set; }
}

/// Manually corrects a playlist track's BPM - the fallback for when BpmAnalyzer's auto-detected value is
/// wrong (no beat detector is perfect) or a track hasn't been analyzed yet.
public sealed class SetTrackBpmCommand
{
    public string PlaylistName { get; set; } = string.Empty;
    public string TrackFilePath { get; set; } = string.Empty;
    /// Null (or &lt;= 0) clears back to "unknown" - lets a future auto-detection result show through again
    /// instead of being permanently overridden.
    public float? Bpm { get; set; }
}

public sealed class SetOutputMutedCommand
{
    public bool Muted { get; set; }
}

public sealed class SoundPadIndexCommand
{
    public int PadIndex { get; set; }
}

public sealed class UploadSoundPadCommand
{
    public int PadIndex { get; set; }
    public string SourceFilePath { get; set; } = string.Empty;
}

public sealed class SetSoundPadLabelCommand
{
    public int PadIndex { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class SetSoundPadLoopCommand
{
    public int PadIndex { get; set; }
    public bool Looping { get; set; }
}

public sealed class SetSoundPadLoopIntervalCommand
{
    public int PadIndex { get; set; }
    public float IntervalSeconds { get; set; }
}

public sealed class SetSoundPadVolumeCommand
{
    public int PadIndex { get; set; }
    public float Volume { get; set; }
}

/// Listener -> their own AudioHost - picks a local file off their own disk to send up as a song request.
public sealed class RequestSongCommand
{
    public string SourceFilePath { get; set; } = string.Empty;
    public string RequesterName { get; set; } = string.Empty;
}

/// DJ -> their own AudioHost - loads a pending song request onto a deck, same as AssignTrackToDeckCommand but
/// by RequestId instead of a full TrackDto (the host already has the file and everything needed to describe
/// it from when the request first came in).
public sealed class AcceptSongRequestCommand
{
    public Guid RequestId { get; set; }
    public DeckId Deck { get; set; }
}

public sealed class DeclineSongRequestCommand
{
    public Guid RequestId { get; set; }
}

public enum SongRequestAccessMode
{
    /// Anyone in the room can submit a request - the default.
    Open,

    /// Only character names in Whitelist may submit a request.
    Whitelist,

    /// Anyone EXCEPT character names in Blacklist may submit a request.
    Blacklist,
}

/// Pushes the DJ's song-request access list down to their own AudioHost - see
/// MessageType.SetSongRequestAccessControl.
public sealed class SetSongRequestAccessControlCommand
{
    public SongRequestAccessMode Mode { get; set; }
    public List<string> Whitelist { get; set; } = new();
    public List<string> Blacklist { get; set; } = new();
}

public sealed class SubmitBugReportCommand
{
    public string? Description { get; set; }

    /// Optional - lets the reporter volunteer a way to be contacted directly about this specific report,
    /// instead of only ever being reachable if they happen to see a follow-up posted back into a public
    /// Discord channel.
    public string? DiscordName { get; set; }

    /// Filled in by the plugin, not AudioHost - same reasoning as StartBroadcastCommand.CharacterName
    /// (AudioHost has no Dalamud access of its own to read either of these itself).
    public string PluginVersion { get; set; } = string.Empty;
    public string? CharacterName { get; set; }

    /// Dalamud version and UI-related settings - see EchoMix.Plugin.SystemDiagnostics.Capture.
    public string? SystemInfo { get; set; }
}

public sealed class BugReportResultMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public sealed class ReportShowCommand
{
    public string RoomCode { get; set; } = string.Empty;
    public string? ShowName { get; set; }
    public string? DjName { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// Filled in by the plugin, not AudioHost - same reasoning as SubmitBugReportCommand.CharacterName
    /// (AudioHost has no Dalamud access of its own).
    public string? ReporterCharacterName { get; set; }
}

public sealed class ReportShowResultMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
