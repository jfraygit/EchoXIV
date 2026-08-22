using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Controls;

/// A sound-effect pad styled like the knob dials - a dark instrument-panel body with an accent-colored ring,
/// rather than the brighter glass/gloss look of TransportButton - plus the same press-in feedback.
public static class SoundPadButton
{
    private const float Rounding = 6f;

    public static bool Draw(string id, string label, bool hasSound, bool looping, Vector2 size, Vector4 accent, out bool middleClicked)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var center = pos + (size / 2f);

        var clicked = ImGui.InvisibleButton(id, size);
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        middleClicked = ImGui.IsItemClicked(ImGuiMouseButton.Middle);

        var pressOffset = active ? 2f : 0f;
        var faceMin = pos + new Vector2(0f, pressOffset);
        var faceMax = faceMin + size;
        var faceCenter = center + new Vector2(0f, pressOffset);

        var shadowScale = active ? 0.4f : 1f;
        for (var i = 3; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 1.2f * shadowScale);
            var shadowAlpha = 0.06f * i;
            drawList.AddRectFilled(pos + offset, pos + size + offset, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, shadowAlpha)), Rounding);
        }

        var bodyColor = active ? Theme.AccentActive : hovered ? Theme.AccentHover : Theme.Panel;
        drawList.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(bodyColor), Rounding);

        var ringColor = active || hovered
            ? accent
            : hasSound ? new Vector4(accent.X, accent.Y, accent.Z, 0.55f) : Theme.Border;
        drawList.AddRect(faceMin, faceMax, ImGui.GetColorU32(ringColor), Rounding, ImDrawFlags.None, 1.5f);

        if (hasSound)
        {
            var textSize = ImGui.CalcTextSize(label);
            var textPos = faceCenter - (textSize / 2f);
            drawList.PushClipRect(faceMin + new Vector2(4f, 2f), faceMax - new Vector2(4f, 2f), true);
            drawList.AddText(textPos, ImGui.GetColorU32(Theme.Text), label);
            drawList.PopClipRect();
        }
        else
        {
            const string plus = "+";
            var plusSize = ImGui.CalcTextSize(plus);
            drawList.AddText(faceCenter - (plusSize / 2f), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.6f)), plus);
        }

        if (looping)
        {
            var dotCenter = faceMax - new Vector2(6f, 6f);
            drawList.AddCircleFilled(dotCenter, 3f, ImGui.GetColorU32(accent));
        }

        if (hovered)
        {
            var tooltip = hasSound ? $"{label}\nRight-click for options\nMiddle-click to toggle looping" : "Click to upload a sound effect";
            if (looping)
                tooltip += "\n(looping)";
            ImGui.SetTooltip(tooltip);
        }

        return clicked;
    }
}
