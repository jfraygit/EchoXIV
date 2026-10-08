using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace EchoMix.AudioHost.Audio;

/// One Windows audio output device.
public sealed class AudioEndpoint
{
    /// The raw MMDevice id, e.g. "{0.0.0.00000000}.{a1b2c3d4-...}".
    public string Id { get; init; } = string.Empty;

    public string FriendlyName { get; init; } = string.Empty;

    public bool IsDefault { get; init; }

    public override string ToString() => IsDefault ? $"{FriendlyName} (default)" : FriendlyName;
}

/// Enumerates audio render endpoints and sessions, via NAudio.
public static class AudioEndpoints
{
    public static List<AudioEndpoint> ListRenderDevices()
    {
        var results = new List<AudioEndpoint>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();

            string? defaultId = null;
            try
            {
                using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId = defaultDevice.ID;
            }
            catch
            {
            }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    results.Add(new AudioEndpoint
                    {
                        Id = device.ID,
                        FriendlyName = device.FriendlyName,
                        IsDefault = string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                    });
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to enumerate audio endpoints", ex);
        }

        return results;
    }

    /// One audio session on the default output device.
    public sealed class AudioSessionInfo
    {
        public int ProcessId { get; init; }

        /// The session identifier, which embeds the rendering executable's path - something like
        /// "...|\Device\HarddiskVolume3\Program Files\Spotify\Spotify.exe%b{...}".
        public string Identifier { get; init; } = string.Empty;

        public bool Matches(string appName) =>
            Identifier.IndexOf(appName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// The audio sessions currently open on the default output device.
    public static List<AudioSessionInfo> ListSessions()
    {
        var results = new List<AudioSessionInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            var sessions = device.AudioSessionManager.Sessions;
            if (sessions == null)
                return results;

            for (var i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];

                    var identifier = string.Empty;
                    try
                    {
                        identifier = session.GetSessionIdentifier ?? string.Empty;
                    }
                    catch
                    {
                    }

                    results.Add(new AudioSessionInfo
                    {
                        ProcessId = (int)session.GetProcessID,
                        Identifier = identifier,
                    });
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to enumerate audio sessions", ex);
        }

        return results;
    }
}
