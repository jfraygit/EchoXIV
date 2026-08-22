using System;
using Dalamud.Plugin.Services;
using EchoRoleplay.Shared;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace EchoRoleplay.Game;

/// Where the player is standing, and whether that is somewhere private.
public sealed class GameLocation
{
    private readonly IClientState clientState;
    private readonly IDataManager data;

    public GameLocation(IClientState clientState, IDataManager data)
    {
        this.clientState = clientState;
        this.data = data;
    }

    /// Reads the current location.
    public ReportLocation Read()
    {
        var location = new ReportLocation();

        try
        {
            var territoryId = clientState.TerritoryType;
            location.TerritoryId = territoryId;

            if (data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId) is { } territory)
            {
                location.Zone = territory.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;

                var region = territory.PlaceNameRegion.ValueNullable?.Name.ExtractText() ?? string.Empty;

                if (region.Length > 0 && !string.Equals(region, location.Zone, StringComparison.Ordinal))
                    location.Region = region;
            }

            ReadHousing(location);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoRoleplay] The report location could not be read");
        }

        return location;
    }

    private static unsafe void ReadHousing(ReportLocation location)
    {
        var housing = HousingManager.Instance();

        if (housing is null)
            return;

        location.Private = housing->IsInside();

        if (!location.Private)
            return;

        var ward = housing->GetCurrentWard();
        var plot = housing->GetCurrentPlot();
        var room = housing->GetCurrentRoom();

        if (ward > 0)
            location.Ward = ward;

        if (plot > 0)
            location.Plot = plot;

        if (room > 0)
            location.Room = room;
    }
}
