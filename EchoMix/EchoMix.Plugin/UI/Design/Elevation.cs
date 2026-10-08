using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Design;

/// The 2.0 surface ramp.
public static class Elevation
{
    /// The window background.
    public static readonly Vector4 Base = new(0.039f, 0.039f, 0.051f, 1f);

    /// Recessed: input fields, fader grooves, meter housings, progress tracks.
    public static readonly Vector4 Sunken = new(0.024f, 0.024f, 0.033f, 1f);

    /// The default panel.
    public static readonly Vector4 Surface = new(0.075f, 0.075f, 0.092f, 1f);

    /// A card sitting on a Surface.
    public static readonly Vector4 Raised = new(0.110f, 0.110f, 0.132f, 1f);

    /// Popups, menus, tooltips, toasts - anything floating free above the window.
    public static readonly Vector4 Overlay = new(0.145f, 0.145f, 0.172f, 1f);

    public static readonly Vector4 TopEdge = new(1f, 1f, 1f, 0.055f);
    public static readonly Vector4 Line = new(1f, 1f, 1f, 0.075f);
    public static readonly Vector4 LineStrong = new(1f, 1f, 1f, 0.13f);

    /// Shadow bands per elevation step: how many layers, how far each is offset, how dark.
    public readonly record struct ShadowSpec(int Bands, float Spread, float Alpha)
    {
        public static readonly ShadowSpec None = new(0, 0f, 0f);

        /// A card on a panel.
        public static readonly ShadowSpec Low = new(3, 1.6f, 0.055f);

        /// A raised control or an expanded region.
        public static readonly ShadowSpec Medium = new(4, 2.2f, 0.07f);

        /// A popup or drawer floating over content.
        public static readonly ShadowSpec High = new(5, 3f, 0.085f);
    }

    /// Draws `spec`'s shadow behind the rect, then nothing else - callers fill on top.
    public static void DrawShadow(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, ShadowSpec spec)
    {
        if (spec.Bands <= 0)
            return;

        for (var i = spec.Bands; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * spec.Spread * Metrics.Scale);
            var grow = i * spec.Spread * 0.5f * Metrics.Scale;
            var color = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, spec.Alpha));
            drawList.AddRectFilled(
                min + offset - new Vector2(grow, 0f),
                max + offset + new Vector2(grow, 0f),
                color,
                rounding + grow);
        }
    }

    /// Shadow, fill, lit top edge, border - the full surface treatment in one call.
    public static void DrawSurface(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        Vector4 fill,
        float rounding,
        ShadowSpec shadow,
        bool topEdge = true,
        Vector4? border = null)
    {
        DrawShadow(drawList, min, max, rounding, shadow);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), rounding);

        if (topEdge)
        {
            var inset = MathF.Min(rounding, (max.Y - min.Y) * 0.5f);
            drawList.PushClipRect(min, new Vector2(max.X, min.Y + MathF.Max(1f, inset)), true);
            drawList.AddRect(min, max, ImGui.GetColorU32(TopEdge), rounding, ImDrawFlags.None, 1f);
            drawList.PopClipRect();
        }

        if (border is { } borderColor)
            drawList.AddRect(min, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.None, 1f);
    }
}
