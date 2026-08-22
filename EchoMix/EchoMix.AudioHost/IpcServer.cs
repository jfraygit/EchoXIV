using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoMix.AudioHost.Audio;
using EchoMix.AudioHost.Audio.ExternalInput;
using EchoMix.AudioHost.Broadcast;
using EchoMix.AudioHost.Playlists;
using EchoMix.AudioHost.Spotify;
using EchoMix.Shared;
using Newtonsoft.Json;

namespace EchoMix.AudioHost;

/// Accepts one client (the Dalamud plugin) at a time over a local named pipe, dispatches its commands against
/// the real engine, and proactively pushes status/spectrum back rather than waiting to be polled.
public sealed class IpcServer
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OwnerProcessCheckInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StatusInterval = TimeSpan.FromMilliseconds(33);

    private readonly string pipeName;
    private readonly MixerEngine mixer;
    private readonly PlaylistManager playlists;
    private readonly SoundPadManager soundPads;
    private readonly DeckQueueManager deckQueues;
    private readonly string libraryRoot;

    private readonly int? ownerProcessId;
    private readonly DateTime?[] nextLoopTriggerUtc = new DateTime?[SoundPadManager.PadCount];
    private volatile bool clientConnected;
    private volatile bool playlistsDirty = true;
    private volatile bool soundPadsDirty = true;
    private volatile bool queuesDirty = true;
    private volatile bool audioInputDevicesDirty = true;
    private volatile bool capturableProcessesDirty = true;
    private volatile bool pendingSongRequestsDirty = true;
    private volatile bool bugReportResultDirty;
    private DateTime lastClientSeenUtc = DateTime.UtcNow;

    private bool bugReportResultSuccess;
    private string? bugReportResultError;

    private volatile bool publicShowsResultDirty;
    private PublicShowsSnapshotMessage? publicShowsSnapshot;

    private volatile bool reportShowResultDirty;
    private bool reportShowResultSuccess;
    private string? reportShowResultError;

    private volatile bool djProfilesResultDirty;
    private DjProfilesSnapshotMessage? djProfilesSnapshot;
    private volatile bool djProfileDetailResultDirty;
    private DjProfileDetailSnapshotMessage? djProfileDetailSnapshot;
    private volatile bool djProfileSaveResultDirty;
    private DjProfileSaveResultMessage? djProfileSaveResult;
    private volatile bool djProfileDeleteResultDirty;
    private DjProfileDeleteResultMessage? djProfileDeleteResult;
    private volatile bool djProfileReportResultDirty;
    private DjProfileReportAckMessage? djProfileReportResult;
    private volatile bool djProfileImageResultDirty;
    private DjProfileImageAckMessage? djProfileImageResult;
    private volatile bool djProfileLikeResultDirty;
    private DjProfileLikeResultMessage? djProfileLikeResult;
    private volatile bool djProfileFollowResultDirty;
    private DjProfileFollowResultMessage? djProfileFollowResult;
    private volatile bool profileLinkCodeResultDirty;
    private ProfileLinkCodeResultMessage? profileLinkCodeResult;
    private volatile bool profileLinkRedeemResultDirty;
    private ProfileLinkRedeemResultMessage? profileLinkRedeemResult;
    private volatile bool profileUnlinkResultDirty;
    private ProfileUnlinkResultMessage? profileUnlinkResult;

    private PresenceClient? presenceClient;
    private string? presenceCharacterName;
    private volatile bool followedDjWentLiveDirty;
    private FollowedDjWentLiveMessage? followedDjWentLive;

    private readonly List<PendingSongRequest> pendingSongRequests = new();
    private string? songRequestSessionTempDir;
    private string? lastSongRequestError;
    private SongRequestAccessMode songRequestAccessMode = SongRequestAccessMode.Open;
    private readonly HashSet<string> songRequestWhitelist = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> songRequestBlacklist = new(StringComparer.OrdinalIgnoreCase);

    private sealed class PendingSongRequest
    {
        public Guid RequestId;
        public Guid ListenerId;
        public string RequesterName = string.Empty;
        public string FileName = string.Empty;
        public string FilePath = string.Empty;
        public double DurationSeconds;
        public DateTime ReceivedAtUtc;
        public bool IsFromCoHost;
    }

    private readonly ConcurrentQueue<(Track Track, BpmAnalysisResult Result)> pendingBpmResults = new();

    private BroadcastHostConnection? broadcastHost;
    private BroadcastListenClient? listenClient;
    private volatile bool isConnectOrHostRequestInFlight;
    private string? lastBroadcastError;
    private string? lastListenError;
    private string? lastSpotifyModeError;
    private string? lastExternalInputModeError;
    private readonly SpotifyNowPlayingReader spotifyNowPlayingReader = new();
    private SpotifyNowPlaying? latestSpotifyNowPlaying;
    private DateTime lastSpotifyPollUtc = DateTime.MinValue;

    private readonly Stopwatch tickStopwatch = Stopwatch.StartNew();
    private DateTime lastTrackInfoPushUtc = DateTime.MinValue;
    private DateTime lastSpectrumPushUtc = DateTime.MinValue;

    public IpcServer(string pipeName, MixerEngine mixer, PlaylistManager playlists, SoundPadManager soundPads, DeckQueueManager deckQueues, string libraryRoot, int? ownerProcessId)
    {
        this.pipeName = pipeName;
        this.mixer = mixer;
        this.playlists = playlists;
        this.soundPads = soundPads;
        this.deckQueues = deckQueues;
        this.libraryRoot = libraryRoot;
        this.ownerProcessId = ownerProcessId;

        foreach (var playlist in this.playlists.Playlists)
        {
            foreach (var track in playlist.Tracks)
            {
                if (track.Bpm is null)
                    _ = Task.Run(() => AnalyzeBpmInBackground(track));
            }
        }
    }

    public async Task RunAsync(CancellationToken shutdownToken)
    {
        _ = Task.Run(() => IdleWatchdogLoop(shutdownToken), shutdownToken);
        if (ownerProcessId.HasValue)
            _ = Task.Run(() => OwnerProcessWatchdogLoop(ownerProcessId.Value, shutdownToken), shutdownToken);

        while (!shutdownToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
                    pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            }
            catch (IOException)
            {
                Console.WriteLine("[EchoMix.AudioHost] Pipe already claimed by another instance - exiting.");
                return;
            }

            using var _ = pipe;

            try
            {
                await pipe.WaitForConnectionAsync(shutdownToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            clientConnected = true;
            lastClientSeenUtc = DateTime.UtcNow;
            playlistsDirty = true;
            soundPadsDirty = true;
            queuesDirty = true;
            audioInputDevicesDirty = true;
            capturableProcessesDirty = true;
            Console.WriteLine("[EchoMix.AudioHost] Client connected.");

            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            var statusTask = Task.Run(() => StatusPushLoop(pipe, connectionCts.Token), connectionCts.Token);

            try
            {
                await ReadLoop(pipe, connectionCts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EchoMix.AudioHost] Client loop ended: {ex.Message}");
            }
            finally
            {
                connectionCts.Cancel();
                try { await statusTask; } catch { }
                clientConnected = false;
                lastClientSeenUtc = DateTime.UtcNow;
                Console.WriteLine("[EchoMix.AudioHost] Client disconnected.");
            }
        }
    }

    private async Task IdleWatchdogLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!clientConnected && DateTime.UtcNow - lastClientSeenUtc > IdleTimeout)
            {
                Console.WriteLine("[EchoMix.AudioHost] No client for a while - shutting down.");
                GracefulShutdownBeforeExit();
                Environment.Exit(0);
            }
        }
    }

    /// The fast path for a game crash or a Task Manager kill - polls whether the game's own process is still
    /// alive rather than waiting on the pipe to notice (a report traced AudioHost surviving well past a Task
    /// Manager kill of the game, which doesn't always close the pipe as promptly/cleanly as a normal
    /// disconnect does, leaving IdleWatchdogLoop's own 30s timeout as the only thing that would eventually
    /// catch it).
    private async Task OwnerProcessWatchdogLoop(int ownerProcessId, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(OwnerProcessCheckInterval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsProcessRunning(ownerProcessId))
                continue;

            Console.WriteLine("[EchoMix.AudioHost] Owner process (the game) is no longer running - shutting down.");
            GracefulShutdownBeforeExit();
            Environment.Exit(0);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task ReadLoop(NamedPipeServerStream pipe, CancellationToken token)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        while (pipe.IsConnected && !token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token);
            if (line == null)
                break;

            HandleMessage(line);
        }
    }

    private void HandleMessage(string json)
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

        try
        {
            HandleMessageCore(envelope);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EchoMix.AudioHost] Command {envelope.Type} failed: {ex}");
        }
    }

    private void HandleMessageCore(IpcEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case MessageType.LoadTrack:
                var loadCmd = envelope.ReadPayload<LoadTrackCommand>();
                GetDeck(loadCmd.Deck).LoadTrack(loadCmd.FilePath);
                break;

            case MessageType.TogglePlay:
                GetDeck(envelope.ReadPayload<DeckCommand>().Deck).TogglePlay();
                break;

            case MessageType.SetCue:
                GetDeck(envelope.ReadPayload<DeckCommand>().Deck).SetCue();
                break;

            case MessageType.JumpToCue:
                GetDeck(envelope.ReadPayload<DeckCommand>().Deck).JumpToCue();
                break;

            case MessageType.UnloadDeck:
                GetDeck(envelope.ReadPayload<DeckCommand>().Deck).Unload();
                break;

            case MessageType.SetDeckAutoplay:
                var autoplayCmd = envelope.ReadPayload<SetDeckAutoplayCommand>();
                deckQueues.SetAutoplayEnabled(autoplayCmd.Deck, autoplayCmd.Enabled);
                break;

            case MessageType.SetDeckSync:
                var syncCmd = envelope.ReadPayload<SetDeckSyncCommand>();
                mixer.SetDeckSync(syncCmd.Deck, syncCmd.Enabled);
                break;

            case MessageType.SetDeckTempo:
                var tempoCmd = envelope.ReadPayload<SetDeckTempoCommand>();
                mixer.SetDeckTempo(tempoCmd.Deck, tempoCmd.Ratio);
                break;

            case MessageType.SetPosition:
                var posCmd = envelope.ReadPayload<SetPositionCommand>();
                GetDeck(posCmd.Deck).Position = TimeSpan.FromSeconds(posCmd.PositionSeconds);
                break;

            case MessageType.SetGain:
                var gainCmd = envelope.ReadPayload<SetGainCommand>();
                if (IsSpotifyDeckA(gainCmd.Deck))
                    mixer.SpotifyGain = gainCmd.Gain;
                else if (IsExternalInputDeckA(gainCmd.Deck))
                    mixer.ExternalInputGain = gainCmd.Gain;
                else if (IsExternalInputDeckB(gainCmd.Deck))
                    mixer.ExternalInputGain2 = gainCmd.Gain;
                else
                    GetDeck(gainCmd.Deck).Gain = gainCmd.Gain;
                break;

            case MessageType.SetTrim:
                var trimCmd = envelope.ReadPayload<SetTrimCommand>();
                if (IsSpotifyDeckA(trimCmd.Deck))
                    mixer.SpotifyTrim = trimCmd.Trim;
                else if (IsExternalInputDeckA(trimCmd.Deck))
                    mixer.ExternalInputTrim = trimCmd.Trim;
                else if (IsExternalInputDeckB(trimCmd.Deck))
                    mixer.ExternalInputTrim2 = trimCmd.Trim;
                else
                    GetDeck(trimCmd.Deck).Trim = trimCmd.Trim;
                break;

            case MessageType.SetEq:
                var eqCmd = envelope.ReadPayload<SetEqCommand>();
                var eq = IsSpotifyDeckA(eqCmd.Deck) ? mixer.SpotifyEq
                    : IsExternalInputDeckA(eqCmd.Deck) ? mixer.ExternalInputEq
                    : IsExternalInputDeckB(eqCmd.Deck) ? mixer.ExternalInputEq2
                    : GetDeck(eqCmd.Deck).Eq;
                if (eq != null)
                {
                    switch (eqCmd.Band)
                    {
                        case EqBand.Low: eq.LowGainDb = eqCmd.GainDb; break;
                        case EqBand.Mid: eq.MidGainDb = eqCmd.GainDb; break;
                        case EqBand.High: eq.HighGainDb = eqCmd.GainDb; break;
                    }
                }

                break;

            case MessageType.SetFilter:
                var filterCmd = envelope.ReadPayload<SetFilterCommand>();
                var filter = IsSpotifyDeckA(filterCmd.Deck) ? mixer.SpotifyFilter
                    : IsExternalInputDeckA(filterCmd.Deck) ? mixer.ExternalInputFilter
                    : IsExternalInputDeckB(filterCmd.Deck) ? mixer.ExternalInputFilter2
                    : GetDeck(filterCmd.Deck).Filter;
                if (filter != null)
                    filter.Knob = filterCmd.Knob;
                break;

            case MessageType.SetCrossfader:
                mixer.CrossfaderPosition = envelope.ReadPayload<SetCrossfaderCommand>().Position;
                mixer.CancelAutoDjFade();
                break;

            case MessageType.SetCrossfaderCurve:
                mixer.CrossfaderCurve = envelope.ReadPayload<SetCrossfaderCurveCommand>().Curve;
                break;

            case MessageType.SetAutoDj:
                var autoDj = envelope.ReadPayload<SetAutoDjCommand>();
                mixer.AutoDjEnabled = autoDj.Enabled;
                mixer.AutoDjFadeSeconds = autoDj.FadeSeconds;

                if (autoDj.Enabled)
                {
                    deckQueues.SetAutoplayEnabled(DeckId.A, false);
                    deckQueues.SetAutoplayEnabled(DeckId.B, false);
                }
                break;

            case MessageType.SetMasterVolume:
                mixer.MasterVolume = envelope.ReadPayload<SetMasterVolumeCommand>().Volume;
                break;

            case MessageType.SetOutputMuted:
                var muteCmd = envelope.ReadPayload<SetOutputMutedCommand>();
                Console.WriteLine($"[EchoMix.AudioHost] SetOutputMuted received: {muteCmd.Muted}");
                mixer.OutputMuted = muteCmd.Muted;
                break;

            case MessageType.CreatePlaylist:
                playlists.CreatePlaylist(envelope.ReadPayload<PlaylistNameCommand>().PlaylistName);
                playlistsDirty = true;
                break;

            case MessageType.DeletePlaylist:
                var deleteCmd = envelope.ReadPayload<PlaylistNameCommand>();
                var toDelete = playlists.Playlists.Find(p => p.Name == deleteCmd.PlaylistName);
                if (toDelete != null)
                    playlists.DeletePlaylist(toDelete);
                playlistsDirty = true;
                break;

            case MessageType.RenamePlaylist:
                var renameCmd = envelope.ReadPayload<RenamePlaylistCommand>();
                var toRename = playlists.Playlists.Find(p => p.Name == renameCmd.PlaylistName);
                if (toRename != null)
                    playlists.RenamePlaylist(toRename, renameCmd.NewName);
                playlistsDirty = true;
                break;

            case MessageType.UploadTrack:
                var uploadCmd = envelope.ReadPayload<UploadTrackCommand>();
                var uploadTarget = playlists.Playlists.Find(p => p.Name == uploadCmd.PlaylistName);
                if (uploadTarget != null && TrackImporter.IsSupported(uploadCmd.SourceFilePath)
                    && !TrackImporter.ExceedsMaxSize(uploadCmd.SourceFilePath))
                {
                    var track = TrackImporter.Import(uploadCmd.SourceFilePath, playlists.LibraryDir);
                    playlists.AddTrack(uploadTarget, track);
                    _ = Task.Run(() => AnalyzeBpmInBackground(track));
                }

                playlistsDirty = true;
                break;

            case MessageType.RemoveTrack:
                var removeCmd = envelope.ReadPayload<RemoveTrackCommand>();
                var removeTarget = playlists.Playlists.Find(p => p.Name == removeCmd.PlaylistName);
                var removeTrack = removeTarget?.Tracks.Find(t => t.FilePath == removeCmd.TrackFilePath);
                if (removeTarget != null && removeTrack != null)
                    playlists.RemoveTrack(removeTarget, removeTrack);
                playlistsDirty = true;
                break;

            case MessageType.RequestPlaylists:
                playlistsDirty = true;
                break;

            case MessageType.AssignTrackToDeck:
                var assignCmd = envelope.ReadPayload<AssignTrackToDeckCommand>();
                deckQueues.Assign(assignCmd.Deck, new Track
                {
                    Title = assignCmd.Title,
                    FilePath = assignCmd.FilePath,
                    Gain = assignCmd.Gain,
                    DurationSeconds = assignCmd.DurationSeconds,
                    Bpm = assignCmd.Bpm,
                    BeatGridOffsetSeconds = assignCmd.BeatGridOffsetSeconds,
                });
                deckQueues.Pump(GetDeck(assignCmd.Deck), assignCmd.Deck);
                queuesDirty = true;
                break;

            case MessageType.RemoveFromDeckQueue:
                var removeQueueCmd = envelope.ReadPayload<RemoveFromDeckQueueCommand>();
                deckQueues.RemoveAt(removeQueueCmd.Deck, removeQueueCmd.Index);
                queuesDirty = true;
                break;

            case MessageType.PreviewTrack:
                var previewCmd = envelope.ReadPayload<PreviewTrackCommand>();
                if (!string.IsNullOrEmpty(previewCmd.FilePath) && File.Exists(previewCmd.FilePath))
                    mixer.StartPreview(previewCmd.FilePath);
                break;

            case MessageType.StopPreviewTrack:
                mixer.StopPreview();
                break;

            case MessageType.SetPreviewVolume:
                mixer.PreviewVolume = envelope.ReadPayload<SetPreviewVolumeCommand>().Volume;
                break;

            case MessageType.SetPreviewDampenVolume:
                mixer.PreviewDampenVolume = envelope.ReadPayload<SetPreviewDampenVolumeCommand>().Volume;
                break;

            case MessageType.SetTrackGain:
                var trackGainCmd = envelope.ReadPayload<SetTrackGainCommand>();
                var gainPlaylist = playlists.Playlists.Find(p => p.Name == trackGainCmd.PlaylistName);
                var gainTrack = gainPlaylist?.Tracks.Find(t => t.FilePath == trackGainCmd.TrackFilePath);
                if (gainTrack != null)
                {
                    gainTrack.Gain = Math.Clamp(trackGainCmd.Gain, 0f, 2f);
                    playlists.Save();
                }

                playlistsDirty = true;
                break;

            case MessageType.SetTrackBpm:
                var trackBpmCmd = envelope.ReadPayload<SetTrackBpmCommand>();
                var bpmPlaylist = playlists.Playlists.Find(p => p.Name == trackBpmCmd.PlaylistName);
                var bpmTrack = bpmPlaylist?.Tracks.Find(t => t.FilePath == trackBpmCmd.TrackFilePath);
                if (bpmTrack != null)
                {
                    bpmTrack.Bpm = trackBpmCmd.Bpm is > 0f ? trackBpmCmd.Bpm : null;
                    playlists.Save();

                    if (mixer.DeckA.LoadedTrackFilePath == bpmTrack.FilePath)
                        mixer.DeckA.SetLoadedTrackBpm(bpmTrack.Bpm);
                    if (mixer.DeckB.LoadedTrackFilePath == bpmTrack.FilePath)
                        mixer.DeckB.SetLoadedTrackBpm(bpmTrack.Bpm);
                    mixer.RecomputeSyncRatios();
                }

                playlistsDirty = true;
                break;

            case MessageType.UploadSoundPad:
                var uploadPadCmd = envelope.ReadPayload<UploadSoundPadCommand>();
                soundPads.Upload(uploadPadCmd.PadIndex, uploadPadCmd.SourceFilePath);
                soundPadsDirty = true;
                break;

            case MessageType.SetSoundPadLabel:
                var labelCmd = envelope.ReadPayload<SetSoundPadLabelCommand>();
                soundPads.SetLabel(labelCmd.PadIndex, labelCmd.Label);
                soundPadsDirty = true;
                break;

            case MessageType.RemoveSoundPad:
                var removePadCmd = envelope.ReadPayload<SoundPadIndexCommand>();
                soundPads.Remove(removePadCmd.PadIndex);
                if (removePadCmd.PadIndex >= 0 && removePadCmd.PadIndex < nextLoopTriggerUtc.Length)
                    nextLoopTriggerUtc[removePadCmd.PadIndex] = null;
                soundPadsDirty = true;
                break;

            case MessageType.PlaySoundPad:
                var playPadCmd = envelope.ReadPayload<SoundPadIndexCommand>();
                if (playPadCmd.PadIndex >= 0 && playPadCmd.PadIndex < soundPads.Pads.Count)
                {
                    var pad = soundPads.Pads[playPadCmd.PadIndex];
                    if (!string.IsNullOrEmpty(pad.FilePath) && File.Exists(pad.FilePath))
                        mixer.PlaySoundEffect(pad.FilePath, pad.Volume);
                }

                break;

            case MessageType.RequestSoundPads:
                soundPadsDirty = true;
                break;

            case MessageType.SetSoundPadLoop:
                var loopCmd = envelope.ReadPayload<SetSoundPadLoopCommand>();
                soundPads.SetLooping(loopCmd.PadIndex, loopCmd.Looping);
                if (loopCmd.PadIndex >= 0 && loopCmd.PadIndex < nextLoopTriggerUtc.Length)
                    nextLoopTriggerUtc[loopCmd.PadIndex] = null;                soundPadsDirty = true;
                break;

            case MessageType.SetSoundPadLoopInterval:
                var intervalCmd = envelope.ReadPayload<SetSoundPadLoopIntervalCommand>();
                soundPads.SetLoopInterval(intervalCmd.PadIndex, intervalCmd.IntervalSeconds);
                soundPadsDirty = true;
                break;

            case MessageType.SetSoundPadVolume:
                var volumeCmd = envelope.ReadPayload<SetSoundPadVolumeCommand>();
                soundPads.SetVolume(volumeCmd.PadIndex, volumeCmd.Volume);
                soundPadsDirty = true;
                break;

            case MessageType.StartBroadcast:
                _ = StartBroadcastAsync(envelope.ReadPayload<StartBroadcastCommand>());
                break;

            case MessageType.StopBroadcast:
                _ = StopBroadcastAsync();
                break;

            case MessageType.JoinAsHost:
                _ = JoinAsHostAsync(envelope.ReadPayload<JoinAsHostCommand>());
                break;

            case MessageType.PromoteHost:
                _ = broadcastHost?.PromoteAsync(envelope.ReadPayload<PromoteHostCommand>().TargetHostId) ?? Task.CompletedTask;
                break;

            case MessageType.SetMonitorVolume:
                if (broadcastHost != null)
                    broadcastHost.MonitorVolume = envelope.ReadPayload<SetMonitorVolumeCommand>().Volume;
                break;

            case MessageType.SetProximityMode:
                var proximityCmd = envelope.ReadPayload<SetProximityModeCommand>();
                _ = broadcastHost?.SetProximitySettingsAsync(proximityCmd.IsProximityAudio, proximityCmd.ProximityRange) ?? Task.CompletedTask;
                break;

            case MessageType.SetShowImage:
                var showImageCmd = envelope.ReadPayload<SetShowImageCommand>();
                _ = broadcastHost?.SendShowImageAsync(showImageCmd.SourceFilePath) ?? Task.CompletedTask;
                break;

            case MessageType.SetShowName:
                var showNameCmd = envelope.ReadPayload<SetShowNameCommand>();
                _ = broadcastHost?.SetShowNameAsync(showNameCmd.ShowName) ?? Task.CompletedTask;
                break;

            case MessageType.SetWebListenLink:
                var webListenLinkCmd = envelope.ReadPayload<SetWebListenLinkCommand>();
                _ = broadcastHost?.SetWebListenLinkAsync(webListenLinkCmd.Enabled) ?? Task.CompletedTask;
                break;

            case MessageType.ConnectToRemote:
                _ = ConnectToRemoteAsync(envelope.ReadPayload<ConnectToRemoteCommand>());
                break;

            case MessageType.DisconnectFromRemote:
                _ = DisconnectFromRemoteAsync();
                break;

            case MessageType.SetListenVolume:
                if (listenClient != null)
                    listenClient.Volume = envelope.ReadPayload<SetListenVolumeCommand>().Volume;
                break;

            case MessageType.RequestSong:
                _ = RequestSongAsync(envelope.ReadPayload<RequestSongCommand>());
                break;

            case MessageType.AcceptSongRequest:
                AcceptSongRequest(envelope.ReadPayload<AcceptSongRequestCommand>());
                break;

            case MessageType.DeclineSongRequest:
                DeclineSongRequest(envelope.ReadPayload<DeclineSongRequestCommand>(), reason: "The DJ declined this request.");
                break;

            case MessageType.SetSongRequestAccessControl:
                var accessCmd = envelope.ReadPayload<SetSongRequestAccessControlCommand>();
                songRequestAccessMode = accessCmd.Mode;
                songRequestWhitelist.Clear();
                foreach (var name in accessCmd.Whitelist)
                    songRequestWhitelist.Add(name);
                songRequestBlacklist.Clear();
                foreach (var name in accessCmd.Blacklist)
                    songRequestBlacklist.Add(name);
                break;

            case MessageType.SubmitBugReport:
                _ = SubmitBugReportAsync(envelope.ReadPayload<SubmitBugReportCommand>());
                break;

            case MessageType.RequestPublicShows:
                _ = RequestPublicShowsAsync();
                break;

            case MessageType.ReportShow:
                _ = SubmitShowReportAsync(envelope.ReadPayload<ReportShowCommand>());
                break;

            case MessageType.RequestDjProfiles:
                _ = RequestDjProfilesAsync(envelope.ReadPayload<RequestDjProfilesMessage>());
                break;

            case MessageType.GetDjProfileDetail:
                _ = GetDjProfileDetailAsync(envelope.ReadPayload<GetDjProfileDetailMessage>());
                break;

            case MessageType.SaveDjProfile:
                _ = SaveDjProfileAsync(envelope.ReadPayload<SaveDjProfileMessage>());
                break;

            case MessageType.DeleteDjProfile:
                _ = DeleteDjProfileAsync(envelope.ReadPayload<DeleteDjProfileMessage>());
                break;

            case MessageType.ReportDjProfile:
                _ = ReportDjProfileAsync(envelope.ReadPayload<SubmitDjProfileReportMessage>());
                break;

            case MessageType.SetDjProfileImage:
                _ = SetDjProfileImageAsync(envelope.ReadPayload<SetDjProfileImageCommand>());
                break;

            case MessageType.ToggleDjProfileLike:
                _ = ToggleDjProfileLikeAsync(envelope.ReadPayload<ToggleDjProfileLikeMessage>());
                break;

            case MessageType.ToggleDjProfileFollow:
                _ = ToggleDjProfileFollowAsync(envelope.ReadPayload<ToggleDjProfileFollowMessage>());
                break;
            case MessageType.GenerateProfileLinkCode:
                _ = GenerateProfileLinkCodeAsync(envelope.ReadPayload<GenerateProfileLinkCodeMessage>());
                break;
            case MessageType.RedeemProfileLinkCode:
                _ = RedeemProfileLinkCodeAsync(envelope.ReadPayload<RedeemProfileLinkCodeMessage>());
                break;
            case MessageType.UnlinkProfileCharacter:
                _ = UnlinkProfileCharacterAsync(envelope.ReadPayload<UnlinkProfileCharacterMessage>());
                break;

            case MessageType.SetLocalIdentity:
                SetLocalIdentity(envelope.ReadPayload<SetLocalIdentityCommand>().CharacterName);
                break;

            case MessageType.StartSpotifyMode:
                _ = StartSpotifyModeAsync();
                break;

            case MessageType.StopSpotifyMode:
                mixer.StopSpotifyMode();
                lastSpotifyModeError = null;
                break;

            case MessageType.SpotifySkipNext:
                if (mixer.IsSpotifyModeActive)
                    _ = spotifyNowPlayingReader.SkipNextAsync();
                break;

            case MessageType.SpotifySkipPrevious:
                if (mixer.IsSpotifyModeActive)
                    _ = spotifyNowPlayingReader.SkipPreviousAsync();
                break;

            case MessageType.SpotifyTogglePlayPause:
                if (mixer.IsSpotifyModeActive)
                    _ = spotifyNowPlayingReader.TogglePlayPauseAsync();
                break;

            case MessageType.RequestAudioInputDevices:
                audioInputDevicesDirty = true;
                break;

            case MessageType.RequestCapturableProcesses:
                capturableProcessesDirty = true;
                break;

            case MessageType.StartExternalInputMode:
                _ = StartExternalInputModeAsync(envelope.ReadPayload<StartExternalInputModeCommand>());
                break;

            case MessageType.StopExternalInputMode:
                mixer.StopExternalInputMode();
                lastExternalInputModeError = null;
                break;

            case MessageType.Shutdown:
                Console.WriteLine("[EchoMix.AudioHost] Shutdown requested by plugin - exiting.");
                GracefulShutdownBeforeExit();
                Environment.Exit(0);
                break;
        }
    }

    /// A bare Environment.Exit alone tears the TCP connection to the relay down abruptly (no TLS
    /// close_notify, just the OS yanking the socket on process death) - that's much less reliably/promptly
    /// noticed by the relay than a clean managed Dispose, which is exactly what made disabling the plugin
    /// while hosting leave a listener's connection in a broken state (stuck playing back nothing cleanly,
    /// instead of getting the usual Bye and gracefully returning to their own deck view).
    private void GracefulShutdownBeforeExit()
    {
        try
        {
            broadcastHost?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        try
        {
            CleanupSongRequestSession();
        }
        catch
        {
        }

        try
        {
            listenClient?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        try
        {
            mixer.StopSpotifyMode();
        }
        catch
        {
        }

        try
        {
            mixer.StopExternalInputMode();
        }
        catch
        {
        }
    }

    private async Task StartBroadcastAsync(StartBroadcastCommand cmd)
    {
        if (broadcastHost != null || listenClient != null || isConnectOrHostRequestInFlight)
        {
            lastBroadcastError = listenClient != null
                ? "You're currently listening to a show - disconnect first."
                : isConnectOrHostRequestInFlight
                    ? "Still connecting - hang on a moment."
                    : "Already broadcasting - stop your current show first.";
            return;
        }

        isConnectOrHostRequestInFlight = true;
        try
        {
            var connection = new BroadcastHostConnection();
            var ok = await connection.StartAsync(cmd.RoomCode, isCoHostJoin: false, cmd.Password, cmd.HostPassword,
                cmd.DjName, cmd.CharacterName, cmd.IsProximityAudio, cmd.ProximityRange,
                cmd.IsPubliclyListed, cmd.ShowName, cmd.VenueName, cmd.VenueDataCenter, cmd.VenueWorld,
                cmd.VenueHousingArea, cmd.VenueWard, cmd.VenuePlot, cmd.VenueIsApartment, cmd.VenueSubdivision);
            if (!ok)
            {
                await connection.DisposeAsync();
                lastBroadcastError = connection.LastError;
                return;
            }

            broadcastHost = connection;
            mixer.BroadcastConnection = connection;
            mixer.AttachMonitorSource(connection.MonitorSource);
            lastBroadcastError = null;
            StartSongRequestSession();
            Console.WriteLine($"[EchoMix.AudioHost] Went live as lead of room {connection.RoomCode} (DJ \"{cmd.DjName}\").");
        }
        finally
        {
            isConnectOrHostRequestInFlight = false;
        }
    }

    /// Joins an already-live room as an additional co-host DJ, mirroring StartBroadcastAsync - same
    /// mutual-exclusion guard, same MixerEngine wiring - but authenticating with the room's host password
    /// instead of creating a new room.
    private async Task JoinAsHostAsync(JoinAsHostCommand cmd)
    {
        if (broadcastHost != null || listenClient != null || isConnectOrHostRequestInFlight)
        {
            lastBroadcastError = listenClient != null
                ? "You're currently listening to a show - disconnect first."
                : isConnectOrHostRequestInFlight
                    ? "Still connecting - hang on a moment."
                    : "Already broadcasting/co-hosting - stop that first.";
            return;
        }

        isConnectOrHostRequestInFlight = true;
        try
        {
            var connection = new BroadcastHostConnection();
            var ok = await connection.StartAsync(cmd.RoomCode, isCoHostJoin: true, password: string.Empty, cmd.HostPassword,
                cmd.DjName, cmd.CharacterName, isProximityAudio: true, proximityRange: 30f);
            if (!ok)
            {
                await connection.DisposeAsync();
                lastBroadcastError = connection.LastError;
                return;
            }

            broadcastHost = connection;
            mixer.BroadcastConnection = connection;
            mixer.AttachMonitorSource(connection.MonitorSource);
            lastBroadcastError = null;
            StartSongRequestSession();
            Console.WriteLine($"[EchoMix.AudioHost] Joined room {connection.RoomCode} as {(connection.IsLead ? "lead" : "co-host")} (DJ \"{cmd.DjName}\").");
        }
        finally
        {
            isConnectOrHostRequestInFlight = false;
        }
    }

    /// A fresh, empty scratch directory for this broadcast session's song-request uploads - never the
    /// permanent library dir (PlaylistManager.LibraryDir), since these files are explicitly session-scoped
    /// (see CleanupSongRequestSession).
    private void StartSongRequestSession()
    {
        pendingSongRequests.Clear();
        pendingSongRequestsDirty = true;
        songRequestSessionTempDir = Path.Combine(Path.GetTempPath(), "EchoMix", "SongRequests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(songRequestSessionTempDir);
    }

    /// Deletes every song-request file from this session - pending, accepted, and declined alike.
    private void CleanupSongRequestSession()
    {
        pendingSongRequests.Clear();
        pendingSongRequestsDirty = true;

        if (songRequestSessionTempDir == null)
            return;

        try
        {
            Directory.Delete(songRequestSessionTempDir, recursive: true);
        }
        catch
        {
        }

        songRequestSessionTempDir = null;
    }

    private async Task StopBroadcastAsync()
    {
        if (broadcastHost == null)
            return;

        mixer.BroadcastConnection = null;
        mixer.AttachMonitorSource(null);
        var connection = broadcastHost;
        broadcastHost = null;
        if (connection.LastError != null)
            lastBroadcastError = connection.LastError;
        Console.WriteLine($"[EchoMix.AudioHost] Stopped {(connection.IsLead ? "broadcasting" : "co-hosting")} (was room {connection.RoomCode}).");
        CleanupSongRequestSession();
        await connection.DisposeAsync();
    }

    /// Listener side - hands the picked file to BroadcastListenClient and stashes whatever feedback comes
    /// back (success is null) for BuildBroadcastStatus to report.
    private async Task RequestSongAsync(RequestSongCommand cmd)
    {
        if (broadcastHost is { IsLead: false })
        {
            lastSongRequestError = await broadcastHost.RequestSongAsync(cmd.SourceFilePath, cmd.RequesterName);
            return;
        }

        lastSongRequestError = listenClient == null
            ? "Not connected to a show."
            : await listenClient.RequestSongAsync(cmd.SourceFilePath, cmd.RequesterName);
    }

    /// DJ side - loads a pending request onto a deck exactly the way AssignTrackToDeck loads a library track
    /// (same DeckQueueManager.Assign/Pump call, same DeckEngine.LoadTrack underneath - it doesn't care where
    /// the file lives).
    private void AcceptSongRequest(AcceptSongRequestCommand cmd)
    {
        var index = pendingSongRequests.FindIndex(r => r.RequestId == cmd.RequestId);
        if (index < 0)
            return;

        var request = pendingSongRequests[index];
        pendingSongRequests.RemoveAt(index);
        pendingSongRequestsDirty = true;

        deckQueues.Assign(cmd.Deck, new Track
        {
            Title = Path.GetFileNameWithoutExtension(request.FileName),
            FilePath = request.FilePath,
            Gain = 1f,
            DurationSeconds = request.DurationSeconds,
        });
        deckQueues.Pump(GetDeck(cmd.Deck), cmd.Deck);
        queuesDirty = true;
    }

    /// DJ side (or the access-control filter below, with its own reason) - drops the pending entry and tells
    /// the relay to let that one listener know, without touching the file on disk (see AcceptSongRequest's
    /// own note on why cleanup is session-scoped, not per-request).
    private void DeclineSongRequest(DeclineSongRequestCommand cmd, string reason)
    {
        var index = pendingSongRequests.FindIndex(r => r.RequestId == cmd.RequestId);
        if (index < 0)
            return;

        var request = pendingSongRequests[index];
        pendingSongRequests.RemoveAt(index);
        pendingSongRequestsDirty = true;

        _ = broadcastHost?.DeclineSongRequestAsync(cmd.RequestId, request.ListenerId, reason, request.IsFromCoHost) ?? Task.CompletedTask;
    }

    /// A soft/trust-based check, not a hard security boundary - RequesterName is self-reported by the
    /// requesting listener's own plugin, the same trust level as everything else in this relay protocol (room
    /// codes/passwords are the real boundary).
    private bool IsSongRequestAllowed(string requesterName) => songRequestAccessMode switch
    {
        SongRequestAccessMode.Whitelist => songRequestWhitelist.Contains(requesterName),
        SongRequestAccessMode.Blacklist => !songRequestBlacklist.Contains(requesterName),
        _ => true,
    };

    /// Drains whatever song-request files finished reassembling on broadcastHost since the last tick (see
    /// BroadcastHostConnection.TryDequeueCompletedSongRequest), writes each to this session's temp dir, and
    /// either adds it to the pending list or - if the DJ's whitelist/ blacklist blocks this requester -
    /// silently discards it (with a decline notice back to the listener so their UI doesn't hang forever
    /// thinking it's still pending).
    private void DrainCompletedSongRequests()
    {
        if (broadcastHost == null)
            return;

        while (broadcastHost.TryDequeueCompletedSongRequest(out var completed))
        {
            if (!completed.IsFromCoHost && !IsSongRequestAllowed(completed.RequesterName))
            {
                _ = broadcastHost.DeclineSongRequestAsync(completed.RequestId, completed.ListenerId, "This DJ isn't accepting requests from you right now.", completed.IsFromCoHost);
                continue;
            }

            if (!SongRequestLimits.IsSupported(completed.FileName) || SongRequestLimits.ExceedsMaxSize(completed.FileBytes.LongLength)
                || songRequestSessionTempDir == null)
            {
                _ = broadcastHost.DeclineSongRequestAsync(completed.RequestId, completed.ListenerId, "That file couldn't be accepted.", completed.IsFromCoHost);
                continue;
            }

            var destPath = Path.Combine(songRequestSessionTempDir, $"{completed.RequestId:N}{Path.GetExtension(completed.FileName)}");
            double durationSeconds;
            try
            {
                File.WriteAllBytes(destPath, completed.FileBytes);
                using var probe = AudioReaderFactory.Open(destPath);
                durationSeconds = probe.TotalTime.TotalSeconds;
            }
            catch
            {
                _ = broadcastHost.DeclineSongRequestAsync(completed.RequestId, completed.ListenerId, "That file couldn't be read.", completed.IsFromCoHost);
                continue;
            }

            pendingSongRequests.Add(new PendingSongRequest
            {
                RequestId = completed.RequestId,
                ListenerId = completed.ListenerId,
                RequesterName = completed.RequesterName,
                FileName = completed.FileName,
                FilePath = destPath,
                DurationSeconds = durationSeconds,
                ReceivedAtUtc = DateTime.UtcNow,
                IsFromCoHost = completed.IsFromCoHost,
            });
            pendingSongRequestsDirty = true;
        }
    }

    /// Gathers everything AudioHost itself knows (its own session log, current mixer/ broadcast state) around
    /// whatever the plugin supplied (its version, the DJ's character name, and an optional description), and
    /// submits it through BugReportClient - a one-shot relay connection independent of any broadcast/listen
    /// session that might or might not currently be active.
    private async Task SubmitBugReportAsync(SubmitBugReportCommand cmd)
    {
        string? logContent = null;
        try
        {
            var logPath = Path.Combine(libraryRoot, "audiohost.log");
            if (File.Exists(logPath))
            {
                using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                logContent = reader.ReadToEnd();
            }
        }
        catch (Exception ex)
        {
            logContent = $"(couldn't read audiohost.log: {ex.Message})";
        }

        var report = new SubmitBugReportMessage
        {
            PluginVersion = cmd.PluginVersion,
            CharacterName = cmd.CharacterName,
            DjName = broadcastHost?.Roster.FirstOrDefault(h => h.HostId == broadcastHost.HostId)?.DjName,
            Description = cmd.Description,
            DiscordName = cmd.DiscordName,
            StatusSnapshot = BuildStatusSnapshotForReport(),
            SystemInfo = cmd.SystemInfo,
            LogContent = logContent,
        };

        var (success, error) = await BugReportClient.SubmitAsync(report);
        bugReportResultSuccess = success;
        bugReportResultError = error;
        bugReportResultDirty = true;
    }

    /// "View Live Shows" - a one-shot relay round trip via PublicShowsClient, same dirty-flag-once-it-lands
    /// pattern as SubmitBugReportAsync above.
    private async Task RequestPublicShowsAsync()
    {
        var (success, error, snapshot) = await PublicShowsClient.RequestAsync();
        publicShowsSnapshot = success ? snapshot! : new PublicShowsSnapshotMessage { Error = error };
        publicShowsResultDirty = true;
    }

    /// "Report Show" - a one-shot relay round trip via ShowReportClient, same dirty-flag-once-it-lands
    /// pattern as SubmitBugReportAsync above.
    private async Task SubmitShowReportAsync(ReportShowCommand cmd)
    {
        var report = new SubmitShowReportMessage
        {
            RoomCode = cmd.RoomCode,
            ShowName = cmd.ShowName,
            DjName = cmd.DjName,
            Reason = cmd.Reason,
            ReporterCharacterName = cmd.ReporterCharacterName,
        };

        var (success, error) = await ShowReportClient.SubmitAsync(report);
        reportShowResultSuccess = success;
        reportShowResultError = error;
        reportShowResultDirty = true;
    }

    private async Task RequestDjProfilesAsync(RequestDjProfilesMessage cmd)
    {
        var (success, error, result) = await DjProfileClient.RequestProfilesAsync(cmd);
        djProfilesSnapshot = success ? result! : new DjProfilesSnapshotMessage { Error = error };
        djProfilesResultDirty = true;
    }

    private async Task GetDjProfileDetailAsync(GetDjProfileDetailMessage cmd)
    {
        var (success, error, result) = await DjProfileClient.GetDetailAsync(cmd);
        djProfileDetailSnapshot = success ? result! : new DjProfileDetailSnapshotMessage { Error = error };
        djProfileDetailResultDirty = true;
    }

    private async Task SaveDjProfileAsync(SaveDjProfileMessage cmd)
    {
        var (success, error, result) = await DjProfileClient.SaveAsync(cmd);
        djProfileSaveResult = success ? result! : new DjProfileSaveResultMessage { Success = false, Error = error };
        djProfileSaveResultDirty = true;
    }

    private async Task DeleteDjProfileAsync(DeleteDjProfileMessage cmd)
    {
        var (success, error, result) = await DjProfileClient.DeleteAsync(cmd);
        djProfileDeleteResult = success ? result! : new DjProfileDeleteResultMessage { Success = false, Error = error };
        djProfileDeleteResultDirty = true;
    }

    private async Task ReportDjProfileAsync(SubmitDjProfileReportMessage cmd)
    {
        var (success, error, result) = await DjProfileClient.ReportAsync(cmd);
        djProfileReportResult = success ? result! : new DjProfileReportAckMessage { Accepted = false, Error = error };
        djProfileReportResultDirty = true;
    }

    private async Task SetDjProfileImageAsync(SetDjProfileImageCommand cmd)
    {
        var (success, error) = await DjProfileClient.UploadImageAsync(cmd.ProfileId, cmd.CharacterName, cmd.Slot, cmd.SourceFilePath);
        djProfileImageResult = new DjProfileImageAckMessage { Success = success, Error = error, ProfileId = cmd.ProfileId, Slot = cmd.Slot };
        djProfileImageResultDirty = true;
    }

    private async Task ToggleDjProfileLikeAsync(ToggleDjProfileLikeMessage cmd)
    {
        var (success, error, result) = await DjProfileClient.ToggleLikeAsync(cmd);
        djProfileLikeResult = success ? result! : new DjProfileLikeResultMessage { Success = false, Error = error };
        djProfileLikeResultDirty = true;
    }

    private async Task ToggleDjProfileFollowAsync(ToggleDjProfileFollowMessage cmd)
    {
        var (success, error, result) = await DjProfileClient.ToggleFollowAsync(cmd);
        djProfileFollowResult = success ? result! : new DjProfileFollowResultMessage { Success = false, Error = error };
        djProfileFollowResultDirty = true;
    }

    private async Task GenerateProfileLinkCodeAsync(GenerateProfileLinkCodeMessage cmd)
    {
        var (success, error, result) = await DjProfileClient.GenerateLinkCodeAsync(cmd);
        profileLinkCodeResult = success ? result! : new ProfileLinkCodeResultMessage { Success = false, Error = error };
        profileLinkCodeResultDirty = true;
    }

    private async Task RedeemProfileLinkCodeAsync(RedeemProfileLinkCodeMessage cmd)
    {
        var (success, error, result) = await DjProfileClient.RedeemLinkCodeAsync(cmd);
        profileLinkRedeemResult = success ? result! : new ProfileLinkRedeemResultMessage { Success = false, Error = error };
        profileLinkRedeemResultDirty = true;
    }

    private async Task UnlinkProfileCharacterAsync(UnlinkProfileCharacterMessage cmd)
    {
        var (success, error, result) = await DjProfileClient.UnlinkCharacterAsync(cmd);
        profileUnlinkResult = success ? result! : new ProfileUnlinkResultMessage { Success = false, Error = error };
        profileUnlinkResultDirty = true;
    }

    /// (Re)starts PresenceClient whenever the plugin reports a character name not already registered for
    /// presence - see SetLocalIdentityCommand's own doc comment for why AudioHost needs to be told this at
    /// all rather than knowing it already.
    private void SetLocalIdentity(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName) || characterName == presenceCharacterName)
            return;

        presenceCharacterName = characterName;
        _ = RestartPresenceClientAsync(characterName);
    }

    private async Task RestartPresenceClientAsync(string characterName)
    {
        var previous = presenceClient;
        presenceClient = new PresenceClient(characterName);
        if (previous != null)
            await previous.DisposeAsync();
    }

    private string BuildStatusSnapshotForReport()
    {
        var lines = new List<string>
        {
            $"Broadcasting: {broadcastHost?.IsLive ?? false} (room {broadcastHost?.RoomCode ?? "-"}, {broadcastHost?.ListenerCount ?? 0} listener(s), lead: {broadcastHost?.IsLead ?? false})",
            $"Listening: {listenClient?.IsConnected ?? false}",
            $"Spotify Mode: {mixer.IsSpotifyModeActive}",
            $"External Input Mode: {mixer.IsExternalInputModeActive}",
        };
        return string.Join("\n", lines);
    }

    /// Spotify Mode and Listen mode both want to be the only thing feeding the local output, so starting
    /// either one first stops the other rather than leaving Spotify's capture running uselessly in the
    /// background (harmless - the mixer graph just never gets read while listening - but there's no reason to
    /// keep the capture thread and its buffer alive for nothing).
    private async Task StartSpotifyModeAsync()
    {
        if (listenClient != null)
        {
            lastSpotifyModeError = "Can't use Spotify Mode while listening to another broadcast.";
            return;
        }

        var ok = await mixer.StartSpotifyModeAsync();
        lastSpotifyModeError = ok ? null : mixer.SpotifyModeError;
    }

    /// Same reasoning as StartSpotifyModeAsync above - Listen mode wants to be the only thing feeding the
    /// local output, so starting it first stops External Input Mode too.
    private async Task StartExternalInputModeAsync(StartExternalInputModeCommand cmd)
    {
        if (listenClient != null)
        {
            lastExternalInputModeError = "Can't use External Input Mode while listening to another broadcast.";
            return;
        }

        var ok = await mixer.StartExternalInputModeAsync(cmd.DeviceId, cmd.ProcessName, cmd.DeviceId2);
        lastExternalInputModeError = ok ? null : mixer.ExternalInputModeError;
    }

    private async Task ConnectToRemoteAsync(ConnectToRemoteCommand cmd)
    {
        if (listenClient != null || broadcastHost != null || isConnectOrHostRequestInFlight)
        {
            lastListenError = broadcastHost != null
                ? "You're currently broadcasting - stop your show first."
                : isConnectOrHostRequestInFlight
                    ? "Still connecting - hang on a moment."
                    : "Already connected to a show - disconnect first.";
            return;
        }

        isConnectOrHostRequestInFlight = true;
        try
        {
            if (mixer.IsSpotifyModeActive)
                mixer.StopSpotifyMode();
            if (mixer.IsExternalInputModeActive)
                mixer.StopExternalInputMode();

            var client = new BroadcastListenClient();
            var ok = await client.ConnectAsync(cmd.RoomCode, cmd.Password, cmd.CharacterName);
            if (!ok)
            {
                lastListenError = client.LastError;
                await client.DisposeAsync();
                return;
            }

            mixer.Stop();
            listenClient = client;
            lastListenError = null;
        }
        finally
        {
            isConnectOrHostRequestInFlight = false;
        }
    }

    private async Task DisconnectFromRemoteAsync()
    {
        if (listenClient == null)
            return;

        var client = listenClient;
        listenClient = null;
        lastListenError = client.LastError;
        mixer.Start();
        await client.DisposeAsync();
    }

    private async Task PollSpotifyNowPlayingAsync()
    {
        var result = await spotifyNowPlayingReader.PollAsync();
        latestSpotifyNowPlaying = result;
    }

    private DeckEngine GetDeck(DeckId id) => id == DeckId.A ? mixer.DeckA : mixer.DeckB;

    /// True when a Gain/Trim/EQ/Filter command for Deck A should actually apply to the Spotify effect chain
    /// instead of DeckA's own (paused, inaudible) one - see MixerEngine.StartSpotifyModeAsync.
    private bool IsSpotifyDeckA(DeckId deck) => deck == DeckId.A && mixer.IsSpotifyModeActive;

    private bool IsExternalInputDeckA(DeckId deck) => deck == DeckId.A && mixer.IsExternalInputModeActive;

    /// Same idea as IsExternalInputDeckA, for the optional second input device - only true once
    /// IsExternalInput2Active (a second device was actually supplied and started), not just because Deck B
    /// happens to be idle.
    private bool IsExternalInputDeckB(DeckId deck) => deck == DeckId.B && mixer.IsExternalInput2Active;

    private async Task StatusPushLoop(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            while (pipe.IsConnected && !token.IsCancellationRequested)
            {
                if (listenClient != null && !listenClient.IsConnected && !listenClient.IsReconnecting)
                    _ = DisconnectFromRemoteAsync();

                if (broadcastHost != null && !broadcastHost.IsLive && !broadcastHost.IsReconnecting)
                    _ = StopBroadcastAsync();

                if (mixer.IsSpotifyModeActive && !mixer.IsSpotifyCaptureAlive)
                {
                    mixer.StopSpotifyMode();
                    lastSpotifyModeError = "Spotify closed - Spotify Mode stopped.";
                }

                if (mixer.IsExternalInputModeActive && !mixer.IsExternalInputCaptureAlive)
                {
                    mixer.StopExternalInputMode();
                    lastExternalInputModeError = "Selected input device disconnected - External Input Mode stopped.";
                }

                if (mixer.IsSpotifyModeActive && DateTime.UtcNow - lastSpotifyPollUtc >= TimeSpan.FromSeconds(1))
                {
                    lastSpotifyPollUtc = DateTime.UtcNow;
                    _ = PollSpotifyNowPlayingAsync();
                }

                TriggerDueLoops();

                if (deckQueues.Pump(mixer.DeckA, DeckId.A))
                    queuesDirty = true;
                if (deckQueues.Pump(mixer.DeckB, DeckId.B))
                    queuesDirty = true;

                mixer.RecomputeSyncRatios();

                var deltaSeconds = (float)tickStopwatch.Elapsed.TotalSeconds;
                tickStopwatch.Restart();
                mixer.Tick(deltaSeconds);

                var wentLive = presenceClient?.DrainNotification();
                if (wentLive != null)
                {
                    followedDjWentLive = wentLive;
                    followedDjWentLiveDirty = true;
                }

                var gotBpmResult = false;
                while (pendingBpmResults.TryDequeue(out var bpmResult))
                {
                    gotBpmResult = true;
                    bpmResult.Track.Bpm = bpmResult.Result.Bpm;
                    bpmResult.Track.BeatGridOffsetSeconds = bpmResult.Result.BeatGridOffsetSeconds;
                    if (mixer.DeckA.LoadedTrackFilePath == bpmResult.Track.FilePath)
                    {
                        mixer.DeckA.SetLoadedTrackBpm(bpmResult.Result.Bpm);
                        mixer.DeckA.SetLoadedTrackBeatGridOffset(bpmResult.Result.BeatGridOffsetSeconds);
                    }
                    if (mixer.DeckB.LoadedTrackFilePath == bpmResult.Track.FilePath)
                    {
                        mixer.DeckB.SetLoadedTrackBpm(bpmResult.Result.Bpm);
                        mixer.DeckB.SetLoadedTrackBeatGridOffset(bpmResult.Result.BeatGridOffsetSeconds);
                    }
                }

                if (gotBpmResult)
                {
                    playlists.Save();
                    playlistsDirty = true;
                    mixer.RecomputeSyncRatios();
                }

                DrainCompletedSongRequests();

                await SendAsync(pipe, IpcEnvelope.For(MessageType.MixerStatus, BuildMixerStatus()), token);
                await SendAsync(pipe, IpcEnvelope.For(MessageType.Spectrum, new SpectrumMessage { Deck = DeckId.A, Bands = mixer.AnalyzerA.GetSpectrum(40) }), token);
                await SendAsync(pipe, IpcEnvelope.For(MessageType.Spectrum, new SpectrumMessage { Deck = DeckId.B, Bands = mixer.AnalyzerB.GetSpectrum(40) }), token);

                if (listenClient != null)
                {
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.ListenSpectrum, new ListenSpectrumMessage
                    {
                        Bands = listenClient.Analyzer.GetSpectrum(40),
                        BandsA = listenClient.SpectrumA,
                        BandsB = listenClient.SpectrumB,
                    }), token);
                }

                if (broadcastHost != null && DateTime.UtcNow - lastTrackInfoPushUtc >= TimeSpan.FromSeconds(1))
                {
                    lastTrackInfoPushUtc = DateTime.UtcNow;
                    var (titleA, positionA, durationA) = GetDeckTrackInfo(DeckId.A);
                    var (titleB, positionB, durationB) = GetDeckTrackInfo(DeckId.B);
                    await broadcastHost.SetTrackInfoAsync(titleA, positionA, durationA, titleB, positionB, durationB, mixer.IsSpotifyModeActive);
                }

                if (broadcastHost != null && DateTime.UtcNow - lastSpectrumPushUtc >= TimeSpan.FromMilliseconds(66))
                {
                    lastSpectrumPushUtc = DateTime.UtcNow;
                    await broadcastHost.SetSpectrumAsync(mixer.AnalyzerA.GetSpectrum(40), mixer.AnalyzerB.GetSpectrum(40));
                }

                if (playlistsDirty)
                {
                    playlistsDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.PlaylistsSnapshot, BuildPlaylistsSnapshot()), token);
                }

                if (soundPadsDirty)
                {
                    soundPadsDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.SoundPadsSnapshot, BuildSoundPadsSnapshot()), token);
                }

                if (queuesDirty)
                {
                    queuesDirty = false;
                    var queuesSnapshot = BuildDeckQueuesSnapshot();
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.DeckQueuesSnapshot, queuesSnapshot), token);

                    if (broadcastHost is { IsLead: true })
                        await broadcastHost.SetDeckQueuesAsync(queuesSnapshot.QueueA, queuesSnapshot.QueueB);
                }

                if (audioInputDevicesDirty)
                {
                    audioInputDevicesDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.AudioInputDevicesSnapshot, BuildAudioInputDevicesSnapshot()), token);
                }

                if (capturableProcessesDirty)
                {
                    capturableProcessesDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.CapturableProcessesSnapshot, BuildCapturableProcessesSnapshot()), token);
                }

                if (pendingSongRequestsDirty)
                {
                    pendingSongRequestsDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.PendingSongRequestsSnapshot, BuildPendingSongRequestsSnapshot()), token);
                }

                if (bugReportResultDirty)
                {
                    bugReportResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.BugReportResult,
                        new BugReportResultMessage { Success = bugReportResultSuccess, Error = bugReportResultError }), token);
                }

                if (publicShowsResultDirty)
                {
                    publicShowsResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.PublicShowsSnapshot, publicShowsSnapshot!), token);
                }

                if (reportShowResultDirty)
                {
                    reportShowResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.ReportShowResult,
                        new ReportShowResultMessage { Success = reportShowResultSuccess, Error = reportShowResultError }), token);
                }

                if (djProfilesResultDirty)
                {
                    djProfilesResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.DjProfilesSnapshot, djProfilesSnapshot!), token);
                }

                if (djProfileDetailResultDirty)
                {
                    djProfileDetailResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.DjProfileDetailSnapshot, djProfileDetailSnapshot!), token);
                }

                if (djProfileSaveResultDirty)
                {
                    djProfileSaveResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.DjProfileSaveResult, djProfileSaveResult!), token);
                }

                if (djProfileDeleteResultDirty)
                {
                    djProfileDeleteResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.DjProfileDeleteResult, djProfileDeleteResult!), token);
                }

                if (djProfileReportResultDirty)
                {
                    djProfileReportResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.DjProfileReportResult, djProfileReportResult!), token);
                }

                if (djProfileImageResultDirty)
                {
                    djProfileImageResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.DjProfileImageResult, djProfileImageResult!), token);
                }

                if (djProfileLikeResultDirty)
                {
                    djProfileLikeResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.DjProfileLikeResult, djProfileLikeResult!), token);
                }

                if (djProfileFollowResultDirty)
                {
                    djProfileFollowResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.DjProfileFollowResult, djProfileFollowResult!), token);
                }

                if (profileLinkCodeResultDirty)
                {
                    profileLinkCodeResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.ProfileLinkCodeResult, profileLinkCodeResult!), token);
                }

                if (profileLinkRedeemResultDirty)
                {
                    profileLinkRedeemResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.ProfileLinkRedeemResult, profileLinkRedeemResult!), token);
                }

                if (profileUnlinkResultDirty)
                {
                    profileUnlinkResultDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.ProfileUnlinkResult, profileUnlinkResult!), token);
                }

                if (followedDjWentLiveDirty)
                {
                    followedDjWentLiveDirty = false;
                    await SendAsync(pipe, IpcEnvelope.For(MessageType.FollowedDjWentLive, followedDjWentLive!), token);
                }

                await Task.Delay(StatusInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    /// Runs BpmAnalyzer's full-decode pass on a thread-pool thread (see BpmAnalyzer's own doc comment for why
    /// this can't run inline in the UploadTrack handler) and hands the result back via pendingBpmResults
    /// instead of mutating `track`/calling playlists.Save() directly from this background thread -
    /// StatusPushLoop drains the queue once per tick, keeping all playlist mutation on the one thread that
    /// already owns it.
    private void AnalyzeBpmInBackground(Track track)
    {
        var result = BpmAnalyzer.Analyze(track.FilePath);
        if (result.Bpm is > 0f)
            pendingBpmResults.Enqueue((track, result));
    }

    /// Runs once per status-loop tick (~33ms): fires any looping pad whose interval has elapsed.
    private void TriggerDueLoops()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < soundPads.Pads.Count; i++)
        {
            var pad = soundPads.Pads[i];
            if (!pad.Looping || string.IsNullOrEmpty(pad.FilePath))
            {
                nextLoopTriggerUtc[i] = null;
                continue;
            }

            if (nextLoopTriggerUtc[i] != null && now < nextLoopTriggerUtc[i])
                continue;

            if (File.Exists(pad.FilePath))
                mixer.PlaySoundEffect(pad.FilePath, pad.Volume);

            nextLoopTriggerUtc[i] = now.AddSeconds(Math.Max(0.1f, pad.LoopIntervalSeconds));
        }
    }

    private static async Task SendAsync(NamedPipeServerStream pipe, IpcEnvelope envelope, CancellationToken token)
    {
        var json = JsonConvert.SerializeObject(envelope);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await pipe.WriteAsync(bytes, token);
        await pipe.FlushAsync(token);
    }

    /// Reports this one deck's own now-playing info for the broadcast's per-deck TrackInfo push - real
    /// per-deck data (unlike the old "whichever is playing wins" heuristic), so listeners can see both decks'
    /// own readouts same as the DJ's own dual-deck display.
    private (string? Title, double Position, double Duration) GetDeckTrackInfo(DeckId id)
    {
        if (id == DeckId.A && mixer.IsSpotifyModeActive && latestSpotifyNowPlaying != null && !string.IsNullOrEmpty(latestSpotifyNowPlaying.Title))
        {
            var artistPart = string.IsNullOrEmpty(latestSpotifyNowPlaying.Artist) ? string.Empty : $" - {latestSpotifyNowPlaying.Artist}";
            return (latestSpotifyNowPlaying.Title + artistPart, latestSpotifyNowPlaying.ProgressMs / 1000.0, latestSpotifyNowPlaying.DurationMs / 1000.0);
        }

        if (id == DeckId.A && mixer.IsExternalInputModeActive)
            return ("Live Input", 0, 0);
        if (id == DeckId.B && mixer.IsExternalInput2Active)
            return ("Live Input", 0, 0);

        var deck = id == DeckId.A ? mixer.DeckA : mixer.DeckB;
        return deck.HasTrack ? (deck.LoadedTrackTitle, deck.Position.TotalSeconds, deck.Duration.TotalSeconds) : (null, 0, 0);
    }

    private MixerStatusMessage BuildMixerStatus() => new()
    {
        DeckA = BuildDeckStatus(DeckId.A, mixer.DeckA, mixer.PeakA),
        DeckB = BuildDeckStatus(DeckId.B, mixer.DeckB, mixer.PeakB),
        CrossfaderPosition = mixer.CrossfaderPosition,
        CrossfaderCurve = mixer.CrossfaderCurve,
        AutoDjEnabled = mixer.AutoDjEnabled,
        AutoDjFadeSeconds = mixer.AutoDjFadeSeconds,
        MasterVolume = mixer.MasterVolume,
        IsOutputMuted = mixer.OutputMuted,
        OutputPeak = mixer.OutputPeak,
        Broadcast = BuildBroadcastStatus(),
        SpotifyMode = BuildSpotifyModeStatus(),
        ExternalInputMode = BuildExternalInputModeStatus(),
        PreviewingFilePath = mixer.PreviewingFilePath,
    };

    private SpotifyModeStatusMessage BuildSpotifyModeStatus()
    {
        var active = mixer.IsSpotifyModeActive;
        var nowPlaying = active ? latestSpotifyNowPlaying : null;

        return new SpotifyModeStatusMessage
        {
            IsActive = active,
            Error = mixer.SpotifyModeError ?? lastSpotifyModeError,
            NowPlayingTitle = nowPlaying?.Title,
            NowPlayingArtist = nowPlaying?.Artist,
            NowPlayingPositionSeconds = (nowPlaying?.ProgressMs ?? 0) / 1000.0,
            NowPlayingDurationSeconds = (nowPlaying?.DurationMs ?? 0) / 1000.0,
            NowPlayingIsPlaying = nowPlaying?.IsPlaying ?? false,
        };
    }

    private ExternalInputModeStatusMessage BuildExternalInputModeStatus() => new()
    {
        IsActive = mixer.IsExternalInputModeActive,
        Error = mixer.ExternalInputModeError ?? lastExternalInputModeError,
        DeviceName = mixer.ExternalInputDeviceName,
        IsSecondActive = mixer.IsExternalInput2Active,
        DeviceName2 = mixer.ExternalInputDeviceName2,
    };

    private AudioInputDevicesSnapshotMessage BuildAudioInputDevicesSnapshot() => new()
    {
        Devices = AudioInputDevices.List().Select(d => new AudioInputDeviceDto { Id = d.Id, Name = d.Name }).ToList(),
    };

    private CapturableProcessesSnapshotMessage BuildCapturableProcessesSnapshot() => new()
    {
        Processes = CapturableProcesses.List().Select(p => new CapturableProcessDto { ProcessName = p.ProcessName, DisplayName = p.DisplayName }).ToList(),
    };

    private BroadcastStatusMessage BuildBroadcastStatus() => new()
    {
        IsLive = broadcastHost?.IsLive ?? false,
        RoomCode = broadcastHost?.RoomCode ?? listenClient?.RoomCode,
        ListenerCount = broadcastHost?.ListenerCount ?? 0,
        ListenerRoster = broadcastHost?.ListenerRoster.ToList() ?? new List<ListenerRosterEntryDto>(),
        BroadcastError = broadcastHost?.LastError ?? lastBroadcastError,
        LiveSinceUtc = broadcastHost?.RoomLiveSinceUtc ?? listenClient?.RoomLiveSinceUtc,
        WebListenUrl = broadcastHost?.WebListenUrl,

        IsListening = listenClient?.IsConnected ?? false,
        IsListenerReconnecting = listenClient?.IsReconnecting ?? false,
        IsHostReconnecting = broadcastHost?.IsReconnecting ?? false,
        HostDjName = listenClient?.HostDjName,
        HostCharacterName = listenClient?.HostCharacterName,
        IsProximityAudio = listenClient?.IsProximityAudio ?? false,
        ProximityRange = listenClient?.ProximityRange ?? 30f,
        NowPlayingTitleA = listenClient?.NowPlayingTitleA ?? broadcastHost?.LatestLeadTrackInfo?.TitleA,
        NowPlayingPositionSecondsA = listenClient?.NowPlayingPositionSecondsA ?? broadcastHost?.LatestLeadTrackInfo?.PositionSecondsA ?? 0,
        NowPlayingDurationSecondsA = listenClient?.NowPlayingDurationSecondsA ?? broadcastHost?.LatestLeadTrackInfo?.DurationSecondsA ?? 0,
        NowPlayingTitleB = listenClient?.NowPlayingTitleB ?? broadcastHost?.LatestLeadTrackInfo?.TitleB,
        NowPlayingPositionSecondsB = listenClient?.NowPlayingPositionSecondsB ?? broadcastHost?.LatestLeadTrackInfo?.PositionSecondsB ?? 0,
        NowPlayingDurationSecondsB = listenClient?.NowPlayingDurationSecondsB ?? broadcastHost?.LatestLeadTrackInfo?.DurationSecondsB ?? 0,
        IsHostSpotifyModeActive = listenClient?.IsHostSpotifyModeActive ?? broadcastHost?.LatestLeadTrackInfo?.IsSpotifyModeActive ?? false,
        ListenError = listenClient?.LastError ?? lastListenError,

        IsLead = broadcastHost?.IsLead ?? true,
        HostId = broadcastHost?.HostId ?? Guid.Empty,
        LeadHostId = broadcastHost?.LeadHostId ?? Guid.Empty,
        HostRoster = broadcastHost?.Roster.ToList() ?? new List<HostRosterEntryDto>(),
        MonitorVolume = broadcastHost?.MonitorVolume ?? 1f,
        LeadQueueA = broadcastHost?.LatestLeadQueues?.QueueA,
        LeadQueueB = broadcastHost?.LatestLeadQueues?.QueueB,
        LeadSpectrumBandsA = broadcastHost?.LatestLeadSpectrum?.BandsA,
        LeadSpectrumBandsB = broadcastHost?.LatestLeadSpectrum?.BandsB,

        SongRequestCooldownSecondsRemaining = listenClient != null ? (float)listenClient.SongRequestCooldownRemaining.TotalSeconds : 0f,
        SongRequestError = lastSongRequestError ?? listenClient?.LastSongRequestFeedback,

        ServerNotice = broadcastHost?.LatestServerNotice ?? listenClient?.LatestServerNotice,
        ServerNoticeReceivedUtc = broadcastHost?.ServerNoticeReceivedUtc ?? listenClient?.ServerNoticeReceivedUtc,
    };

    private DeckStatus BuildDeckStatus(DeckId id, DeckEngine deck, float peakLevel)
    {
        var status = new DeckStatus
        {
            HasTrack = deck.HasTrack,
            TrackTitle = deck.LoadedTrackTitle,
            IsPlaying = deck.IsPlaying,
            PositionSeconds = deck.Position.TotalSeconds,
            DurationSeconds = deck.Duration.TotalSeconds,
            CuePointSeconds = deck.CuePoint.TotalSeconds,
            Gain = deck.Gain,
            Trim = deck.Trim,
            LowGainDb = deck.Eq?.LowGainDb ?? 0f,
            MidGainDb = deck.Eq?.MidGainDb ?? 0f,
            HighGainDb = deck.Eq?.HighGainDb ?? 0f,
            FilterKnob = deck.Filter?.Knob ?? 0f,
            PeakLevel = peakLevel,
            AutoplayEnabled = deckQueues.IsAutoplayEnabled(id),
            Bpm = deck.LoadedTrackBpm,
            SyncEnabled = id == DeckId.A ? mixer.SyncEnabledA : mixer.SyncEnabledB,
            TempoRatio = deck.TempoRatio,
        };

        if (IsSpotifyDeckA(id))
        {
            status.Gain = mixer.SpotifyGain;
            status.Trim = mixer.SpotifyTrim;
            status.LowGainDb = mixer.SpotifyEq?.LowGainDb ?? 0f;
            status.MidGainDb = mixer.SpotifyEq?.MidGainDb ?? 0f;
            status.HighGainDb = mixer.SpotifyEq?.HighGainDb ?? 0f;
            status.FilterKnob = mixer.SpotifyFilter?.Knob ?? 0f;
            status.Bpm = null;        }
        else if (IsExternalInputDeckA(id))
        {
            status.Gain = mixer.ExternalInputGain;
            status.Trim = mixer.ExternalInputTrim;
            status.LowGainDb = mixer.ExternalInputEq?.LowGainDb ?? 0f;
            status.MidGainDb = mixer.ExternalInputEq?.MidGainDb ?? 0f;
            status.HighGainDb = mixer.ExternalInputEq?.HighGainDb ?? 0f;
            status.FilterKnob = mixer.ExternalInputFilter?.Knob ?? 0f;
            status.Bpm = null;        }
        else if (IsExternalInputDeckB(id))
        {
            status.Gain = mixer.ExternalInputGain2;
            status.Trim = mixer.ExternalInputTrim2;
            status.LowGainDb = mixer.ExternalInputEq2?.LowGainDb ?? 0f;
            status.MidGainDb = mixer.ExternalInputEq2?.MidGainDb ?? 0f;
            status.HighGainDb = mixer.ExternalInputEq2?.HighGainDb ?? 0f;
            status.FilterKnob = mixer.ExternalInputFilter2?.Knob ?? 0f;
            status.Bpm = null;
        }

        return status;
    }

    private PlaylistsSnapshotMessage BuildPlaylistsSnapshot() => new()
    {
        Playlists = playlists.Playlists.Select(p => new PlaylistDto
        {
            Name = p.Name,
            Tracks = p.Tracks.Select(ToTrackDto).ToList(),
        }).ToList(),
    };

    private DeckQueuesSnapshotMessage BuildDeckQueuesSnapshot() => new()
    {
        QueueA = new DeckQueueDto { Tracks = deckQueues.QueueA.Select(ToTrackDto).ToList() },
        QueueB = new DeckQueueDto { Tracks = deckQueues.QueueB.Select(ToTrackDto).ToList() },
    };

    private PendingSongRequestsSnapshotMessage BuildPendingSongRequestsSnapshot() => new()
    {
        Requests = pendingSongRequests.Select(r => new PendingSongRequestDto
        {
            RequestId = r.RequestId,
            RequesterName = r.RequesterName,
            FileName = r.FileName,
            DurationSeconds = r.DurationSeconds,
            ReceivedAtUtc = r.ReceivedAtUtc,
            IsFromCoHost = r.IsFromCoHost,
        }).ToList(),
    };

    private static TrackDto ToTrackDto(Track t) => new()
    {
        Title = t.Title,
        FilePath = t.FilePath,
        DurationSeconds = t.DurationSeconds,
        Gain = t.Gain,
        Bpm = t.Bpm,
        BeatGridOffsetSeconds = t.BeatGridOffsetSeconds,
    };

    private SoundPadsSnapshotMessage BuildSoundPadsSnapshot() => new()
    {
        Pads = soundPads.Pads.Select(p => new SoundPadDto
        {
            Label = p.Label,
            FilePath = p.FilePath,
            Looping = p.Looping,
            LoopIntervalSeconds = p.LoopIntervalSeconds,
            Volume = p.Volume,
        }).ToList(),
    };
}
