using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoMix.AudioHost.Audio;
using EchoMix.Shared;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Newtonsoft.Json;

namespace EchoMix.AudioHost.Broadcast;

/// The listener side of a broadcast: joins a room on the relay, decodes the incoming Opus stream, and plays
/// it through its own WasapiOut - entirely separate from MixerEngine's own decks/output, since listening and
/// DJing are mutually exclusive per AudioHost instance (see IpcServer, which stops the local mixer's output
/// before creating one of these).
public sealed class BroadcastListenClient : IAsyncDisposable
{
    private TcpClient? tcpClient;
    private SslStream? ssl;
    private OpusDecoderStream? decoder;
    private QueueSampleProvider? playbackQueue;
    private VolumeSampleProvider? volumeStage;
    private WasapiOut? output;
    private CancellationTokenSource? cts;
    private Task? receiveLoopTask;
    private Task? reconnectLoopTask;

    private string? savedRoomCode;
    private string? savedPassword;
    private string? savedCharacterName;
    private volatile bool disposed;

    private const int MaxReconnectAttempts = 8;

    private readonly SemaphoreSlim songRequestSendLock = new(1, 1);
    private DateTime lastSongRequestSentUtc = DateTime.MinValue;
    private static readonly TimeSpan SongRequestCooldown = TimeSpan.FromSeconds(60);
    private const int SongRequestChunkSize = 256 * 1024;

    public bool IsConnected { get; private set; }
    public bool IsReconnecting { get; private set; }
    public string? RoomCode { get; private set; }
    public DateTime? RoomLiveSinceUtc { get; private set; }
    public string? HostDjName { get; private set; }
    public string? HostCharacterName { get; private set; }
    public bool IsProximityAudio { get; private set; }
    public float ProximityRange { get; private set; } = 30f;
    public string? NowPlayingTitleA { get; private set; }
    public double NowPlayingPositionSecondsA { get; private set; }
    public double NowPlayingDurationSecondsA { get; private set; }
    public string? NowPlayingTitleB { get; private set; }
    public double NowPlayingPositionSecondsB { get; private set; }
    public double NowPlayingDurationSecondsB { get; private set; }

    /// Deck B can never be used while the host's Spotify Mode is active - listeners use this to hide Deck B's
    /// now-useless half entirely rather than showing it sitting frozen.
    public bool IsHostSpotifyModeActive { get; private set; }
    public string? LastError { get; private set; }

    /// Set when this listener's own most recent song request gets declined (by the DJ, the DJ's
    /// whitelist/blacklist, or the relay's rate limit) - surfaced to the UI once, then left as-is until the
    /// next request attempt overwrites or clears it.
    public string? LastSongRequestFeedback { get; private set; }

    /// A short-lived maintenance notice from the relay (see RelayServer.
    public string? LatestServerNotice { get; private set; }
    public DateTime? ServerNoticeReceivedUtc { get; private set; }

    /// Fed live decoded audio - lets the listener's own visualizer reuse the exact same widget/analysis code
    /// the host's deck screens do.
    public FftAnalyzer Analyzer { get; } = new();

    /// Deck A/B's own spectrum bands, as pushed by the host (see BroadcastHostConnection.SetSpectrumAsync) -
    /// real per-deck data, unlike Analyzer above which can only ever see the one already-mixed stream this
    /// client actually receives.
    public float[] SpectrumA { get; private set; } = Array.Empty<float>();
    public float[] SpectrumB { get; private set; } = Array.Empty<float>();

    public float Volume
    {
        get => volumeStage?.Volume ?? 1f;
        set { if (volumeStage != null) volumeStage.Volume = Math.Clamp(value, 0f, 1.5f); }
    }

    public TimeSpan SongRequestCooldownRemaining
    {
        get
        {
            var elapsed = DateTime.UtcNow - lastSongRequestSentUtc;
            return elapsed >= SongRequestCooldown ? TimeSpan.Zero : SongRequestCooldown - elapsed;
        }
    }

