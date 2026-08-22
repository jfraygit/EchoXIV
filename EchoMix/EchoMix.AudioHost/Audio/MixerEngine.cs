using System;
using System.Threading;
using System.Threading.Tasks;
using EchoMix.AudioHost.Audio.Effects;
using EchoMix.AudioHost.Audio.ExternalInput;
using EchoMix.AudioHost.Audio.Spotify;
using EchoMix.AudioHost.Broadcast;
using EchoMix.Shared;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EchoMix.AudioHost.Audio;

public sealed class MixerEngine : IDisposable
{
    public DeckEngine DeckA { get; } = new();
    public DeckEngine DeckB { get; } = new();

    public FftAnalyzer AnalyzerA { get; } = new();
    public FftAnalyzer AnalyzerB { get; } = new();

    private readonly TapSampleProvider tapA;
    private readonly TapSampleProvider tapB;
    private readonly CrossfadeSampleProvider crossfade;
    private readonly SourceSwitchSampleProvider modeSwitch;
    private readonly SoundEffectMixer soundEffects;
    private readonly VolumeSampleProvider masterVolume;
    private readonly BroadcastTapSampleProvider broadcastTap;
    private readonly SmoothedVolumeSampleProvider monitorMainVolume;
    private readonly MixingSampleProvider monitorMixer;
    private readonly MuteGateSampleProvider outputGate;
    private ISampleProvider? attachedMonitorSource;
    private SmoothedVolumeSampleProvider? attachedPreviewSource;
    private EventHandler<SampleProviderEventArgs>? previewEndedHandler;
    private WaveStream? previewReader;
    private WasapiOut? output;
    private MMDeviceEnumerator? deviceEnumerator;
    private MMDevice? device;
    private SimpleAudioVolume? sessionVolume;
    private Timer? sessionMuteDelayTimer;
    private ProcessLoopbackCapture? spotifyCapture;

    private SmoothedVolumeSampleProvider? spotifyTrimStage;
    private EqSampleProvider? spotifyEq;
    private FilterSampleProvider? spotifyFilterStage;
    private SmoothedVolumeSampleProvider? spotifyGainStage;

    private readonly object externalInputStopGate = new();

    private IExternalAudioSource? externalInputCapture;

    private SmoothedVolumeSampleProvider? externalInputTrimStage;
    private EqSampleProvider? externalInputEq;
    private FilterSampleProvider? externalInputFilterStage;
    private SmoothedVolumeSampleProvider? externalInputGainStage;

    private ExternalInputCapture? externalInputCapture2;
    private SmoothedVolumeSampleProvider? externalInputTrimStage2;
    private EqSampleProvider? externalInputEq2;
    private FilterSampleProvider? externalInputFilterStage2;
    private SmoothedVolumeSampleProvider? externalInputGainStage2;

    private const int WasapiBufferMs = 100;
    private const int FadeOutMs = 120;
    private const int SessionMuteDelayMs = WasapiBufferMs + FadeOutMs + 40;

    public MixerEngine()
    {
        tapA = new TapSampleProvider(DeckA, AnalyzerA);
        tapB = new TapSampleProvider(DeckB, AnalyzerB);
        crossfade = new CrossfadeSampleProvider(tapA, tapB);
        modeSwitch = new SourceSwitchSampleProvider(crossfade);
        soundEffects = new SoundEffectMixer(modeSwitch);
        masterVolume = new VolumeSampleProvider(soundEffects) { Volume = 1f };
        broadcastTap = new BroadcastTapSampleProvider(masterVolume);
        monitorMainVolume = new SmoothedVolumeSampleProvider(broadcastTap) { Volume = 1f };
        monitorMixer = new MixingSampleProvider(broadcastTap.WaveFormat) { ReadFully = true };
        monitorMixer.AddMixerInput(monitorMainVolume);
        outputGate = new MuteGateSampleProvider(monitorMixer);
    }

