using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace EchoMix.Shared;

/// Fixed, shared-by-everyone connection info for the one relay this build of the plugin talks to - not a
/// per-user setting, since a listener has to reach the exact same relay the host registered their room on.
public static class RelayConfig
{
    public const string DefaultHost = "relay.echoxiv.com";

#if DEBUG
    public const int DefaultPort = 8421;
#else
    public const int DefaultPort = 8420;
#endif

    public const string PinnedCertThumbprint = "B596524F96A39FA307DADB0017ED053AD8C1799DA6E32F4AC365F70DC853EE15";

    public const int MaxListenersPerRoom = 100;

    public const int MaxHostsPerRoom = 8;

    public const int MaxWebListenersPerRoom = 50;

    public const string WebListenBaseUrl = "https://echoxiv.com/listen";
}

/// Type string constants for RelayEnvelope.Type - the relay's own control-message vocabulary, separate from
/// EchoMix.Shared.MessageType (which is the *local* pipe protocol between the plugin and its own AudioHost,
/// and never crosses a real network).
public static class RelayMessageType
{
    public const string RegisterHost = nameof(RegisterHost);
    public const string HostRegistered = nameof(HostRegistered);
    public const string JoinRoom = nameof(JoinRoom);
    public const string JoinAccepted = nameof(JoinAccepted);
    public const string JoinRejected = nameof(JoinRejected);
    public const string TrackInfo = nameof(TrackInfo);
    public const string ProximityModeChanged = nameof(ProximityModeChanged);

    public const string ListenerRosterChanged = nameof(ListenerRosterChanged);
    public const string Bye = nameof(Bye);

    public const string HostRosterChanged = nameof(HostRosterChanged);
    public const string PromoteHost = nameof(PromoteHost);
    public const string LeadChanged = nameof(LeadChanged);

    public const string SpectrumUpdate = nameof(SpectrumUpdate);

    public const string SongRequestChunk = nameof(SongRequestChunk);
    public const string SongRequestDeclined = nameof(SongRequestDeclined);

    public const string SubmitBugReport = nameof(SubmitBugReport);
    public const string BugReportAck = nameof(BugReportAck);

    public const string ServerNotice = nameof(ServerNotice);

    public const string RequestPublicShows = nameof(RequestPublicShows);
    public const string PublicShowsSnapshot = nameof(PublicShowsSnapshot);

    public const string ShowImageChunk = nameof(ShowImageChunk);

    public const string UpdateShowName = nameof(UpdateShowName);

    public const string SubmitShowReport = nameof(SubmitShowReport);
    public const string ShowReportAck = nameof(ShowReportAck);

    public const string RequestDjProfiles = nameof(RequestDjProfiles);
    public const string DjProfilesSnapshot = nameof(DjProfilesSnapshot);
    public const string GetDjProfileDetail = nameof(GetDjProfileDetail);
    public const string DjProfileDetailSnapshot = nameof(DjProfileDetailSnapshot);
    public const string SaveDjProfile = nameof(SaveDjProfile);
    public const string DjProfileSaveResult = nameof(DjProfileSaveResult);
    public const string DeleteDjProfile = nameof(DeleteDjProfile);
    public const string DjProfileDeleteResult = nameof(DjProfileDeleteResult);
    public const string DjProfileImageChunk = nameof(DjProfileImageChunk);
    public const string DjProfileImageAck = nameof(DjProfileImageAck);
    public const string SubmitDjProfileReport = nameof(SubmitDjProfileReport);
    public const string DjProfileReportAck = nameof(DjProfileReportAck);
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

    public const string DeckQueuesUpdate = nameof(DeckQueuesUpdate);

    public const string RegisterPresence = nameof(RegisterPresence);
    public const string FollowedDjWentLive = nameof(FollowedDjWentLive);

    public const string SetWebListenLink = nameof(SetWebListenLink);
    public const string WebListenLinkUpdated = nameof(WebListenLinkUpdated);
}

/// Wraps every control frame crossing the relay connection - mirrors EchoMix.Shared.IpcEnvelope's shape (a
/// type tag plus a generic JObject payload) but kept as its own type since the two protocols' message
/// vocabularies are unrelated.
public sealed class RelayEnvelope
{
    public string Type { get; set; } = string.Empty;
    public JObject Payload { get; set; } = new();

