using System;
using NAudio.Wave;

namespace EchoMix.AudioHost.Audio.Effects;

/// Scrubs NaN/Infinity samples to silence right at a raw hardware capture boundary (Spotify's process
/// loopback, External Input's device capture) before they can reach EqSampleProvider or FilterSampleProvider
/// downstream.
public sealed class SanitizingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly string label;

    private long samplesProcessed;
    private long samplesSanitized;
    private DateTime lastLogUtc = DateTime.UtcNow;
    private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(30);

    public WaveFormat WaveFormat => source.WaveFormat;

    public SanitizingSampleProvider(ISampleProvider source, string label)
    {
        this.source = source;
        this.label = label;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = source.Read(buffer, offset, count);
        var caughtThisRead = 0;
        for (var n = 0; n < samplesRead; n++)
        {
            if (!float.IsFinite(buffer[offset + n]))
            {
                buffer[offset + n] = 0f;
                caughtThisRead++;
            }
        }

        samplesProcessed += samplesRead;
        samplesSanitized += caughtThisRead;

        if (samplesSanitized > 0 && DateTime.UtcNow - lastLogUtc >= LogInterval)
        {
            Console.WriteLine($"[EchoMix.AudioHost] SanitizingSampleProvider ({label}): caught {samplesSanitized} non-finite sample(s) out of {samplesProcessed} processed in the last ~{LogInterval.TotalSeconds:0}s.");
            samplesProcessed = 0;
            samplesSanitized = 0;
            lastLogUtc = DateTime.UtcNow;
        }

        return samplesRead;
    }
}