    public void PlaySoundEffect(string filePath, float volume = 1f) => soundEffects.Play(filePath, volume);

    public bool SyncEnabledA { get; private set; }
    public bool SyncEnabledB { get; private set; }

    private string? lastPhaseSnapSlaveFilePath;
    private string? lastPhaseSnapMasterFilePath;

    /// Mutually exclusive per deck, mirroring a mixer's solo buttons - enabling Sync on one deck turns the
    /// other's off first (avoids a circular "both decks chasing each other" state).
    public void SetDeckSync(DeckId deck, bool enabled)
    {
        if (deck == DeckId.A)
        {
            if (enabled && SyncEnabledB)
                SetDeckSync(DeckId.B, false);
            SyncEnabledA = enabled;
            if (!enabled)
                DeckA.TempoRatio = 1f;
        }
        else
        {
            if (enabled && SyncEnabledA)
                SetDeckSync(DeckId.A, false);
            SyncEnabledB = enabled;
            if (!enabled)
                DeckB.TempoRatio = 1f;
        }

        RecomputeSyncRatios();
    }

    /// Manually sets a deck's tempo directly - the BPM slider under its display.
    public void SetDeckTempo(DeckId deck, float ratio)
    {
        if (deck == DeckId.A)
        {
            SyncEnabledA = false;
            DeckA.TempoRatio = ratio;
        }
        else
        {
            SyncEnabledB = false;
            DeckB.TempoRatio = ratio;
        }
    }

    /// Called once per IpcServer status tick (~33ms) unconditionally - cheap (a couple of float comparisons;
    /// TimeStretchSampleProvider.TempoRatio's setter itself no-ops on an unchanged value) - rather than
    /// threaded through every LoadTrack call site (there are several: IpcServer's LoadTrack handler,
    /// AssignTrackToDeck, and DeckQueueManager.Pump's auto-advance).
    public void RecomputeSyncRatios()
    {
        if (SyncEnabledA)
        {
            DeckA.TempoRatio = ComputeSyncRatio(masterBpm: DeckB.EffectiveBpm, slaveBpm: DeckA.LoadedTrackBpm);
            MaybeSnapPhaseOnTrackChange(DeckA, DeckB);
        }
        else if (SyncEnabledB)
        {
            DeckB.TempoRatio = ComputeSyncRatio(masterBpm: DeckA.EffectiveBpm, slaveBpm: DeckB.LoadedTrackBpm);
            MaybeSnapPhaseOnTrackChange(DeckB, DeckA);
        }
        else
        {
            lastPhaseSnapSlaveFilePath = null;
            lastPhaseSnapMasterFilePath = null;
        }
    }

    /// masterBpm/slaveBpm null (unknown) yields a flat 1.0 bypass.
    private static float ComputeSyncRatio(float? masterBpm, float? slaveBpm)
    {
        if (masterBpm is not > 0f || slaveBpm is not > 0f)
            return 1f;

        var ratio = masterBpm.Value / slaveBpm.Value;
        while (ratio < 0.6f)
            ratio *= 2f;
        while (ratio > 1.6f)
            ratio /= 2f;
        return ratio;
    }

    /// Re-attempts SnapPhase whenever the slave's or master's loaded track differs from the pairing it last
    /// actually succeeded for - not every tick (~33ms), which would mean constant unwanted re-seeking.
    private void MaybeSnapPhaseOnTrackChange(DeckEngine slaveDeck, DeckEngine masterDeck)
    {
        if (slaveDeck.LoadedTrackFilePath == lastPhaseSnapSlaveFilePath && masterDeck.LoadedTrackFilePath == lastPhaseSnapMasterFilePath)
            return;

        if (SnapPhase(slaveDeck, masterDeck))
        {
            lastPhaseSnapSlaveFilePath = slaveDeck.LoadedTrackFilePath;
            lastPhaseSnapMasterFilePath = masterDeck.LoadedTrackFilePath;
        }
    }

