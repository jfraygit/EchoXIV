using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace EchoMix.AudioHost.Spotify;

public sealed class SpotifyNowPlaying
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public double DurationMs { get; set; }
    public double ProgressMs { get; set; }
    public bool IsPlaying { get; set; }
}

/// Reads Spotify's own now-playing info straight from Windows' System Media Transport Controls - the same
/// OS-level "now playing" hook the Windows volume flyout, Task Manager, and most third-party "now playing"
/// widgets already use for Spotify/YouTube Music/etc.
public sealed class SpotifyNowPlayingReader
{
    private const string SpotifyAppUserModelId = "Spotify.exe";

    private static async Task<GlobalSystemMediaTransportControlsSession?> GetSpotifySessionAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return manager.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId == SpotifyAppUserModelId);
    }

    public async Task<bool> SkipNextAsync()
    {
        try
        {
            var session = await GetSpotifySessionAsync();
            if (session == null)
                return false;
            return await session.TrySkipNextAsync();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SkipPreviousAsync()
    {
        try
        {
            var session = await GetSpotifySessionAsync();
            if (session == null)
                return false;
            return await session.TrySkipPreviousAsync();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TogglePlayPauseAsync()
    {
        try
        {
            var session = await GetSpotifySessionAsync();
            if (session == null)
                return false;
            return await session.TryTogglePlayPauseAsync();
        }
        catch
        {
            return false;
        }
    }

    public async Task<SpotifyNowPlaying?> PollAsync()
    {
        try
        {
            var session = await GetSpotifySessionAsync();
            if (session == null)
                return null;

            var props = await session.TryGetMediaPropertiesAsync();
            if (string.IsNullOrEmpty(props.Title))
                return null;

            var playback = session.GetPlaybackInfo();
            var isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var timeline = session.GetTimelineProperties();
            var duration = timeline.EndTime - timeline.StartTime;

            var position = timeline.Position;
            if (isPlaying)
                position += DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (duration > TimeSpan.Zero && position > duration)
                position = duration;

            return new SpotifyNowPlaying
            {
                Title = props.Title,
                Artist = props.Artist,
                DurationMs = duration.TotalMilliseconds,
                ProgressMs = position.TotalMilliseconds,
                IsPlaying = isPlaying,
            };
        }
        catch
        {
            return null;
        }
    }
}
