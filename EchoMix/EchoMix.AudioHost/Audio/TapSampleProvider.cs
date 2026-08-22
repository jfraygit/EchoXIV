using System;
using NAudio.Wave;

namespace EchoMix.AudioHost.Audio;

/// Passes audio through unchanged while feeding a copy to the analyzer, and tracking the peak absolute sample
/// value of the last buffer for the per-deck UI peak meter - tapped here (pre-crossfade) rather than post-mix
/// so each deck's meter reflects only its own signal.
public sealed class TapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly FftAnalyzer analyzer;

    public float Peak { get; private set; }

    public WaveFormat WaveFormat => source.WaveFormat;

    public TapSampleProvider(ISampleProvider source, FftAnalyzer analyzer)
    {
        this.source = source;
        this.analyzer = analyzer;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        analyzer.Feed(buffer, offset, read, WaveFormat.Channels);

        var peak = 0f;
        for (var i = 0; i < read; i++)
        {
            var abs = Math.Abs(buffer[offset + i]);
            if (abs > peak)
                peak = abs;
        }
        Peak = peak;

        return read;
    }
}
