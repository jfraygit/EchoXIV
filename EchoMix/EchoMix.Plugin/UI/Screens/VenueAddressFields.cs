using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Screens;

/// A venue's name plus its fully constrained FFXIV housing address, as one reusable block.
internal static class VenueAddressFields
{
    public static readonly string[] Types = { "House", "Apartment" };

    /// Draws the block.
    public static bool Draw(
        string idPrefix,
        float fieldWidth,
        ref string name,
        ref string dataCenter,
        ref string world,
        ref string housingArea,
        ref string ward,
        ref string plot,
        ref bool isApartment,
        ref bool subdivision,
        bool nameRequired = false)
    {
        var changed = false;

        if (Fields.TextRow($"{idPrefix}Name", "Venue Name", ref name, 40,
                hint: nameRequired ? "Required" : "Optional", controlWidth: fieldWidth))
        {
            changed = true;
        }

        var typeIndex = isApartment ? 1 : 0;

        if (Fields.SegmentedRow($"{idPrefix}Type", "Type", ref typeIndex, Types, controlWidth: fieldWidth))
        {
            isApartment = typeIndex == 1;
            if (!isApartment)
                subdivision = false;

            changed = true;
        }

        if (Picker($"{idPrefix}DataCenter", "Data Center", DjDeckWindow.HousingDataCenters, ref dataCenter, fieldWidth))
        {
            world = DjDeckWindow.HousingWorldOrEmpty(dataCenter, world);
            changed = true;
        }

        var worlds = DjDeckWindow.HousingWorldsIn(dataCenter);
        ImGui.BeginDisabled(worlds.Count == 0);
        if (Picker($"{idPrefix}World", "World", worlds, ref world, fieldWidth))
            changed = true;
        ImGui.EndDisabled();

        if (Picker($"{idPrefix}Area", "Housing Area", DjDeckWindow.HousingAreas, ref housingArea, fieldWidth))
            changed = true;

        if (Picker($"{idPrefix}Ward", "Ward", DjDeckWindow.HousingWards, ref ward, fieldWidth))
            changed = true;

        if (Picker(isApartment ? $"{idPrefix}Apartment" : $"{idPrefix}Plot",
                isApartment ? "Apartment" : "Plot",
                isApartment ? DjDeckWindow.HousingApartments : DjDeckWindow.HousingPlots,
                ref plot, fieldWidth))
        {
            changed = true;
        }

        if (!isApartment)
            return changed;

        if (Fields.Switch($"{idPrefix}Subdivision", "Subdivision", ref subdivision,
                "The second apartment building, where an area has one."))
        {
            changed = true;
        }

        return changed;
    }

    /// True when every part the relay needs for a resolvable address is filled in.
    public static bool IsAddressComplete(string dataCenter, string world, string housingArea, string ward, string plot) =>
        !string.IsNullOrWhiteSpace(dataCenter)
        && !string.IsNullOrWhiteSpace(world)
        && !string.IsNullOrWhiteSpace(housingArea)
        && !string.IsNullOrWhiteSpace(ward)
        && !string.IsNullOrWhiteSpace(plot);

    /// A dropdown over a fixed option table that reads and writes a string buffer rather than an index.
    private static bool Picker(string id, string label, IReadOnlyList<string> options, ref string value, float width)
    {
        var entries = new List<string>(options.Count + 1) { "Not Set" };
        var selected = 0;

        for (var i = 0; i < options.Count; i++)
        {
            entries.Add(options[i]);
            if (options[i] == value)
                selected = i + 1;
        }

        if (!Fields.Dropdown(id, label, ref selected, entries, controlWidth: width))
            return false;

        value = selected <= 0 ? string.Empty : options[selected - 1];
        return true;
    }

    /// Whatever is in the given parts, as one readable line.
    public static string FormatAddress(
        string dataCenter, string world, string housingArea, string ward, string plot,
        bool isApartment, bool subdivision)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(world))
        {
            parts.Add(string.IsNullOrWhiteSpace(dataCenter)
                ? world
                : $"{world} ({dataCenter})");
        }

        if (!string.IsNullOrWhiteSpace(housingArea))
            parts.Add(housingArea);

        if (!string.IsNullOrWhiteSpace(ward))
            parts.Add(isApartment && subdivision ? $"Ward {ward} (Subdivision)" : $"Ward {ward}");

        if (!string.IsNullOrWhiteSpace(plot))
            parts.Add(isApartment ? $"Apartment {plot}" : $"Plot {plot}");

        return parts.Count == 0 ? "No address set." : string.Join("  -  ", parts);
    }
}