    /// One-shot beat-phase alignment - nudges the slave deck's Position by a small (well under one beat)
    /// amount so its beatgrid lines up with the master's, using the same declick-masked seek an ordinary
    /// JumpToCue uses (see DeckEngine.Position/RequestSeek) - inaudible as a "jump," just a snap into
    /// alignment.
    private static bool SnapPhase(DeckEngine slaveDeck, DeckEngine masterDeck)
    {
        if (slaveDeck.LoadedTrackBpm is not > 0f || masterDeck.LoadedTrackBpm is not > 0f)
            return false;

        var rawRatio = masterDeck.LoadedTrackBpm.Value / slaveDeck.LoadedTrackBpm.Value;
        if (rawRatio < 0.6f || rawRatio > 1.6f)
            return false;

        if (slaveDeck.CurrentBeatPhase01 is not float slavePhase || masterDeck.CurrentBeatPhase01 is not float masterPhase)
            return false;

        var beatInterval = 60.0 / slaveDeck.LoadedTrackBpm.Value;
        var phaseDelta = masterPhase - slavePhase;        if (phaseDelta > 0.5f)
            phaseDelta -= 1f;
        else if (phaseDelta < -0.5f)
            phaseDelta += 1f;

        slaveDeck.Position += TimeSpan.FromSeconds(phaseDelta * beatInterval);
        return true;
    }

    public float PeakA => tapA.Peak;
    public float PeakB => tapB.Peak;

    /// The live broadcast connection to feed a copy of the mix to, or null when not broadcasting - tapped
    /// after master volume/before the mute gate (see BroadcastTapSampleProvider), so this is independent of
    /// the DJ's own local mute-when-unfocused preference.
    public BroadcastHostConnection? BroadcastConnection
    {
        get => broadcastTap.Broadcast;
        set => broadcastTap.Broadcast = value;
    }

    /// Mixes a co-host's incoming lead audio in for LOCAL MONITORING ONLY, so a DJ who isn't currently lead
    /// can still hear the show (and beat-match into their own turn) while their own decks keep running
    /// normally.
    public void AttachMonitorSource(ISampleProvider? source)
    {
        if (attachedMonitorSource != null)
            monitorMixer.RemoveMixerInput(attachedMonitorSource);

        attachedMonitorSource = source;

        if (source != null)
            monitorMixer.AddMixerInput(source);
    }

    private float previewDampenVolume = 0.5f;
    private float previewVolume = 0.6f;

    /// How much the main mix quiets down (for the DJ only) while a preview plays - see StartPreview.
    public float PreviewDampenVolume
    {
        get => previewDampenVolume;
        set
        {
            previewDampenVolume = Math.Clamp(value, 0f, 1f);
            if (attachedPreviewSource != null)
                monitorMainVolume.Volume = previewDampenVolume;
        }
    }

    /// How loud a cue preview itself plays locally - see PreviewDampenVolume.
    public float PreviewVolume
    {
        get => previewVolume;
        set
        {
            previewVolume = Math.Clamp(value, 0f, 1.5f);
            if (attachedPreviewSource != null)
                attachedPreviewSource.Volume = previewVolume;
        }
    }

    /// File path of the upcoming song currently being cue-previewed, or null when nothing's cueing - see
    /// StartPreview.
    public string? PreviewingFilePath { get; private set; }

