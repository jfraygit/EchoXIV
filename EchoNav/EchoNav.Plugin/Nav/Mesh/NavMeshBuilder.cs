using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;

namespace EchoNav.Nav.Mesh;

/// How a character is assumed to move, which is what turns raw geometry into walkable ground.
public sealed record AgentSettings
{
    /// Horizontal resolution of the voxel grid, in yalms.
    public float CellSize { get; init; } = 0.3f;

    public float CellHeight { get; init; } = 0.2f;

    /// Height of the character, used to reject anything it couldn't fit under.
    public float AgentHeight { get; init; } = 2f;

    /// How far the walkable surface is pushed back from walls.
    public float AgentRadius { get; init; } = 0.5f;

    /// Tallest step that can be walked up without jumping.
    public float AgentMaxClimb { get; init; } = 0.8f;

    /// Steepest ground that still counts as floor, in degrees.
    public float AgentMaxSlope { get; init; } = 55f;

    /// Tile edge in cells.
    public int TileSize { get; init; } = 128;

    /// Corners per navigation polygon.
    public int VertsPerPolygon { get; init; } = 6;

    /// Whether to discard walkable surface that sits at the lip of a drop.
    public bool FilterLedgeSpans { get; init; } = true;

    public override string ToString() =>
        $"cell {CellSize}, radius {AgentRadius}, climb {AgentMaxClimb}, " +
        $"slope {AgentMaxSlope:F0}deg, ledge filter {(FilterLedgeSpans ? "on" : "off")}";
}

/// Turns a triangle soup into a Detour navmesh.
public static class NavMeshBuilder
{
    public static DtNavMesh Build(
        TriangleMesh mesh, AgentSettings settings, out int tileCount,
        IReadOnlyList<OffMeshConnection>? connections = null, Action<string>? log = null)
    {
        connections ??= [];

        var geometry = ToGeometry(mesh);
        var config = Configure(settings);

        var results = new RcBuilder().BuildTiles(
            geometry, config, keepInterResults: false, buildAll: true,
            threads: Environment.ProcessorCount, taskFactory: Task.Factory, cancellation: default);

        var bounds = geometry.GetMeshBoundsMin();
        var boundsMax = geometry.GetMeshBoundsMax();

        var tileWidth = settings.TileSize * settings.CellSize;
        var tilesX = (int)MathF.Ceiling((boundsMax.X - bounds.X) / tileWidth);
        var tilesZ = (int)MathF.Ceiling((boundsMax.Z - bounds.Z) / tileWidth);

        var navMesh = new DtNavMesh();

        var navMeshParams = new DtNavMeshParams
        {
            orig = bounds,
            tileWidth = tileWidth,
            tileHeight = tileWidth,
            maxTiles = Math.Max(1, tilesX * tilesZ),
            maxPolys = 32768,
        };

        navMesh.Init(in navMeshParams, maxVertsPerPoly: settings.VertsPerPolygon);

        tileCount = 0;
        var rejected = 0;

        foreach (var result in results)
        {
            var data = ToTileData(result, settings, connections);
            if (data == null)
                continue;

            var status = navMesh.AddTile(data, flags: 0, lastRef: 0, result: out _);
            if (status.Succeeded())
                tileCount++;
            else
                rejected++;
        }

        if (rejected > 0)
            log?.Invoke($"{rejected} tiles refused by the navmesh (limits: " +
                              $"{navMeshParams.maxTiles} tiles, {navMeshParams.maxPolys} polygons)");

        return navMesh;
    }

