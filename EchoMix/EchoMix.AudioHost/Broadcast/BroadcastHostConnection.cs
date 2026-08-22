using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

/// One listener's song-request file, fully reassembled from its chunks and ready for IpcServer to write to
/// disk and surface to the DJ - see BroadcastHostConnection's TryDequeueCompletedSongRequest.
public sealed class CompletedSongRequest
{
    public Guid RequestId { get; init; }
    public Guid ListenerId { get; init; }
    public string RequesterName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public byte[] FileBytes { get; init; } = Array.Empty<byte>();
    public bool IsFromCoHost { get; init; }
}

/// Owns the outbound TLS connection to the relay while the DJ is "live" - either as the original creator of
/// the room or as a later co-host who joined it.
public sealed class BroadcastHostConnection : IAsyncDisposable
{
    private TcpClient? tcpClient;
    private SslStream? ssl;
    private QueueSampleProvider? feedQueue;
    private ISampleProvider? resampledSource;
    private OpusEncoderStream? encoder;
    private OpusDecoderStream? monitorDecoder;
    private QueueSampleProvider? monitorQueue;
    private VolumeSampleProvider? monitorVolumeStage;
    private CancellationTokenSource? lifetimeCts;
    private CancellationTokenSource? cts;
    private Task? sendLoopTask;
    private Task? receiveLoopTask;
    private Task? reconnectLoopTask;
    private volatile bool disposed;

    private string? savedRoomCode;
    private bool savedIsCoHostJoin;
    private string savedPassword = string.Empty;
    private string savedHostPassword = string.Empty;
    private string savedDjName = string.Empty;
    private string savedCharacterName = string.Empty;
    private bool savedIsProximityAudio;
    private float savedProximityRange;
    private bool savedIsPubliclyListed;
    private string? savedShowName;
    private string? savedVenueName;
    private string? savedVenueDataCenter;
    private string? savedVenueWorld;
    private string? savedVenueHousingArea;
    private string? savedVenueWard;
    private string? savedVenuePlot;
    private bool savedVenueIsApartment;
    private bool savedVenueSubdivision;

    private bool savedWebListenEnabled;
    private string? savedWebListenToken;

    /// Null until the DJ opts into a Web Listen Link (SetWebListenLinkAsync) - the full shareable URL,
    /// already combined with RelayConfig.WebListenBaseUrl so callers never need to know the relay only hands
    /// back a bare token.
    public string? WebListenUrl { get; private set; }

    private const int MaxReconnectAttempts = 8;
    public bool IsReconnecting { get; private set; }

    private static readonly TimeSpan NetworkWriteTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, SongRequestReceiveState> songRequestReceiveStates = new();
    private readonly ConcurrentQueue<CompletedSongRequest> completedSongRequests = new();

    private sealed class SongRequestReceiveState
    {
        public Guid ListenerId;
        public string RequesterName = string.Empty;
        public string FileName = string.Empty;
        public byte[]?[] Chunks = Array.Empty<byte[]?>();
        public int ReceivedCount;
        public bool IsFromCoHost;
    }

    private readonly SemaphoreSlim writeLock = new(1, 1);

    private readonly SemaphoreSlim showImageSendLock = new(1, 1);
    private const int ShowImageChunkSize = 64 * 1024;

    private readonly SemaphoreSlim songRequestSendLock = new(1, 1);
    private const int SongRequestChunkSize = 256 * 1024;

    public bool IsLive { get; private set; }
    public string? RoomCode { get; private set; }
    public string? LastError { get; private set; }

    /// When the room was first created, from the relay's own authoritative clock - not this connection's own
    /// StartAsync time, so a co-host joining an already-live room reports the room's real elapsed time
    /// instead of restarting the clock at 0:00.
    public DateTime? RoomLiveSinceUtc { get; private set; }

    /// A short-lived maintenance notice from the relay (see RelayServer.
    public string? LatestServerNotice { get; private set; }
    public DateTime? ServerNoticeReceivedUtc { get; private set; }

    /// Updated wholesale from the relay's own ListenerRosterChanged pushes (see ReceiveLoopAsync) -
    /// ListenerCount is just this list's length rather than a separately tracked field, so the two can never
    /// drift apart.
    public IReadOnlyList<ListenerRosterEntryDto> ListenerRoster { get; private set; } = Array.Empty<ListenerRosterEntryDto>();
    public int ListenerCount => ListenerRoster.Count;

