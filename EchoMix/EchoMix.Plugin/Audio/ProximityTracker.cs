using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;

namespace EchoMix.Plugin.Audio;

/// Computes a 0-1 volume falloff based on distance from the broadcast host's character, for Proximity Audio
/// mode - runs entirely locally (no network traffic involved at all), reading whatever's currently loaded
/// into this client's own object table.
public sealed class ProximityTracker
{
    private readonly IObjectTable objectTable;

    public ProximityTracker(IObjectTable objectTable)
    {
        this.objectTable = objectTable;
    }

    /// `maxDistance` (in yalms) is the host's own choice, not this listener's - it comes from the broadcast
    /// itself (BroadcastStatusMessage.ProximityRange), adjustable by the DJ via the header's proximity icon
    /// right-click menu.
    public float ComputeVolume(string? hostCharacterName, float maxDistance)
    {
        if (string.IsNullOrEmpty(hostCharacterName) || maxDistance <= 0f)
            return 0f;

        var localPlayer = objectTable.LocalPlayer;
        if (localPlayer == null)
            return 0f;

        var host = objectTable.PlayerObjects.FirstOrDefault(
            p => string.Equals(p.Name.TextValue, hostCharacterName, StringComparison.OrdinalIgnoreCase));
        if (host == null)
            return 0f;

        var distance = Vector3.Distance(localPlayer.Position, host.Position);
        return Math.Clamp(1f - (distance / maxDistance), 0f, 1f);
    }
}
