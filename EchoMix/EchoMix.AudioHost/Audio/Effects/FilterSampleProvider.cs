using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace EchoMix.AudioHost.Audio.Effects;

/// Single-knob sweep filter, the classic DJ mixer "filter" control: negative = low-pass sweeping closed,
/// positive = high-pass sweeping closed, 0 = wide open (both ends land near-transparent at the center, not a
/// true bypass).
public sealed class FilterSampleProvider : ISampleProvider
{
    private const float SmoothingPerSample = 0.006f;
    private const float DenormalPreventionOffset = 1e-7f;
    private bool denormalToggle;

    private const float Resonance = 0.7071f;

    private readonly ISampleProvider source;
    private readonly int sampleRate;
    private readonly BiQuadFilter[] lowFilters = new BiQuadFilter[2];
    private readonly BiQuadFilter[] highFilters = new BiQuadFilter[2];
    private float targetKnob;
    private float smoothedKnob;
    private float lowMix = 0.5f;

    public WaveFormat WaveFormat => source.WaveFormat;

    public FilterSampleProvider(ISampleProvider source, int sampleRate)
    {
        this.source = source;
        this.sampleRate = sampleRate;
        for (var ch = 0; ch < 2; ch++)
        {
            lowFilters[ch] = BiQuadFilter.LowPassFilter(sampleRate, 18000f, Resonance);
            highFilters[ch] = BiQuadFilter.HighPassFilter(sampleRate, 60f, Resonance);
        }
    }

    public float Knob
    {
        get => targetKnob;
        set => targetKnob = Math.Clamp(value, -1f, 1f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = source.Read(buffer, offset, count);
        var channels = Math.Max(1, WaveFormat.Channels);

        for (var n = 0; n < samplesRead; n++)
        {
            var ch = n % channels;

            if (ch == 0)
            {
                smoothedKnob += (targetKnob - smoothedKnob) * SmoothingPerSample;
                ApplyKnob(smoothedKnob);
            }

            denormalToggle = !denormalToggle;
            var dry = buffer[offset + n] + (denormalToggle ? DenormalPreventionOffset : -DenormalPreventionOffset);
            var low = lowFilters[ch].Transform(dry);
            var high = highFilters[ch].Transform(dry);
            buffer[offset + n] = (low * lowMix) + (high * (1f - lowMix));
        }

        return samplesRead;
    }

    private void ApplyKnob(float knob)
    {
        var lowT = Math.Clamp(-knob, 0f, 1f);
        var lowCutoff = 18000f - (lowT * 17600f);
        for (var ch = 0; ch < 2; ch++)
            lowFilters[ch].SetLowPassFilter(sampleRate, lowCutoff, Resonance);

        var highT = Math.Clamp(knob, 0f, 1f);
        var highCutoff = 60f + (highT * 7940f);
        for (var ch = 0; ch < 2; ch++)
            highFilters[ch].SetHighPassFilter(sampleRate, highCutoff, Resonance);

        lowMix = Math.Clamp((1f - knob) / 2f, 0f, 1f);
    }
}