    public static RelayEnvelope For<T>(string type, T payload) =>
        new() { Type = type, Payload = JObject.FromObject(payload!) };

    public T ReadPayload<T>() => Payload.ToObject<T>()!;
}

public sealed class RegisterHostMessage
{
    /// Null/empty asks the relay to generate a short random code when creating a new room; a caller-supplied
    /// value is either a vanity code request (creating) or the room to join (co-hosting) - required whenever
    /// IsCoHostJoin is true.
    public string? RoomCode { get; set; }

    /// Explicit intent flag rather than inferring "join vs create" from whether RoomCode is already live - a
    /// vanity-code create request would otherwise be ambiguous with joining an existing room of the same
    /// code.
    public bool IsCoHostJoin { get; set; }

    /// The listener password - set when creating a room, ignored when co-host joining (a co-host
    /// authenticates with HostPasswordHash instead).
    public string PasswordHash { get; set; } = string.Empty;

    /// The separate co-host password: set (along with PasswordHash) when creating a room, and the credential
    /// a later DJ authenticates with to join that same room as a co-host instead of a plain listener.
    public string HostPasswordHash { get; set; } = string.Empty;

    public string DjName { get; set; } = string.Empty;

    /// The host's real in-game character name, used for the listener's proximity distance lookup
    /// (IObjectTable is keyed by actual character names, not the display-only DjName above, which the host
    /// can override to something else entirely).
    public string CharacterName { get; set; } = string.Empty;

    /// Create-only - a co-host joining an already-live room inherits the room's existing proximity settings
    /// rather than supplying their own.
    public bool IsProximityAudio { get; set; } = true;
    public float ProximityRange { get; set; } = 30f;
    public int SampleRate { get; set; }

    /// Create-only, like the proximity fields above.
    public bool IsPubliclyListed { get; set; }

    /// Create-only.
    public string? ShowName { get; set; }

    /// Create-only.
    public string? VenueName { get; set; }
    public string? VenueDataCenter { get; set; }
    public string? VenueWorld { get; set; }
    public string? VenueHousingArea { get; set; }
    public string? VenueWard { get; set; }

    /// Plot number for a house, or apartment number when VenueIsApartment is true - Lifestream's own
    /// AddressBookEntry reuses one field for both (Plot and Apartment always hold the same value there), so
    /// this does too rather than adding a parallel field.
    public string? VenuePlot { get; set; }

    /// False (default) = house, so every pre-existing venue/show with no opinion on this field is still a
    /// valid house address with zero migration.
    public bool VenueIsApartment { get; set; }

    /// Only meaningful when VenueIsApartment is true - several housing areas have a second, separate
    /// apartment building added as a "subdivision" of the same area (e.g.
    public bool VenueSubdivision { get; set; }

    /// Carries whatever Web Listen Link state the client last knew about through a RE-registration (a co-host
    /// join never has one yet, so both default off/null) - see RelayMessageType.SetWebListenLink's own doc
    /// comment.
    public bool WebListenEnabled { get; set; }
    public string? WebListenToken { get; set; }
}

public sealed class HostRegisteredMessage
{
    public bool Accepted { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public string? RejectReason { get; set; }

    /// This connection's own id within the room - referenced by PromoteHostMessage and compared against
    /// HostRosterChangedMessage.LeadHostId to know whether "you" are lead.
    public Guid HostId { get; set; }
    public bool IsLead { get; set; }

    /// When the room was first created, regardless of when this particular connection joined it - lets a
    /// co-host joining an already-live room show the real elapsed time instead of starting its own timer at
    /// 0:00.
    public DateTime? RoomCreatedAtUtc { get; set; }

    /// Echoes back whatever Web Listen Link state now actually applies to this room (the re-minted/restored
    /// token on a reconnect, or whatever the register request asked for on a fresh room) - lets
    /// BroadcastHostConnection update its own saved state and the plugin's
    /// BroadcastStatusMessage.WebListenUrl without a separate round trip.
    public bool WebListenEnabled { get; set; }
    public string? WebListenToken { get; set; }
}

public sealed class JoinRoomMessage
{
    public string RoomCode { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// The listener's own real character name - purely for display in the Host Lobby's Listeners section (see
    /// ListenerRosterEntryDto), never used for authentication or proximity (that's the host's own
    /// CharacterName in JoinAcceptedMessage).
    public string CharacterName { get; set; } = string.Empty;
}

public sealed class JoinAcceptedMessage
{
    public string DjName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public bool IsProximityAudio { get; set; }
    public float ProximityRange { get; set; } = 30f;
    public int SampleRate { get; set; }

