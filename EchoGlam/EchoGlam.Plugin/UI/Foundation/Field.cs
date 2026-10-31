using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// A form field's label and the style its box wears.
public static class Field
{
    /// A field's name, with its budget at the right end of the same line.
    public static void Label(string label, float width) => Label(label, 0, 0, width);

    /// The same label with a budget on the end of its row.
    public static void Label(string label, int used, int limit, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        using (Text.PushCaption())
        {
            Text.Tracked(drawList, origin, label.ToUpperInvariant(), Col.U32(Col.TextMuted), 1.8f, width);

            if (limit > 0)
            {
                var count = $"{used}/{limit}";

                var colour = used >= limit
                    ? Col.Danger
                    : used > limit * 0.85f ? Col.Caution : Col.TextMuted with { W = 0.7f };

                drawList.AddText(
                    new Vector2(origin.X + width - Text.Width(count), origin.Y), Col.U32(colour), count);
            }

            ImGui.Dummy(new Vector2(width, LabelHeight));
        }
    }

    /// How much room Label takes, so a band budgeting its height can ask before drawing.
    public static float LabelHeight => (Text.CaptionSize * 1.4f) + Sp.Xs;

    /// How much room a text box's own frame takes beyond the text in it.
    public static float Inset => (ImGui.GetStyle().FramePadding.X * 2f) + (18f * UiHelpers.Scale);

    /// Rounded and padded to match the rest of the plugin's inputs.
    public static void PushStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Rad.Ctl);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Sp.XY(10f, 7f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Col.Sunken);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Lerp(Col.Sunken, Theme.Accent, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Lerp(Col.Sunken, Theme.Accent, 0.09f));
    }

    public static void PopStyle()
    {
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
    }
}
