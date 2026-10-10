using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoMix.Shared;
using Newtonsoft.Json;

namespace EchoMix.Plugin.Ipc;

/// Named pipe client talking to the standalone EchoMix.AudioHost process.
public sealed class AudioHostClient : IDisposable
{
    private static readonly string PipeName = PipeNaming.ForProcess(Environment.ProcessId);

    private readonly object cacheGate = new();
    private readonly object writeGate = new();
    private NamedPipeClientStream? pipe;

    /// Raised the instant a relay reply carries a freshly minted DJ listing owner token, as (profileId,
    /// token).
    public Action<string, string, string>? OwnerTokenMinted { get; set; }
    private StreamWriter? writer;
    private CancellationTokenSource? readCts;
    private DateTime lastMessageUtc = DateTime.MinValue;

    private static readonly TimeSpan StaleTimeout = TimeSpan.FromSeconds(5);

    /// The same signal as StaleTimeout, read far sooner because acting on it costs nothing but a changed
    /// label - see Health.
    private static readonly TimeSpan UnresponsiveTimeout = TimeSpan.FromSeconds(1);

    private MixerStatusMessage latestStatus = new();
    private float[] latestSpectrumA = Array.Empty<float>();
    private float[] latestSpectrumB = Array.Empty<float>();
    private float[] latestListenSpectrum = Array.Empty<float>();
    private float[] latestListenSpectrumA = Array.Empty<float>();
    private float[] latestListenSpectrumB = Array.Empty<float>();
    private List<PlaylistDto> latestPlaylists = new();
    private List<SoundPadDto> latestSoundPads = new();
    private DeckQueuesSnapshotMessage latestDeckQueues = new();
    private List<AudioInputDeviceDto> latestAudioInputDevices = new();
    private List<AudioOutputDeviceDto> latestAudioOutputDevices = new();
    private bool latestOutputRoutingAvailable = true;
    private List<CapturableProcessDto> latestCapturableProcesses = new();
    private List<PendingSongRequestDto> latestPendingSongRequests = new();
    private BugReportResultMessage? latestBugReportResult;
    private PublicShowsSnapshotMessage? latestPublicShows;
    private ReportShowResultMessage? latestReportShowResult;
    private DjProfilesSnapshotMessage? latestDjProfiles;
    private DjStatsSnapshotMessage? latestDjStats;
    private DjProfileDetailSnapshotMessage? latestDjProfileDetail;
    private DjProfileSaveResultMessage? latestDjProfileSaveResult;
    private DjProfileDeleteResultMessage? latestDjProfileDeleteResult;
    private DjProfileReportAckMessage? latestDjProfileReportResult;
    private DjProfileImageAckMessage? latestDjProfileImageResult;
    private DjProfileLikeResultMessage? latestDjProfileLikeResult;
    private DjProfileFollowResultMessage? latestDjProfileFollowResult;
    private ProfileLinkCodeResultMessage? latestProfileLinkCodeResult;
    private ProfileLinkRedeemResultMessage? latestProfileLinkRedeemResult;
    private ProfileUnlinkResultMessage? latestProfileUnlinkResult;
    private FollowedDjWentLiveMessage? latestFollowedDjWentLive;

    public bool IsConnected => pipe?.IsConnected ?? false;

    public bool IsStale => IsConnected && DateTime.UtcNow - lastMessageUtc > StaleTimeout;

    /// One answer to "is the engine actually there", for anything that reports it to the user.
    public AudioHostHealth Health => !IsConnected
        ? AudioHostHealth.Disconnected
        : DateTime.UtcNow - lastMessageUtc > UnresponsiveTimeout
            ? AudioHostHealth.NotResponding
            : AudioHostHealth.Connected;

    /// The process AudioHostLauncher most recently launched and successfully connected to, if any - null if
    /// the current connection was inherited (already running before this launch attempt, e.g. across a plugin
    /// hot-reload) rather than started by it.
    public Process? HostProcess { get; set; }

    public MixerStatusMessage LatestStatus { get { lock (cacheGate) return latestStatus; } }

