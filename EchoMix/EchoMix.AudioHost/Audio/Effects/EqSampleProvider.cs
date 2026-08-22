using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace EchoMix.AudioHost.Audio.Effects;

/// 3-band EQ built from peaking filters, since BiQuadFilter only exposes an in-place setter for peaking EQ
/// (not shelving) without resetting filter state.
public sealed class EqSampleProvider : ISampleProvider
{
    private const float LowFreq = 150f;
    private const float MidFreq = 1000f;
    private const float HighFreq = 6000f;
    private const float Bandwidth = 1.2f;

    private const float DenormalPreventionOffset = 1e-7f;
    private bool denormalToggle;

    private readonly ISampleProvider source;
    private readonly int sampleRate;
    private readonly BiQuadFilter[] low = new BiQuadFilter[2];
    private readonly BiQuadFilter[] mid = new BiQuadFilter[2];
    private readonly BiQuadFilter[] high = new BiQuadFilter[2];

    private float lowGainDb;
    private float midGainDb;
    private float highGainDb;

    public WaveFormat WaveFormat => source.WaveFormat;

    public EqSampleProvider(ISampleProvider source, int sampleRate)
    {
        this.source = source;
        this.sampleRate = sampleRate;
        for (var ch = 0; ch < 2; ch++)
        {
            low[ch] = BiQuadFilter.PeakingEQ(sampleRate, LowFreq, Bandwidth, 0f);
            mid[ch] = BiQuadFilter.PeakingEQ(sampleRate, MidFreq, Bandwidth, 0f);
            high[ch] = BiQuadFilter.PeakingEQ(sampleRate, HighFreq, Bandwidth, 0f);
        }
    }

    public float LowGainDb
    {
        get => lowGainDb;
        set { lowGainDb = Clamp(value); Apply(low, LowFreq, lowGainDb); }
    }

    public float MidGainDb
    {
        get => midGainDb;
        set { midGainDb = Clamp(value); Apply(mid, MidFreq, midGainDb); }
    }

    public float HighGainDb
    {
        get => highGainDb;
        set { highGainDb = Clamp(value); Apply(high, HighFreq, highGainDb); }
    }

    private static float Clamp(float db) => Math.Clamp(db, -15f, 15f);

    private void Apply(BiQuadFilter[] band, float freq, float gainDb)
    {
        for (var ch = 0; ch < 2; ch++)
            band[ch].SetPeakingEq(sampleRate, freq, Bandwidth, gainDb);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = source.Read(buffer, offset, count);
        var channels = Math.Max(1, WaveFormat.Channels);

        for (var n = 0; n < samplesRead; n++)
        {
            var ch = n % channels;
            denormalToggle = !denormalToggle;
            var sample = buffer[offset + n] + (denormalToggle ? DenormalPreventionOffset : -DenormalPreventionOffset);
            sample = low[ch].Transform(sample);
            sample = mid[ch].Transform(sample);
            sample = high[ch].Transform(sample);
            buffer[offset + n] = sample;
        }

        return samplesRead;
    }
}