    /// Starts (or switches to) a local-only "cue" preview of an upcoming song - a real mixer's headphone/cue
    /// channel: the DJ hears it mixed in at PreviewVolume on top of the current mix, which ducks to
    /// PreviewDampenVolume for their own monitoring only (see monitorMainVolume above) - listeners never hear
    /// either the duck or the preview itself, since both sit downstream of broadcastTap.
    public void StartPreview(string filePath)
    {
        StopPreview();

        WaveStream reader;
        try
        {
            reader = AudioReaderFactory.Open(filePath);
        }
        catch
        {
            return;
        }

        ISampleProvider src = reader.ToSampleProvider();
        if (src.WaveFormat.Channels == 1)
            src = new MonoToStereoSampleProvider(src);
        if (src.WaveFormat.SampleRate != DeckEngine.MasterSampleRate)
            src = new WdlResamplingSampleProvider(src, DeckEngine.MasterSampleRate);

        var previewStage = new SmoothedVolumeSampleProvider(src, initialVolume: 0f) { Volume = previewVolume };

        previewReader = reader;
        attachedPreviewSource = previewStage;
        PreviewingFilePath = filePath;
        monitorMainVolume.Volume = previewDampenVolume;

        previewEndedHandler = (_, e) =>
        {
            if (e.SampleProvider != previewStage)
                return;
            FinishPreview(alreadyRemovedFromMixer: true);
        };
        monitorMixer.MixerInputEnded += previewEndedHandler;
        monitorMixer.AddMixerInput(previewStage);
    }

    /// Stops whatever's currently cue-previewing (if anything) and restores the DJ's normal local monitoring
    /// volume - middle-clicking the same song again, per StartPreview.
    public void StopPreview() => FinishPreview(alreadyRemovedFromMixer: false);

    private void FinishPreview(bool alreadyRemovedFromMixer)
    {
        if (attachedPreviewSource == null)
            return;

        if (previewEndedHandler != null)
        {
            monitorMixer.MixerInputEnded -= previewEndedHandler;
            previewEndedHandler = null;
        }

        if (!alreadyRemovedFromMixer)
            monitorMixer.RemoveMixerInput(attachedPreviewSource);

        attachedPreviewSource = null;
        previewReader?.Dispose();
        previewReader = null;
        PreviewingFilePath = null;
        monitorMainVolume.Volume = 1f;
    }

    public float CrossfaderPosition
    {
        get => crossfade.Position;
        set => crossfade.Position = value;
    }

    public CrossfaderCurve? CrossfaderCurve
    {
        get => crossfade.Curve;
        set => crossfade.Curve = value;
    }

    public bool AutoDjEnabled { get; set; }
    public float AutoDjFadeSeconds { get; set; } = 6f;

    private bool autoDjFading;
    private float autoDjFadeElapsed;
    private float autoDjFadeStartPosition;
    private float autoDjFadeTargetPosition;

    /// Called once per IpcServer status tick (~33ms), same cadence RecomputeSyncRatios already uses - while
    /// AutoDjEnabled, watches whichever deck the crossfader currently favors (position &lt;= 0.5 = Deck A,
    /// matching CrossfadeSampleProvider's own 0=A/1=B convention) and starts fading over to the other deck
    /// once either (a) the favored deck's remaining time drops under AutoDjFadeSeconds while it's still
    /// playing, or (b) it's already stopped/ended before that window was ever caught (e.g.
    public void Tick(float deltaSeconds)
    {
        if (!AutoDjEnabled)
        {
            autoDjFading = false;
            return;
        }

        if (autoDjFading)
        {
            autoDjFadeElapsed += deltaSeconds;
            var t = AutoDjFadeSeconds <= 0f ? 1f : Math.Clamp(autoDjFadeElapsed / AutoDjFadeSeconds, 0f, 1f);
            crossfade.Position = autoDjFadeStartPosition + ((autoDjFadeTargetPosition - autoDjFadeStartPosition) * t);
            if (t >= 1f)
                autoDjFading = false;
            return;
        }

        var favoringA = crossfade.Position <= 0.5f;
        var favoredDeck = favoringA ? DeckA : DeckB;
        var otherDeck = favoringA ? DeckB : DeckA;

        if (!otherDeck.HasTrack)
            return;

        var remainingSeconds = (float)(favoredDeck.Duration - favoredDeck.Position).TotalSeconds;
        var nearingEnd = favoredDeck.HasTrack && favoredDeck.IsPlaying && remainingSeconds <= AutoDjFadeSeconds;
        var alreadyStopped = favoredDeck.HasTrack && !favoredDeck.IsPlaying && favoredDeck.HasEnded;
        if (!nearingEnd && !alreadyStopped)
            return;

        if (!otherDeck.IsPlaying)
            otherDeck.Play();

        autoDjFading = true;
        autoDjFadeElapsed = 0f;
        autoDjFadeStartPosition = crossfade.Position;
        autoDjFadeTargetPosition = favoringA ? 1f : 0f;
    }

