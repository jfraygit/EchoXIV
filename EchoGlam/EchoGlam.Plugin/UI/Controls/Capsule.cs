using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Controls;

/// A small flat capsule: a marker, or a single secondary action, in a band of chrome.
public static class Capsule
{
    /// Height of a capsule, which is a caption line and a pad either side.
    public static float Height => (Text.CaptionSize * 1.15f) + (Sp.Sm * 2f);

    /// Room a capsule's contents need, so a caller placing one flush right can ask rather than guess.
    public static float Width(string label, FontAwesomeIcon? icon = null)
    {
        float text;
        using (Text.PushCaption())
            text = Text.TrackedWidth(label.ToUpperInvariant(), 1.2f);

        return text + (Sp.Lg * 2f) + (icon is null ? 0f : (Height * 0.55f) + Sp.Sm);
    }

    /// A capsule that states something and is not pressed.
    public static void Marker(
        ImDrawListPtr drawList, Vector2 min, string label, FontAwesomeIcon? icon = null,
        string? tooltip = null, Vector4? tone = null) =>
        Paint(drawList, min, label, icon, warm: 0f, filled: false, tone, enabled: true, tooltip);

    /// A capsule with a hit test on it.
    public static bool Button(
        string id, Vector2 min, string label, FontAwesomeIcon? icon = null, string? tooltip = null,
        Vector4? tone = null, bool enabled = true)
    {
        var size = new Vector2(Width(label, icon), Height);

        var cursor = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(min);

        var clicked = ImGui.InvisibleButton(id, size, ImGuiButtonFlags.MouseButtonLeft) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();
        var active = enabled && ImGui.IsItemActive();

        lastRightClicked = enabled && ImGui.IsItemClicked(ImGuiMouseButton.Right);

        ImGui.SetCursorScreenPos(cursor);

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (!string.IsNullOrEmpty(tooltip))
                UiHelpers.WrappedTooltip(tooltip);
        }

        var warm = Motion.Toward($"capsule{id}", hovered, 20f);
        var face = active ? min + new Vector2(0f, Stroke.Hair) : min;

        Paint(ImGui.GetWindowDrawList(), face, label, icon, warm, filled: true, tone, enabled, tooltip: null);

        return clicked;
    }

    private static bool lastRightClicked;

    /// Whether the capsule just drawn was right-clicked.
    public static bool RightClicked() => lastRightClicked;

    /// warm is the hover amount, 0 for a marker that does not react.
    private static void Paint(
        ImDrawListPtr drawList, Vector2 min, string label, FontAwesomeIcon? icon,
        float warm, bool filled, Vector4? tone, bool enabled, string? tooltip)
    {
        var colour = tone ?? Theme.Accent;
        var width = Width(label, icon);
        var height = Height;
        var max = min + new Vector2(width, height);
        var rounding = Rad.Pill(height);

        if (!enabled)
        {
            drawList.AddRectFilled(min, max, Col.U32(Col.Surface with { W = 0.5f }), rounding);

            drawList.AddRect(
                min, max, Col.U32(Col.EdgeRest with { W = 0.25f }), rounding, ImDrawFlags.None, Stroke.Hair);
        }
        else
        {
            drawList.AddRectFilled(
                min, max,
                Col.U32(Vector4.Lerp(colour with { W = filled ? 0.12f : 0.08f }, colour with { W = 0.26f }, warm)),
                rounding);

            drawList.AddRect(
                min, max,
                Col.U32(Vector4.Lerp(colour with { W = filled ? 0.45f : 0.3f }, colour, warm)),
                rounding, ImDrawFlags.None, Stroke.Hair);
        }

        var tint = !enabled
            ? Col.U32(Col.TextMuted with { W = 0.5f })
            : Col.U32(Vector4.Lerp(
                filled ? Col.TextDim : colour with { W = 0.85f }, colour, MathF.Max(warm, 0.35f)));

        var glyphRoom = icon is null ? 0f : (height * 0.55f) + Sp.Sm;
        var centreY = min.Y + (height / 2f);

        float text;
        using (Text.PushCaption())
            text = Text.TrackedWidth(label.ToUpperInvariant(), 1.2f);

        var x = min.X + ((width - glyphRoom - text) / 2f);

        if (icon is { } glyph)
        {
            using (Text.PushIcon())
                UiHelpers.DrawScaledIcon(drawList, glyph, new Vector2(x + (height * 0.275f), centreY), tint);

            x += glyphRoom;
        }

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, new Vector2(x, centreY - (Text.CaptionSize / 2f)),
                label.ToUpperInvariant(), tint, 1.2f);
        }

        if (tooltip is not null && ImGui.IsMouseHoveringRect(min, max))
            UiHelpers.WrappedTooltip(tooltip);
    }
}
