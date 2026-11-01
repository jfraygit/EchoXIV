using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Controls.V2;

/// An action with no body: an icon and a tracked uppercase word that brighten under the pointer.
public static class LinkAction
{
    /// Letter spacing on the label.
    private const float Tracking = 1.6f;

    /// Hover easing per id, so two links on screen at once cannot share a warm value.
    private static readonly Dictionary<string, float> Warmth = new();

    /// Room this link needs, for a caller laying a band out from both ends.
    public static float Width(string label, bool hasIcon)
    {
        using (TypeScale.Caption())
        {
            var text = Chrome.TrackedWidth(label.ToUpperInvariant(), Tracking);
            return hasIcon ? Metrics.ControlMd + Metrics.Xs + text : text;
        }
    }

    /// Draws the link at the cursor and returns true on the frame it was pressed.
    public static bool Draw(
        string id,
        string label,
        FontAwesomeIcon? icon,
        Vector4? accent = null,
        bool enabled = true,
        float lean = 0f,
        float? height = null)
    {
        var rowHeight = height ?? MathF.Round(Metrics.ControlMd);
        var upper = label.ToUpperInvariant();
        var warmTo = accent ?? Semantic.Primary;

        float textWidth;
        using (TypeScale.Caption())
            textWidth = Chrome.TrackedWidth(upper, Tracking);

        var iconRoom = icon.HasValue ? Metrics.ControlMd + Metrics.Xs : 0f;
        var min = Chrome.Snap(ImGui.GetCursorScreenPos());
        var width = iconRoom + textWidth;

        var clicked = ImGui.InvisibleButton($"##link{id}", new Vector2(MathF.Max(1f, width), rowHeight)) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var warm = Warmth.TryGetValue(id, out var w) ? w : 0f;
        warm = Motion.Approach(warm, hovered ? 1f : 0f, Motion.SpeedFast);
        Warmth[id] = warm;

        var slide = MathF.Round(Metrics.Sm * lean * warm);

        var resting = enabled ? Semantic.TextTertiary : Semantic.TextDisabled;
        var tint = ImGui.GetColorU32(enabled ? Vector4.Lerp(resting, warmTo, warm) : resting);

        var drawList = ImGui.GetWindowDrawList();

        var centreY = min.Y + (rowHeight * 0.5f);

        if (icon is { } glyph)
        {
            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, glyph,
                    Chrome.Snap(new Vector2(min.X + (Metrics.ControlMd * 0.5f) + slide, centreY)),
                    tint);
        }

        using (TypeScale.Caption())
            Chrome.TrackedTextCenteredOnInk(
                drawList, min.X + iconRoom + slide, centreY, tint, upper, Tracking);

        return clicked;
    }

    /// The way out of a page.
    public static bool Back(string id, string label, float? height = null)
        => Draw($"back{id}", label, FontAwesomeIcon.ChevronLeft, lean: -1f, height: height);
}
