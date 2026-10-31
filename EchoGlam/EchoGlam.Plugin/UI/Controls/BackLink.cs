using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Controls;

/// The way out of a page, as a link rather than a button.
public static class BackLink
{
    /// Letter spacing on the label.
    private const float Tracking = 1.6f;

    /// Room the link needs, so a caller laying a chrome band out from both ends can ask.
    public static float Width(string label)
    {
        using (Text.PushCaption())
            return Metrics.TouchMin + Sp.Xs + Text.TrackedWidth(label.ToUpperInvariant(), Tracking);
    }

    /// Draws the link and returns true on the frame it was pressed.
    public static bool Draw(
        ImDrawListPtr drawList, IFontHandle iconFont, string id, string label, Vector2 min, float height)
    {
        float text;
        using (Text.PushCaption())
            text = Text.TrackedWidth(label.ToUpperInvariant(), Tracking);

        ImGui.SetCursorScreenPos(min);

        var clicked = ImGui.InvisibleButton(
            id, new Vector2(Metrics.TouchMin + Sp.Xs + text, height), ImGuiButtonFlags.MouseButtonLeft);

        var hovered = ImGui.IsItemHovered();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var warm = Motion.Toward($"backlink{id}", hovered, 20f);

        var lean = MathF.Round(-Sp.Xs * warm);
        var tint = Col.U32(Vector4.Lerp(Col.TextMuted, Theme.Accent, warm));

        using (iconFont.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList, FontAwesomeIcon.ChevronLeft,
                min + new Vector2((Metrics.TouchMin / 2f) + lean, height / 2f), tint);
        }

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList,
                new Vector2(min.X + Metrics.TouchMin + Sp.Xs + lean, min.Y + ((height - Text.CaptionSize) / 2f)),
                label.ToUpperInvariant(), tint, Tracking);
        }

        return clicked;
    }
}
