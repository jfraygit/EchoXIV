using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;

namespace EchoRoleplay.Game;

/// Whether there is anything solid between the camera and a character.
public sealed class LineOfSight
{
    /// How far up the character's body the ray starts, in world units (metres).
    private const float ChestHeight = 1.1f;

    /// How much of the final stretch to the camera to ignore.
    private const float CameraMargin = 0.4f;

    /// How many frames an answer is kept before being asked again.
    private const int RecheckFrames = 6;

    /// Everything solid.
    private const int CollisionLayerMask = 1;

    private readonly record struct Answer(int Frame, bool Blocked);

    private readonly Dictionary<ulong, Answer> cache = [];
    private readonly List<ulong> stale = [];

    private int frame;

    /// Diagnostics: how many rays were actually cast on the last frame, as opposed to answered from the
    /// cache.
    public int LastRaysCast { get; private set; }

    /// Whether the collision module was reachable at all.
    public bool CollisionAvailable { get; private set; }

    /// Where the camera is this frame, or null if it could not be read.
    public Vector3? CameraPosition { get; private set; }

    /// Called once per frame, before any query.
    public unsafe void BeginFrame()
    {
        frame++;
        LastRaysCast = 0;

        CameraPosition = null;

        var manager = CameraManager.Instance();
        if (manager != null)
        {
            var camera = manager->GetActiveCamera();
            if (camera != null)
            {
                CameraPosition = new Vector3(
                    camera->LastPosition.X, camera->LastPosition.Y, camera->LastPosition.Z);
            }
        }

        if (frame % 600 != 0)
            return;

        stale.Clear();

        foreach (var (id, answer) in cache)
        {
            if (frame - answer.Frame > 600)
                stale.Add(id);
        }

        foreach (var id in stale)
            cache.Remove(id);
    }

    /// Whether this character can be seen from the camera.
    public unsafe bool IsVisible(ulong gameObjectId, Vector3 feet)
    {
        var due = (frame + (int)(gameObjectId % RecheckFrames)) % RecheckFrames == 0;

        if (!due && cache.TryGetValue(gameObjectId, out var cached))
            return !cached.Blocked;

        var blocked = Raycast(feet);
        cache[gameObjectId] = new Answer(frame, blocked);
        return !blocked;
    }

    private unsafe bool Raycast(Vector3 feet)
    {
        if (CameraPosition is not { } eye)
        {
            CollisionAvailable = false;
            return false;
        }

        var framework = Framework.Instance();
        if (framework == null || framework->BGCollisionModule == null)
        {
            CollisionAvailable = false;
            return false;
        }

        CollisionAvailable = true;
        LastRaysCast++;

        var chest = feet with { Y = feet.Y + ChestHeight };

        var delta = eye - chest;
        var distance = delta.Length();

        if (distance <= CameraMargin)
            return false;

        var direction = delta / distance;

        return BGCollisionModule.RaycastMaterialFilter(
            new FFXIVClientStructs.FFXIV.Common.Math.Vector3 { X = chest.X, Y = chest.Y, Z = chest.Z },
            new FFXIVClientStructs.FFXIV.Common.Math.Vector3 { X = direction.X, Y = direction.Y, Z = direction.Z },
            out _,
            distance - CameraMargin);
    }
}
