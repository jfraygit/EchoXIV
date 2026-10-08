using System;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Design;

/// Motion tokens and easing for the 2.0 look.
public static class Motion
{
    /// Hover/press feedback.
    public const float SpeedInstant = 32f;

    /// Toggle knobs, segmented highlights, lit indicators.
    public const float SpeedFast = 22f;

    /// Glow and emphasis settling.
    public const float SpeedNormal = 14f;

    /// Window size and layout shifts, where slower reads as deliberate rather than sluggish.
    public const float SpeedSlow = 9f;

    public const float DurationFast = 0.12f;
    public const float DurationNormal = 0.18f;
    public const float DurationSlow = 0.3f;

    /// Frame-rate-independent exponential approach - the same curve as UiHelpers.Lerp, restated here so V2
    /// code has one obvious place to reach for motion rather than importing a helper that lives next to 1.0's
    /// truncation and icon-nudge utilities.
    public static float Approach(float current, float target, float speed)
        => UiHelpers.Lerp(current, target, speed, ImGui.GetIO().DeltaTime);

    /// Decelerating.
    public static float EaseOutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var inv = 1f - t;
        return 1f - (inv * inv * inv);
    }

    /// Accelerate then decelerate.
    public static float EaseInOutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - (MathF.Pow((-2f * t) + 2f, 3f) / 2f);
    }

    /// Overshoots slightly past 1 before settling.
    public static float EaseOutBack(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var inv = t - 1f;
        return 1f + (c3 * inv * inv * inv) + (c1 * inv * inv);
    }

    /// A 0..1 triangle wave at `hz`, for pulsing a live indicator.
    public static float Pulse(float hz)
    {
        var phase = (float)(ImGui.GetTime() * hz % 1.0);
        return phase < 0.5f ? phase * 2f : (1f - phase) * 2f;
    }
}
