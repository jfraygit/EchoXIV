using System.Numerics;
using Lumina;
using Lumina.Data.Files;
using Lumina.Data.Files.Pcb;
using Lumina.Data.Parsing.Layer;

namespace EchoNav.Nav.Mesh;

/// Assembles a zone's collision from its layer files.
public sealed class ZoneExtractor(GameData data)
{
    /// Parsed collision files, kept because a few hundred distinct meshes back many thousand placements - the
    /// same rock appears all over a zone.
    private readonly Dictionary<string, PcbResourceFile?> pcbCache = [];
    private readonly Dictionary<string, SgbFile?> sgbCache = [];
    private readonly Dictionary<string, MdlFile?> modelCache = [];

    public int Placements { get; private set; }
    public int SharedGroups { get; private set; }
    public int MissingCollision { get; private set; }

    /// Placements whose collision is a box built from the model bounds.
    public int Boxes { get; private set; }

    /// Guards against a shared group that somehow contains itself.
    private const int MaxDepth = 8;

    /// Files the game ships that this parser can't read.
    public int Unreadable { get; private set; }

    private T? TryGet<T>(string path) where T : Lumina.Data.FileResource
    {
        try
        {
            return data.GetFile<T>(path);
        }
        catch (Exception)
        {
            Unreadable++;
            return null;
        }
    }

    public void AppendLayerFile(string path, TriangleMesh mesh)
    {
        var lgb = TryGet<LgbFile>(path);
        if (lgb == null)
            return;

        foreach (var layer in lgb.Layers)
            AppendInstances(layer.InstanceObjects, Matrix4x4.Identity, mesh, depth: 0);
    }

    private void AppendInstances(
        LayerCommon.InstanceObject[] instances, Matrix4x4 parent, TriangleMesh mesh, int depth)
    {
        foreach (var instance in instances)
        {
            var transform = ComposeTransform(instance.Transform) * parent;

            switch (instance.Object)
            {
                case LayerCommon.BGInstanceObject bg:
                    if (bg.CollisionType == ModelCollisionType.Box)
                        AppendBox(bg.AssetPath, transform, mesh);
                    else
                        AppendCollision(bg.CollisionAssetPath, transform, mesh);
                    break;

                case LayerCommon.SharedGroupInstanceObject group:
                    AppendSharedGroup(group.AssetPath, transform, mesh, depth);
                    break;
            }
        }
    }

    private void AppendCollision(string? path, Matrix4x4 transform, TriangleMesh mesh)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (!pcbCache.TryGetValue(path, out var pcb))
            pcbCache[path] = pcb = TryGet<PcbResourceFile>(path);

        if (pcb == null)
        {
            MissingCollision++;
            return;
        }

        Placements++;
        PcbReader.Append(pcb, transform, mesh);
    }

    /// Emits the model's bounding box as solid geometry.
    private void AppendBox(string? modelPath, Matrix4x4 transform, TriangleMesh mesh)
    {
        if (string.IsNullOrEmpty(modelPath))
            return;

        if (!modelCache.TryGetValue(modelPath, out var model))
            modelCache[modelPath] = model = TryGet<MdlFile>(modelPath);

        if (model == null)
            return;

        var bounds = model.BoundingBoxes;
        var min = new Vector3(bounds.Min[0], bounds.Min[1], bounds.Min[2]);
        var max = new Vector3(bounds.Max[0], bounds.Max[1], bounds.Max[2]);

        if (max.X - min.X < 0.01f && max.Y - min.Y < 0.01f && max.Z - min.Z < 0.01f)
            return;

        Boxes++;

        var corners = new Vector3[8];
        for (var i = 0; i < 8; i++)
        {
            corners[i] = Vector3.Transform(new Vector3(
                (i & 1) == 0 ? min.X : max.X,
                (i & 2) == 0 ? min.Y : max.Y,
                (i & 4) == 0 ? min.Z : max.Z), transform);
        }

        var baseIndex = mesh.VertexCount;
        foreach (var corner in corners)
            mesh.AddVertex(corner);

        int[][] faces =
        [
            [0, 2, 3], [0, 3, 1],            [4, 5, 7], [4, 7, 6],            [0, 1, 5], [0, 5, 4],            [2, 6, 7], [2, 7, 3],            [0, 4, 6], [0, 6, 2],            [1, 3, 7], [1, 7, 5],        ];

        foreach (var face in faces)
            mesh.AddTriangle(baseIndex + face[0], baseIndex + face[1], baseIndex + face[2]);
    }

    private void AppendSharedGroup(string? path, Matrix4x4 transform, TriangleMesh mesh, int depth)
    {
        if (string.IsNullOrEmpty(path) || depth >= MaxDepth)
            return;

        if (!sgbCache.TryGetValue(path, out var sgb))
            sgbCache[path] = sgb = TryGet<SgbFile>(path);

        if (sgb == null)
            return;

        SharedGroups++;

        foreach (var group in sgb.LayerGroups)
        {
            foreach (var layer in group.Layers)
                AppendInstances(layer.InstanceObjects, transform, mesh, depth + 1);
        }
    }

    /// Scale, then rotate, then translate.
    private static Matrix4x4 ComposeTransform(Lumina.Data.Parsing.Common.Transformation transform)
    {
        var scale = transform.Scale;
        var rotation = transform.Rotation;
        var translation = transform.Translation;

        return Matrix4x4.CreateScale(scale.X, scale.Y, scale.Z)
               * Matrix4x4.CreateRotationX(rotation.X)
               * Matrix4x4.CreateRotationY(rotation.Y)
               * Matrix4x4.CreateRotationZ(rotation.Z)
               * Matrix4x4.CreateTranslation(translation.X, translation.Y, translation.Z);
    }
}
