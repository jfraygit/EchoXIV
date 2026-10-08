using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Where a show physically is, as a fully constrained address.
public sealed class VenueForm
{
    private readonly Plugin plugin;

    public VenueForm(Plugin plugin) => this.plugin = plugin;

    private Configuration Config => plugin.Configuration;

    private State.EchoMixEditState Edit => plugin.EditState;

    /// True when every part the relay needs for a resolvable address is filled in.
    public bool IsComplete => VenueAddressFields.IsAddressComplete(
        Edit.VenueDataCenterBuffer, Edit.VenueWorldBuffer, Edit.VenueHousingAreaBuffer,
        Edit.VenueWardBuffer, Edit.VenuePlotBuffer);

    public void Draw(string characterName)
    {
        plugin.DjDeckWindow.EnsureOwnSavedVenuesRequested(characterName);

        Surfaces.RowText(Config.IsProximityAudio
            ? "Proximity Audio needs an address so nearby listeners can find you."
            : "Optional on a Global show - shown to listeners if you set it.", Semantic.TextTertiary);

        Surfaces.Gap(Metrics.Lg);

        var saved = plugin.DjDeckWindow.OwnSavedVenues;
        var selectedId = Config.LastSelectedSavedVenueId;

        if (selectedId != null && saved != null && IndexOf(saved, selectedId) < 0)
        {
            selectedId = null;
            Config.LastSelectedSavedVenueId = null;
            Config.Save();
        }

        if (saved is { Count: > 0 })
        {
            var options = new List<string> { "Manual Entry" };
            foreach (var venue in saved)
                options.Add(venue.Name);

            var selected = selectedId == null ? 0 : IndexOf(saved, selectedId) + 1;

            if (Fields.Dropdown("##v2SavedVenue", "Venue", ref selected, options,
                    "One of your profile's saved venues, or type a one-off address."))
            {
                selectedId = selected <= 0 ? null : saved[selected - 1].Id;
                Config.LastSelectedSavedVenueId = selectedId;

                if (selectedId != null)
                    Adopt(saved[selected - 1]);

                Config.Save();
            }

            if (selectedId != null)
            {
                Fields.Divider();

                Surfaces.RowText(FormatAddress(), Semantic.TextSecondary);
                Surfaces.Gap(Metrics.Sm);
                Surfaces.RowText(
                    "Saved venues are edited on your DJ profile. Pick Manual Entry for a one-off address.",
                    Semantic.TextTertiary);

                return;
            }

            Fields.Divider();
        }

        DrawManualEntry();
    }

    /// The shared seven-field block, plus this screen's own persistence.
    private void DrawManualEntry()
    {
        var changed = VenueAddressFields.Draw("##v2Venue", 170f * Metrics.Scale,
            ref Edit.VenueNameBuffer,
            ref Edit.VenueDataCenterBuffer,
            ref Edit.VenueWorldBuffer,
            ref Edit.VenueHousingAreaBuffer,
            ref Edit.VenueWardBuffer,
            ref Edit.VenuePlotBuffer,
            ref Edit.VenueIsApartmentBuffer,
            ref Edit.VenueSubdivisionBuffer);

        if (!changed)
            return;

        Config.LastVenueName = Edit.VenueNameBuffer.Trim();
        Config.LastVenueDataCenter = Edit.VenueDataCenterBuffer;
        Config.LastVenueWorld = Edit.VenueWorldBuffer;
        Config.LastVenueHousingArea = Edit.VenueHousingAreaBuffer;
        Config.LastVenueWard = Edit.VenueWardBuffer;
        Config.LastVenuePlot = Edit.VenuePlotBuffer;
        Config.LastVenueIsApartment = Edit.VenueIsApartmentBuffer;
        Config.LastVenueSubdivision = Edit.VenueSubdivisionBuffer;
        Config.Save();
    }

    private void Adopt(SavedVenueDto venue)
    {
        Edit.VenueNameBuffer = venue.Name;
        Edit.VenueDataCenterBuffer = venue.DataCenter;
        Edit.VenueWorldBuffer = venue.World;
        Edit.VenueHousingAreaBuffer = venue.HousingArea;
        Edit.VenueWardBuffer = venue.Ward;
        Edit.VenuePlotBuffer = venue.Plot;
        Edit.VenueIsApartmentBuffer = venue.IsApartment;
        Edit.VenueSubdivisionBuffer = venue.Subdivision;

        Config.LastVenueName = Edit.VenueNameBuffer;
        Config.LastVenueDataCenter = Edit.VenueDataCenterBuffer;
        Config.LastVenueWorld = Edit.VenueWorldBuffer;
        Config.LastVenueHousingArea = Edit.VenueHousingAreaBuffer;
        Config.LastVenueWard = Edit.VenueWardBuffer;
        Config.LastVenuePlot = Edit.VenuePlotBuffer;
        Config.LastVenueIsApartment = Edit.VenueIsApartmentBuffer;
        Config.LastVenueSubdivision = Edit.VenueSubdivisionBuffer;
    }

    /// Whatever is currently in the buffers, as one readable line.
    public string FormatAddress() => VenueAddressFields.FormatAddress(
        Edit.VenueDataCenterBuffer, Edit.VenueWorldBuffer, Edit.VenueHousingAreaBuffer,
        Edit.VenueWardBuffer, Edit.VenuePlotBuffer,
        Edit.VenueIsApartmentBuffer, Edit.VenueSubdivisionBuffer);

    private static int IndexOf(IReadOnlyList<SavedVenueDto> venues, string id)
    {
        for (var i = 0; i < venues.Count; i++)
        {
            if (string.Equals(venues[i].Id, id, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
