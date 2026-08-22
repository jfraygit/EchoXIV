using NAudio.Wave;

namespace EchoMix.AudioHost.Audio.Effects;

/// Applies gain with a short per-sample ramp toward the target instead of jumping instantly.
public sealed class SmoothedVolumeSampleProvider : ISampleProvider
{
    private const float SmoothingPerSample = 0.005f;

    private readonly ISampleProvider source;
    private float currentVolume;

    public WaveFormat WaveFormat => source.WaveFormat;

    /// `initialVolume` lets a caller start already-ramped-in instead of always starting from unity and
    /// smoothing toward whatever Volume is set to next - e.g. a cue preview starts at 0 so its very first
    /// buffer fades in from silence instead of popping in at full volume.
    public SmoothedVolumeSampleProvider(ISampleProvider source, float initialVolume = 1f)
    {
        this.source = source;
        currentVolume = initialVolume;
        Volume = initialVolume;
    }

    public float Volume { get; set; } = 1f;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);

        for (var i = 0; i < read; i++)
        {
            currentVolume += (Volume - currentVolume) * SmoothingPerSample;
            buffer[offset + i] *= currentVolume;
        }

        return read;
    }
}