    public float[] LatestSpectrumA { get { lock (cacheGate) return latestSpectrumA; } }

    public float[] LatestSpectrumB { get { lock (cacheGate) return latestSpectrumB; } }

    public float[] LatestListenSpectrum { get { lock (cacheGate) return latestListenSpectrum; } }

    public float[] LatestListenSpectrumA { get { lock (cacheGate) return latestListenSpectrumA; } }

    public float[] LatestListenSpectrumB { get { lock (cacheGate) return latestListenSpectrumB; } }

    public IReadOnlyList<PlaylistDto> LatestPlaylists { get { lock (cacheGate) return latestPlaylists; } }

    public IReadOnlyList<SoundPadDto> LatestSoundPads { get { lock (cacheGate) return latestSoundPads; } }

    public DeckQueuesSnapshotMessage LatestDeckQueues { get { lock (cacheGate) return latestDeckQueues; } }

    public IReadOnlyList<AudioInputDeviceDto> LatestAudioInputDevices { get { lock (cacheGate) return latestAudioInputDevices; } }

    /// Windows output devices, for choosing where Spotify's own audio should play while Spotify Mode
    /// broadcasts it - see AudioHost's SpotifyOutputRouting.
    public IReadOnlyList<AudioOutputDeviceDto> LatestAudioOutputDevices { get { lock (cacheGate) return latestAudioOutputDevices; } }

    /// Whether this Windows build exposes per-app routing at all.
    public bool IsOutputRoutingAvailable { get { lock (cacheGate) return latestOutputRoutingAvailable; } }

    public IReadOnlyList<CapturableProcessDto> LatestCapturableProcesses { get { lock (cacheGate) return latestCapturableProcesses; } }

    public IReadOnlyList<PendingSongRequestDto> LatestPendingSongRequests { get { lock (cacheGate) return latestPendingSongRequests; } }

    /// Null until the first RequestPublicShows round trip actually lands - the Browse Shows view uses that to
    /// tell "haven't asked yet"/"still waiting" apart from "asked, and zero shows are currently public,"
    /// rather than defaulting to an empty list either way.
    public PublicShowsSnapshotMessage? LatestPublicShows { get { lock (cacheGate) return latestPublicShows; } }

    /// Consume-once rather than a plain cached getter - the Report Bug popup should show this exactly once
    /// (whenever it next arrives), not keep re-displaying a stale result every time the popup happens to be
    /// open when this is polled.
    public BugReportResultMessage? ConsumeBugReportResult()
    {
        lock (cacheGate)
        {
            var result = latestBugReportResult;
            latestBugReportResult = null;
            return result;
        }
    }

    /// Consume-once, same reasoning as ConsumeBugReportResult above - the Report Show popup should show this
    /// exactly once, not keep re-displaying a stale result.
    public ReportShowResultMessage? ConsumeReportShowResult()
    {
        lock (cacheGate)
        {
            var result = latestReportShowResult;
            latestReportShowResult = null;
            return result;
        }
    }

    /// Null until the first RequestDjProfiles round trip lands - same "haven't asked yet" vs "asked, zero
    /// profiles exist" distinction LatestPublicShows already draws.
    public DjProfilesSnapshotMessage? LatestDjProfiles
    {
        get
        {
#if DEBUG

            if (SampleProfilesOverride is { } sample)
                return sample;
#endif
            lock (cacheGate)
                return latestDjProfiles;
        }
    }

#if DEBUG
    /// Debug-only stand-in for the profile snapshot, set from Plugin.OnFrameworkUpdate while the Sample
    /// Browse Data toggle is on.
    public static DjProfilesSnapshotMessage? SampleProfilesOverride { get; set; }
#endif

    /// Null until the first RequestDjStats round trip lands.
    public DjStatsSnapshotMessage? LatestDjStats
    {
        get
        {
            lock (cacheGate)
                return latestDjStats;
        }
    }

