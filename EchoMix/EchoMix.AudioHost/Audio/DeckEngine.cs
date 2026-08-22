using System;
using System.IO;
using EchoMix.AudioHost.Audio.Effects;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EchoMix.AudioHost.Audio;

/// One DJ deck: loads a track, exposes play/pause/cue/seek, and chains EQ + filter + gain.
public sealed class DeckEngine : ISampleProvider, IDisposable
{
    public const int MasterSampleRate = 44100;

    private const int DeclickFadeSamples = 1800;

    private const int PlayFadeSamples = 1800;

    private readonly object gate = new();
    private WaveStream? reader;
    private ISampleProvider? chain;
    private TimeStretchSampleProvider? timeStretch;
    private SmoothedVolumeSampleProvider? trimStage;
    private SmoothedVolumeSampleProvider? gainStage;
    private TimeSpan cuePoint = TimeSpan.Zero;
    private float declickGain = 1f;
    private bool declickFadingOut;
    private bool seekPending;
    private TimeSpan pendingSeekTarget;
    private bool tempoChangePending;
    private float pendingTempoRatio = 1f;
    private float playGain;
    private volatile bool unloadPending;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(MasterSampleRate, 2);

    public string? LoadedTrackTitle { get; private set; }
    public string? LoadedTrackFilePath { get; private set; }

    /// The loaded track's BPM, from BpmAnalyzer or a DJ override (see SetLoadedTrackBpm) - null if unknown.
    public float? LoadedTrackBpm { get; private set; }

    /// The loaded track's beatgrid anchor (see Track.BeatGridOffsetSeconds) - null if unknown, in which case
    /// MixerEngine.SnapPhase skips phase alignment for this deck and Sync falls back to tempo-only.
    public float? LoadedTrackBeatGridOffset { get; private set; }

    /// The loaded track's current effective tempo - native BPM adjusted by whatever TempoRatio is presently
    /// applied, whether from Sync or a manual tempo-slider override.
    public float? EffectiveBpm => LoadedTrackBpm.HasValue ? LoadedTrackBpm.Value * TempoRatio : null;

    public bool IsPlaying { get; private set; }
    public bool HasTrack => chain != null;

    /// True once the currently loaded track has played all the way to the end on its own (as opposed to being
    /// manually paused mid-track) - lets a queue manager tell "finished, safe to load the next queued track"
    /// apart from "paused, leave it alone".
    public bool HasEnded { get; private set; }

    public EqSampleProvider? Eq { get; private set; }
    public FilterSampleProvider? Filter { get; private set; }

    public float Gain
    {
        get => gainStage?.Volume ?? 1f;
        set { if (gainStage != null) gainStage.Volume = Math.Clamp(value, 0f, 2f); }
    }

    /// Input trim - a separate gain stage from Gain (the channel fader), applied earlier in the chain (before
    /// EQ/filter) rather than at the end of it.
    public float Trim
    {
        get => trimStage?.Volume ?? 1f;
        set { if (trimStage != null) trimStage.Volume = Math.Clamp(value, 0f, 2f); }
    }

    /// 1.0 = unchanged.
    public float TempoRatio
    {
        get => timeStretch?.TempoRatio ?? 1f;
        set
        {
            if (timeStretch == null)
                return;

            var wasBypass = timeStretch.TempoRatio == 1f;
            var willBeBypass = Math.Clamp(value, 0.5f, 2f) == 1f;            if (wasBypass == willBeBypass)
            {
                timeStretch.TempoRatio = value;
                return;
            }

            lock (gate)
            {
                pendingTempoRatio = value;
                tempoChangePending = true;
                declickFadingOut = true;
            }
        }
    }

    /// Corrects the BPM of the currently-loaded track in place, without a full reload - used only by
    /// IpcServer's SetTrackBpm handler (and its background-analysis counterpart) when a value becomes
    /// known/corrected for a track that's already sitting on this deck.
    public void SetLoadedTrackBpm(float? bpm) => LoadedTrackBpm = bpm;