    public Guid HostId { get; private set; }
    public bool IsLead { get; private set; } = true;
    public Guid LeadHostId { get; private set; }
    public IReadOnlyList<HostRosterEntryDto> Roster { get; private set; } = Array.Empty<HostRosterEntryDto>();

    /// The current lead's own now-playing/spectrum/queue info, mirrored to every other connected co-host by
    /// the relay (see RelayServer.HandleHostAsync's TrackInfo/SpectrumUpdate/ DeckQueuesUpdate fan-out) -
    /// null until at least one update has actually arrived.
    public TrackInfoMessage? LatestLeadTrackInfo { get; private set; }
    public SpectrumUpdateMessage? LatestLeadSpectrum { get; private set; }
    public DeckQueuesUpdateMessage? LatestLeadQueues { get; private set; }

    /// Decoded audio from whoever's currently lead, for local monitoring only - silent (nothing ever
    /// enqueued) while this connection itself is lead, since the relay never echoes a lead's own frames back
    /// to them.
    public ISampleProvider? MonitorSource => monitorVolumeStage;

    public float MonitorVolume
    {
        get => monitorVolumeStage?.Volume ?? 1f;
        set { if (monitorVolumeStage != null) monitorVolumeStage.Volume = Math.Clamp(value, 0f, 1.5f); }
    }

    public async Task<bool> StartAsync(string? roomCode, bool isCoHostJoin, string password, string hostPassword,
        string djName, string characterName, bool isProximityAudio, float proximityRange,
        bool isPubliclyListed = false, string? showName = null, string? venueName = null, string? venueDataCenter = null,
        string? venueWorld = null, string? venueHousingArea = null, string? venueWard = null, string? venuePlot = null,
        bool venueIsApartment = false, bool venueSubdivision = false)
    {
        savedRoomCode = roomCode;
        savedIsCoHostJoin = isCoHostJoin;
        savedPassword = password;
        savedHostPassword = hostPassword;
        savedDjName = djName;
        savedCharacterName = characterName;
        savedIsProximityAudio = isProximityAudio;
        savedProximityRange = proximityRange;
        savedIsPubliclyListed = isPubliclyListed;
        savedShowName = showName;
        savedVenueName = venueName;
        savedVenueDataCenter = venueDataCenter;
        savedVenueWorld = venueWorld;
        savedVenueHousingArea = venueHousingArea;
        savedVenueWard = venueWard;
        savedVenuePlot = venuePlot;
        savedVenueIsApartment = venueIsApartment;
        savedVenueSubdivision = venueSubdivision;

        if (!await NetworkConnectAsync())
            return false;

        var sourceFormat = WaveFormat.CreateIeeeFloatWaveFormat(DeckEngine.MasterSampleRate, 2);
        feedQueue = new QueueSampleProvider("broadcast feed", sourceFormat);
        resampledSource = sourceFormat.SampleRate == OpusEncoderStream.SampleRate
            ? feedQueue
            : new WdlResamplingSampleProvider(feedQueue, OpusEncoderStream.SampleRate);
        encoder = new OpusEncoderStream();

        const int MonitorPrefillMs = 250;
        var monitorPrefillSamples = OpusEncoderStream.SampleRate * OpusEncoderStream.Channels * MonitorPrefillMs / 1000;
        monitorDecoder = new OpusDecoderStream();
        monitorQueue = new QueueSampleProvider("co-host monitor", WaveFormat.CreateIeeeFloatWaveFormat(OpusEncoderStream.SampleRate, OpusEncoderStream.Channels), monitorPrefillSamples);
        ISampleProvider monitorResampled = OpusEncoderStream.SampleRate == DeckEngine.MasterSampleRate
            ? monitorQueue
            : new WdlResamplingSampleProvider(monitorQueue, DeckEngine.MasterSampleRate);
        monitorVolumeStage = new VolumeSampleProvider(monitorResampled) { Volume = 1f };

        lifetimeCts = new CancellationTokenSource();
        cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        sendLoopTask = Task.Run(() => SendLoopAsync(cts.Token));
        receiveLoopTask = Task.Run(() => ReceiveLoopAsync(cts.Token));
        IsLive = true;
        return true;
    }

    /// Just the relay handshake (TCP + TLS + RegisterHost/HostRegistered) - split out from StartAsync so
    /// ReconnectLoopAsync can redo only this part after an unexpected drop, reusing the existing feed
    /// queue/encoder/monitor pipeline rather than tearing the whole connection object down and making the
    /// caller start over from scratch.
    private async Task<bool> NetworkConnectAsync()
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

