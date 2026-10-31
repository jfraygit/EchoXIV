using System;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// Out, swap, back in - for a region whose whole contents change at once.
public sealed class Crossfade
{
    private string shown;

    private string? pending;

    private float alpha = 1f;

    public Crossfade(string initial) => shown = initial;

    /// What the content should be drawn at this frame.
    public float Alpha => alpha;

    /// The key currently on screen, for anything that has to act when the content actually changes rather
    /// than when it was asked to - a window resize, say, which should start as the new pane fades IN rather
    /// than while the old one is still fading out.
    public string Showing => shown;

    /// Advances the fade and returns the key that should be DRAWN, which lags target by the length of the
    /// fade-out whenever the two differ.
    public string Show(string target)
    {
        var rate = Anim.Step(ImGui.GetIO().DeltaTime) / Anim.FadeSeconds;

        pending = string.Equals(target, shown, StringComparison.Ordinal) ? null : target;

        if (pending is { } next)
        {
            alpha -= rate;

            if (alpha > 0f)
                return shown;

            alpha = 0f;
            shown = next;
            pending = null;

            return shown;
        }

        if (alpha < 1f)
            alpha = MathF.Min(1f, alpha + rate);

        return shown;
    }

    /// Jumps straight to a key, for restoring state on open - where easing in from whatever happened to be
    /// showing is a transition nobody asked for.
    public void SetImmediate(string key)
    {
        shown = key;
        pending = null;
        alpha = 1f;
    }
}