    /// Null until a GetDjProfileDetail round trip lands for whichever profile was last clicked - the DJ
    /// List's profile-view screen reads this, not a per-profile cache, since only one profile is ever open at
    /// a time.
    public DjProfileDetailSnapshotMessage? LatestDjProfileDetail { get { lock (cacheGate) return latestDjProfileDetail; } }

    /// Consume-once, same reasoning as ConsumeBugReportResult - the edit form should show this exactly once.
    public DjProfileSaveResultMessage? ConsumeDjProfileSaveResult()
    {
        lock (cacheGate)
        {
            var result = latestDjProfileSaveResult;
            latestDjProfileSaveResult = null;
            return result;
        }
    }

    public DjProfileDeleteResultMessage? ConsumeDjProfileDeleteResult()
    {
        lock (cacheGate)
        {
            var result = latestDjProfileDeleteResult;
            latestDjProfileDeleteResult = null;
            return result;
        }
    }

    public DjProfileReportAckMessage? ConsumeDjProfileReportResult()
    {
        lock (cacheGate)
        {
            var result = latestDjProfileReportResult;
            latestDjProfileReportResult = null;
            return result;
        }
    }

    public DjProfileImageAckMessage? ConsumeDjProfileImageResult()
    {
        lock (cacheGate)
        {
            var result = latestDjProfileImageResult;
            latestDjProfileImageResult = null;
            return result;
        }
    }

    public DjProfileLikeResultMessage? ConsumeDjProfileLikeResult()
    {
        lock (cacheGate)
        {
            var result = latestDjProfileLikeResult;
            latestDjProfileLikeResult = null;
            return result;
        }
    }

    public DjProfileFollowResultMessage? ConsumeDjProfileFollowResult()
    {
        lock (cacheGate)
        {
            var result = latestDjProfileFollowResult;
            latestDjProfileFollowResult = null;
            return result;
        }
    }

    public ProfileLinkCodeResultMessage? ConsumeProfileLinkCodeResult()
    {
        lock (cacheGate)
        {
            var result = latestProfileLinkCodeResult;
            latestProfileLinkCodeResult = null;
            return result;
        }
    }

    public ProfileLinkRedeemResultMessage? ConsumeProfileLinkRedeemResult()
    {
        lock (cacheGate)
        {
            var result = latestProfileLinkRedeemResult;
            latestProfileLinkRedeemResult = null;
            return result;
        }
    }

    public ProfileUnlinkResultMessage? ConsumeProfileUnlinkResult()
    {
        lock (cacheGate)
        {
            var result = latestProfileUnlinkResult;
            latestProfileUnlinkResult = null;
            return result;
        }
    }

    /// Consumed by FollowNotificationToast, which triggers its whole fade-in/hold/fade-out sequence off of
    /// one of these arriving - not by DjDeckWindow, unlike almost everything else this client caches.
    public FollowedDjWentLiveMessage? ConsumeFollowedDjWentLive()
    {
        lock (cacheGate)
        {
            var result = latestFollowedDjWentLive;
            latestFollowedDjWentLive = null;
            return result;
        }
    }

    public async Task<bool> TryConnectAsync(TimeSpan timeout)
    {
        if (IsConnected)
            return true;

        CleanupConnection();

        var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            await client.ConnectAsync(timeoutCts.Token);
        }
        catch (Exception)
        {
            client.Dispose();
            return false;
        }

