using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;

namespace EchoMix.Plugin.UI.Controls;

/// A small rectangular button in the same dark-instrument-panel style as TransportButton/ SoundPadButton
/// (dark panel body, accent-colored ring, press-in animation) instead of a native ImGui.Button - used for the
/// deck-assign/remove/etc.
public static class PanelButton
{
    private const float Rounding = 6f;

    public static bool Draw(string id, IFontHandle? iconFont, FontAwesomeIcon? icon, string? text, Vector2 size, Vector4 accent)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var center = pos + (size / 2f);

        var clicked = ImGui.InvisibleButton(id, size);
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();

        var pressOffset = active ? 1.5f : 0f;
        var faceMin = pos + new Vector2(0f, pressOffset);
        var faceMax = faceMin + size;
        var faceCenter = center + new Vector2(0f, pressOffset);

        var shadowScale = active ? 0.4f : 1f;
        for (var i = 2; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 1.2f * shadowScale);
            var shadowAlpha = 0.06f * i;
            drawList.AddRectFilled(pos + offset, pos + size + offset, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, shadowAlpha)), Rounding);
        }

        var bodyColor = active ? Theme.AccentActive : hovered ? Theme.AccentHover : Theme.Panel;
        drawList.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(bodyColor), Rounding);

        var ringAlpha = active || hovered ? 1f : 0.7f;
        drawList.AddRect(faceMin, faceMax, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, ringAlpha)), Rounding, ImDrawFlags.None, 1.5f);

        var hasIcon = iconFont != null && icon.HasValue;
        var hasText = !string.IsNullOrEmpty(text);

        if (hasIcon && hasText)
        {
            const float gap = 6f;
            var glyph = icon!.Value.ToIconString();

            Vector2 glyphSize;
            using (iconFont!.PushSafe())
                glyphSize = ImGui.CalcTextSize(glyph);
            var textSize = ImGui.CalcTextSize(text);

            var totalWidth = glyphSize.X + gap + textSize.X;
            var startX = faceCenter.X - (totalWidth / 2f);

            using (iconFont!.PushSafe())
                UiHelpers.DrawScaledIcon(drawList, icon!.Value, new Vector2(startX + (glyphSize.X / 2f), faceCenter.Y), ImGui.GetColorU32(accent));

            drawList.AddText(new Vector2(startX + glyphSize.X + gap, faceCenter.Y - (textSize.Y / 2f)), ImGui.GetColorU32(accent), text);
        }
        else if (hasIcon)
        {
            using (iconFont!.PushSafe())
                UiHelpers.DrawScaledIcon(drawList, icon!.Value, faceCenter, ImGui.GetColorU32(accent));
        }
        else if (hasText)
        {
            var textPos = faceCenter - (ImGui.CalcTextSize(text) / 2f);
            drawList.AddText(textPos, ImGui.GetColorU32(accent), text);
        }

        return clicked;
    }
}
