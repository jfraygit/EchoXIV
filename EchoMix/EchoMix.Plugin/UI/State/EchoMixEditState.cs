using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.State;

/// Form and text-input state for every editable field in the UI, shared by the 1.0 window and the 2.0 shell
/// so that typing a room code in one look and flipping to the other keeps what you typed, and so the shared
/// actions in EchoMixActions can read the values they need to send.
public sealed class EchoMixEditState
{
    public string BroadcastDjNameBuffer = string.Empty;
    public string BroadcastPasswordBuffer = string.Empty;
    public string BroadcastHostPasswordBuffer = string.Empty;
    public string BroadcastRoomCodeBuffer = string.Empty;

    /// Whether the join form is set to take a deck rather than listen.
    public bool JoinAsDjMode;

    /// Which playlist is open.
    public string? SelectedPlaylistName;

    /// The "create a playlist" field on the Library screen.
    public string NewPlaylistNameBuffer = string.Empty;

    public string ConnectRoomCodeBuffer = string.Empty;
    public string ConnectPasswordBuffer = string.Empty;
    public string ConnectHostPasswordBuffer = string.Empty;
    public string ConnectDjNameBuffer = string.Empty;

    /// Set when Connect is clicked on the onboarding "Join a Show" card with a blank room code - that click
    /// never reaches AudioHost at all, so there's no server round trip to produce a broadcast.ListenError the
    /// way every other rejection reason does.
    public bool JoinShowBlankRoomCodeHint;

    /// Password prompt for a card whose show.HasPassword is true.
    public string JoinPasswordBuffer = string.Empty;

    public string PublicShowNameBuffer = string.Empty;

    public string VenueNameBuffer = string.Empty;
    public string VenueDataCenterBuffer = string.Empty;
    public string VenueWorldBuffer = string.Empty;
    public string VenueHousingAreaBuffer = string.Empty;
    public string VenueWardBuffer = string.Empty;
    public string VenuePlotBuffer = string.Empty;
    public bool VenueIsApartmentBuffer;
    public bool VenueSubdivisionBuffer;

    public string ReportShowReasonBuffer = string.Empty;
    public string DjProfileReportReasonBuffer = string.Empty;

    /// Local-only grid filters, deliberately not persisted - there's no fixed genre vocabulary anywhere
    /// (genres are freeform tags a DJ types in), so these are built from whatever distinct values happen to
    /// be in the currently-loaded grid.
    public string DjListGenreFilter = string.Empty;
    public string LiveShowsGenreFilter = string.Empty;
    public string DjListNameSearchBuffer = string.Empty;

    public string? EditingDjProfileId;
    public string DjEditDjNameBuffer = string.Empty;
    public string DjEditBioBuffer = string.Empty;

    /// Set when the bio is loaded from an existing profile rather than typed, so it's laid out to the box's
    /// width on the first frame instead of only as it's edited - see WrappedInput.Fold, which InputText's own
    /// live-buffer wrap can't do for a value assigned in from outside.
    public bool DjEditBioWrapPending;

    /// Cached from the draw call since the box's own width isn't known outside a frame's draw pass, but the
    /// save-then-navigate action (which unfolds the bio before sending it) can run from places, like clicking
    /// Back, that never draw the form.
    public float DjEditBioWrapWidth;

    public string DjEditVenueNameBuffer = string.Empty;
    public string DjEditVenueDataCenterBuffer = string.Empty;
    public string DjEditVenueWorldBuffer = string.Empty;
    public string DjEditVenueHousingAreaBuffer = string.Empty;
    public string DjEditVenueWardBuffer = string.Empty;
    public string DjEditVenuePlotBuffer = string.Empty;
    public bool DjEditVenueIsApartment;
    public bool DjEditVenueSubdivision;

    public string DjEditGenreEntryBuffer = string.Empty;
    public bool DjEditShowLinkedCharacters;

    /// Which timezone the weekly availability slots are quoted in.
    public string DjEditAvailabilityZone = AvailabilitySlots.DefaultZone;

    public Vector3 DjEditFrameColor = new(0.25f, 0.85f, 0.95f);
    public Vector3 DjEditNameColor = new(0.25f, 0.85f, 0.95f);
    public string DjEditFrameStyle = "Solid";
    public string DjEditNameEffect = "None";

    /// The "Link a Character" popup's code-entry box - the other end of the edit form's own code generation,
    /// used from a character that has no listing yet.
    public string DjRedeemCodeBuffer = string.Empty;

    /// Song Requests access-control editor - the character-name entry box, cleared after each successful add.
    public string SongRequestNameBuffer = string.Empty;

    public string ReportBugDescriptionBuffer = string.Empty;
    public string ReportBugDiscordNameBuffer = string.Empty;

    public List<SavedVenueDto> DjEditSavedVenues { get; } = new();
    public List<string> DjEditGenres { get; } = new();
    public List<DjAvailabilityDayDto> DjEditAvailability { get; } = Enumerable.Range(0, 7).Select(_ => new DjAvailabilityDayDto()).ToList();
    public List<string> DjEditLinkedCharacterNames { get; } = new();

    /// Seeds the buffers that mirror a persisted Configuration value, so reopening the UI shows what was last
    /// entered.
    public void SeedFromConfiguration(Configuration configuration)
    {
        BroadcastDjNameBuffer = configuration.HostDisplayName ?? string.Empty;
        BroadcastRoomCodeBuffer = configuration.LastVanityRoomCode ?? string.Empty;
        ConnectRoomCodeBuffer = configuration.LastRoomCode ?? string.Empty;
        PublicShowNameBuffer = configuration.LastShowName ?? string.Empty;
        VenueNameBuffer = configuration.LastVenueName ?? string.Empty;
        VenueDataCenterBuffer = configuration.LastVenueDataCenter ?? string.Empty;
        VenueWorldBuffer = configuration.LastVenueWorld ?? string.Empty;
        VenueHousingAreaBuffer = configuration.LastVenueHousingArea ?? string.Empty;
        VenueWardBuffer = configuration.LastVenueWard ?? string.Empty;
        VenuePlotBuffer = configuration.LastVenuePlot ?? string.Empty;
        VenueIsApartmentBuffer = configuration.LastVenueIsApartment;
        VenueSubdivisionBuffer = configuration.LastVenueSubdivision;
    }
}
