using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EchoMix.Plugin.UI;

public static class UiHelpers
{
    /// Frame-rate independent exponential ease toward a target value.
    public static float Lerp(float current, float target, float speed, float deltaTime)
    {
        var t = 1f - MathF.Exp(-speed * deltaTime);
        return current + ((target - current) * t);
    }

    private const float IconDrawScale = 0.82f;

    private static readonly Dictionary<FontAwesomeIcon, Vector2> IconCenterNudge = new()
    {
        [FontAwesomeIcon.Plus] = new Vector2(0.7f, 0f),
        [FontAwesomeIcon.Heart] = new Vector2(0.44f, 0.5f),

        [FontAwesomeIcon.Bell] = new Vector2(0.8f, 0f),
    };

    /// Draws one icon glyph slightly smaller than its pushed font's natural size, centered at `center` - call
    /// with `iconFont` already pushed (every existing call site already does this for CalcTextSize's sake),
    /// this only replaces the final AddText call.
    public static void DrawScaledIcon(ImDrawListPtr drawList, FontAwesomeIcon icon, Vector2 center, uint color) =>
        DrawScaledIcon(drawList, icon, center, color, ImGui.GetFontSize() * IconDrawScale);

    /// Same as the 4-argument overload, but at a caller-chosen pixel size instead of one derived from
    /// whichever font happens to be pushed - for a glyph that has to fit a specific container regardless of
    /// ambient font (e.g.
    public static void DrawScaledIcon(ImDrawListPtr drawList, FontAwesomeIcon icon, Vector2 center, uint color, float drawFontSize)
    {
        var glyph = icon.ToIconString();
        var font = ImGui.GetFont();
        var nudge = IconCenterNudge.TryGetValue(icon, out var iconNudge) ? iconNudge : Vector2.Zero;

        var glyphInfo = ImGui.FindGlyph(font, glyph[0]);
        if (!glyphInfo.IsNull && font.FontSize > 0f)
        {
            var inkScale = drawFontSize / font.FontSize;
            var inkMin = new Vector2(glyphInfo.X0, glyphInfo.Y0) * inkScale;
            var inkMax = new Vector2(glyphInfo.X1, glyphInfo.Y1) * inkScale;
            var inkCenter = (inkMin + inkMax) / 2f;
            drawList.AddText(font, drawFontSize, center - inkCenter + nudge, color, glyph, 0f);
            return;
        }

        var naturalSize = ImGui.CalcTextSize(glyph);
        var scaledSize = naturalSize * (drawFontSize / ImGui.GetFontSize());
        drawList.AddText(font, drawFontSize, center - (scaledSize / 2f) + nudge, color, glyph, 0f);
    }

    /// Multiply this into every Window.Size assignment in this plugin's own windows (DjDeckWindow,
    /// HostLobbyWindow, SongRequestWindow) - it cancels out a real, confirmed Dalamud behavior:
    /// WindowHost.DrawInternal (the code that actually hosts every Window- derived class, in Dalamud's own
    /// source) does ImGui.SetNextWindowSize(this.Window.Size.Value * ImGuiHelpers.GlobalScale, ...) -
    /// automatically multiplying every window's SIZE (never its Position) by Global Font Scale, completely
    /// independent of fonts or text rendering.
    public static float WindowSizeCompensation
    {
        get
        {
            try
            {
                var globalScale = ImGui.GetIO().FontGlobalScale;
                return globalScale > 0f ? 1f / globalScale : 1f;
            }
            catch
            {
                return 1f;
            }
        }
    }

    /// Ellipsizes to fit `maxWidth` rather than letting a long string overflow a fixed-size container -
    /// checked whole-string-first (the common case) before falling back to a per-character trim, since
    /// CalcTextSize per character would be wasteful for strings that already fit.
    public static string TruncateToWidth(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid] + ellipsis).X <= maxWidth)
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo <= 0 ? ellipsis : text[..lo] + ellipsis;
    }
}
