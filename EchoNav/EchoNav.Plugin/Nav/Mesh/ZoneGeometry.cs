using System.Numerics;
using Lumina;
using Lumina.Data.Files.Pcb;
using Lumina.Excel.Sheets;

namespace EchoNav.Nav.Mesh;

/// Pulls a whole zone's collision out of the game's own files.
public static class ZoneGeometry
{
    /// How far to look for terrain files.
    private const int TerrainScanLimit = 16384;

    public sealed record Result(TriangleMesh Mesh, int TerrainFiles, int Placements, int Boxes, int Unreadable);

    /// Reads one collision file, or null if it can't be read.
    private static PcbResourceFile? TryRead(GameData data, string path, ref int unreadable)
    {
        try
        {
            return data.GetFile<PcbResourceFile>(path);
        }
        catch (Exception)
        {
            unreadable++;
            return null;
        }
    }

    /// The zone's collision, or null if the territory names no background.
    public static Result? Extract(GameData data, uint territory)
    {
        var background = data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territory)?.Bg.ExtractText();
        if (string.IsNullOrEmpty(background))
            return null;

        var levelIndex = background.IndexOf("/level/", StringComparison.Ordinal);
        if (levelIndex < 0)
            return null;

        var zoneRoot = $"bg/{background[..levelIndex]}";
        var levelDirectory = $"bg/{background[..(background.LastIndexOf('/') + 1)]}";

        var mesh = new TriangleMesh();
        var terrain = 0;
        var unreadable = 0;

        for (var index = 0; index < TerrainScanLimit; index++)
        {
            var path = $"{zoneRoot}/collision/tr{index:D4}.pcb";
            if (!data.FileExists(path))
                continue;

            var pcb = TryRead(data, path, ref unreadable);
            if (pcb == null)
                continue;

            terrain++;
            PcbReader.Append(pcb, Matrix4x4.Identity, mesh);
        }

        var extractor = new ZoneExtractor(data);
        foreach (var name in new[] { "bg", "planmap", "planevent" })
            extractor.AppendLayerFile($"{levelDirectory}{name}.lgb", mesh);

        return new Result(mesh, terrain, extractor.Placements, extractor.Boxes, unreadable + extractor.Unreadable);
    }
}
