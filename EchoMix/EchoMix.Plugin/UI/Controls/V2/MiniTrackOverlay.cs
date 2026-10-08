using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Controls.V2;

/// The title and progress readout laid over a half of the minimized box, for both the DJ's own minimized
/// decks and the listener's.
public static class MiniTrackOverlay
{
    private static readonly Vector2 Pad = new(7f, 4f);

    /// How tall the fade is, given the ambient line height.
    public static float ScrimHeight(float lineHeight)
        => lineHeight + (18f * Metrics.Scale);

    /// Where a progress bar belongs under the title.
    public static (Vector2 Origin, Vector2 Size) BarRect(Vector2 origin, Vector2 size, float lineHeight)
    {
        var padX = Pad.X * Metrics.Scale;
        var top = origin.Y + (Pad.Y * Metrics.Scale) + lineHeight + (3f * Metrics.Scale);
        return (
            Chrome.Snap(new Vector2(origin.X + padX, top)),
            new Vector2(size.X - (padX * 2f), MathF.Max(3f, 4f * Metrics.Scale)));
    }

    /// Scrim plus title.
    public static void Draw(
        ImDrawListPtr drawList,
        Vector2 origin,
        Vector2 size,
        string? title,
        string? deckLabel,
        Vector4 accent,
        bool roundTopCorners)
    {
        var lineHeight = ImGui.GetTextLineHeight();
        var scrim = ScrimHeight(lineHeight);
        var top = ImGui.GetColorU32(Semantic.Alpha(Elevation.Base, 0.88f));
        var bottom = ImGui.GetColorU32(Semantic.Alpha(Elevation.Base, 0f));

        var windowPos = ImGui.GetWindowPos();
        var touchesCorner = origin.Y <= windowPos.Y + 0.5f && origin.X <= windowPos.X + 0.5f;

        if (roundTopCorners && touchesCorner)
        {
            var radius = Metrics.RadiusPanel;
            drawList.AddRectFilled(origin, new Vector2(origin.X + size.X, origin.Y + radius), top,
                radius, ImDrawFlags.RoundCornersTop);

            drawList.AddRectFilledMultiColor(
                new Vector2(origin.X, origin.Y + radius),
                new Vector2(origin.X + size.X, origin.Y + scrim),
                top, top, bottom, bottom);
        }
        else
        {
            drawList.AddRectFilledMultiColor(
                origin, new Vector2(origin.X + size.X, origin.Y + scrim),
                top, top, bottom, bottom);
        }

        var x = origin.X + (Pad.X * Metrics.Scale);
        var y = origin.Y + (Pad.Y * Metrics.Scale);
        var available = size.X - (Pad.X * 2f * Metrics.Scale);

        if (deckLabel != null)
        {
            var labelWidth = ImGui.CalcTextSize(deckLabel).X;
            Chrome.Text(drawList, new Vector2(x, y), ImGui.GetColorU32(accent), deckLabel);
            x += labelWidth + (5f * Metrics.Scale);
            available -= labelWidth + (5f * Metrics.Scale);
        }

        var shown = UiHelpers.TruncateToWidth(title ?? "No Track Loaded", MathF.Max(1f, available));
        Chrome.Text(drawList, new Vector2(x, y),
            ImGui.GetColorU32(title == null ? Semantic.TextTertiary : Semantic.TextPrimary), shown);
    }

    /// The listener's read-only progress fill, in the same geometry the DJ's SeekBar occupies.
    public static void DrawStaticBar(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float progress, Vector4 accent)
    {
        var radius = Metrics.RadiusSharp;
        drawList.AddRectFilled(origin, origin + size,
            ImGui.GetColorU32(Semantic.Alpha(Elevation.Base, 0.7f)), radius);

        var filled = Math.Clamp(progress, 0f, 1f) * size.X;
        if (filled > 0.5f)
        {
            drawList.AddRectFilled(origin, origin + new Vector2(filled, size.Y),
                ImGui.GetColorU32(Semantic.Alpha(accent, 0.9f)), radius);
        }
    }
}