    /// Same "room's real start time" as HostRegisteredMessage's own field, and the same
    /// nullable-for-backward-compatibility reasoning - see that field's own doc comment.
    public DateTime? RoomCreatedAtUtc { get; set; }
}

public sealed class JoinRejectedMessage
{
    public string Reason { get; set; } = string.Empty;
}

/// Both decks' own now-playing info, not just whichever one happens to be audible right now - lets listeners
/// see a genuine per-deck readout (title/seek/time for A and B separately), matching the DJ's own dual-deck
/// display, instead of one unified "now playing" the broadcast used to report.
public sealed class TrackInfoMessage
{
    public string? TitleA { get; set; }
    public double PositionSecondsA { get; set; }
    public double DurationSecondsA { get; set; }
    public string? TitleB { get; set; }
    public double PositionSecondsB { get; set; }
    public double DurationSecondsB { get; set; }

    /// Deck B can never be used while Spotify Mode is active (it replaces Deck A only - see
    /// MixerEngine.StartSpotifyModeAsync), so listeners use this to hide Deck B's now-useless half entirely
    /// instead of just seeing it sit frozen.
    public bool IsSpotifyModeActive { get; set; }
}

/// Deck A/B's own spectrum bands (see MixerEngine.AnalyzerA/B), pushed periodically so listeners can see a
/// genuinely per-deck dual visualizer instead of one computed locally from the already-mixed decoded audio -
/// the same 40-band FftAnalyzer.GetSpectrum output the DJ's own screens already use, just also sent across
/// the relay.
public sealed class SpectrumUpdateMessage
{
    public float[] BandsA { get; set; } = Array.Empty<float>();
    public float[] BandsB { get; set; } = Array.Empty<float>();
}

public sealed class ProximityModeChangedMessage
{
    public bool IsProximityAudio { get; set; }
    public float ProximityRange { get; set; } = 30f;
}

/// One connected listener, as shown in the Host Lobby window's Listeners section - the listener-facing
/// counterpart to HostRosterEntryDto.
public sealed class ListenerRosterEntryDto
{
    public Guid ListenerId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public DateTime ConnectedAtUtc { get; set; }
}

/// Relay -> all hosts, pushed whenever a listener joins or leaves the room - lets the Broadcast settings
/// tab's count and the Host Lobby's Listeners list both stay in sync without polling, the same way
/// HostRosterChangedMessage does for the DJ roster.
public sealed class ListenerRosterChangedMessage
{
    public List<ListenerRosterEntryDto> Listeners { get; set; } = new();
}

/// One connected co-host, as shown in the plugin's Host Lobby window roster.
public sealed class HostRosterEntryDto
{
    public Guid HostId { get; set; }
    public string DjName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public DateTime ConnectedAtUtc { get; set; }
}

/// Relay -> all hosts, pushed whenever the roster or the lead changes (a host joins, leaves, or is promoted)
/// - lets every co-host's Host Lobby window stay in sync without polling.
public sealed class HostRosterChangedMessage
{
    public List<HostRosterEntryDto> Hosts { get; set; } = new();
    public Guid LeadHostId { get; set; }
}

/// Lead host -> relay only.
public sealed class PromoteHostMessage
{
    public Guid TargetHostId { get; set; }
}

/// Relay -> all listeners (a parallel, listener-facing announcement to HostRosterChangedMessage, which only
/// goes to hosts).
public sealed class LeadChangedMessage
{
    public string DjName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
}

/// One chunk of a listener's (or co-host's - see IsFromCoHost) uploaded song-request file.
public sealed class SongRequestChunkMessage
{
    public Guid RequestId { get; set; }
    public Guid ListenerId { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public int TotalChunks { get; set; }
    public string DataBase64 { get; set; } = string.Empty;
    public bool IsFromCoHost { get; set; }
}

/// One chunk of a live show's uploaded image - same shape and chunking reasoning as SongRequestChunkMessage
/// above, just without a per-listener destination (this one just updates Room.ImageBytes directly - see
/// RelayServer.HandleHostAsync's read loop).
public sealed class ShowImageChunkMessage
{
    public Guid RequestId { get; set; }
    public int ChunkIndex { get; set; }
    public int TotalChunks { get; set; }
    public string DataBase64 { get; set; } = string.Empty;
}

/// Lead host -> relay only, mirrors RegisterHostMessage.ShowName but for a rename mid-show - see
/// RelayMessageType.UpdateShowName's own doc comment.
public sealed class UpdateShowNameMessage
{
    public string? ShowName { get; set; }
}

/// Host -> relay -> one specific listener (or co-host - see IsFromCoHost) - sent when that request is
/// explicitly declined, or silently discarded by the DJ's whitelist/blacklist - so their own UI can clear the
/// "pending" state instead of it just hanging forever.
public sealed class SongRequestDeclinedMessage
{
    public Guid RequestId { get; set; }
    public Guid ListenerId { get; set; }
    public string? Reason { get; set; }
    public bool IsFromCoHost { get; set; }
}

/// Lead host -> relay -> every other host in the room, pushed whenever either deck's upcoming queue changes -
/// lets a waiting co-host see what's coming up next without needing to be lead themselves.
public sealed class DeckQueuesUpdateMessage
{
    public DeckQueueDto QueueA { get; set; } = new();
    public DeckQueueDto QueueB { get; set; } = new();
}

/// Client -> relay, a one-shot diagnostic report - see RelayMessageType.SubmitBugReport's own doc comment for
/// why this doesn't carry the Discord webhook itself.
public sealed class SubmitBugReportMessage
{
    public string PluginVersion { get; set; } = string.Empty;
    public string? DjName { get; set; }
    public string? CharacterName { get; set; }
    public string? Description { get; set; }