    public void CancelAutoDjFade() => autoDjFading = false;

    public float MasterVolume
    {
        get => masterVolume.Volume;
        set => masterVolume.Volume = Math.Clamp(value, 0f, 2f);
    }

    /// Purely a user preference (e.g.
    public bool OutputMuted
    {
        get => outputGate.Muted;
        set
        {
            outputGate.Muted = value;
            EnsureSessionVolumeControl();

            sessionMuteDelayTimer?.Dispose();
            sessionMuteDelayTimer = null;

            if (value)
            {
                sessionMuteDelayTimer = new Timer(
                    _ => { if (sessionVolume != null) sessionVolume.Mute = true; },
                    null, SessionMuteDelayMs, Timeout.Infinite);
            }
            else if (sessionVolume != null)
            {
                sessionVolume.Mute = false;
            }
        }
    }

    public float OutputPeak => outputGate.LastPeak;

    public bool IsSpotifyModeActive => modeSwitch.UseSecondary;

    /// True whenever Spotify Mode isn't active at all - only meaningful (and only ever false) while it is,
    /// once the capture loop has stopped on its own (e.g.
    public bool IsSpotifyCaptureAlive => spotifyCapture?.IsRunning ?? true;

    public string? SpotifyModeError { get; private set; }

    /// Deck A's Gain/Trim/EQ/Filter dials control these while Spotify Mode is active, same shape as
    /// DeckEngine's own equivalents so IpcServer can report whichever is live through the same DeckStatus
    /// fields without the UI needing to know which mode it's in.
    public float SpotifyGain
    {
        get => spotifyGainStage?.Volume ?? 1f;
        set { if (spotifyGainStage != null) spotifyGainStage.Volume = Math.Clamp(value, 0f, 2f); }
    }

    public float SpotifyTrim
    {
        get => spotifyTrimStage?.Volume ?? 1f;
        set { if (spotifyTrimStage != null) spotifyTrimStage.Volume = Math.Clamp(value, 0f, 2f); }
    }

    public EqSampleProvider? SpotifyEq => spotifyEq;
    public FilterSampleProvider? SpotifyFilter => spotifyFilterStage;

    /// Replaces the deck mix with Spotify's own audio, captured directly from its process (not the whole
    /// system output) via ProcessLoopbackCapture - everything downstream (sound pads, master volume, the
    /// broadcast tap) stays exactly as it is, so Spotify Mode broadcasts and locally monitors the same way
    /// the decks normally do.
    public async Task<bool> StartSpotifyModeAsync()
    {
        if (modeSwitch.UseSecondary)
            return true;

        if (externalInputCapture != null)
        {
            SpotifyModeError = "Can't use Spotify Mode while External Input Mode is active.";
            return false;
        }

        if (!ProcessLoopbackCapture.TryFindProcessId("Spotify", out var pid, out var findError))
        {
            SpotifyModeError = findError;
            Console.WriteLine($"[EchoMix.AudioHost] Spotify Mode failed to start: {findError}");
            return false;
        }

        var capture = new ProcessLoopbackCapture(pid, "Spotify");
        var (ok, startError) = await capture.StartAsync();
        if (!ok)
        {
            capture.Dispose();
            SpotifyModeError = startError;
            Console.WriteLine($"[EchoMix.AudioHost] Spotify Mode failed to start: {startError}");
            return false;
        }

        Console.WriteLine($"[EchoMix.AudioHost] Spotify Mode started (capturing process {pid}, {capture.WaveFormat}).");

        DeckA.Pause();
        DeckB.Pause();

        spotifyCapture = capture;

        ISampleProvider spotifySource = new SanitizingSampleProvider(capture.WaveProvider.ToSampleProvider(), "Spotify");
        spotifyTrimStage = new SmoothedVolumeSampleProvider(spotifySource) { Volume = 1f };
        spotifyEq = new EqSampleProvider(spotifyTrimStage, DeckEngine.MasterSampleRate);
        spotifyFilterStage = new FilterSampleProvider(spotifyEq, DeckEngine.MasterSampleRate);
        spotifyGainStage = new SmoothedVolumeSampleProvider(spotifyFilterStage) { Volume = 1f };

        modeSwitch.Secondary = new TapSampleProvider(spotifyGainStage, AnalyzerA);
        modeSwitch.UseSecondary = true;
        SpotifyModeError = null;
        return true;
    }