        pipe = client;
        writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };
        lastMessageUtc = DateTime.UtcNow;

        readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoop(client, readCts.Token));

        Send(MessageType.RequestPlaylists, new object());
        Send(MessageType.RequestSoundPads, new object());
        Send(MessageType.RequestAudioInputDevices, new object());
        return true;
    }

    private async Task ReadLoop(NamedPipeClientStream client, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(client, Encoding.UTF8, false, 4096, leaveOpen: true);
            while (client.IsConnected && !token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line == null)
                    break;

                lastMessageUtc = DateTime.UtcNow;
                Handle(line);
            }
        }
        catch (Exception)
        {
        }
    }

    private void Handle(string json)
    {
        IpcEnvelope? envelope;
        try
        {
            envelope = JsonConvert.DeserializeObject<IpcEnvelope>(json);
        }
        catch (JsonException)
        {
            return;
        }

        if (envelope == null)
            return;

        switch (envelope.Type)
        {
            case MessageType.MixerStatus:
                lock (cacheGate)
                    latestStatus = envelope.ReadPayload<MixerStatusMessage>();
                break;

            case MessageType.Spectrum:
                var spectrum = envelope.ReadPayload<SpectrumMessage>();
                lock (cacheGate)
                {
                    if (spectrum.Deck == DeckId.A)
                        latestSpectrumA = spectrum.Bands;
                    else
                        latestSpectrumB = spectrum.Bands;
                }
                break;

            case MessageType.ListenSpectrum:
                var listenSpectrum = envelope.ReadPayload<ListenSpectrumMessage>();
                lock (cacheGate)
                {
                    latestListenSpectrum = listenSpectrum.Bands;
                    latestListenSpectrumA = listenSpectrum.BandsA;
                    latestListenSpectrumB = listenSpectrum.BandsB;
                }
                break;

            case MessageType.PlaylistsSnapshot:
                lock (cacheGate)
                    latestPlaylists = envelope.ReadPayload<PlaylistsSnapshotMessage>().Playlists;
                break;

            case MessageType.SoundPadsSnapshot:
                lock (cacheGate)
                    latestSoundPads = envelope.ReadPayload<SoundPadsSnapshotMessage>().Pads;
                break;

            case MessageType.DeckQueuesSnapshot:
                lock (cacheGate)
                    latestDeckQueues = envelope.ReadPayload<DeckQueuesSnapshotMessage>();
                break;

            case MessageType.AudioInputDevicesSnapshot:
                lock (cacheGate)
                    latestAudioInputDevices = envelope.ReadPayload<AudioInputDevicesSnapshotMessage>().Devices;
                break;

            case MessageType.AudioOutputDevicesSnapshot:
                {
                    var snapshot = envelope.ReadPayload<AudioOutputDevicesSnapshotMessage>();
                    lock (cacheGate)
                    {
                        latestAudioOutputDevices = snapshot.Devices;
                        latestOutputRoutingAvailable = snapshot.RoutingAvailable;
                    }
                }
                break;

            case MessageType.CapturableProcessesSnapshot:
                lock (cacheGate)
                    latestCapturableProcesses = envelope.ReadPayload<CapturableProcessesSnapshotMessage>().Processes;
                break;

            case MessageType.PendingSongRequestsSnapshot:
                lock (cacheGate)
                    latestPendingSongRequests = envelope.ReadPayload<PendingSongRequestsSnapshotMessage>().Requests;
                break;

            case MessageType.BugReportResult:
                lock (cacheGate)
                    latestBugReportResult = envelope.ReadPayload<BugReportResultMessage>();
                break;

            case MessageType.PublicShowsSnapshot:
                lock (cacheGate)
                    latestPublicShows = envelope.ReadPayload<PublicShowsSnapshotMessage>();
                break;

            case MessageType.ReportShowResult:
                lock (cacheGate)
                    latestReportShowResult = envelope.ReadPayload<ReportShowResultMessage>();
                break;

            case MessageType.DjProfilesSnapshot:
                lock (cacheGate)
                    latestDjProfiles = envelope.ReadPayload<DjProfilesSnapshotMessage>();
                break;

            case MessageType.DjStatsSnapshot:
                lock (cacheGate)
                    latestDjStats = envelope.ReadPayload<DjStatsSnapshotMessage>();
                break;

            case MessageType.DjProfileDetailSnapshot:
                lock (cacheGate)
                    latestDjProfileDetail = envelope.ReadPayload<DjProfileDetailSnapshotMessage>();
                break;

            case MessageType.DjProfileSaveResult:
            {
                var saveResult = envelope.ReadPayload<DjProfileSaveResultMessage>();
                lock (cacheGate)
                    latestDjProfileSaveResult = saveResult;

                if (saveResult.Success && saveResult.ProfileId is { Length: > 0 } savedId
                    && saveResult.OwnerToken is { Length: > 0 } savedToken
                    && saveResult.OwnerTokenCharacterName is { Length: > 0 } savedFor)
                {
                    OwnerTokenMinted?.Invoke(savedId, savedFor, savedToken);
                }

                break;
            }

            case MessageType.DjProfileDeleteResult:
                lock (cacheGate)
                    latestDjProfileDeleteResult = envelope.ReadPayload<DjProfileDeleteResultMessage>();
                break;

            case MessageType.DjProfileReportResult:
                lock (cacheGate)
                    latestDjProfileReportResult = envelope.ReadPayload<DjProfileReportAckMessage>();
                break;

            case MessageType.DjProfileImageResult:
                lock (cacheGate)
                    latestDjProfileImageResult = envelope.ReadPayload<DjProfileImageAckMessage>();
                break;

            case MessageType.DjProfileLikeResult:
                lock (cacheGate)
                    latestDjProfileLikeResult = envelope.ReadPayload<DjProfileLikeResultMessage>();
                break;

            case MessageType.DjProfileFollowResult:
                lock (cacheGate)
                    latestDjProfileFollowResult = envelope.ReadPayload<DjProfileFollowResultMessage>();
                break;

            case MessageType.ProfileLinkCodeResult:
                lock (cacheGate)
                    latestProfileLinkCodeResult = envelope.ReadPayload<ProfileLinkCodeResultMessage>();
                break;

            case MessageType.ProfileLinkRedeemResult:
            {
                var redeemResult = envelope.ReadPayload<ProfileLinkRedeemResultMessage>();
                lock (cacheGate)
                    latestProfileLinkRedeemResult = redeemResult;

                if (redeemResult.Success && redeemResult.ProfileId is { Length: > 0 } linkedId
                    && redeemResult.OwnerToken is { Length: > 0 } linkedToken
                    && redeemResult.OwnerTokenCharacterName is { Length: > 0 } linkedFor)
                {
                    OwnerTokenMinted?.Invoke(linkedId, linkedFor, linkedToken);
                }

                break;
            }

            case MessageType.ProfileUnlinkResult:
                lock (cacheGate)
                    latestProfileUnlinkResult = envelope.ReadPayload<ProfileUnlinkResultMessage>();
                break;

            case MessageType.FollowedDjWentLive:
                lock (cacheGate)
                    latestFollowedDjWentLive = envelope.ReadPayload<FollowedDjWentLiveMessage>();
                break;
        }
    }

    /// Safe to call from multiple threads - the focus-mute check runs on its own timer thread (for tighter
    /// response than the game's frame rate) while other commands come from the main/UI thread, and both write
    /// to the same underlying StreamWriter.
    public void Send<T>(string type, T payload)
    {
        if (writer == null)
            return;

        try
        {
            lock (writeGate)
                writer.WriteLine(JsonConvert.SerializeObject(IpcEnvelope.For(type, payload)));
        }
        catch (Exception)
        {
        }
    }

    private void CleanupConnection()
    {
        lock (writeGate)
        {
            TryDispose(readCts, cancelFirst: true);
            TryDispose(writer);
            TryDispose(pipe);

            writer = null;
            pipe = null;
            readCts = null;
        }
    }

    private static void TryDispose(IDisposable? resource, bool cancelFirst = false)
    {
        try
        {
            if (cancelFirst && resource is CancellationTokenSource cts)
                cts.Cancel();
        }
        catch (Exception)
        {
        }

        try
        {
            resource?.Dispose();
        }
        catch (Exception)
        {
        }
    }

    /// Called when IsStale catches a connection that still looks up but has gone quiet - kills the tracked
    /// process if it's still running (rather than just dropping this end and leaving it stuck in the
    /// background, unreachable, forever holding the pipe a fresh AudioHost launch would need) and tears down
    /// this side.
    public void ForceDisconnect()
    {
        if (HostProcess is { } process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
            }

            HostProcess = null;
        }

        CleanupConnection();
    }

    public void Dispose() => CleanupConnection();
}
