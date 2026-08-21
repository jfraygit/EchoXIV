using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace EchoNav.Nav;

/// Somewhere to get the current zone's mesh from.
public interface INavMeshSource
{
    DtNavMesh? Mesh { get; }

    bool IsReady => Mesh != null;
}

/// Turns "I'm here, I want to be there" into a list of points to walk through, using the zone's navmesh.
public sealed class MeshRoutePlanner(INavMeshSource provider)
{
    /// How far from a given point the search looks for walkable ground.
    private static readonly RcVec3f SearchBox = new(4f, 6f, 4f);

    /// Search box for re-snapping a point deliberately moved a short distance.
    private static readonly RcVec3f SettleBox = new(0.6f, 2f, 0.6f);

    /// Ceiling on route complexity.
    private const int MaxPolygons = 2048;

    private const int MaxRoutePoints = 512;

    /// Where to send the one warning this can produce.
    public Action<string>? Log { get; set; }

    public bool IsReady => provider.IsReady;

    /// How long the last route took to work out, and how many points came back.
    public bool LastPlanReachedGoal { get; private set; } = true;

    public double LastPlanMs { get; private set; }
    public int LastPlanPoints { get; private set; }
    public double LastSearchMs { get; private set; }

    /// One query object per mesh, kept rather than made per route.
    private DtNavMeshQuery? query;
    private DtNavMesh? queryFor;
    private readonly DtQueryDefaultFilter filter = new();

    private DtNavMeshQuery? Query()
    {
        var navMesh = provider.Mesh;
        if (navMesh == null)
            return null;

        if (!ReferenceEquals(navMesh, queryFor))
        {
            query = new DtNavMeshQuery(navMesh);
            queryFor = navMesh;
        }

        return query;
    }