    /// Also called from IpcServer's shutdown paths (plugin disable, game close, idle timeout) - not just the
    /// DJ toggling the button off - so the capture always gets torn down cleanly instead of being abandoned
    /// mid-process.
    public void StopSpotifyMode()
    {
        if (!modeSwitch.UseSecondary)
            return;

        Console.WriteLine("[EchoMix.AudioHost] Spotify Mode stopped.");
        modeSwitch.UseSecondary = false;
        modeSwitch.Secondary = null;
        spotifyCapture?.Dispose();
        spotifyCapture = null;
        spotifyTrimStage = null;
        spotifyEq = null;
        spotifyFilterStage = null;
        spotifyGainStage = null;
        SpotifyModeError = null;
    }

    public bool IsExternalInputModeActive => externalInputCapture != null;

    /// True only when a second device is actually capturing alongside the first - see
    /// StartExternalInputMode's own doc comment on why this exists.
    public bool IsExternalInput2Active => externalInputCapture2 != null;

    /// True whenever External Input Mode isn't active at all - only meaningful (and only ever false) while it
    /// is, once either capture has genuinely given up (e.g.
    public bool IsExternalInputCaptureAlive =>
        (externalInputCapture?.IsAlive ?? true) && (externalInputCapture2?.IsAlive ?? true);

    public string? ExternalInputModeError { get; private set; }
    public string? ExternalInputDeviceName => externalInputCapture?.SourceName;
    public string? ExternalInputDeviceName2 => externalInputCapture2?.DeviceName;

    /// Deck A's Gain/Trim/EQ/Filter dials control these while External Input Mode is active, same shape as
    /// SpotifyGain/SpotifyTrim/etc.
    public float ExternalInputGain
    {
        get => externalInputGainStage?.Volume ?? 1f;
        set { if (externalInputGainStage != null) externalInputGainStage.Volume = Math.Clamp(value, 0f, 2f); }
    }

    public float ExternalInputTrim
    {
        get => externalInputTrimStage?.Volume ?? 1f;
        set { if (externalInputTrimStage != null) externalInputTrimStage.Volume = Math.Clamp(value, 0f, 2f); }
    }

    public EqSampleProvider? ExternalInputEq => externalInputEq;
    public FilterSampleProvider? ExternalInputFilter => externalInputFilterStage;

    /// Deck B's own Gain/Trim/EQ/Filter dials control these while a second input is active - mirrors
    /// ExternalInputGain/Trim/Eq/Filter above exactly, just for the second device.
    public float ExternalInputGain2
    {
        get => externalInputGainStage2?.Volume ?? 1f;
        set { if (externalInputGainStage2 != null) externalInputGainStage2.Volume = Math.Clamp(value, 0f, 2f); }
    }

    public float ExternalInputTrim2
    {
        get => externalInputTrimStage2?.Volume ?? 1f;
        set { if (externalInputTrimStage2 != null) externalInputTrimStage2.Volume = Math.Clamp(value, 0f, 2f); }
    }

    public EqSampleProvider? ExternalInputEq2 => externalInputEq2;
    public FilterSampleProvider? ExternalInputFilter2 => externalInputFilterStage2;

