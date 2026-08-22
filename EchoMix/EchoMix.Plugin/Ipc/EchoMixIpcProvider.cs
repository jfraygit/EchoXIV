using Dalamud.Plugin.Ipc;

namespace EchoMix.Plugin.Ipc;

/// Public cross-plugin IPC surface for other Dalamud plugins (e.g.
public sealed class EchoMixIpcProvider : System.IDisposable
{
    /// Bump only if a gate's tuple shape changes incompatibly - add a new gate name (e.g.
    private const int CurrentApiVersion = 1;

    private readonly Plugin plugin;

    private readonly ICallGateProvider<int> apiVersionGate;
    private readonly ICallGateProvider<(bool IsLive, bool IsLead, bool IsListening, string DjName, string RoomCode, int ListenerCount)> statusGate;
    private readonly ICallGateProvider<(bool HasTrack, string Title, double PositionSeconds, double DurationSeconds)> deckAGate;
    private readonly ICallGateProvider<(bool HasTrack, string Title, double PositionSeconds, double DurationSeconds)> deckBGate;
    private readonly ICallGateProvider<(bool IsActive, bool HasTrack, string Title, string Artist, double PositionSeconds, double DurationSeconds)> spotifyGate;
    private readonly ICallGateProvider<object> statusChangedGate;

    private object? lastPublishedSnapshot;

    public EchoMixIpcProvider(Plugin plugin)
    {
        this.plugin = plugin;

        apiVersionGate = Plugin.PluginInterface.GetIpcProvider<int>("EchoMix.ApiVersion");
        apiVersionGate.RegisterFunc(() => CurrentApiVersion);

        statusGate = Plugin.PluginInterface.GetIpcProvider<(bool, bool, bool, string, string, int)>("EchoMix.GetStatus");
        statusGate.RegisterFunc(GetStatus);

        deckAGate = Plugin.PluginInterface.GetIpcProvider<(bool, string, double, double)>("EchoMix.GetDeckA");
        deckAGate.RegisterFunc(() => GetDeck(isDeckA: true));

        deckBGate = Plugin.PluginInterface.GetIpcProvider<(bool, string, double, double)>("EchoMix.GetDeckB");
        deckBGate.RegisterFunc(() => GetDeck(isDeckA: false));

        spotifyGate = Plugin.PluginInterface.GetIpcProvider<(bool, bool, string, string, double, double)>("EchoMix.GetSpotifyNowPlaying");
        spotifyGate.RegisterFunc(GetSpotifyNowPlaying);

        statusChangedGate = Plugin.PluginInterface.GetIpcProvider<object>("EchoMix.StatusChanged");
    }

    public void Dispose()
    {
        apiVersionGate.UnregisterFunc();
        statusGate.UnregisterFunc();
        deckAGate.UnregisterFunc();
        deckBGate.UnregisterFunc();
        spotifyGate.UnregisterFunc();
    }

    private (bool, bool, bool, string, string, int) GetStatus()
    {
        var broadcast = plugin.AudioHostClient.LatestStatus.Broadcast;
        var djName = broadcast.IsLive && broadcast.IsLead
            ? plugin.Configuration.HostDisplayName ?? string.Empty
            : broadcast.HostDjName ?? string.Empty;

        return (broadcast.IsLive, broadcast.IsLead, broadcast.IsListening, djName, broadcast.RoomCode ?? string.Empty, broadcast.ListenerCount);
    }

    private (bool, string, double, double) GetDeck(bool isDeckA)
    {
        var status = plugin.AudioHostClient.LatestStatus;
        var broadcast = status.Broadcast;
        if (!broadcast.IsLive)
            return (false, string.Empty, 0, 0);

        if (!isDeckA && broadcast.IsHostSpotifyModeActive)
            return (false, string.Empty, 0, 0);

        if (broadcast.IsLead)
        {
            var deck = isDeckA ? status.DeckA : status.DeckB;
            return (deck.HasTrack, deck.TrackTitle ?? string.Empty, deck.PositionSeconds, deck.DurationSeconds);
        }

        var title = isDeckA ? broadcast.NowPlayingTitleA : broadcast.NowPlayingTitleB;
        var position = isDeckA ? broadcast.NowPlayingPositionSecondsA : broadcast.NowPlayingPositionSecondsB;
        var duration = isDeckA ? broadcast.NowPlayingDurationSecondsA : broadcast.NowPlayingDurationSecondsB;
        return (!string.IsNullOrEmpty(title), title ?? string.Empty, position, duration);
    }

    private (bool, bool, string, string, double, double) GetSpotifyNowPlaying()
    {
        var status = plugin.AudioHostClient.LatestStatus;
        var broadcast = status.Broadcast;

        if (broadcast.IsLive && broadcast.IsLead)
        {
            var spotify = status.SpotifyMode;
            return (spotify.IsActive, spotify.IsActive && !string.IsNullOrEmpty(spotify.NowPlayingTitle),
                spotify.NowPlayingTitle ?? string.Empty, spotify.NowPlayingArtist ?? string.Empty,
                spotify.NowPlayingPositionSeconds, spotify.NowPlayingDurationSeconds);
        }

        if (broadcast.IsLive && broadcast.IsHostSpotifyModeActive)
        {
            return (true, !string.IsNullOrEmpty(broadcast.NowPlayingTitleA), broadcast.NowPlayingTitleA ?? string.Empty,
                string.Empty, broadcast.NowPlayingPositionSecondsA, broadcast.NowPlayingDurationSecondsA);
        }

        return (false, false, string.Empty, string.Empty, 0, 0);
    }

    /// Call once per Framework.Update tick (see Plugin.OnFrameworkUpdate).
    public void CheckForChanges()
    {
        var snapshot = (GetStatus(), GetDeck(isDeckA: true), GetDeck(isDeckA: false), GetSpotifyNowPlaying());
        if (snapshot.Equals(lastPublishedSnapshot))
            return;

        lastPublishedSnapshot = snapshot;
        statusChangedGate.SendMessage();
    }
}
