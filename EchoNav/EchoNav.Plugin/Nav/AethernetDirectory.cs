using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EchoNav.Nav;

/// One aethernet destination as the game defines it: the name that appears in the teleport menu, and where in
/// the world it puts you.
public sealed record AethernetDestination(
    uint RowId, string Name, Vector3 Position, bool IsMainAetheryte, uint AetheryteTerritory, uint LevelTerritory);

/// Bridges the gap between a shard standing in the world and its entry in the teleport menu.
public sealed class AethernetDirectory(IDataManager dataManager)
{
    /// How close a sheet entry must be to a discovered shard to be considered the same one.
    private const float MatchDistance = 25f;

    private List<AethernetDestination>? all;

    /// Every aetheryte in the game that has a world position, regardless of zone.
    public IReadOnlyList<AethernetDestination> All()
    {
        if (all != null)
            return all;

        all = [];

        try
        {
            var sheet = dataManager.GetExcelSheet<Aetheryte>();
            if (sheet == null)
                return all;

            foreach (var row in sheet)
            {
                if (row.Level.Count == 0)
                    continue;

                var levelRef = row.Level[0];
                if (!levelRef.IsValid)
                    continue;

                var level = levelRef.Value;
                var position = new Vector3(level.X, level.Y, level.Z);
                if (position == Vector3.Zero)
                    continue;

                var name = row.AethernetName.ValueNullable?.Name.ExtractText() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    name = row.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;

                all.Add(new AethernetDestination(
                    row.RowId, name, position, row.IsAetheryte,
                    row.Territory.RowId, level.Territory.RowId));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Could not read aethernet destinations");
        }

        return all;
    }

    /// Destinations belonging to a zone, accepting either territory column as evidence.
    public IReadOnlyList<AethernetDestination> For(uint territory)
    {
        var result = new List<AethernetDestination>();
        foreach (var destination in All())
        {
            if (destination.AetheryteTerritory == territory || destination.LevelTerritory == territory)
                result.Add(destination);
        }

        return result;
    }

    /// Closest sheet entries to a point, ignoring territory entirely.
    public IReadOnlyList<(AethernetDestination Destination, float Distance)> NearestAnywhere(Vector3 to, int count)
    {
        var scored = new List<(AethernetDestination, float)>();
        foreach (var destination in All())
            scored.Add((destination, Vector3.Distance(destination.Position, to)));

        scored.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return scored.Count > count ? scored.GetRange(0, count) : scored;
    }

    /// Names a discovered shard by finding the sheet entry standing in the same place.
    public AethernetDestination? Match(uint territory, Vector3 shardPosition)
    {
        AethernetDestination? best = null;
        var bestDistance = MatchDistance;

        foreach (var destination in For(territory))
        {
            var distance = Vector3.Distance(destination.Position, shardPosition);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = destination;
            }
        }

        return best;
    }
}
