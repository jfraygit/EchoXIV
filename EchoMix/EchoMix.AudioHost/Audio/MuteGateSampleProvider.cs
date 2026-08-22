using System;
using NAudio.Wave;

namespace EchoMix.AudioHost.Audio;

/// Silences output without pausing anything upstream - decks keep advancing in real time underneath, they
/// just don't get heard while muted.
public sealed class MuteGateSampleProvider : ISampleProvider
{
    private const float FadeSeconds = 0.12f;

    private readonly ISampleProvider source;
    private readonly float gainStep;
    private float currentGain = 1f;
    private float targetGain = 1f;

    public bool Muted
    {
        get => targetGain == 0f;
        set => targetGain = value ? 0f : 1f;
    }

    /// Peak absolute sample value of the last buffer actually handed to WASAPI, post- gate.
    public float LastPeak { get; private set; }

    public WaveFormat WaveFormat => source.WaveFormat;

    public MuteGateSampleProvider(ISampleProvider source)
    {
        this.source = source;
        gainStep = 1f / (FadeSeconds * source.WaveFormat.SampleRate);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        var channels = Math.Max(1, WaveFormat.Channels);
        var peak = 0f;

        for (var i = 0; i < read; i += channels)
        {
            if (currentGain < targetGain)
                currentGain = Math.Min(targetGain, currentGain + gainStep);
            else if (currentGain > targetGain)
                currentGain = Math.Max(targetGain, currentGain - gainStep);

            for (var c = 0; c < channels && i + c < read; c++)
            {
                var sample = buffer[offset + i + c] * currentGain;
                buffer[offset + i + c] = sample;

                var abs = Math.Abs(sample);
                if (abs > peak)
                    peak = abs;
            }
        }

        LastPeak = peak;
        return read;
    }
}
