using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EchoMix.Shared;

namespace EchoMix.AudioHost.Audio;

/// Routes Spotify's own audio output to a DJ-chosen device.
public static class SpotifyOutputRouting
{
    /// Matched against the audio session identifier, which embeds the rendering executable's path.
    private const string SpotifyExecutableMarker = "Spotify.exe";

    public static bool IsAvailable => AudioEndpointRouter.IsAvailable;

    public static List<AudioOutputDeviceDto> ListDevices() =>
        AudioEndpoints.ListRenderDevices()
            .Select(d => new AudioOutputDeviceDto
            {
                Id = d.Id,
                FriendlyName = d.FriendlyName,
                IsDefault = d.IsDefault,
            })
            .ToList();

    /// The process id that owns Spotify's render session, or null when Spotify isn't playing.
    private static int? FindSpotifyProcessId()
    {
        try
        {
            foreach (var session in AudioEndpoints.ListSessions())
            {
                if (session.Matches(SpotifyExecutableMarker))
                    return session.ProcessId;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to locate Spotify's audio session", ex);
        }

        return null;
    }

    /// Pins Spotify to `deviceId`, or releases it back to the system default when null/empty.
    public static bool SetDevice(string? deviceId)
    {
        if (!AudioEndpointRouter.IsAvailable)
            return false;

        routedReadAt = DateTime.MinValue;

        var processId = FindSpotifyProcessId();
        if (processId == null)
        {
            Log.Warn("No Spotify audio session is open, so there's nothing to route yet.");
            return false;
        }

        return AudioEndpointRouter.SetProcessEndpoint(processId.Value, deviceId);
    }

    /// A single immutable reading, swapped in by reference so a caller can never observe an id from one query
    /// paired with a name from another.
    private sealed record RoutedDevice(string? Id, string? Name);

    private static volatile RoutedDevice routed = new(null, null);
    private static DateTime routedReadAt = DateTime.MinValue;
    private static int refreshInFlight;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);

    /// Where Spotify is currently pinned, resolved to a friendly name for display.
    public static (string? Id, string? Name) GetCurrentDevice()
    {
        if (DateTime.UtcNow - routedReadAt >= CacheLifetime
            && Interlocked.CompareExchange(ref refreshInFlight, 1, 0) == 0)
        {
            _ = Task.Run(RefreshCurrentDevice);
        }

        var snapshot = routed;
        return (snapshot.Id, snapshot.Name);
    }

    private static void RefreshCurrentDevice()
    {
        try
        {
            routed = QueryCurrentDevice();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to refresh Spotify's routed output device", ex);
        }
        finally
        {
            routedReadAt = DateTime.UtcNow;
            Interlocked.Exchange(ref refreshInFlight, 0);
        }
    }

    private static RoutedDevice QueryCurrentDevice()
    {
        if (!AudioEndpointRouter.IsAvailable)
            return new RoutedDevice(null, null);

        var processId = FindSpotifyProcessId();
        if (processId == null)
            return new RoutedDevice(null, null);

        var id = AudioEndpointRouter.GetProcessEndpoint(processId.Value);
        if (string.IsNullOrWhiteSpace(id))
            return new RoutedDevice(null, null);

        string? name = null;
        try
        {
            name = AudioEndpoints.ListRenderDevices()
                .FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
                ?.FriendlyName;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to resolve the routed device's name", ex);
        }

        return new RoutedDevice(id, name);
    }
}