    /// Optional - a reporter's own Discord username/tag, volunteered purely so the developer can follow up
    /// with them directly instead of only ever posting back into a public channel and hoping they see it.
    public string? DiscordName { get; set; }
    public string? StatusSnapshot { get; set; }
    public string? SystemInfo { get; set; }
    public string? LogContent { get; set; }
}

public sealed class BugReportAckMessage
{
    public bool Accepted { get; set; }
    public string? Error { get; set; }
}

/// Client -> relay, a listener reporting a public show for misuse - see RelayMessageType.SubmitShowReport.
public sealed class SubmitShowReportMessage
{
    public string RoomCode { get; set; } = string.Empty;
    public string? ShowName { get; set; }
    public string? DjName { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ReporterCharacterName { get; set; }
}

public sealed class ShowReportAckMessage
{
    public bool Accepted { get; set; }
    public string? Error { get; set; }
}

/// One publicly-listed show, as shown in a listener's "View Live Shows" grid - see
/// RelayServer.HandlePublicShowsQueryAsync.
public sealed class PublicShowEntryDto
{
    public string RoomCode { get; set; } = string.Empty;
    public string? ShowName { get; set; }
    public string? DjName { get; set; }
    public DateTime LiveSinceUtc { get; set; }
    public int ListenerCount { get; set; }
    public bool IsVenueShow { get; set; }
    public string? VenueName { get; set; }
    public string? VenueDataCenter { get; set; }
    public string? VenueWorld { get; set; }
    public string? VenueHousingArea { get; set; }
    public string? VenueWard { get; set; }
    public string? VenuePlot { get; set; }
    public bool VenueIsApartment { get; set; }
    public bool VenueSubdivision { get; set; }

    /// The host's real in-game character name and configured proximity radius, present only when IsVenueShow
    /// is true (see RelayServer.HandlePublicShowsQueryAsync) - lets a listener's client run the same
    /// IObjectTable-based distance check ProximityTracker already does for a joined show, but against every
    /// currently-listed Proximity show, before ever joining one (see AutoJoinTracker).
    public string? HostCharacterName { get; set; }
    public float ProximityRange { get; set; }

    /// True if joining this room needs a real password - see RelayServer.HandlePublicShowsQueryAsync's own
    /// doc comment for how this is computed (a public listing doesn't imply passwordless; a DJ can go public
    /// and still gate it).
    public bool HasPassword { get; set; }

    /// Null until the host uploads one (see ShowImageChunkMessage/Room.ImageBytes) - already a small,
    /// pre-cropped/resized JPEG (see ShowImageProcessor on the plugin side), so it's sent inline here rather
    /// than needing its own separate per-image request.
    public string? ImageBase64 { get; set; }

