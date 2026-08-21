using System.Collections.Generic;
using System.Numerics;

namespace EchoNav.Nav.Mesh;

/// A growing triangle soup: the raw collision of a zone, before anything decides what part of it can be
/// walked on.
public sealed class TriangleMesh
{
    private readonly List<Vector3> vertices = [];
    private readonly List<(int A, int B, int C)> triangles = [];

    public int VertexCount => vertices.Count;
    public int TriangleCount => triangles.Count;

    public IReadOnlyList<Vector3> Vertices => vertices;
    public IReadOnlyList<(int A, int B, int C)> Triangles => triangles;

    public Vector3 Min { get; private set; } = new(float.MaxValue);
    public Vector3 Max { get; private set; } = new(float.MinValue);

    public void AddVertex(Vector3 vertex)
    {
        vertices.Add(vertex);
        Min = Vector3.Min(Min, vertex);
        Max = Vector3.Max(Max, vertex);
    }

    public void AddTriangle(int a, int b, int c) => triangles.Add((a, b, c));

    /// The bounding box as one readable line.
    public string Extent => VertexCount == 0
        ? "empty"
        : $"x {Min.X,9:F1}..{Max.X,-9:F1} y {Min.Y,8:F1}..{Max.Y,-8:F1} z {Min.Z,9:F1}..{Max.Z,-9:F1}";

    /// A copy holding only the triangles inside a box.
    public TriangleMesh Clip(Vector3 min, Vector3 max)
    {
        var clipped = new TriangleMesh();
        var remapped = new Dictionary<int, int>();

        foreach (var (a, b, c) in triangles)
        {
            if (!Inside(vertices[a], min, max) && !Inside(vertices[b], min, max) && !Inside(vertices[c], min, max))
                continue;

            clipped.AddTriangle(Remap(a), Remap(b), Remap(c));
            continue;

            int Remap(int index)
            {
                if (remapped.TryGetValue(index, out var existing))
                    return existing;

                var added = clipped.VertexCount;
                clipped.AddVertex(vertices[index]);
                remapped[index] = added;
                return added;
            }
        }

        return clipped;
    }

    private static bool Inside(Vector3 point, Vector3 min, Vector3 max) =>
        point.X >= min.X && point.X <= max.X &&
        point.Y >= min.Y && point.Y <= max.Y &&
        point.Z >= min.Z && point.Z <= max.Z;
}