    private static RcConfig Configure(AgentSettings settings) => new(
        useTiles: true,
        tileSizeX: settings.TileSize,
        tileSizeZ: settings.TileSize,
        borderSize: RcConfig.CalcBorder(settings.AgentRadius, settings.CellSize),
        partition: RcPartition.WATERSHED,
        cellSize: settings.CellSize,
        cellHeight: settings.CellHeight,
        agentMaxSlope: settings.AgentMaxSlope,
        agentHeight: settings.AgentHeight,
        agentRadius: settings.AgentRadius,
        agentMaxClimb: settings.AgentMaxClimb,
        minRegionArea: 8 * 8 * settings.CellSize * settings.CellSize,
        mergeRegionArea: 20 * 20 * settings.CellSize * settings.CellSize,
        edgeMaxLen: 12f,
        edgeMaxError: 1.3f,
        vertsPerPoly: settings.VertsPerPolygon,
        detailSampleDist: 6f,
        detailSampleMaxError: 1f,
        filterLowHangingObstacles: true,
        filterLedgeSpans: settings.FilterLedgeSpans,
        filterWalkableLowHeightSpans: true,
        walkableAreaMod: new RcAreaModification(RcRecast.RC_WALKABLE_AREA),
        buildMeshDetail: true);

    private static IRcInputGeomProvider ToGeometry(TriangleMesh mesh)
    {
        var vertices = new float[mesh.VertexCount * 3];
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            var vertex = mesh.Vertices[i];
            vertices[i * 3] = vertex.X;
            vertices[i * 3 + 1] = vertex.Y;
            vertices[i * 3 + 2] = vertex.Z;
        }

        var faces = new int[mesh.TriangleCount * 3];
        for (var i = 0; i < mesh.TriangleCount; i++)
        {
            var (a, b, c) = mesh.Triangles[i];
            faces[i * 3] = a;
            faces[i * 3 + 1] = b;
            faces[i * 3 + 2] = c;
        }

        return new RcSampleInputGeomProvider(vertices, faces);
    }

    /// Converts one built tile into the form Detour stores.
    private static DtMeshData? ToTileData(
        RcBuilderResult result, AgentSettings settings, IReadOnlyList<OffMeshConnection> connections)
    {
        var polyMesh = result.Mesh;
        if (polyMesh == null || polyMesh.npolys == 0)
            return null;

        for (var i = 0; i < polyMesh.npolys; i++)
            polyMesh.flags[i] = 1;

        var mine = connections
            .Where(c => c.From.X >= polyMesh.bmin.X && c.From.X <= polyMesh.bmax.X
                        && c.From.Z >= polyMesh.bmin.Z && c.From.Z <= polyMesh.bmax.Z)
            .ToList();

        var option = new DtNavMeshCreateParams
        {
            offMeshConVerts = mine.SelectMany(c => new[]
                { c.From.X, c.From.Y, c.From.Z, c.To.X, c.To.Y, c.To.Z }).ToArray(),
            offMeshConRad = mine.Select(c => c.Radius).ToArray(),
            offMeshConDir = mine.Select(c => c.Bidirectional ? 1 : 0).ToArray(),
            offMeshConAreas = mine.Select(_ => (int)RcRecast.RC_WALKABLE_AREA).ToArray(),
            offMeshConFlags = mine.Select(_ => 1).ToArray(),
            offMeshConUserID = mine.Select((_, i) => i).ToArray(),
            offMeshConCount = mine.Count,

            verts = polyMesh.verts,
            vertCount = polyMesh.nverts,
            polys = polyMesh.polys,
            polyAreas = polyMesh.areas,
            polyFlags = polyMesh.flags,
            polyCount = polyMesh.npolys,
            nvp = polyMesh.nvp,
            detailMeshes = result.MeshDetail?.meshes,
            detailVerts = result.MeshDetail?.verts,
            detailVertsCount = result.MeshDetail?.nverts ?? 0,
            detailTris = result.MeshDetail?.tris,
            detailTriCount = result.MeshDetail?.ntris ?? 0,
            walkableHeight = settings.AgentHeight,
            walkableRadius = settings.AgentRadius,
            walkableClimb = settings.AgentMaxClimb,
            bmin = polyMesh.bmin,
            bmax = polyMesh.bmax,
            cs = settings.CellSize,
            ch = settings.CellHeight,
            tileX = result.TileX,
            tileZ = result.TileZ,
            buildBvTree = true,
        };

        return DtNavMeshBuilder.CreateNavMeshData(option);
    }

    public static RcVec3f ToRc(Vector3 value) => new(value.X, value.Y, value.Z);
}
