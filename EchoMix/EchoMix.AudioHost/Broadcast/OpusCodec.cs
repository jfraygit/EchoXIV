using System;
using Concentus;
using Concentus.Enums;

namespace EchoMix.AudioHost.Broadcast;

/// Thin wrapper around Concentus's Opus encoder, fixed to the broadcast format: 48kHz stereo (the closest
/// rate Opus's encoder accepts to the mixer's own 44.1kHz - see BroadcastHostConnection's resample stage) in
/// 20ms frames (960 samples/channel), a standard Opus frame size balancing latency against per-packet
/// overhead.
public sealed class OpusEncoderStream
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int FrameSamplesPerChannel = 960;    public const int FrameSamplesTotal = FrameSamplesPerChannel * Channels;

    private readonly IOpusEncoder encoder;

    private readonly byte[] outputBuffer = new byte[4000];

    public OpusEncoderStream(int bitrate = 128_000)
    {
        encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        encoder.Bitrate = bitrate;
    }

    /// `pcm` must be exactly FrameSamplesTotal interleaved float samples.
    public ReadOnlySpan<byte> Encode(ReadOnlySpan<float> pcm)
    {
        var length = encoder.Encode(pcm, FrameSamplesPerChannel, outputBuffer, outputBuffer.Length);
        return outputBuffer.AsSpan(0, length);
    }
}

/// The decode-side counterpart - same fixed 48kHz/stereo/20ms format, since both ends of a broadcast always
/// agree on it (the encoder's format, not something negotiated per-room).
public sealed class OpusDecoderStream
{
    private readonly IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(OpusEncoderStream.SampleRate, OpusEncoderStream.Channels);
    private readonly float[] outputBuffer = new float[OpusEncoderStream.FrameSamplesTotal];

    /// Returns a slice of this decoder's own reusable buffer, sized to the interleaved samples actually
    /// decoded - copy it out before the next call if the caller needs to hold onto it.
    public ReadOnlySpan<float> Decode(ReadOnlySpan<byte> packet)
    {
        var samplesPerChannel = decoder.Decode(packet, outputBuffer, OpusEncoderStream.FrameSamplesPerChannel, false);
        return outputBuffer.AsSpan(0, samplesPerChannel * OpusEncoderStream.Channels);
    }
}
