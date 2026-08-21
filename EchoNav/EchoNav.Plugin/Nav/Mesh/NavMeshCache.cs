using System.IO.Compression;
using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Detour.Io;

namespace EchoNav.Nav.Mesh;

/// Reads and writes a built navmesh.
public static class NavMeshCache
{
    private const uint Magic = 0x564E4E45;    private const int Version = 1;

    public static void Save(string path, DtNavMesh navMesh, AgentSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var stream = File.Create(path);
        using var header = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        header.Write(Magic);
        header.Write(Version);
        header.Write(settings.CellSize);
        header.Write(settings.CellHeight);
        header.Write(settings.AgentHeight);
        header.Write(settings.AgentRadius);
        header.Write(settings.AgentMaxClimb);
        header.Write(settings.AgentMaxSlope);
        header.Write(settings.TileSize);
        header.Write(settings.FilterLedgeSpans);
        header.Flush();

        using var compressed = new GZipStream(stream, CompressionLevel.SmallestSize, leaveOpen: true);
        using var body = new BinaryWriter(compressed, System.Text.Encoding.UTF8, leaveOpen: true);

        new DtMeshSetWriter().Write(body, navMesh, RcByteOrder.LITTLE_ENDIAN, false);
    }

    /// Loads a cached mesh, or null when there isn't one or it was built for different settings.
    public static DtNavMesh? Load(string path, AgentSettings settings, Action<string>? onError = null)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                return null;

            var stored = new AgentSettings
            {
                CellSize = reader.ReadSingle(),
                CellHeight = reader.ReadSingle(),
                AgentHeight = reader.ReadSingle(),
                AgentRadius = reader.ReadSingle(),
                AgentMaxClimb = reader.ReadSingle(),
                AgentMaxSlope = reader.ReadSingle(),
                TileSize = reader.ReadInt32(),
                FilterLedgeSpans = reader.ReadBoolean(),
            };

            if (stored != settings)
                return null;

            using var decompressed = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
            using var body = new BinaryReader(decompressed, System.Text.Encoding.UTF8, leaveOpen: true);

            return new DtMeshSetReader().Read(body, settings.VertsPerPolygon);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"cached navmesh at {path} could not be read: {ex.Message}");
            return null;
        }
    }
}
