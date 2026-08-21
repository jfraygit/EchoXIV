using System.Collections.Generic;
using System.Numerics;

namespace EchoNav.Game;

/// Everything the UI is allowed to know about the current zone, captured on the framework thread and then
/// safe to read from anywhere.
public sealed record NavSnapshot
{
    public uint TerritoryType { get; init; }

    /// The pot cycle, shown in the title row.
    public PotForecast Pots { get; init; } = PotForecast.Unknown;

    public bool HasPlayer { get; init; }
    public Vector3 PlayerPosition { get; init; }

    /// Base camp, once the registry has seen it, and whether Occult Return can be used right now.
    public Vector3? BaseCamp { get; init; }

    public string BaseCampName { get; init; } = string.Empty;
    public bool ReturnAvailable { get; init; }

    public IReadOnlyList<NavTarget> Fates { get; init; } = [];
    public DynamicEventReadResult DynamicEvents { get; init; } = DynamicEventReadResult.NotFound;

    /// Whether the zone's navmesh is ready, and what it's doing if not.
    public Nav.NavMeshState MeshState { get; init; }
    public string MeshDetail { get; init; } = string.Empty;

    /// Both sources merged, which is what the real list will eventually show.
    public IEnumerable<NavTarget> AllTargets => [.. DynamicEvents.Targets, .. Fates];

    public static NavSnapshot Empty { get; } = new();
}
