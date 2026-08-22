namespace EchoSim.Game;

/// Turns a character's home world into the region slug FFLogs indexes players by.
public static class WorldRegions
{
    /// The FFLogs slug for a WorldRegionGroup row id, or null when it is one that has not been seen.
    public static string? SlugFor(uint regionGroupId) => regionGroupId switch
    {
        1 => "JP",
        2 => "NA",
        3 => "EU",
        4 => "OC",

        7 => "NA",
        _ => null,
    };

    /// The slug for whichever world the local player calls home, or empty if unresolvable.
    public static string ForLocalPlayer()
    {
        var world = Plugin.ObjectTable.LocalPlayer?.HomeWorld.ValueNullable;
        var dataCentre = world?.DataCenter.ValueNullable;

        return dataCentre is null ? string.Empty : SlugFor(dataCentre.Value.Region.RowId) ?? string.Empty;
    }
}
