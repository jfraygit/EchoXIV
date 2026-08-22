using System;
using NAudio.Wave;
using SoundTouch;

namespace EchoMix.AudioHost.Audio.Effects;

/// Pitch-preserving tempo control for Sync (see MixerEngine.SetDeckSync) - wraps SoundTouch.Net's
/// SoundTouchProcessor directly via its float push/pull model rather than taking the
/// SoundTouch.Net.NAudioSupport package (that wraps byte-based IWaveProvider/WaveStream; this codebase's
/// whole chain is ISampleProvider-based) - same reasoning as EqSampleProvider/FilterSampleProvider being
/// hand-rolled instead of an off-the-shelf effect.
public sealed class TimeStretchSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly SoundTouchProcessor processor;
    private readonly int channels;
    private float tempoRatio = 1f;
    private bool sourceExhausted;
    private float[] pullScratch = Array.Empty<float>();
    private float[] receiveScratch = Array.Empty<float>();

    public WaveFormat WaveFormat => source.WaveFormat;

    public TimeStretchSampleProvider(ISampleProvider source, int sampleRate)
    {
        this.source = source;
        channels = Math.Max(1, source.WaveFormat.Channels);
        processor = new SoundTouchProcessor
        {
            SampleRate = sampleRate,
            Channels = channels,
        };
    }

    /// 1.0 = unchanged.
    public float TempoRatio
    {
        get => tempoRatio;
        set
        {
            var clamped = Math.Clamp(value, 0.5f, 2f);
            if (clamped == tempoRatio)
                return;
            tempoRatio = clamped;
            if (tempoRatio == 1f)
                processor.Clear();
            processor.Tempo = tempoRatio;
        }
    }

    /// Discards any audio SoundTouch is still holding onto internally, without touching TempoRatio - called
    /// by DeckEngine right when a seek splice actually happens (JumpToCue / seek-bar drag), so a handful of
    /// already-stretched-but- unplayed pre-seek frames can't leak out after the jump.
    public void Reset()
    {
        processor.Clear();
        sourceExhausted = false;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (tempoRatio == 1f)
            return source.Read(buffer, offset, count);
        var framesNeeded = count / channels;
        var framesWritten = DrainInto(buffer, offset, framesNeeded);

        if (!sourceExhausted)
        {
            EnsurePullScratch(count);
            while (framesWritten < framesNeeded)
            {
                var pulled = source.Read(pullScratch, 0, count);
                if (pulled == 0)
                {
                    sourceExhausted = true;
                    processor.Flush();
                    framesWritten += DrainInto(buffer, offset + (framesWritten * channels), framesNeeded - framesWritten);
                    break;
                }

                processor.PutSamples(pullScratch.AsSpan(0, pulled), pulled / channels);
                framesWritten += DrainInto(buffer, offset + (framesWritten * channels), framesNeeded - framesWritten);
            }
        }

        var samplesWritten = framesWritten * channels;
        if (samplesWritten < count)
            Array.Clear(buffer, offset + samplesWritten, count - samplesWritten);
        return samplesWritten;    }

    private int DrainInto(float[] dest, int destOffset, int maxFrames)
    {
        if (maxFrames <= 0)
            return 0;
        EnsureReceiveScratch(maxFrames * channels);
        var got = processor.ReceiveSamples(receiveScratch.AsSpan(0, maxFrames * channels), maxFrames);
        if (got > 0)
            Array.Copy(receiveScratch, 0, dest, destOffset, got * channels);
        return got;
    }

    private void EnsurePullScratch(int minLength)
    {
        if (pullScratch.Length < minLength)
            pullScratch = new float[minLength];
    }

    private void EnsureReceiveScratch(int minLength)
    {
        if (receiveScratch.Length < minLength)
            receiveScratch = new float[minLength];
    }
}