    /// Broadcasts a DJ-selected Windows recording device's audio instead of the decks - unlike Spotify Mode,
    /// this does NOT go through modeSwitch, so it never reaches the DJ's own local output (see
    /// BroadcastTapSampleProvider.BroadcastOnlySource's doc comment for why: the DJ is already monitoring
    /// this input directly through their own external hardware, so echoing it back locally too would mean
    /// hearing their own mix twice).
    public async Task<bool> StartExternalInputModeAsync(string? deviceId, string? processName, string? deviceId2 = null)
    {
        if (externalInputCapture != null)
            return true;

        if (modeSwitch.UseSecondary)
        {
            ExternalInputModeError = "Can't use External Input Mode while Spotify Mode is active.";
            return false;
        }

        MMDevice? device2 = null;
        if (!string.IsNullOrEmpty(deviceId2))
        {
            device2 = AudioInputDevices.FindById(deviceId2);
            if (device2 == null)
            {
                ExternalInputModeError = "Selected second input device wasn't found - it may have been unplugged.";
                return false;
            }
        }

        IExternalAudioSource capture;
        if (!string.IsNullOrEmpty(processName))
        {
            if (!ProcessLoopbackCapture.TryFindProcessId(processName, out var pid, out var findError))
            {
                device2?.Dispose();
                ExternalInputModeError = findError;
                return false;
            }

            var processCapture = new ProcessLoopbackCapture(pid, processName);
            var (ok, startError) = await processCapture.StartAsync();
            if (!ok)
            {
                processCapture.Dispose();
                device2?.Dispose();
                ExternalInputModeError = startError;
                return false;
            }

            capture = processCapture;
        }
        else
        {
            var device = AudioInputDevices.FindById(deviceId ?? string.Empty);
            if (device == null)
            {
                device2?.Dispose();
                ExternalInputModeError = "Selected input device wasn't found - it may have been unplugged.";
                return false;
            }

            try
            {
                var deviceCapture = new ExternalInputCapture(device);
                deviceCapture.Start();
                capture = deviceCapture;
            }
            catch (Exception ex)
            {
                device.Dispose();
                device2?.Dispose();
                ExternalInputModeError = $"Couldn't start capturing that device: {ex.Message}";
                return false;
            }
        }

        ExternalInputCapture? capture2 = null;
        if (device2 != null)
        {
            try
            {
                capture2 = new ExternalInputCapture(device2);
                capture2.Start();
            }
            catch (Exception ex)
            {
                capture.Dispose();
                device2.Dispose();
                ExternalInputModeError = $"Couldn't start capturing the second device: {ex.Message}";
                return false;
            }
        }

        DeckA.Pause();
        DeckB.Pause();

        externalInputCapture = capture;
        externalInputCapture2 = capture2;

        var gainStage = BuildExternalInputChain(capture, "External Input",
            trim => externalInputTrimStage = trim, eq => externalInputEq = eq, filter => externalInputFilterStage = filter);
        externalInputGainStage = gainStage;

        ISampleProvider broadcastSource = new TapSampleProvider(gainStage, AnalyzerA);

        if (capture2 != null)
        {
            var gainStage2 = BuildExternalInputChain(capture2, "External Input 2",
                trim => externalInputTrimStage2 = trim, eq => externalInputEq2 = eq, filter => externalInputFilterStage2 = filter);
            externalInputGainStage2 = gainStage2;

            var mixer = new MixingSampleProvider(broadcastSource.WaveFormat) { ReadFully = true };
            mixer.AddMixerInput(broadcastSource);
            mixer.AddMixerInput(new TapSampleProvider(gainStage2, AnalyzerB));
            broadcastSource = mixer;
        }

        broadcastTap.BroadcastOnlySource = broadcastSource;
        ExternalInputModeError = null;

        Console.WriteLine($"[EchoMix.AudioHost] External Input Mode started ({capture.SourceName}{(capture2 != null ? $" + {capture2.SourceName}" : "")}).");
        return true;
    }

