using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// Eased values, keyed by whatever the caller calls them.
public static class Motion
{
    private record struct Tracked(float Value, int Frame);

    private static readonly Dictionary<string, Tracked> Values = new(StringComparer.Ordinal);

    /// Roughly two seconds at 60fps.
    private const int StaleAfterFrames = 120;

    private static int lastSweep;

    /// Eases a named value toward a target and returns where it is now.
    public static float Ease(string id, float target, float speed)
    {
        var frame = ImGui.GetFrameCount();
        var step = Anim.Step(ImGui.GetIO().DeltaTime);

        if (!Values.TryGetValue(id, out var tracked))
        {
            Values[id] = new Tracked(target, frame);
            return target;
        }

        var value = UiHelpers.Lerp(tracked.Value, target, speed, step);

        if (MathF.Abs(value - target) < 0.001f)
            value = target;

        Values[id] = new Tracked(value, frame);
        return value;
    }

    /// An eased 0-to-1 for a boolean state - hovered, selected, open.
    public static float Toward(string id, bool on, float speed = 14f) => Ease(id, on ? 1f : 0f, speed);

    /// A number that travels to its new value rather than jumping, for a count that changes while somebody is
    /// looking at it.
    public static float Count(string id, int target, float speed = 9f) => Ease(id, target, speed);

    /// Drops anything nothing has asked about recently.
    public static void Sweep()
    {
        var frame = ImGui.GetFrameCount();

        if (frame - lastSweep < 60)
            return;

        lastSweep = frame;

        List<string>? stale = null;

        foreach (var (key, tracked) in Values)
        {
            if (frame - tracked.Frame <= StaleAfterFrames)
                continue;

            stale ??= [];
            stale.Add(key);
        }

        if (stale is null)
            return;

        foreach (var key in stale)
            Values.Remove(key);
    }

    /// Everything, dropped.
    internal static void Reset() => Values.Clear();
}
