using NAudio.Wave;

namespace EchoMix.AudioHost.Broadcast;

/// Passes audio through unchanged while feeding a copy to whichever BroadcastHostConnection is currently live
/// (null when not broadcasting, in which case this is a no-op passthrough).
public sealed class BroadcastTapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private float[] mixScratch = System.Array.Empty<float>();

    public BroadcastHostConnection? Broadcast { get; set; }

    /// Mixed in on top of source for whatever Broadcast receives - but NEVER for the buffer this returns,
    /// which is what actually reaches outputGate/WasapiOut and plays locally.
    public ISampleProvider? BroadcastOnlySource { get; set; }

    public WaveFormat WaveFormat => source.WaveFormat;

    public BroadcastTapSampleProvider(ISampleProvider source) => this.source = source;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);

        var extra = BroadcastOnlySource;
        if (extra == null)
        {
            Broadcast?.Feed(buffer, offset, read);
            return read;
        }

        if (mixScratch.Length < read)
            mixScratch = new float[read];

        var extraRead = extra.Read(mixScratch, 0, read);

        if (Broadcast == null)
            return read;
        for (var i = 0; i < extraRead; i++)
            mixScratch[i] += buffer[offset + i];
        for (var i = extraRead; i < read; i++)
            mixScratch[i] = buffer[offset + i];

        Broadcast.Feed(mixScratch, 0, read);
        return read;
    }
}