    /// Same idea as SetLoadedTrackBpm, for the beatgrid anchor - set by IpcServer once background BPM
    /// analysis completes for a track already loaded on this deck.
    public void SetLoadedTrackBeatGridOffset(float? beatGridOffsetSeconds) => LoadedTrackBeatGridOffset = beatGridOffsetSeconds;

    /// Fraction (0..1) of the way through the current beat, based on this track's own native BPM/beatgrid -
    /// both measured against the file's own timeline, which Position already tracks regardless of any
    /// tempo-stretching currently applied, so no correction for TempoRatio is needed here.
    public float? CurrentBeatPhase01
    {
        get
        {
            if (LoadedTrackBpm is not > 0f || LoadedTrackBeatGridOffset is not float offset)
                return null;

            var beatInterval = 60.0 / LoadedTrackBpm.Value;
            var phase = (Position.TotalSeconds - offset) % beatInterval;
            if (phase < 0)
                phase += beatInterval;
            return (float)(phase / beatInterval);
        }
    }

    public TimeSpan Duration => reader?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan CuePoint => cuePoint;

    public TimeSpan Position
    {
        get => reader?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (reader == null)
                return;
            var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value > reader.TotalTime ? reader.TotalTime : value;
            lock (gate)
                RequestSeek(clamped);
        }
    }

    /// Queues a seek to happen once the declick fade-out reaches silence (in Read) instead of jumping
    /// immediately - must be called with `gate` held.
    private void RequestSeek(TimeSpan target)
    {
        pendingSeekTarget = target;
        seekPending = true;
        declickFadingOut = true;
        HasEnded = false;
    }

    public void LoadTrack(string filePath, string? title = null, float? bpm = null, float? beatGridOffsetSeconds = null)
    {
        lock (gate)
        {
            reader?.Dispose();
            reader = AudioReaderFactory.Open(filePath);

            ISampleProvider src = reader.ToSampleProvider();
            if (src.WaveFormat.Channels == 1)
                src = new MonoToStereoSampleProvider(src);
            if (src.WaveFormat.SampleRate != MasterSampleRate)
                src = new WdlResamplingSampleProvider(src, MasterSampleRate);

            timeStretch = new TimeStretchSampleProvider(src, MasterSampleRate);

            trimStage = new SmoothedVolumeSampleProvider(timeStretch) { Volume = 1f };
            Eq = new EqSampleProvider(trimStage, MasterSampleRate);
            Filter = new FilterSampleProvider(Eq, MasterSampleRate);
            gainStage = new SmoothedVolumeSampleProvider(Filter) { Volume = 1f };

            chain = gainStage;
            LoadedTrackTitle = !string.IsNullOrEmpty(title) ? title : Path.GetFileNameWithoutExtension(filePath);
            LoadedTrackFilePath = filePath;
            LoadedTrackBpm = bpm;
            LoadedTrackBeatGridOffset = beatGridOffsetSeconds;
            IsPlaying = false;
            HasEnded = false;
            cuePoint = TimeSpan.Zero;
            declickGain = 1f;
            declickFadingOut = false;
            seekPending = false;
            tempoChangePending = false;
            playGain = 0f;
        }
    }

    /// Clears the deck back to its pre-load state (no title, no track) - used both for a user-requested
    /// unload and once a track finishes with nothing queued behind it, so the digital display reverts to "No
    /// track loaded" instead of leaving a title sitting there indefinitely.
    public void Unload()
    {
        lock (gate)
        {
            IsPlaying = false;
            unloadPending = true;
        }
    }

    private void FinishPendingUnload()
    {
        if (!unloadPending)
            return;

        lock (gate)
        {
            if (!unloadPending)
                return;

            reader?.Dispose();
            reader = null;
            chain = null;
            timeStretch = null;
            trimStage = null;
            Eq = null;
            Filter = null;
            gainStage = null;
            LoadedTrackTitle = null;
            LoadedTrackFilePath = null;
            LoadedTrackBpm = null;
            LoadedTrackBeatGridOffset = null;
            IsPlaying = false;
            HasEnded = false;
            unloadPending = false;
            cuePoint = TimeSpan.Zero;
            declickGain = 1f;
            declickFadingOut = false;
            seekPending = false;
            tempoChangePending = false;
            playGain = 0f;
        }
    }

    public void Play()
    {
        lock (gate)
        {
            IsPlaying = chain != null;
            HasEnded = false;
        }
    }

    public void Pause()
    {
        lock (gate)
            IsPlaying = false;
    }

    public void TogglePlay()
    {
        lock (gate)
        {
            IsPlaying = chain != null && !IsPlaying;
            if (IsPlaying)
                HasEnded = false;
        }
    }

    public void SetCue()
    {
        lock (gate)
        {
            if (reader != null)
                cuePoint = reader.CurrentTime;
        }
    }

    public void JumpToCue()
    {
        lock (gate)
        {
            if (reader != null)
                RequestSeek(cuePoint);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        ISampleProvider? c;
        bool playing;
        lock (gate)
        {
            c = chain;
            playing = IsPlaying;
        }

        var needsAudio = playing || playGain > 0f;
        if (c == null || !needsAudio)
        {
            Array.Clear(buffer, offset, count);
            playGain = 0f;
            FinishPendingUnload();
            return count;
        }

        var read = c.Read(buffer, offset, count);
        if (read < count)
        {
            Array.Clear(buffer, offset + read, count - read);
            lock (gate)
            {
                IsPlaying = false;
                HasEnded = true;
            }
            read = count;
        }

        ApplyDeclick(c, buffer, offset, read);
        ApplyPlayFade(buffer, offset, read, playing);

        if (unloadPending && playGain <= 0f)
            FinishPendingUnload();

        return read;
    }

    /// Fades this deck's output to silence, performs any pending seek once it gets there, then fades back in
    /// - see the DeclickFadeSamples comment for why.
    private void ApplyDeclick(ISampleProvider chainProvider, float[] buffer, int offset, int count)
    {
        var channels = Math.Max(1, WaveFormat.Channels);
        const float fadeStep = 1f / DeclickFadeSamples;

        for (var i = 0; i < count; i++)
        {
            if (i % channels == 0)
            {
                if (declickFadingOut)
                {
                    declickGain = MathF.Max(0f, declickGain - fadeStep);
                    if (declickGain <= 0f)
                    {
                        declickFadingOut = false;
                        lock (gate)
                        {
                            if (seekPending && reader != null)
                            {
                                reader.CurrentTime = pendingSeekTarget;
                                seekPending = false;
                            }

                            if (tempoChangePending && timeStretch != null)
                            {
                                timeStretch.TempoRatio = pendingTempoRatio;
                                tempoChangePending = false;
                            }
                        }

                        timeStretch?.Reset();

                        var remaining = count - i;
                        if (remaining > 0)
                            chainProvider.Read(buffer, offset + i, remaining);
                    }
                }
                else if (declickGain < 1f)
                {
                    declickGain = MathF.Min(1f, declickGain + fadeStep);
                }
            }

            buffer[offset + i] *= declickGain;
        }
    }

    /// Fades toward silence on pause and back up on play/resume, instead of the hard cut that otherwise
    /// happens the instant IsPlaying flips - runs once per stereo frame like ApplyDeclick.
    private void ApplyPlayFade(float[] buffer, int offset, int count, bool playing)
    {
        var channels = Math.Max(1, WaveFormat.Channels);
        const float fadeStep = 1f / PlayFadeSamples;
        var target = playing ? 1f : 0f;

        for (var i = 0; i < count; i++)
        {
            if (i % channels == 0)
            {
                if (playGain < target)
                    playGain = MathF.Min(target, playGain + fadeStep);
                else if (playGain > target)
                    playGain = MathF.Max(target, playGain - fadeStep);
            }

            buffer[offset + i] *= playGain;
        }
    }

    public void Dispose() => reader?.Dispose();
}