    /// Resolved server-side from the host's own DJ List profile (see DjProfileStore.FindByCharacterName), not
    /// something a show sets directly - lets Live Shows filter by genre the same way the DJ List already can.
    public List<string> Genres { get; set; } = new();
}

public sealed class PublicShowsSnapshotMessage
{
    public List<PublicShowEntryDto> Shows { get; set; } = new();

    /// Never set by the relay itself (a successful query is just an empty Shows list) - this is populated
    /// only when AudioHost reuses this same class to push the result of a failed PublicShowsClient round trip
    /// back to the plugin (see IpcServer.
    public string? Error { get; set; }
}

/// A short-lived maintenance notice pushed to every connection - see
/// RelayServer.BroadcastServerNoticeToEveryoneAsync.
public sealed class ServerNoticeMessage
{
    public string Text { get; set; } = string.Empty;
}

/// One day's worth of availability on a DJ List profile - see DjProfileStore's own doc comment for why this
/// persists indefinitely rather than living only as long as a connection.
public sealed class DjAvailabilityDayDto
{
    public bool IsAvailable { get; set; }
    public string? TimeNote { get; set; }
}

/// One of a DJ's saved venue addresses (DjProfileDetailDto.SavedVenues/ SaveDjProfileMessage.SavedVenues).
public sealed class SavedVenueDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DataCenter { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string HousingArea { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;

    /// Plot number for a house, or apartment number when IsApartment is true - see
    /// RegisterHostMessage.VenuePlot's doc comment for why this one field serves both.
    public string Plot { get; set; } = string.Empty;
    public bool IsApartment { get; set; }
    public bool Subdivision { get; set; }
}

/// One DJ List card, as shown in the browse grid - see RelayServer's DJ profile handlers.
public sealed class DjProfileSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string DjName { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public List<string> Genres { get; set; } = new();

    /// Already a small, pre-cropped/resized JPEG (see ShowImageProcessor.DjAvatarSize on the plugin side) -
    /// small enough to inline for every card in one request, same reasoning PublicShowEntryDto.ImageBase64
    /// already uses for live shows.
    public string? AvatarBase64 { get; set; }

    public float FrameColorR { get; set; }
    public float FrameColorG { get; set; }
    public float FrameColorB { get; set; }
    public string FrameStyle { get; set; } = "Solid";

    /// The DJ name's own animated text effect and base color (see DjDeckWindow.DrawDjName) - previously only
    /// carried on DjProfileDetailDto, deliberately excluded here so the grid didn't turn into a wall of
    /// differently-colored, harder-to-scan text.
    public string NameEffect { get; set; } = "None";
    public float NameColorR { get; set; }
    public float NameColorG { get; set; }
    public float NameColorB { get; set; }

    public int LikeCount { get; set; }

    /// Whether whoever's making this request has already liked this profile - lets the card show a filled vs.
    public bool IsLikedByRequester { get; set; }

    public int FollowerCount { get; set; }

    /// Whether whoever's making this request already follows this profile - see
    /// ToggleDjProfileFollowMessage's own doc comment for what following actually does (a "went live"
    /// notification, not just a badge).
    public bool IsFollowedByRequester { get; set; }

    public bool IsLiveNow { get; set; }
    public string? LiveRoomCode { get; set; }

    /// How many listeners are currently tuned in, mirroring PublicShowEntryDto.
    public int ListenerCount { get; set; }

