using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;

namespace EchoRoleplay.UI.Controls;

/// The suite's button: a dark body, an accent ring, a slight press-in and a soft shadow.
public static class EchoButton
{
    private const float RoundingDesign = 7f;

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
        string? tooltip = null)
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

        var pressOffset = active ? 1.5f * scale : 0f;
        var faceMin = pos + new Vector2(0f, pressOffset);
        var faceMax = faceMin + size;
        var faceCenter = faceMin + (size / 2f);

        var shadowScale = active ? 0.4f : 1f;
        for (var i = 2; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 1.2f * scale * shadowScale);
            drawList.AddRectFilled(
                pos + offset, pos + size + offset, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.06f * i)), rounding);
        }

        var filled = active || selected;
        var body = filled
            ? accent
            : hovered
                ? Vector4.Lerp(Theme.Panel, accent, 0.18f)
                : Theme.Panel;

        if (!enabled)
            body = Theme.Panel;

        drawList.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(body), rounding);

        var ringAlpha = !enabled ? 0.25f : active || hovered || selected ? 1f : 0.7f;
        drawList.AddRect(
            faceMin, faceMax, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, ringAlpha)),
            rounding, ImDrawFlags.None, 1.5f * scale);

        var contentColor = !enabled
            ? Theme.TextDisabled
            : filled
                ? Theme.Background
                : accent;

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
