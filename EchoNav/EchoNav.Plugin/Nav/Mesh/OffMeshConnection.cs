using System.Numerics;

namespace EchoNav.Nav.Mesh;

/// A short traversal a character can make that the navmesh cannot represent.
public sealed record OffMeshConnection
{
    public required Vector3 From { get; init; }
    public required Vector3 To { get; init; }

    /// How close a route has to come before it may use this link.
    public float Radius { get; init; } = 2f;

    /// Whether it works both ways.
    public bool Bidirectional { get; init; } = true;
}