    /// True only for the profile owned by whoever's making this request (see
    /// RequestDjProfilesMessage.RequesterCharacterName) - lets the plugin show "Edit" instead of "Report"
    /// without the relay ever exposing anyone else's real CharacterName to it.
    public bool IsOwnProfile { get; set; }
}

/// The full profile, fetched on demand when a specific card is clicked - everything DjProfileSummaryDto has,
/// plus what only the detail view needs.
public sealed class DjProfileDetailDto
{
    public string Id { get; set; } = string.Empty;
    public string DjName { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public List<SavedVenueDto> SavedVenues { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public List<DjAvailabilityDayDto> Availability { get; set; } = new();
    public string? AvatarBase64 { get; set; }
    public string? BannerBase64 { get; set; }
    public float FrameColorR { get; set; }
    public float FrameColorG { get; set; }
    public float FrameColorB { get; set; }
    public string FrameStyle { get; set; } = "Solid";

    /// The DJ name's own animated text effect, shown big on the profile detail page (see
    /// DjDeckWindow.DrawDjName) - same fields DjProfileSummaryDto now also carries for the grid card's
    /// smaller name.
    public string NameEffect { get; set; } = "None";

    /// The name's own base color on the profile detail page, independent of FrameColorR/G/B.
    public float NameColorR { get; set; }
    public float NameColorG { get; set; }
    public float NameColorB { get; set; }

    /// Freeform, optional - copy/paste convenience for a listener to manually add the DJ in their own
    /// Aetherphone (no IPC integration exists for auto-filling that dialog).
    public string? AetherphoneNumber { get; set; }

    public int LikeCount { get; set; }
    public bool IsLikedByRequester { get; set; }
    public int FollowerCount { get; set; }
    public bool IsFollowedByRequester { get; set; }
    public bool IsLiveNow { get; set; }
    public string? LiveRoomCode { get; set; }
    public bool IsOwnProfile { get; set; }

    /// Other characters with full edit access to this same profile - always populated for the owner's own
    /// management UI regardless of ShowLinkedCharacters, since hiding your own list from yourself would make
    /// it unmanageable.
    public List<string> LinkedCharacterNames { get; set; } = new();

    /// Whether LinkedCharacterNames should also render on the public-facing part of the profile page (an
    /// "also seen as" line) for anyone who isn't the owner - linking itself is never hidden from the owner,
    /// this only controls whether OTHER people can see the alt list.
    public bool ShowLinkedCharacters { get; set; }
}

public sealed class RequestDjProfilesMessage
{
    public string? RequesterCharacterName { get; set; }
}

public sealed class DjProfilesSnapshotMessage
{
    public List<DjProfileSummaryDto> Profiles { get; set; } = new();

    /// Never set by the relay itself - populated only when AudioHost reuses this class to push a failed
    /// round-trip back to the plugin, same convention as PublicShowsSnapshotMessage.Error.
    public string? Error { get; set; }
}

public sealed class GetDjProfileDetailMessage
{
    public string Id { get; set; } = string.Empty;
    public string? RequesterCharacterName { get; set; }
}

public sealed class DjProfileDetailSnapshotMessage
{
    public DjProfileDetailDto? Profile { get; set; }
    public string? Error { get; set; }
}

/// Create-or-update - the relay decides which based on whether CharacterName already owns a profile (see
/// DjProfileStore.Save), so the client never needs to know or track a distinction between the two.
public sealed class SaveDjProfileMessage
{
    public string CharacterName { get; set; } = string.Empty;
    public string DjName { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public List<SavedVenueDto> SavedVenues { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public List<DjAvailabilityDayDto> Availability { get; set; } = new();
    public float FrameColorR { get; set; } = 0.25f;
    public float FrameColorG { get; set; } = 0.85f;
    public float FrameColorB { get; set; } = 0.95f;
    public string FrameStyle { get; set; } = "Solid";
    public string NameEffect { get; set; } = "None";
    public float NameColorR { get; set; } = 0.25f;
    public float NameColorG { get; set; } = 0.85f;
    public float NameColorB { get; set; } = 0.95f;
    public string? AetherphoneNumber { get; set; }
    public bool ShowLinkedCharacters { get; set; }
}

public sealed class DjProfileSaveResultMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ProfileId { get; set; }
}

public sealed class DeleteDjProfileMessage
{
    public string Id { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
}

public sealed class DjProfileDeleteResultMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// Chunked upload for a profile's avatar or banner - same reasoning/shape as ShowImageChunkMessage, but its
/// own standalone one-shot connection rather than piggybacking on an already-open host connection, since a DJ
/// profile has no persistent connection of its own to ride along on (it can be created/edited whether or not
/// that DJ is currently broadcasting).
public sealed class DjProfileImageChunkMessage
{
    public string ProfileId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;

    /// "avatar" or "banner" - a plain string tag rather than a shared enum type, matching this whole
    /// protocol's existing convention of plain-string message vocabularies.
    public string Slot { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public int ChunkIndex { get; set; }
    public int TotalChunks { get; set; }
    public string DataBase64 { get; set; } = string.Empty;
}

public sealed class DjProfileImageAckMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    public string ProfileId { get; set; } = string.Empty;
    public string Slot { get; set; } = string.Empty;
}

/// A listener reporting a DJ List profile for misuse - mirrors SubmitShowReportMessage/ ShowReportForwarder
/// exactly (own Discord embed via the same webhook), since a public profile can be misused the same way a
/// public show can.
public sealed class SubmitDjProfileReportMessage
{
    public string ProfileId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? ReporterCharacterName { get; set; }
}

public sealed class DjProfileReportAckMessage
{
    public bool Accepted { get; set; }
    public string? Error { get; set; }
}

/// Toggles the requester's own like on/off - see DjProfileStore.ToggleLike.
public sealed class ToggleDjProfileLikeMessage
{
    public string ProfileId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
}

public sealed class DjProfileLikeResultMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    /// Echoed back from the request - lets the plugin patch the right card's LikeCount/ IsLikedByRequester in
    /// place instead of needing a full RequestDjProfiles round trip just to reflect one toggle (see
    /// DjDeckWindow's like-result polling in DrawHeader).
    public string ProfileId { get; set; } = string.Empty;

    public int LikeCount { get; set; }
    public bool IsLiked { get; set; }
}

/// Following is deliberately separate from Liking - a Like is a one-off "I enjoyed this profile," while a
/// Follow's entire purpose is standing up a FollowedDjWentLive notification the moment this DJ starts a
/// *publicly listed* show (see RelayServer's room-creation code and PresenceClient) - a private/password-only
/// room never triggers it.
public sealed class ToggleDjProfileFollowMessage
{
    public string ProfileId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
}

public sealed class DjProfileFollowResultMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public int FollowerCount { get; set; }
    public bool IsFollowing { get; set; }
}

/// Called from the character that already owns the profile (the main, or an existing linked alt - anyone with
/// edit rights) to mint a short code for a NEW alt to redeem.
public sealed class GenerateProfileLinkCodeMessage
{
    public string ProfileId { get; set; } = string.Empty;
    public string RequesterCharacterName { get; set; } = string.Empty;
}

public sealed class ProfileLinkCodeResultMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public string? Code { get; set; }
    public int ExpiresInSeconds { get; set; }
}

/// Called from the character being linked - the code is the entire proof of ownership (it only ever appears
/// in the game client of whoever generated it), so there's no separate IP/identity check on this side.
public sealed class RedeemProfileLinkCodeMessage
{
    public string Code { get; set; } = string.Empty;
    public string RequesterCharacterName { get; set; } = string.Empty;
}

public sealed class ProfileLinkRedeemResultMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ProfileId { get; set; }
    public string? DjName { get; set; }
}

/// Removes one linked character - callable by the main or any other linked alt (anyone with edit rights),
/// including a character removing itself.
public sealed class UnlinkProfileCharacterMessage
{
    public string ProfileId { get; set; } = string.Empty;
    public string RequesterCharacterName { get; set; } = string.Empty;
    public string CharacterNameToRemove { get; set; } = string.Empty;
}

public sealed class ProfileUnlinkResultMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public List<string> LinkedCharacterNames { get; set; } = new();
}

/// Sent once, immediately after connecting a PresenceClient - registers this character name so the relay
/// knows where to deliver a FollowedDjWentLive push if/when someone they follow goes live.
public sealed class RegisterPresenceMessage
{
    public string CharacterName { get; set; } = string.Empty;
}

/// The only thing the relay ever pushes down a presence connection - fired once per followed DJ's
/// publicly-listed room going live (see RelayServer's room-creation code, right after ReserveRoom succeeds).
public sealed class FollowedDjWentLiveMessage
{
    public string ProfileId { get; set; } = string.Empty;
    public string DjName { get; set; } = string.Empty;
    public string RoomCode { get; set; } = string.Empty;
}

/// Lead host -> relay only, toggled live from the Broadcast tab - see RelayMessageType.SetWebListenLink's own
/// doc comment.
public sealed class SetWebListenLinkMessage
{
    public bool Enabled { get; set; }
}

/// Relay -> the requesting host only (WriteHostSafelyAsync, not a room broadcast) - the direct response to
/// SetWebListenLinkMessage.
public sealed class WebListenLinkUpdatedMessage
{
    public bool Enabled { get; set; }
    public string? Token { get; set; }
}
