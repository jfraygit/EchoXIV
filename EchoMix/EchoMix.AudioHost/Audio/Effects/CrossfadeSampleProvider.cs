using System;
using EchoMix.Shared;
using NAudio.Utils;
using NAudio.Wave;

namespace EchoMix.AudioHost.Audio.Effects;

/// Blends two decks according to whichever CrossfaderCurve is selected - defaults to equal-power (constant
/// perceived loudness through the sweep, no dip in the middle) since that's what this always did before curve
/// selection existed as a user-facing choice - see Curve's own doc comment for why null resolves to the same
/// math as CrossfaderCurve.Power rather than a separate "no curve" formula.
public sealed class CrossfadeSampleProvider : ISampleProvider
{
    private const float SmoothingPerSample = 0.005f;

    private const float CutExponent = 8f;

    private readonly ISampleProvider a;
    private readonly ISampleProvider b;
    private float position = 0.5f;
    private CrossfaderCurve? curve;
    private float targetGainA = 1f;
    private float targetGainB = 1f;
    private float currentGainA = 1f;
    private float currentGainB = 1f;
    private float[] bufferA = Array.Empty<float>();
    private float[] bufferB = Array.Empty<float>();

    public WaveFormat WaveFormat { get; }

    public CrossfadeSampleProvider(ISampleProvider a, ISampleProvider b)
    {
        if (!a.WaveFormat.Equals(b.WaveFormat))
            throw new ArgumentException("Both decks must share the same wave format.");

        this.a = a;
        this.b = b;
        WaveFormat = a.WaveFormat;
        RecomputeTargetGains();
        currentGainA = targetGainA;
        currentGainB = targetGainB;
    }

    public float Position
    {
        get => position;
        set
        {
            position = Math.Clamp(value, 0f, 1f);
            RecomputeTargetGains();
        }
    }

    /// Null (no curve button toggled on in the UI) is treated identically to CrossfaderCurve.Power - see this
    /// class's own doc comment.
    public CrossfaderCurve? Curve
    {
        get => curve;
        set
        {
            curve = value;
            RecomputeTargetGains();
        }
    }

    private void RecomputeTargetGains()
    {
        switch (curve ?? CrossfaderCurve.Power)
        {
            case CrossfaderCurve.Linear:
                targetGainA = 1f - position;
                targetGainB = position;
                break;
            case CrossfaderCurve.Cut:
                targetGainA = MathF.Pow(1f - position, CutExponent);
                targetGainB = MathF.Pow(position, CutExponent);
                break;
            default:
                var angle = position * Math.PI / 2.0;
                targetGainA = (float)Math.Cos(angle);
                targetGainB = (float)Math.Sin(angle);
                break;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        bufferA = BufferHelpers.Ensure(bufferA, count);
        bufferB = BufferHelpers.Ensure(bufferB, count);

        var readA = a.Read(bufferA, 0, count);
        var readB = b.Read(bufferB, 0, count);
        var n = Math.Max(readA, readB);

        for (var i = 0; i < n; i++)
        {
            currentGainA += (targetGainA - currentGainA) * SmoothingPerSample;
            currentGainB += (targetGainB - currentGainB) * SmoothingPerSample;

            var sa = i < readA ? bufferA[i] : 0f;
            var sb = i < readB ? bufferB[i] : 0f;
            buffer[offset + i] = (sa * currentGainA) + (sb * currentGainB);
        }

        return n;
    }
}
