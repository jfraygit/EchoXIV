using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Controls;

/// The suite's button: a raised body, a thread of light along its top edge, and a press-in.
public static class EchoButton
{
    private const float RoundingDesign = 6f;

    /// How far a lit button's halo carries beyond its own box, in design pixels.
    public const float GlowReachDesign = 7.8f;

    /// The same, in real pixels.
    public static float GlowReach => GlowReachDesign * UiHelpers.Scale;

    /// Breathing room around a button's label, added to the measured content to get the floor below.
    private static Vector2 Padding => new Vector2(14f, 4f) * UiHelpers.Scale;

    /// Gap between an icon and the label beside it.
    private static float IconGap => 7f * UiHelpers.Scale;

    /// The smallest box this button's contents will fit in.
    public static Vector2 ContentSize(string? label, IFontHandle? iconFont = null, FontAwesomeIcon? icon = null)
    {
        var content = Vector2.Zero;

        if (iconFont != null && icon.HasValue)
        {
            using (iconFont.PushSafe())
                content = ImGui.CalcTextSize(icon.Value.ToIconString()) * UiHelpers.IconDrawScale;
        }

        if (!string.IsNullOrEmpty(label))
        {
            var text = ImGui.CalcTextSize(label);
            content = new Vector2(
                content.X + text.X + (content.X > 0f ? IconGap : 0f),
                MathF.Max(content.Y, text.Y));
        }

        return content + (Padding * 2f);
    }

    public static bool Draw(
        string id,
        string? label,
        Vector2 size,
        Vector4? accentOverride = null,
        IFontHandle? iconFont = null,
        FontAwesomeIcon? icon = null,
        bool selected = false,
        bool enabled = true,
        string? tooltip = null,
        bool primary = false)
    {
        var accent = accentOverride ?? Theme.Accent;
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var scale = UiHelpers.Scale;
        var rounding = RoundingDesign * scale;

        size = Vector2.Max(size, ContentSize(label, iconFont, icon));

        var clicked = ImGui.InvisibleButton(id, size, ImGuiButtonFlags.MouseButtonLeft) && enabled;
        var active = enabled && ImGui.IsItemActive();
        var hovered = enabled && ImGui.IsItemHovered();

        if (hovered && !string.IsNullOrEmpty(tooltip))
            UiHelpers.WrappedTooltip(tooltip);

        var pressOffset = active ? 1f * scale : 0f;
        var faceMin = pos + new Vector2(0f, pressOffset);
        var faceMax = faceMin + size;
        var faceCenter = faceMin + (size / 2f);

        var filled = active || selected || (primary && enabled);

        var body = !enabled
            ? Col.Surface
            : primary
                ? Vector4.Lerp(accent, Vector4.One, active ? 0f : hovered ? 0.14f : 0.04f)
                : selected || active
                    ? Vector4.Lerp(Col.Raised, accent, 0.18f)
                    : hovered
                        ? Col.RaisedHigh
                        : Col.Raised;

        if (primary && enabled)
            Elev.Glow(drawList, faceMin, faceMax, accent, hovered ? 0.42f : 0.27f, rounding, GlowReachDesign);
        else if (selected && enabled)
            Elev.Glow(drawList, faceMin, faceMax, accent, 0.12f, rounding, 4.4f);

        if (!active)
            Elev.Shadow(drawList, faceMin, faceMax, Elev.Block, rounding);

        drawList.AddRectFilled(faceMin, faceMax, Col.U32(body), rounding);

        if (!primary || !enabled)
        {
            drawList.AddLine(
                new Vector2(faceMin.X + rounding, faceMin.Y + (Stroke.Hair / 2f)),
                new Vector2(faceMax.X - rounding, faceMin.Y + (Stroke.Hair / 2f)),
                Col.U32(new Vector4(1f, 1f, 1f, enabled ? 0.08f : 0.03f)),
                Stroke.Hair);
        }

        var edge = !enabled
            ? Col.EdgeRest with { W = 0.25f }
            : selected || active
                ? accent with { W = 0.85f }
                : hovered
                    ? Col.EdgeHover
                    : Col.EdgeRest with { W = 0.6f };

        if (!primary || !enabled)
        {
            drawList.AddRect(
                faceMin, faceMax, Col.U32(edge), rounding, ImDrawFlags.None,
                selected || active ? Stroke.Edge : Stroke.Hair);
        }

        var contentColor = !enabled
            ? Col.TextMuted
            : primary
                ? Theme.Background
                : selected || active
                    ? accent
                    : hovered
                        ? Col.Text
                        : Col.TextDim;

        DrawContent(drawList, faceCenter, label, iconFont, icon, ImGui.GetColorU32(contentColor));
        return clicked;
    }

    /// Whether the button just drawn was right-clicked.
    public static bool RightClicked() => ImGui.IsItemClicked(ImGuiMouseButton.Right);

    private static void DrawContent(
        ImDrawListPtr drawList, Vector2 center, string? label,
        IFontHandle? iconFont, FontAwesomeIcon? icon, uint color)
    {
        var hasIcon = iconFont != null && icon.HasValue;
        var hasText = !string.IsNullOrEmpty(label);

        if (hasIcon && hasText)
        {
            var gap = IconGap;
            var glyph = icon!.Value.ToIconString();

            Vector2 glyphSize;
            using (iconFont!.PushSafe())
                glyphSize = ImGui.CalcTextSize(glyph);

            var drawnWidth = glyphSize.X * UiHelpers.IconDrawScale;

            var textSize = ImGui.CalcTextSize(label);
            var startX = center.X - ((drawnWidth + gap + textSize.X) / 2f);

            using (iconFont!.PushSafe())
                UiHelpers.DrawScaledIcon(
                    drawList, icon!.Value, new Vector2(startX + (drawnWidth / 2f), center.Y), color);

            drawList.AddText(new Vector2(startX + drawnWidth + gap, center.Y - (textSize.Y / 2f)), color, label);
        }
        else if (hasIcon)
        {
            using (iconFont!.PushSafe())
                UiHelpers.DrawScaledIcon(drawList, icon!.Value, center, color);
        }
        else if (hasText)
        {
            drawList.AddText(center - (ImGui.CalcTextSize(label) / 2f), color, label);
        }
    }

    /// Just the glyph - no body, no ring.
    public static bool BareIcon(
        string id, IFontHandle iconFont, FontAwesomeIcon icon, float size, string? tooltip = null,
        bool selected = false, Vector4? colourOverride = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (hovered && !string.IsNullOrEmpty(tooltip))
            UiHelpers.WrappedTooltip(tooltip);

        var color = colourOverride is { } fixedColour
            ? (hovered || active ? Vector4.Lerp(fixedColour, Vector4.One, 0.25f) : fixedColour)
            : active || selected
                ? Theme.Accent
                : hovered
                    ? Theme.AccentHover
                    : Theme.TextDim;

        using (iconFont.PushSafe())
            UiHelpers.DrawScaledIcon(drawList, icon, pos + new Vector2(size / 2f, size / 2f), ImGui.GetColorU32(color));

        return clicked;
    }
}
