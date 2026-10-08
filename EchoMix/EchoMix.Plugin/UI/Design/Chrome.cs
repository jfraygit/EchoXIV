using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Design;

/// Window-edge chrome and pixel-accurate text, shared by every 2.0 surface.
public static class Chrome
{
    /// The deck gradient: Deck A at the left, the user's Blend colour at the midpoint, Deck B at the right.
    public static Vector4 DeckGradient(float t)
        => t <= 0.5f
            ? Vector4.Lerp(Theme.CyanAccent, Theme.NeutralAccent, t * 2f)
            : Vector4.Lerp(Theme.NeutralAccent, Theme.OrangeAccent, (t - 0.5f) * 2f);

    /// The glowing gradient border around a window's own rect, on the window's own draw list, so the window
    /// reads as a distinct object against the game behind it rather than sitting flat.
    public static void DrawWindowBorder(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float rounding, float thickness)
    {
        drawList.PushClipRect(
            pos - new Vector2(thickness, thickness),
            pos + size + new Vector2(thickness, thickness),
            false);

        var points = BuildRoundedRectPoints(pos, size, rounding);
        var joinRadius = thickness * 0.5f;

        for (var i = 0; i < points.Count; i++)
        {
            var p0 = points[i];
            var p1 = points[(i + 1) % points.Count];
            var blend = Math.Clamp((((p0.X + p1.X) / 2f) - pos.X) / MathF.Max(1f, size.X), 0f, 1f);
            var color = ImGui.GetColorU32(DeckGradient(blend));

            drawList.AddLine(p0, p1, color, thickness);

            drawList.AddCircleFilled(p0, joinRadius, color);
        }

        drawList.PopClipRect();
    }

    /// A horizontally-graded border drawn as a stack of clipped AddRect passes.
    public static void DrawGradientBorder(
        ImDrawListPtr drawList,
        Vector2 pos,
        Vector2 size,
        float rounding,
        float thickness,
        float alpha = 1f,
        int bands = 12)
    {
        drawList.PushClipRect(pos - new Vector2(thickness, thickness), pos + size + new Vector2(thickness, thickness), false);

        var bandWidth = size.X / bands;
        for (var i = 0; i < bands; i++)
        {
            var x0 = pos.X + (i * bandWidth);
            drawList.PushClipRect(new Vector2(x0 - 0.5f, pos.Y - thickness),
                new Vector2(x0 + bandWidth + 0.5f, pos.Y + size.Y + thickness), true);

            var t = (i + 0.5f) / bands;
            var color = DeckGradient(t);
            drawList.AddRect(pos, pos + size,
                ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, color.W * alpha)),
                rounding, ImDrawFlags.None, thickness);

            drawList.PopClipRect();
        }

        drawList.PopClipRect();
    }

    private static List<Vector2> BuildRoundedRectPoints(Vector2 pos, Vector2 size, float r)
    {
        const int cornerSegments = 16;
        const float edgeStep = 20f;

        var points = new List<Vector2>();
        var straightX = MathF.Max(0f, size.X - (2f * r));
        var straightY = MathF.Max(0f, size.Y - (2f * r));

        void AddArc(Vector2 center, float startAngle)
        {
            for (var i = 0; i <= cornerSegments; i++)
            {
                var a = startAngle + (i / (float)cornerSegments * (MathF.PI / 2f));
                points.Add(center + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * r));
            }
        }

        void AddEdge(Vector2 from, Vector2 to)
        {
            var steps = Math.Max(1, (int)(Vector2.Distance(from, to) / edgeStep));
            for (var i = 0; i < steps; i++)
                points.Add(Vector2.Lerp(from, to, i / (float)steps));
        }

        AddEdge(new Vector2(pos.X + r, pos.Y), new Vector2(pos.X + r + straightX, pos.Y));
        AddArc(new Vector2(pos.X + size.X - r, pos.Y + r), -MathF.PI / 2f);
        AddEdge(new Vector2(pos.X + size.X, pos.Y + r), new Vector2(pos.X + size.X, pos.Y + r + straightY));
        AddArc(new Vector2(pos.X + size.X - r, pos.Y + size.Y - r), 0f);
        AddEdge(new Vector2(pos.X + size.X - r, pos.Y + size.Y), new Vector2(pos.X + r, pos.Y + size.Y));
        AddArc(new Vector2(pos.X + r, pos.Y + size.Y - r), MathF.PI / 2f);
        AddEdge(new Vector2(pos.X, pos.Y + size.Y - r), new Vector2(pos.X, pos.Y + r));
        AddArc(new Vector2(pos.X + r, pos.Y + r), MathF.PI);

        return points;
    }

    /// Snaps a position to whole pixels.
    public static Vector2 Snap(Vector2 position)
        => new(MathF.Round(position.X), MathF.Round(position.Y));

    /// Pixel-snapped text on a draw list.
    public static void Text(ImDrawListPtr drawList, Vector2 position, uint color, string text)
        => drawList.AddText(Snap(position), color, text);

    /// Vertically centres text in a row of `height` starting at `top`, snapped.
    public static Vector2 CenterY(float x, float top, float height, float textHeight)
        => Snap(new Vector2(x, top + ((height - textHeight) * 0.5f)));
}