    /// Reads the picked file, validates it, and streams it up to the room's lead host in chunks (see
    /// SongRequestChunkMessage - a whole song would blow past FrameIO's 1 MB/frame cap).
    public async Task<string?> RequestSongAsync(string filePath, string requesterName)
    {
        if (!IsConnected || ssl == null)
            return "Not connected to a show.";
        if (SongRequestCooldownRemaining > TimeSpan.Zero)
            return $"Wait {(int)Math.Ceiling(SongRequestCooldownRemaining.TotalSeconds)}s before requesting again.";
        if (!SongRequestLimits.IsSupported(filePath))
            return "Unsupported audio format.";
        if (SongRequestLimits.ExceedsMaxSize(filePath))
            return "File is too large (max 20 MB).";

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        lastSongRequestSentUtc = DateTime.UtcNow;
        LastSongRequestFeedback = null;

        var requestId = Guid.NewGuid();
        var fileName = Path.GetFileName(filePath);
        var totalChunks = Math.Max(1, (int)Math.Ceiling(bytes.Length / (double)SongRequestChunkSize));

        await songRequestSendLock.WaitAsync();
        try
        {
            for (var i = 0; i < totalChunks; i++)
            {
                var offset = i * SongRequestChunkSize;
                var length = Math.Min(SongRequestChunkSize, bytes.Length - offset);
                var chunkBytes = new byte[length];
                Array.Copy(bytes, offset, chunkBytes, 0, length);

                await SendControlAsync(RelayMessageType.SongRequestChunk, new SongRequestChunkMessage
                {
                    RequestId = requestId,
                    RequesterName = requesterName,
                    FileName = fileName,
                    ChunkIndex = i,
                    TotalChunks = totalChunks,
                    DataBase64 = Convert.ToBase64String(chunkBytes),
                });
            }
        }
        finally
        {
            songRequestSendLock.Release();
        }

        return null;
    }

    public async Task<bool> ConnectAsync(string roomCode, string password, string characterName)
    {
        savedRoomCode = roomCode;
        savedPassword = password;
        savedCharacterName = characterName;

        if (!await NetworkConnectAsync(roomCode, password, characterName))
            return false;

        const int PrefillMs = 250;
        var prefillSamples = OpusEncoderStream.SampleRate * OpusEncoderStream.Channels * PrefillMs / 1000;

        decoder = new OpusDecoderStream();
        playbackQueue = new QueueSampleProvider("listener playback", WaveFormat.CreateIeeeFloatWaveFormat(OpusEncoderStream.SampleRate, OpusEncoderStream.Channels), prefillSamples);
        volumeStage = new VolumeSampleProvider(playbackQueue) { Volume = 1f };

        output = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);
        output.Init(volumeStage);
        output.Play();