    /// Builds the Trim -> Eq -> Filter -> Gain chain StartExternalInputMode needs for one capture device -
    /// factored out so the (identical) second-device chain doesn't duplicate it.
    private static SmoothedVolumeSampleProvider BuildExternalInputChain(IExternalAudioSource capture, string sanitizeLabel,
        Action<SmoothedVolumeSampleProvider> setTrim, Action<EqSampleProvider> setEq, Action<FilterSampleProvider> setFilter)
    {
        ISampleProvider src = new SanitizingSampleProvider(capture.WaveProvider.ToSampleProvider(), sanitizeLabel);
        if (src.WaveFormat.Channels == 1)
            src = new MonoToStereoSampleProvider(src);
        if (src.WaveFormat.SampleRate != DeckEngine.MasterSampleRate)
            src = new WdlResamplingSampleProvider(src, DeckEngine.MasterSampleRate);

        var trimStage = new SmoothedVolumeSampleProvider(src) { Volume = 1f };
        var eq = new EqSampleProvider(trimStage, DeckEngine.MasterSampleRate);
        var filterStage = new FilterSampleProvider(eq, DeckEngine.MasterSampleRate);
        var gainStage = new SmoothedVolumeSampleProvider(filterStage) { Volume = 1f };

        setTrim(trimStage);
        setEq(eq);
        setFilter(filterStage);
        return gainStage;
    }

    /// Also called from IpcServer's shutdown paths, same reasoning as StopSpotifyMode.
    public void StopExternalInputMode()
    {
        IExternalAudioSource capture;
        ExternalInputCapture? capture2;
        lock (externalInputStopGate)
        {
            if (externalInputCapture == null)
                return;

            capture = externalInputCapture;
            capture2 = externalInputCapture2;
            externalInputCapture = null;
            externalInputCapture2 = null;
        }

        Console.WriteLine($"[EchoMix.AudioHost] External Input Mode stopping ({capture.SourceName}{(capture2 != null ? $" + {capture2.SourceName}" : "")}).");

        broadcastTap.BroadcastOnlySource = null;
        capture.Dispose();
        externalInputTrimStage = null;
        externalInputEq = null;
        externalInputFilterStage = null;
        externalInputGainStage = null;

        capture2?.Dispose();
        externalInputTrimStage2 = null;
        externalInputEq2 = null;
        externalInputFilterStage2 = null;
        externalInputGainStage2 = null;

        ExternalInputModeError = null;
        Console.WriteLine("[EchoMix.AudioHost] External Input Mode stopped.");
    }

    public void Start()
    {
        if (output != null)
            return;

        output = new WasapiOut(AudioClientShareMode.Shared, WasapiBufferMs);
        output.Init(outputGate);
        output.Play();

        EnsureSessionVolumeControl();
    }

    /// Stops local deck playback without tearing down the whole engine - used while entering Listener mode
    /// (broadcasting and listening are mutually exclusive per AudioHost instance, and the listener's own
    /// BroadcastListenClient owns its own WasapiOut instead).
    public void Stop()
    {
        output?.Stop();
        output?.Dispose();
        output = null;
    }

    private void EnsureSessionVolumeControl()
    {
        if (sessionVolume != null)
            return;

        try
        {
            deviceEnumerator ??= new MMDeviceEnumerator();
            device ??= deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            var sessions = device.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.GetProcessID == (uint)Environment.ProcessId)
                {
                    sessionVolume = session.SimpleAudioVolume;
                    break;
                }
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        sessionMuteDelayTimer?.Dispose();
        StopPreview();
        spotifyCapture?.Dispose();
        externalInputCapture?.Dispose();
        externalInputCapture2?.Dispose();
        output?.Stop();
        output?.Dispose();
        device?.Dispose();
        DeckA.Dispose();
        DeckB.Dispose();
    }
}
