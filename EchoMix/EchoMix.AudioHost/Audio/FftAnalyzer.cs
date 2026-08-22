using System;
using NAudio.Dsp;

namespace EchoMix.AudioHost.Audio;

/// Keeps a rolling window of the mixed output and produces a log-spaced spectrum on demand.
public sealed class FftAnalyzer
{
    private const int FftLength = 2048;
    private readonly float[] sampleRing = new float[FftLength];
    private readonly object gate = new();
    private int ringPos;
    private bool ready;

    public void Feed(float[] buffer, int offset, int count, int channels)
    {
        if (count <= 0 || channels <= 0)
            return;

        lock (gate)
        {
            for (var i = 0; i + channels <= count; i += channels)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                    sum += buffer[offset + i + c];

                sampleRing[ringPos] = sum / channels;
                ringPos = (ringPos + 1) % FftLength;
            }

            ready = true;
        }
    }

    public float[] GetSpectrum(int bands)
    {
        var result = new float[bands];
        var work = new Complex[FftLength];

        lock (gate)
        {
            if (!ready)
                return result;

            for (var i = 0; i < FftLength; i++)
            {
                var idx = (ringPos + i) % FftLength;
                var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftLength - 1)));
                work[i].X = (float)(sampleRing[idx] * window);
                work[i].Y = 0f;
            }
        }

        FastFourierTransform.FFT(true, (int)Math.Log2(FftLength), work);

        var magnitudeCount = FftLength / 2;
        var magnitudes = new float[magnitudeCount];
        for (var i = 0; i < magnitudeCount; i++)
            magnitudes[i] = MathF.Sqrt((work[i].X * work[i].X) + (work[i].Y * work[i].Y));

        var maxLog = Math.Log10(magnitudeCount);
        for (var b = 0; b < bands; b++)
        {
            var loLog = maxLog * b / bands;
            var hiLog = maxLog * (b + 1) / bands;
            var lo = Math.Max(1, (int)Math.Pow(10, loLog));
            var hi = Math.Min(magnitudeCount, Math.Max(lo + 1, (int)Math.Pow(10, hiLog)));

            var peak = 0f;
            for (var i = lo; i < hi; i++)
                peak = Math.Max(peak, magnitudes[i]);

            result[b] = peak;
        }

        return result;
    }
}
