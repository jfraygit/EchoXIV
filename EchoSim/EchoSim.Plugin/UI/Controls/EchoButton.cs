using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;

namespace EchoSim.UI.Controls;

/// The plugin's button, in the same dark-instrument-panel family as EchoMix's PanelButton: a dark body, an
/// accent ring, a slight press-in, and a soft drop shadow.
public static class EchoButton
{
    private const float Rounding = 7f;

    /// The size this button will actually occupy, given what it has to hold.
    public static Vector2 Measure(string? label, FontAwesomeIcon? icon, Vector2 desired)
    {
        if (string.IsNullOrEmpty(label) && icon is null)
            return desired;

        var labelWidth = string.IsNullOrEmpty(label) ? 0f : ImGui.CalcTextSize(label).X;
        var iconWidth = icon is not null
            ? (ImGui.GetFontSize() * UiHelpers.IconDrawScale) + UiHelpers.S(7f)
            : 0f;

        var padding = MathF.Max(ImGui.GetStyle().FramePadding.X * 2f, UiHelpers.S(22f));

        var needed = labelWidth + iconWidth + padding;

        var iconRoom = icon is not null ? ImGui.GetFontSize() * UiHelpers.IconDrawScale / 0.5f : 0f;

        return new Vector2(
            MathF.Max(desired.X, needed),
            MathF.Max(desired.Y, MathF.Max(ImGui.GetFrameHeight(), iconRoom)));
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
        size = Measure(label, icon, size);

        var accent = accentOverride ?? Theme.Accent;
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton(id, size) && enabled;
        var active = enabled && ImGui.IsItemActive();
        var hovered = enabled && ImGui.IsItemHovered();

        if (hovered && !string.IsNullOrEmpty(tooltip))
            UiHelpers.WrappedTooltip(tooltip);

        var pressOffset = active ? 1.5f : 0f;
        var faceMin = pos + new Vector2(0f, pressOffset);
        var faceMax = faceMin + size;
        var faceCenter = faceMin + (size / 2f);

        var shadowScale = active ? 0.4f : 1f;
        for (var i = 2; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 1.2f * shadowScale);
            drawList.AddRectFilled(pos + offset, pos + size + offset, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.06f * i)), Rounding);
        }

        var filled = active || selected;
        var body = filled
            ? accent
            : hovered
                ? Vector4.Lerp(Theme.Panel, accent, 0.18f)
                : Theme.Panel;

        if (!enabled)
            body = Theme.Panel;

        drawList.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(body), Rounding);

        var ringAlpha = !enabled ? 0.25f : active || hovered || selected ? 1f : 0.7f;
        drawList.AddRect(faceMin, faceMax, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, ringAlpha)), Rounding, ImDrawFlags.None, 1.5f);

        var contentColor = !enabled
            ? Theme.TextDisabled
            : filled
                ? Theme.Background
                : accent;

        DrawContent(drawList, faceCenter, label, iconFont, icon, ImGui.GetColorU32(contentColor));
        return clicked;
    }

    private static void DrawContent(
        ImDrawListPtr drawList,
        Vector2 center,
        string? label,
        IFontHandle? iconFont,
        FontAwesomeIcon? icon,
        uint color)
    {
        var hasIcon = iconFont != null && icon.HasValue;
        var hasText = !string.IsNullOrEmpty(label);

        if (hasIcon && hasText)
        {
            var gap = UiHelpers.S(7f);
            var glyph = icon!.Value.ToIconString();

            Vector2 glyphSize;
            using (iconFont!.PushSafe())
                glyphSize = ImGui.CalcTextSize(glyph);

            var glyphWidth = glyphSize.X * UiHelpers.IconDrawScale;

            var textSize = ImGui.CalcTextSize(label);
            var startX = center.X - ((glyphWidth + gap + textSize.X) / 2f);

            using (iconFont!.PushSafe())
                UiHelpers.DrawScaledIcon(drawList, icon!.Value, new Vector2(startX + (glyphWidth / 2f), center.Y), color);

            drawList.AddText(new Vector2(startX + glyphWidth + gap, center.Y - (textSize.Y / 2f)), color, label);
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

    /// A square icon-only button, for window chrome.
    public static bool Icon(string id, IFontHandle iconFont, FontAwesomeIcon icon, float size, string? tooltip = null, bool selected = false, Vector4? accent = null)
        => Draw(id, null, new Vector2(size, size), accent, iconFont, icon, selected, true, tooltip);

    /// Just the glyph - no body, no ring.
    public static bool BareIcon(string id, IFontHandle iconFont, FontAwesomeIcon icon, float size, string? tooltip = null, bool selected = false)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (hovered && !string.IsNullOrEmpty(tooltip))
            UiHelpers.WrappedTooltip(tooltip);

        var color = active || selected
            ? Theme.Accent
            : hovered
                ? Theme.AccentHover
                : Theme.TextDim;

        using (iconFont.PushSafe())
            UiHelpers.DrawScaledIcon(drawList, icon, pos + new Vector2(size / 2f, size / 2f), ImGui.GetColorU32(color));

        return clicked;
    }
}
