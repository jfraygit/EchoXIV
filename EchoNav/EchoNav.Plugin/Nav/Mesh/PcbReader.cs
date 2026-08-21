using System.Numerics;
using Lumina.Data.Files.Pcb;

namespace EchoNav.Nav.Mesh;

/// Pulls a triangle soup out of the game's collision files.
public static class PcbReader
{
    /// Appends every triangle in file to mesh, transformed into world space by transform.
    public static void Append(PcbResourceFile file, Matrix4x4 transform, TriangleMesh mesh)
    {
        foreach (var node in file.Nodes.Children)
            Append(node, transform, mesh);
    }

    private static void Append(PcbResourceFile.ResourceNode node, Matrix4x4 transform, TriangleMesh mesh)
    {
        if (node.Vertices is { Length: > 0 } && node.Polygons is { Length: > 0 })
        {
            var baseIndex = mesh.VertexCount;

            foreach (var vertex in node.Vertices)
                mesh.AddVertex(Vector3.Transform(new Vector3(vertex.X, vertex.Y, vertex.Z), transform));

            foreach (var polygon in node.Polygons)
            {
                mesh.AddTriangle(
                    baseIndex + polygon.VertexIndex[0],
                    baseIndex + polygon.VertexIndex[1],
                    baseIndex + polygon.VertexIndex[2]);
            }
        }

        if (node.Children == null)
            return;

        foreach (var child in node.Children)
            Append(child, transform, mesh);
    }
}
