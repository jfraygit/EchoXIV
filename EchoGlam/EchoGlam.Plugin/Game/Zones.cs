using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace EchoGlam.Game;

/// One place a rule can name: an open-world map, or a duty.
public readonly record struct Zone(uint TerritoryId, string Name, string Region, bool IsDuty)
{
    public string Label => Region.Length > 0 && !Region.Equals(Name, StringComparison.OrdinalIgnoreCase)
        ? $"{Name} — {Region}"
        : Name;
}

/// Every named place in the game - maps and duties alike - out of the game's own data.
public sealed class Zones
{
    private readonly List<Zone> all;

    /// Territory id to the id of the entry that represents its name in the picker.
    private readonly Dictionary<uint, uint> group = [];

    public Zones()
    {
        var primary = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var list = new List<Zone>();

        try
        {
            foreach (var row in Plugin.DataManager.GetExcelSheet<TerritoryType>())
            {
                if (!row.PlaceName.IsValid)
                    continue;

                var name = row.PlaceName.Value.Name.ExtractText();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (primary.TryGetValue(name, out var head))
                {
                    group[row.RowId] = head;
                    continue;
                }

                primary[name] = row.RowId;
                group[row.RowId] = row.RowId;

                var region = row.PlaceNameRegion.IsValid
                    ? row.PlaceNameRegion.Value.Name.ExtractText()
                    : string.Empty;

                list.Add(new Zone(row.RowId, name, region, row.ContentFinderCondition.RowId != 0));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not read the territory list. Map rules will be unavailable.");
        }

        all = [.. list.OrderBy(z => z.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public IReadOnlyList<Zone> All => all;

    public bool Ready => all.Count > 0;

    /// Whether the player being in current satisfies a rule set on ruleTerritory.
    public bool Matches(uint ruleTerritory, uint current)
    {
        if (ruleTerritory == 0 || current == 0)
            return false;

        if (ruleTerritory == current)
            return true;

        return group.TryGetValue(ruleTerritory, out var a)
               && group.TryGetValue(current, out var b)
               && a == b;
    }

    /// The name for a territory id, or a readable stand-in.
    public string NameFor(uint territoryId)
    {
        if (territoryId == 0)
            return "Nowhere yet";

        var head = group.TryGetValue(territoryId, out var g) ? g : territoryId;

        foreach (var zone in all)
        {
            if (zone.TerritoryId == head)
                return zone.Name;
        }

        return $"Map {territoryId}";
    }

    /// Whether this map is instanced content, for the rule summary line.
    public bool IsDuty(uint territoryId)
    {
        var head = group.TryGetValue(territoryId, out var g) ? g : territoryId;
        return all.Any(z => z.TerritoryId == head && z.IsDuty);
    }

    public IEnumerable<Zone> Search(string term, bool? dutiesOnly)
    {
        var source = dutiesOnly is { } duty ? all.Where(z => z.IsDuty == duty) : all;

        if (string.IsNullOrWhiteSpace(term))
            return source;

        return source.Where(z =>
            z.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || z.Region.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