    /// Plans a walkable route.
    public IReadOnlyList<Vector3> Plan(Vector3 from, Vector3 destination)
    {
        Vector3[] direct = [destination];
        LastPlanReachedGoal = false;

        if (Query() is not { } query)
            return direct;

        var clock = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (!Locate(query, filter, from, out var startRef, out var startPoint) ||
                !Locate(query, filter, destination, out var endRef, out var endPoint))
                return direct;

            var polygons = new long[MaxPolygons];
            var status = query.FindPath(
                startRef, endRef, startPoint, endPoint, filter, polygons, out var polygonCount, MaxPolygons);

            LastSearchMs = clock.Elapsed.TotalMilliseconds;

            if (!status.Succeeded() || polygonCount == 0)
                return direct;

            var straight = new DtStraightPath[MaxRoutePoints];
            var straightStatus = query.FindStraightPath(
                startPoint, endPoint, polygons, polygonCount, straight, out var pointCount, MaxRoutePoints, 0);

            if (!straightStatus.Succeeded() || pointCount == 0)
                return direct;

            var route = new List<Vector3>(pointCount);
            for (var i = 0; i < pointCount; i++)
            {
                var point = straight[i].pos;
                route.Add(new Vector3(point.X, point.Y, point.Z));
            }

            LastPlanReachedGoal = polygons[polygonCount - 1] == endRef;

            if (!LastPlanReachedGoal)
                route.Add(destination);
            else if (route.Count > 0)
                route[^1] = destination;

            EaseOffWalls(query, route);
            AvoidSolidObjects(query, route);

            LastPlanMs = clock.Elapsed.TotalMilliseconds;
            LastPlanPoints = route.Count;

            return route.Count > 0 ? route : direct;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"route planning failed, falling back to a straight line: {ex.Message}");
            return direct;
        }
    }

    /// How much room to leave between a route corner and the wall it turns around.
    private const float WallClearance = 1.2f;

    /// Nudges route corners away from the walls they hug.
    private void EaseOffWalls(DtNavMeshQuery query, List<Vector3> route)
    {
        for (var i = 1; i < route.Count - 1; i++)
        {
            var point = route[i];
            if (!Locate(query, filter, point, out var reference, out var onMesh))
                continue;

            var status = query.FindDistanceToWall(
                reference, onMesh, WallClearance * 2f, filter,
                out var distance, out _, out var normal);

            if (!status.Succeeded() || distance >= WallClearance || distance <= 0f)
                continue;

            var push = WallClearance - distance;
            var moved = new RcVec3f(
                onMesh.X + (normal.X * push),
                onMesh.Y,
                onMesh.Z + (normal.Z * push));

            var settleStatus = query.FindNearestPoly(
                moved, SettleBox, filter, out var settledRef, out var settled, out _);

            if (!settleStatus.Succeeded() || settledRef == 0)
                continue;

            var improved = query.FindDistanceToWall(
                settledRef, settled, WallClearance * 2f, filter, out var newDistance, out _, out _);

            if (improved.Succeeded() && newDistance > distance)
                route[i] = new Vector3(settled.X, settled.Y, settled.Z);
        }
    }

    /// Things standing in the world that the navmesh doesn't know about.
    public sealed record Obstacle(Vector3 Position, float Radius, bool OnlyInTheOpen = false);

    /// Things standing in the world worth steering around.
    public IReadOnlyList<Obstacle> SolidObjects { get; set; } = [];

    /// Extra room to leave when going round one.
    private const float ObjectMargin = 1.5f;

    /// How much clear ground a detour needs on either side before it counts as open.
    private const float OpenGroundClearance = 2f;

    /// How far a bypass may be moved by snapping it to the mesh before it stops being the point that was
    /// asked for.
    private const float MaxBypassCorrection = 2f;

    private void AvoidSolidObjects(DtNavMeshQuery query, List<Vector3> route)
    {
        if (SolidObjects.Count == 0)
            return;

        foreach (var (centre, radius, onlyInTheOpen) in SolidObjects)
        {
            for (var i = 0; i < route.Count - 1; i++)
            {
                var from = route[i];
                var to = route[i + 1];

                var clearance = radius + ObjectMargin;
                var closest = ClosestPointOnSegment(from, to, centre);
                var offset = new Vector2(closest.X - centre.X, closest.Z - centre.Z);

                if (offset.Length() >= clearance)
                    continue;

                var along = Vector2.Normalize(new Vector2(to.X - from.X, to.Z - from.Z));
                var across = Perpendicular(along);

                if (Vector2.Dot(across, offset) < 0f)
                    across = -across;

                var bypass = new RcVec3f(
                    centre.X + (across.X * clearance),
                    closest.Y,
                    centre.Z + (across.Y * clearance));

                if (!query.FindNearestPoly(bypass, SearchBox, filter, out var reference, out var onMesh, out _)
                        .Succeeded() || reference == 0)
                    continue;

                if (onlyInTheOpen)
                {
                    var moved = Vector3.Distance(
                        new Vector3(bypass.X, bypass.Y, bypass.Z),
                        new Vector3(onMesh.X, onMesh.Y, onMesh.Z));

                    if (moved > MaxBypassCorrection)
                        continue;

                    var wall = query.FindDistanceToWall(
                        reference, onMesh, OpenGroundClearance * 2f, filter, out var toWall, out _, out _);

                    if (!wall.Succeeded() || toWall < OpenGroundClearance)
                        continue;
                }

                route.Insert(i + 1, new Vector3(onMesh.X, onMesh.Y, onMesh.Z));

                break;
            }
        }
    }

    private static Vector2 Perpendicular(Vector2 direction) =>
        direction.LengthSquared() < 0.01f ? Vector2.UnitX : Vector2.Normalize(new Vector2(-direction.Y, direction.X));

    private static Vector3 ClosestPointOnSegment(Vector3 from, Vector3 to, Vector3 point)
    {
        var segment = new Vector2(to.X - from.X, to.Z - from.Z);
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared < 0.01f)
            return from;

        var along = Vector2.Dot(new Vector2(point.X - from.X, point.Z - from.Z), segment) / lengthSquared;
        along = Math.Clamp(along, 0f, 1f);

        return Vector3.Lerp(from, to, along);
    }

    /// Whether a point stands in open ground rather than merely on the mesh.
    public bool IsOpenGround(Vector3 position, float clearance)
    {
        if (Query() is not { } query)
            return false;

        var status = query.FindNearestPoly(
            new RcVec3f(position.X, position.Y, position.Z), OpenGroundBox, filter,
            out var reference, out var onMesh, out _);

        if (!status.Succeeded() || reference == 0)
            return false;

        var drift = new Vector2(position.X - onMesh.X, position.Z - onMesh.Z).Length();
        if (drift > clearance)
            return false;

        var wall = query.FindDistanceToWall(reference, onMesh, clearance * 2f, filter, out var toWall, out _, out _);
        return wall.Succeeded() && toWall >= clearance;
    }

    /// Search box for IsOpenGround: narrow across, tall up and down.
    private static readonly RcVec3f OpenGroundBox = new(1f, 8f, 1f);

    /// Whether there's walkable ground near a point at all.
    public bool IsReachable(Vector3 position) =>
        Query() is { } query && Locate(query, filter, position, out _, out _);

    private static bool Locate(
        DtNavMeshQuery query, IDtQueryFilter filter, Vector3 point, out long reference, out RcVec3f onMesh)
    {
        var status = query.FindNearestPoly(
            new RcVec3f(point.X, point.Y, point.Z), SearchBox, filter, out reference, out onMesh, out _);

        return status.Succeeded() && reference != 0;
    }
}
