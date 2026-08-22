using System;
using System.Collections.Concurrent;
using NAudio.Wave;

namespace EchoMix.AudioHost.Broadcast;

/// Bridges a producer thread (the real-time mixer thread while broadcasting, or the network receive loop
/// while listening) to a consumer that pulls samples on its own schedule via the ISampleProvider.Read this
/// implements - a resampler, an encoder loop, or WasapiOut.
public sealed class QueueSampleProvider : ISampleProvider
{
    private const int MaxQueuedSamples = 48000 * 2 * 5;

    private const float NearFullThreshold = 0.9f;
    private static readonly TimeSpan OverflowRecoveryWindow = TimeSpan.FromSeconds(2);
    private DateTime? nearFullSinceUtc;

    private static readonly TimeSpan UnderrunRecoveryWindow = TimeSpan.FromSeconds(2);
    private DateTime? emptySinceUtc;

    private readonly ConcurrentQueue<float> queue = new();
    private readonly string name;
    private readonly int prefillSamples;
    private volatile bool hasPrefilled;

    public WaveFormat WaveFormat { get; }

    /// `name` identifies this instance in its own log lines - three different queues (broadcast feed, co-host
    /// monitor, listener playback) all used to log the exact same wording, so a report's log couldn't say
    /// which one was actually in trouble without reasoning it out from everything else in the log.
    public QueueSampleProvider(string name, WaveFormat waveFormat, int prefillSamples = 0)
    {
        this.name = name;
        WaveFormat = waveFormat;
        this.prefillSamples = prefillSamples;
    }

    public void Enqueue(float[] buffer, int offset, int count)
    {
        for (var i = 0; i < count; i++)
            queue.Enqueue(buffer[offset + i]);

        while (queue.Count > MaxQueuedSamples && queue.TryDequeue(out _))
        {
        }

        CheckOverflowHealth();
    }

    private void CheckOverflowHealth()
    {
        if (queue.Count < MaxQueuedSamples * NearFullThreshold)
        {
            nearFullSinceUtc = null;
            return;
        }

        nearFullSinceUtc ??= DateTime.UtcNow;
        if (DateTime.UtcNow - nearFullSinceUtc.Value < OverflowRecoveryWindow)
            return;

        Console.WriteLine($"[EchoMix.AudioHost] The {name} queue looked stuck (chronic overflow) - resetting it.");
        while (queue.TryDequeue(out _)) { }
        hasPrefilled = false;
        nearFullSinceUtc = null;
    }

    /// Pads with silence on underrun (e.g.
    public int Read(float[] buffer, int offset, int count)
    {
        if (!hasPrefilled)
        {
            if (queue.Count < prefillSamples)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            hasPrefilled = true;
        }

        var read = 0;
        while (read < count && queue.TryDequeue(out var sample))
        {
            buffer[offset + read] = sample;
            read++;
        }

        if (read < count)
            Array.Clear(buffer, offset + read, count - read);

        CheckUnderrunHealth();

        return count;
    }

    private void CheckUnderrunHealth()
    {
        if (prefillSamples <= 0 || queue.Count > 0)
        {
            emptySinceUtc = null;
            return;
        }

        emptySinceUtc ??= DateTime.UtcNow;
        if (DateTime.UtcNow - emptySinceUtc.Value < UnderrunRecoveryWindow)
            return;

        Console.WriteLine($"[EchoMix.AudioHost] The {name} queue's producer looked stalled - re-arming its prefill cushion.");
        hasPrefilled = false;
        emptySinceUtc = null;
    }
}
