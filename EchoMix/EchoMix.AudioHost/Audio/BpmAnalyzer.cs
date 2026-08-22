using System;
using NAudio.Wave;
using SoundTouch;

namespace EchoMix.AudioHost.Audio;

/// Bpm null means detection failed entirely - BeatGridOffsetSeconds can still be null even when Bpm isn't, if
/// no single detected beat was confident enough to anchor a grid on (see BpmAnalyzer.FindBeatGridOffset).
public readonly record struct BpmAnalysisResult(float? Bpm, float? BeatGridOffsetSeconds);

/// One-time BPM + beatgrid estimation for a track, built on SoundTouch.Net's own BpmDetect - the same
/// autocorrelation-based beat detector shipped with SoundTouch's `soundstretch` CLI tool, rather than a
/// hand-rolled algorithm, since it's already a dependency (see TimeStretchSampleProvider) and is far more
/// tested than anything worth writing from scratch here.
public static class BpmAnalyzer
{
    private const int ReadBufferFrames = 4096;

    private const float MinBeatConsiderSeconds = 0.5f;

    /// Returns the detected BPM/beatgrid, or a result with a null Bpm if the file couldn't be decoded or no
    /// confident tempo emerged at all (e.g.
    public static BpmAnalysisResult Analyze(string filePath)
    {
        try
        {
            using var reader = AudioReaderFactory.Open(filePath);
            var source = reader.ToSampleProvider();
            var channels = Math.Max(1, source.WaveFormat.Channels);
            var detector = new BpmDetect(channels, source.WaveFormat.SampleRate);

            var buffer = new float[ReadBufferFrames * channels];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                detector.InputSamples(buffer.AsSpan(0, read), read / channels);

            var bpm = detector.GetBpm();
            if (bpm <= 0f)
                return default;

            return new BpmAnalysisResult(bpm, FindBeatGridOffset(detector));
        }
        catch
        {
            return default;
        }
    }

    /// The earliest beat past the analysis window's own ramp-up becomes the grid anchor - every subsequent
    /// beat is assumed to fall at offset + n * (60 / bpm) (see DeckEngine.CurrentBeatPhase01).
    private static float? FindBeatGridOffset(BpmDetect detector)
    {
        var count = detector.GetBeats(Span<float>.Empty, Span<float>.Empty);
        if (count == 0)
            return null;

        var positions = new float[count];
        var strengths = new float[count];
        detector.GetBeats(positions, strengths);

        for (var i = 0; i < count; i++)
        {
            if (positions[i] >= MinBeatConsiderSeconds)
                return positions[i];
        }

        return null;
    }
}
