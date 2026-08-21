using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using DotRecast.Detour;
using EchoNav.Nav.Mesh;

namespace EchoNav.Nav;

public enum NavMeshState
{
    None,
    Loading,
    Building,
    Ready,
    Failed,
}

/// Keeps the current zone's navmesh available, and gets one however it can.
public sealed class NavMeshProvider : INavMeshSource, IDisposable
{
    private readonly IDataManager data;
    private readonly IClientState clientState;
    private readonly string shippedDirectory;
    private readonly string cacheDirectory;

    private readonly AgentSettings settings = new();
    private CancellationTokenSource? building;
    private uint loadedTerritory;

    public NavMeshProvider(IDataManager data, IClientState clientState, string pluginDirectory, string configDirectory)
    {
        this.data = data;
        this.clientState = clientState;
        shippedDirectory = Path.Combine(pluginDirectory, "meshes");
        cacheDirectory = Path.Combine(configDirectory, "navmesh");
    }

    public DtNavMesh? Mesh { get; private set; }
    public NavMeshState State { get; private set; } = NavMeshState.None;
    public string Detail { get; private set; } = string.Empty;

    public bool IsReady => State == NavMeshState.Ready && Mesh != null;

    /// Notices when the player changes zone and starts fetching the new mesh.
    public void Tick()
    {
        var territory = clientState.TerritoryType;
        if (territory == loadedTerritory || territory == 0)
            return;

        loadedTerritory = territory;

        if (!SupportedZones.Supports(territory))
        {
            building?.Cancel();
            Mesh = null;
            State = NavMeshState.None;
            Detail = $"EchoNav only works in {SupportedZones.SupportedName}";
            return;
        }

        Begin(territory);
    }

    private void Begin(uint territory)
    {
        building?.Cancel();
        building = new CancellationTokenSource();
        var token = building.Token;

        Mesh = null;
        State = NavMeshState.Loading;
        Detail = $"loading mesh for zone {territory}";

        var shipped = Path.Combine(shippedDirectory, $"{territory}.navmesh");
        var cached = Path.Combine(cacheDirectory, $"{territory}.navmesh");

        Task.Run(() =>
        {
            try
            {
                foreach (var (path, source) in new[] { (shipped, "shipped"), (cached, "cached") })
                {
                    if (token.IsCancellationRequested)
                        return;

                    var loaded = NavMeshCache.Load(path, settings, message => Plugin.Log.Warning($"[EchoNav] {message}"));
                    if (loaded == null)
                        continue;

                    Publish(loaded, $"{source} mesh", token);
                    return;
                }

                Build(territory, cached, token);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, $"[EchoNav] navmesh for zone {territory} failed");
                if (!token.IsCancellationRequested)
                {
                    State = NavMeshState.Failed;
                    Detail = ex.Message;
                }
            }
        }, token);
    }

    private void Build(uint territory, string cachePath, CancellationToken token)
    {
        State = NavMeshState.Building;
        Detail = "reading zone collision";

        var geometry = ZoneGeometry.Extract(data.GameData, territory);
        if (geometry == null || geometry.Mesh.TriangleCount == 0)
        {
            State = NavMeshState.Failed;
            Detail = "this zone has no collision data we can read";
            return;
        }

        if (token.IsCancellationRequested)
            return;

        if (geometry.Unreadable > 0)
            Plugin.Log.Warning(
                $"[EchoNav] zone {territory}: skipped {geometry.Unreadable} collision file(s) that could not be read");

        Detail = $"building navmesh from {geometry.Mesh.TriangleCount:N0} triangles";

        var built = NavMeshBuilder.Build(
            geometry.Mesh, settings, out var tiles, connections: null,
            log: message => Plugin.Log.Warning($"[EchoNav] {message}"));

        if (token.IsCancellationRequested)
            return;

        try
        {
            NavMeshCache.Save(cachePath, built, settings);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[EchoNav] could not cache the navmesh for zone {territory}");
        }

        Publish(built, $"built {tiles} tiles", token);
    }

    private void Publish(DtNavMesh mesh, string detail, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        Mesh = mesh;
        State = NavMeshState.Ready;
        Detail = detail;
    }

    public void Dispose()
    {
        building?.Cancel();
        building?.Dispose();
    }
}