        cts = new CancellationTokenSource();
        receiveLoopTask = Task.Run(() => ReceiveLoopAsync(cts.Token));
        IsConnected = true;
        return true;
    }

    /// Just the relay handshake (TCP + TLS + JoinRoom/JoinAccepted) - split out from ConnectAsync so
    /// ReconnectLoopAsync can redo only this part after an unexpected drop, reusing the existing
    /// decoder/playback queue/WasapiOut/volume rather than tearing down and recreating the whole audio
    /// pipeline (which would both click audibly and reset the listener's chosen volume back to default on
    /// every retry).
    private async Task<bool> NetworkConnectAsync(string roomCode, string password, string characterName)
    {
        try
        {
            tcpClient = new TcpClient { NoDelay = true };
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await tcpClient.ConnectAsync(RelayConfig.DefaultHost, RelayConfig.DefaultPort, connectTimeout.Token);

            ssl = new SslStream(tcpClient.GetStream(), false, (_, cert, _, _) => RelayTls.ValidatePinnedCertificate(cert));
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = RelayConfig.DefaultHost,
                EnabledSslProtocols = SslProtocols.None,
            }, connectTimeout.Token);

            await SendControlAsync(RelayMessageType.JoinRoom, new JoinRoomMessage
            {
                RoomCode = roomCode,
                PasswordHash = PasswordHasher.Hash(password),
                CharacterName = characterName,
            });

            var frame = await FrameIO.ReadFrameAsync(ssl, connectTimeout.Token);
            if (frame == null || frame.Value.Type != FrameIO.ControlFrame)
                throw new InvalidOperationException("Relay closed the connection before responding.");

            var envelope = JsonConvert.DeserializeObject<RelayEnvelope>(Encoding.UTF8.GetString(frame.Value.Payload))
                ?? throw new InvalidOperationException("Relay sent an unreadable response.");

            if (envelope.Type == RelayMessageType.JoinRejected)
            {
                LastError = envelope.ReadPayload<JoinRejectedMessage>().Reason;
                CleanupConnection();
                return false;
            }

            var accepted = envelope.ReadPayload<JoinAcceptedMessage>();
            RoomCode = roomCode;
            RoomLiveSinceUtc = accepted.RoomCreatedAtUtc;
            HostDjName = accepted.DjName;
            HostCharacterName = accepted.CharacterName;
            IsProximityAudio = accepted.IsProximityAudio;
            ProximityRange = accepted.ProximityRange;
            LastError = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            LastError = "Couldn't reach the relay - check your connection and try again.";
            CleanupConnection();
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            CleanupConnection();
            return false;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var suppressReconnect = false;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var frame = await FrameIO.ReadFrameAsync(ssl!, token);
                if (frame == null)
                    break;

                if (frame.Value.Type == FrameIO.AudioFrame)
                {
                    var pcm = decoder!.Decode(frame.Value.Payload);
                    var samples = pcm.ToArray();
                    playbackQueue!.Enqueue(samples, 0, samples.Length);
                    Analyzer.Feed(samples, 0, samples.Length, OpusEncoderStream.Channels);
                    continue;
                }

                var envelope = JsonConvert.DeserializeObject<RelayEnvelope>(Encoding.UTF8.GetString(frame.Value.Payload));
                if (envelope == null)
                    continue;

                switch (envelope.Type)
                {
                    case RelayMessageType.TrackInfo:
                        var trackInfo = envelope.ReadPayload<TrackInfoMessage>();
                        NowPlayingTitleA = trackInfo.TitleA;
                        NowPlayingPositionSecondsA = trackInfo.PositionSecondsA;
                        NowPlayingDurationSecondsA = trackInfo.DurationSecondsA;
                        NowPlayingTitleB = trackInfo.TitleB;
                        NowPlayingPositionSecondsB = trackInfo.PositionSecondsB;
                        NowPlayingDurationSecondsB = trackInfo.DurationSecondsB;
                        IsHostSpotifyModeActive = trackInfo.IsSpotifyModeActive;
                        break;
                    case RelayMessageType.ProximityModeChanged:
                        var proximityChange = envelope.ReadPayload<ProximityModeChangedMessage>();
                        IsProximityAudio = proximityChange.IsProximityAudio;
                        ProximityRange = proximityChange.ProximityRange;
                        break;
                    case RelayMessageType.SpectrumUpdate:
                        var spectrumUpdate = envelope.ReadPayload<SpectrumUpdateMessage>();
                        SpectrumA = spectrumUpdate.BandsA;
                        SpectrumB = spectrumUpdate.BandsB;
                        break;
                    case RelayMessageType.SongRequestDeclined:
                        var declined = envelope.ReadPayload<SongRequestDeclinedMessage>();
                        LastSongRequestFeedback = declined.Reason ?? "Your song request was declined.";
                        break;
                    case RelayMessageType.Bye:
                        LastError = "The host ended the broadcast.";
                        return;
                    case RelayMessageType.ServerNotice:
                        LatestServerNotice = envelope.ReadPayload<ServerNoticeMessage>().Text;
                        ServerNoticeReceivedUtc = DateTime.UtcNow;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            suppressReconnect = true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsConnected = false;
            CleanupConnection();

            if (!suppressReconnect && !disposed)
                reconnectLoopTask = Task.Run(() => ReconnectLoopAsync(token));
        }
    }

    /// Retries just the network connection (see NetworkConnectAsync) after an unexpected drop OR a Bye (see
    /// RelayMessageType.Bye's own doc comment - the relay can't tell "DJ ended the show" apart from "DJ's
    /// connection died," so this can't either), so a brief relay/network blip doesn't kick the listener all
    /// the way back to the deck view - IsReconnecting is what tells DjDeckWindow to stay put in the Listener
    /// view meanwhile (see BroadcastStatusMessage.IsReconnecting).
    private async Task ReconnectLoopAsync(CancellationToken token)
    {
        IsReconnecting = true;
        try
        {
            for (var attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
            {
                var delaySeconds = Math.Min(3 + (attempt - 1) * 3, 20);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (disposed)
                    return;

                decoder = new OpusDecoderStream();
                if (await NetworkConnectAsync(savedRoomCode!, savedPassword!, savedCharacterName!))
                {
                    if (disposed)
                    {
                        CleanupConnection();
                        return;
                    }

                    IsConnected = true;
                    receiveLoopTask = Task.Run(() => ReceiveLoopAsync(token));
                    return;
                }

                if (LastError is "Incorrect password." or "Room is full.")
                    break;
            }
        }
        finally
        {
            IsReconnecting = false;
        }
    }

    private async Task SendControlAsync<T>(string type, T payload)
    {
        if (ssl == null)
            return;

        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(RelayEnvelope.For(type, payload)));
        await FrameIO.WriteFrameAsync(ssl, FrameIO.ControlFrame, bytes);
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        IsConnected = false;
        IsReconnecting = false;
        cts?.Cancel();

        if (receiveLoopTask != null)
            await receiveLoopTask.ContinueWith(_ => { });
        if (reconnectLoopTask != null)
            await reconnectLoopTask.ContinueWith(_ => { });

        output?.Stop();
        output?.Dispose();
        output = null;

        CleanupConnection();
        cts?.Dispose();
    }

    private void CleanupConnection()
    {
        ssl?.Dispose();
        tcpClient?.Dispose();
        ssl = null;
        tcpClient = null;
    }
}
