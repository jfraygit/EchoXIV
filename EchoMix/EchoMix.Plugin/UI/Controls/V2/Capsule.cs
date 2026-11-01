using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Controls.V2;

/// A social action: an icon and a tracked uppercase label in a hairline capsule, with an on state.
public static class Capsule
{
    /// Matches LinkAction's tracking, because these sit a few pixels apart from each other on the same page
    /// and anything else reads as two typographic systems.
    private const float Tracking = 1.6f;

    private static readonly Dictionary<string, float> Warmth = new();

    /// The icon's slot plus the gap before the label.
    private static float IconRoom(bool hasIcon) => hasIcon ? Metrics.Xl + Metrics.Lg : 0f;

    /// Room this capsule needs, so a caller laying a row out from the right can measure first.
    public static float Width(string label, bool hasIcon)
    {
        using (TypeScale.Caption())
        {
            var text = Chrome.TrackedWidth(label.ToUpperInvariant(), Tracking);
            return MathF.Round(text + IconRoom(hasIcon) + (Metrics.Xl * 2f));
        }
    }

    /// Draws at the cursor and returns true on the frame it was pressed.
    public static bool Draw(
        string id,
        string label,
        FontAwesomeIcon? icon,
        bool active,
        Vector4? accent = null,
        bool enabled = true,
        float? height = null)
    {
        var boxHeight = MathF.Round(height ?? Metrics.ControlSm);
        var upper = label.ToUpperInvariant();
        var tint = accent ?? Semantic.Primary;

        float textWidth;
        using (TypeScale.Caption())
            textWidth = Chrome.TrackedWidth(upper, Tracking);

        var iconRoom = IconRoom(icon.HasValue);
        var boxWidth = MathF.Round(textWidth + iconRoom + (Metrics.Xl * 2f));

        var min = Chrome.Snap(ImGui.GetCursorScreenPos());
        var pressed = ImGui.InvisibleButton($"##capsule{id}", new Vector2(boxWidth, boxHeight)) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var warm = Warmth.TryGetValue(id, out var w) ? w : 0f;
        warm = Motion.Approach(warm, hovered ? 1f : 0f, Motion.SpeedFast);
        Warmth[id] = warm;

        var max = min + new Vector2(boxWidth, boxHeight);
        var radius = Metrics.Pill(boxHeight);
        var drawList = ImGui.GetWindowDrawList();

        Vector4 fill, border, content;

        if (!enabled)
        {
            fill = Semantic.Alpha(Vector4.One, 0.02f);
            border = Elevation.Line;
            content = Semantic.TextDisabled;
        }
        else if (active)
        {
            fill = Semantic.Alpha(tint, 0.12f + (0.08f * warm));
            border = Semantic.Alpha(tint, 0.65f + (0.35f * warm));
            content = tint;
        }
        else
        {
            fill = Semantic.Alpha(Vector4.One, 0.04f + (0.05f * warm));
            border = Vector4.Lerp(Elevation.LineStrong, Semantic.Alpha(Semantic.TextPrimary, 0.4f), warm);
            content = Vector4.Lerp(Semantic.TextSecondary, Semantic.TextPrimary, warm);
        }

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), radius);
        drawList.AddRect(min, max, ImGui.GetColorU32(border), radius, ImDrawFlags.None, Metrics.Hairline);

        var centreY = min.Y + (boxHeight * 0.5f);
        var colour = ImGui.GetColorU32(content);
        var contentX = min.X + Metrics.Xl;

        if (icon is { } glyph)
        {
            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, glyph,
                    Chrome.Snap(new Vector2(contentX + (Metrics.Xl * 0.5f), centreY)), colour);

            contentX += iconRoom;
        }

        using (TypeScale.Caption())
            Chrome.TrackedTextCenteredOnInk(drawList, contentX, centreY, colour, upper, Tracking);

        return pressed;
    }
}
