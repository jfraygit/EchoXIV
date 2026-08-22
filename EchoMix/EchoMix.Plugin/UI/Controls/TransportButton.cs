using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;

namespace EchoMix.Plugin.UI.Controls;

/// A rounded-rect, custom-drawn transport button, styled to match the dark instrument-panel look the knobs
/// and sound pads use (Theme.Panel/AccentActive/AccentHover body, accent-colored ring and icon) instead of a
/// brighter glass/gloss finish - native ImGui.Button only supports flat per-state colors, so this is built
/// the same way as those other controls (InvisibleButton + ImDrawList).
public static class TransportButton
{
    private const float Rounding = 10f;

    /// toggledOn - Persistent on/off state for a toggle button (e.g.
    public static bool Draw(string id, IFontHandle iconFont, FontAwesomeIcon icon, string tooltip, float size, Vector4 accent, bool primary, bool toggledOn = false)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var sizeVec = new Vector2(size, size);
        var center = pos + (sizeVec / 2f);

        var clicked = ImGui.InvisibleButton(id, sizeVec);
        var pressed = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        var active = pressed || toggledOn;

        var pressOffset = pressed ? 2f : 0f;
        var faceMin = pos + new Vector2(0f, pressOffset);
        var faceMax = faceMin + sizeVec;
        var faceCenter = center + new Vector2(0f, pressOffset);

        var shadowScale = pressed ? 0.4f : 1f;
        for (var i = 3; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 1.3f * shadowScale);
            var shadowAlpha = 0.07f * i;
            drawList.AddRectFilled(pos + offset, pos + sizeVec + offset, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, shadowAlpha)), Rounding);
        }

        var bodyColor = active ? Theme.AccentActive : hovered ? Theme.AccentHover : Theme.Panel;
        drawList.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(bodyColor), Rounding);

        var ringAlpha = active || hovered ? 1f : primary ? 0.85f : 0.55f;
        drawList.AddRect(faceMin, faceMax, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, ringAlpha)), Rounding, ImDrawFlags.None, 1.5f);

        using (iconFont.PushSafe())
            UiHelpers.DrawScaledIcon(drawList, icon, faceCenter, ImGui.GetColorU32(accent));

        if (hovered)
            ImGui.SetTooltip(tooltip);

        return clicked;
    }
}