            await SendControlAsync(RelayMessageType.RegisterHost, new RegisterHostMessage
            {
                RoomCode = savedRoomCode,
                IsCoHostJoin = savedIsCoHostJoin,
                PasswordHash = PasswordHasher.Hash(savedPassword),
                HostPasswordHash = PasswordHasher.Hash(savedHostPassword),
                DjName = savedDjName,
                CharacterName = savedCharacterName,
                IsProximityAudio = savedIsProximityAudio,
                ProximityRange = savedProximityRange,
                SampleRate = OpusEncoderStream.SampleRate,
                IsPubliclyListed = savedIsPubliclyListed,
                ShowName = savedShowName,
                VenueName = savedVenueName,
                VenueDataCenter = savedVenueDataCenter,
                VenueWorld = savedVenueWorld,
                VenueHousingArea = savedVenueHousingArea,
                VenueWard = savedVenueWard,
                VenuePlot = savedVenuePlot,
                VenueIsApartment = savedVenueIsApartment,
                VenueSubdivision = savedVenueSubdivision,
                WebListenEnabled = savedWebListenEnabled,
                WebListenToken = savedWebListenToken,
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

            var registered = envelope.ReadPayload<HostRegisteredMessage>();
            if (!registered.Accepted)
            {
                LastError = registered.RejectReason ?? "Registration rejected.";
                CleanupConnection();
                return false;
            }

            RoomCode = registered.RoomCode;
            savedRoomCode = registered.RoomCode;
            HostId = registered.HostId;
            LeadHostId = registered.IsLead ? registered.HostId : Guid.Empty;
            IsLead = registered.IsLead;
            RoomLiveSinceUtc = registered.RoomCreatedAtUtc;
            ApplyWebListenState(registered.WebListenEnabled, registered.WebListenToken);
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

    /// Retries the relay handshake (see NetworkConnectAsync) a handful of times, with a growing delay, after
    /// SendLoopAsync gives up on an unexpected drop - IsReconnecting is what tells DjDeckWindow's Broadcast
    /// tab to show "reconnecting" instead of falling back to the Go Live form (see
    /// BroadcastStatusMessage.IsReconnecting).
    private async Task ReconnectLoopAsync()
    {
        IsReconnecting = true;
        try
        {
            if (receiveLoopTask != null)
                await receiveLoopTask.ContinueWith(_ => { });

            CleanupConnection();
            cts?.Dispose();
            cts = null;

            for (var attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
            {
                if (disposed)
                    return;

                var delaySeconds = Math.Min(3 + (attempt - 1) * 3, 20);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), lifetimeCts!.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (disposed)
                    return;

                encoder = new OpusEncoderStream();
                monitorDecoder = new OpusDecoderStream();

                if (await NetworkConnectAsync())
                {
                    if (disposed)
                    {
                        CleanupConnection();
                        return;
                    }

                    cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
                    sendLoopTask = Task.Run(() => SendLoopAsync(cts.Token));
                    receiveLoopTask = Task.Run(() => ReceiveLoopAsync(cts.Token));
                    IsLive = true;
                    Console.WriteLine($"[EchoMix.AudioHost] Reconnected to room {RoomCode} after an unexpected drop (attempt {attempt}/{MaxReconnectAttempts}).");
                    return;
                }

                Console.WriteLine($"[EchoMix.AudioHost] Reconnect attempt {attempt}/{MaxReconnectAttempts} for room {savedRoomCode} failed: {LastError}");
            }

            LastError = $"Lost connection to the relay and couldn't reconnect after {MaxReconnectAttempts} attempts ({LastError}) - go live again to restart your show.";
        }
        finally
        {
            IsReconnecting = false;
        }
    }

    /// Called from the real-time mixer thread - just enqueues, never blocks on the network.
    public void Feed(float[] buffer, int offset, int count) => feedQueue?.Enqueue(buffer, offset, count);

    public Task SetProximitySettingsAsync(bool isProximityAudio, float proximityRange) =>
        IsLive
            ? SendControlAsync(RelayMessageType.ProximityModeChanged, new ProximityModeChangedMessage { IsProximityAudio = isProximityAudio, ProximityRange = proximityRange })
            : Task.CompletedTask;

    /// Renames the show's own browse-grid listing without a stop/restart - only meaningful for a
    /// publicly-listed show, but harmless to send either way since the relay just updates Room.ShowName
    /// regardless (a private room's ShowName is never displayed anywhere).
    public Task SetShowNameAsync(string? showName) =>
        IsLive
            ? SendControlAsync(RelayMessageType.UpdateShowName, new UpdateShowNameMessage { ShowName = showName })
            : Task.CompletedTask;

    /// Toggles the public Web Listen Link - fire-and-forget like SetShowNameAsync above (no direct return
    /// value), since the relay's WebListenLinkUpdated response arrives asynchronously on the receive loop and
    /// is what actually updates WebListenUrl - see ReceiveLoopAsync's own case for it.
    public Task SetWebListenLinkAsync(bool enabled) =>
        IsLive
            ? SendControlAsync(RelayMessageType.SetWebListenLink, new SetWebListenLinkMessage { Enabled = enabled })
            : Task.CompletedTask;

    /// Shared by both the initial/reconnect HostRegistered response and the live WebListenLinkUpdated push -
    /// keeps savedWebListenEnabled/savedWebListenToken (what the NEXT reconnect re-sends) and the public
    /// WebListenUrl (what IpcServer actually reads) in sync from exactly one place.
    private void ApplyWebListenState(bool enabled, string? token)
    {
        savedWebListenEnabled = enabled;
        savedWebListenToken = token;
        WebListenUrl = enabled && !string.IsNullOrEmpty(token) ? $"{RelayConfig.WebListenBaseUrl}/{token}" : null;
    }

    public Task SetTrackInfoAsync(string? titleA, double positionSecondsA, double durationSecondsA, string? titleB, double positionSecondsB, double durationSecondsB, bool isSpotifyModeActive) =>
        IsLive
            ? SendControlAsync(RelayMessageType.TrackInfo, new TrackInfoMessage
            {
                TitleA = titleA, PositionSecondsA = positionSecondsA, DurationSecondsA = durationSecondsA,
                TitleB = titleB, PositionSecondsB = positionSecondsB, DurationSecondsB = durationSecondsB,
                IsSpotifyModeActive = isSpotifyModeActive,
            })
            : Task.CompletedTask;

    /// Pushes Deck A/B's own spectrum bands so listeners can render a genuinely per-deck dual visualizer
    /// instead of one computed locally from the already-mixed decoded audio.
    public Task SetSpectrumAsync(float[] bandsA, float[] bandsB) =>
        IsLive
            ? SendControlAsync(RelayMessageType.SpectrumUpdate, new SpectrumUpdateMessage { BandsA = bandsA, BandsB = bandsB })
            : Task.CompletedTask;

    /// Pushes Deck A/B's own upcoming queues so every other connected co-host can see what's coming up next
    /// without being lead themselves - same "not self-gated on IsLead, the relay is the actual authority"
    /// reasoning as SetTrackInfoAsync/SetSpectrumAsync above.
    public Task SetDeckQueuesAsync(DeckQueueDto queueA, DeckQueueDto queueB) =>
        IsLive
            ? SendControlAsync(RelayMessageType.DeckQueuesUpdate, new DeckQueuesUpdateMessage { QueueA = queueA, QueueB = queueB })
            : Task.CompletedTask;

    /// A co-host's counterpart to BroadcastListenClient.RequestSongAsync - same read/ validate/chunk shape,
    /// just sent with IsFromCoHost = true so the lead's Requests tab can tell it apart from a listener's
    /// request.
    public async Task<string?> RequestSongAsync(string filePath, string requesterName)
    {
        if (!IsLive || ssl == null)
            return "Not connected to a show.";
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
                    IsFromCoHost = true,
                });
            }
        }
        finally
        {
            songRequestSendLock.Release();
        }

        return null;
    }

    /// Uploads (or replaces) this show's own public-listing image - filePath is already an
    /// cropped/resized-to-thumbnail temp file the plugin wrote (see ShowImageProcessor), so this just streams
    /// whatever's on disk there in chunks, same shape as BroadcastListenClient.RequestSongAsync and for the
    /// same reason (FrameIO's 1MB/frame cap).
    public async Task SendShowImageAsync(string filePath)
    {
        if (!IsLive)
            return;

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath);
        }
        catch
        {
            return;
        }

        var requestId = Guid.NewGuid();
        var totalChunks = Math.Max(1, (int)Math.Ceiling(bytes.Length / (double)ShowImageChunkSize));

        await showImageSendLock.WaitAsync();
        try
        {
            for (var i = 0; i < totalChunks; i++)
            {
                var offset = i * ShowImageChunkSize;
                var length = Math.Min(ShowImageChunkSize, bytes.Length - offset);
                var chunkBytes = new byte[length];
                Array.Copy(bytes, offset, chunkBytes, 0, length);

                await SendControlAsync(RelayMessageType.ShowImageChunk, new ShowImageChunkMessage
                {
                    RequestId = requestId,
                    ChunkIndex = i,
                    TotalChunks = totalChunks,
                    DataBase64 = Convert.ToBase64String(chunkBytes),
                });
            }
        }
        finally
        {
            showImageSendLock.Release();
        }
    }

    /// Hands lead to another connected co-host - only meaningful while this connection itself is lead
    /// (skipped client-side otherwise, though the relay re-validates that too and is the actual authority).
    public Task PromoteAsync(Guid targetHostId) =>
        IsLive && IsLead
            ? SendControlAsync(RelayMessageType.PromoteHost, new PromoteHostMessage { TargetHostId = targetHostId })
            : Task.CompletedTask;

    /// Pulled by IpcServer's status loop once per tick - true (with a completed request) each time a
    /// listener's upload finishes reassembling.
    public bool TryDequeueCompletedSongRequest(out CompletedSongRequest request) =>
        completedSongRequests.TryDequeue(out request!);

    /// Tells the relay to notify one specific listener that their song request was declined (explicitly by
    /// the DJ, or silently by the whitelist/blacklist) - see RelayServer's one-to-one SongRequestDeclined
    /// routing.
    public Task DeclineSongRequestAsync(Guid requestId, Guid listenerId, string? reason, bool isFromCoHost = false) =>
        IsLive
            ? SendControlAsync(RelayMessageType.SongRequestDeclined, new SongRequestDeclinedMessage { RequestId = requestId, ListenerId = listenerId, Reason = reason, IsFromCoHost = isFromCoHost })
            : Task.CompletedTask;

    private async Task SendLoopAsync(CancellationToken token)
    {
        var frameBuffer = new float[OpusEncoderStream.FrameSamplesTotal];
        var frameInterval = TimeSpan.FromMilliseconds(20);
        var nextFrameDue = DateTime.UtcNow;

        try
        {
            while (!token.IsCancellationRequested)
            {
                resampledSource!.Read(frameBuffer, 0, frameBuffer.Length);

                if (IsLead)
                {
                    var packet = encoder!.Encode(frameBuffer).ToArray();

                    await writeLock.WaitAsync(token);
                    try
                    {
                        using var writeTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        writeTimeoutCts.CancelAfter(NetworkWriteTimeout);
                        try
                        {
                            await FrameIO.WriteFrameAsync(ssl!, FrameIO.AudioFrame, packet, writeTimeoutCts.Token);
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            throw new TimeoutException($"Sending audio to the relay timed out after {NetworkWriteTimeout.TotalSeconds:0}s - your connection looks stalled.");
                        }
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                }

                nextFrameDue += frameInterval;
                var delay = nextFrameDue - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, token);
                else
                    nextFrameDue = DateTime.UtcNow;            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsLive = false;

            if (!disposed)
            {
                cts?.Cancel();
                reconnectLoopTask = Task.Run(ReconnectLoopAsync);
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var frame = await FrameIO.ReadFrameAsync(ssl!, token);
                if (frame == null)
                    continue;

                if (frame.Value.Type == FrameIO.AudioFrame)
                {
                    var pcm = monitorDecoder!.Decode(frame.Value.Payload);
                    var samples = pcm.ToArray();
                    monitorQueue!.Enqueue(samples, 0, samples.Length);
                    continue;
                }

                var envelope = JsonConvert.DeserializeObject<RelayEnvelope>(Encoding.UTF8.GetString(frame.Value.Payload));
                if (envelope == null)
                    continue;

                switch (envelope.Type)
                {
                    case RelayMessageType.ListenerRosterChanged:
                        ListenerRoster = envelope.ReadPayload<ListenerRosterChangedMessage>().Listeners;
                        break;
                    case RelayMessageType.HostRosterChanged:
                        var roster = envelope.ReadPayload<HostRosterChangedMessage>();
                        Roster = roster.Hosts;
                        LeadHostId = roster.LeadHostId;
                        var wasLead = IsLead;
                        IsLead = roster.LeadHostId == HostId;

                        if (IsLead != wasLead)
                            Console.WriteLine(IsLead
                                ? $"[EchoMix.AudioHost] Now lead of room {RoomCode} ({roster.Hosts.Count} host(s))."
                                : $"[EchoMix.AudioHost] No longer lead of room {RoomCode} - now monitoring the current lead ({roster.Hosts.Count} host(s)).");
                        break;
                    case RelayMessageType.SongRequestChunk:
                        HandleSongRequestChunk(envelope.ReadPayload<SongRequestChunkMessage>());
                        break;
                    case RelayMessageType.ServerNotice:
                        LatestServerNotice = envelope.ReadPayload<ServerNoticeMessage>().Text;
                        ServerNoticeReceivedUtc = DateTime.UtcNow;
                        break;
                    case RelayMessageType.TrackInfo:
                        LatestLeadTrackInfo = envelope.ReadPayload<TrackInfoMessage>();
                        break;
                    case RelayMessageType.SpectrumUpdate:
                        LatestLeadSpectrum = envelope.ReadPayload<SpectrumUpdateMessage>();
                        break;
                    case RelayMessageType.DeckQueuesUpdate:
                        LatestLeadQueues = envelope.ReadPayload<DeckQueuesUpdateMessage>();
                        break;
                    case RelayMessageType.WebListenLinkUpdated:
                        var linkUpdate = envelope.ReadPayload<WebListenLinkUpdatedMessage>();
                        ApplyWebListenState(linkUpdate.Enabled, linkUpdate.Token);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// Stashes one chunk against its RequestId, allocating that request's reassembly buffer on the first
    /// chunk seen (chunks aren't guaranteed to arrive strictly in order, just reliably at all - TCP/TLS
    /// underneath - so this indexes by ChunkIndex rather than appending).
    private void HandleSongRequestChunk(SongRequestChunkMessage chunk)
    {
        var state = songRequestReceiveStates.GetOrAdd(chunk.RequestId, _ => new SongRequestReceiveState
        {
            ListenerId = chunk.ListenerId,
            RequesterName = chunk.RequesterName,
            FileName = chunk.FileName,
            Chunks = new byte[Math.Max(1, chunk.TotalChunks)][],
            IsFromCoHost = chunk.IsFromCoHost,
        });

        if (chunk.ChunkIndex < 0 || chunk.ChunkIndex >= state.Chunks.Length || state.Chunks[chunk.ChunkIndex] != null)
            return;

        state.Chunks[chunk.ChunkIndex] = Convert.FromBase64String(chunk.DataBase64);
        state.ReceivedCount++;

        if (state.ReceivedCount < state.Chunks.Length)
            return;

        songRequestReceiveStates.TryRemove(chunk.RequestId, out _);

        var fileBytes = new byte[state.Chunks.Sum(c => c!.Length)];
        var offset = 0;
        foreach (var part in state.Chunks)
        {
            Buffer.BlockCopy(part!, 0, fileBytes, offset, part!.Length);
            offset += part.Length;
        }

        completedSongRequests.Enqueue(new CompletedSongRequest
        {
            RequestId = chunk.RequestId,
            ListenerId = state.ListenerId,
            RequesterName = state.RequesterName,
            FileName = state.FileName,
            FileBytes = fileBytes,
            IsFromCoHost = state.IsFromCoHost,
        });
    }

    private async Task SendControlAsync<T>(string type, T payload)
    {
        if (ssl == null)
            return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(RelayEnvelope.For(type, payload)));

            await writeLock.WaitAsync();
            try
            {
                await FrameIO.WriteFrameAsync(ssl, FrameIO.ControlFrame, bytes);
            }
            finally
            {
                writeLock.Release();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        IsLive = false;
        IsReconnecting = false;
        lifetimeCts?.Cancel();
        cts?.Cancel();

        if (reconnectLoopTask != null)
            await reconnectLoopTask.ContinueWith(_ => { });
        if (sendLoopTask != null)
            await sendLoopTask.ContinueWith(_ => { });
        if (receiveLoopTask != null)
            await receiveLoopTask.ContinueWith(_ => { });

        if (ssl != null)
        {
            try
            {
                await SendControlAsync(RelayMessageType.Bye, new object());
            }
            catch
            {
            }
        }

        CleanupConnection();
        cts?.Dispose();
        lifetimeCts?.Dispose();
        writeLock.Dispose();
    }

    private void CleanupConnection()
    {
        ssl?.Dispose();
        tcpClient?.Dispose();
        ssl = null;
        tcpClient = null;
    }
}
