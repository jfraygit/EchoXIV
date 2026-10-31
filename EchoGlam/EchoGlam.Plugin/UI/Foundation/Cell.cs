using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// One pickable cell in a grid: a gear icon, a hairstyle portrait, a numbered variation.
public static class Cell
{
    /// Paints a cell's body, its top-edge light and its edge.
    public static void Paint(
        ImDrawListPtr drawList, Vector2 min, Vector2 max, bool selected, bool hovered, bool glow = true)
    {
        if (selected && glow)
            Elev.Glow(drawList, min, max, Theme.Accent, 0.27f, Rad.Ctl, 7.8f);

        var body = selected
            ? Vector4.Lerp(Col.Raised, Theme.Accent, 0.16f)
            : hovered
                ? Col.RaisedHigh
                : Col.Raised;

        drawList.AddRectFilled(min, max, Col.U32(body), Rad.Ctl);

        if (selected || hovered)
        {
            drawList.AddLine(
                new Vector2(min.X + Rad.Ctl, min.Y + (Stroke.Hair / 2f)),
                new Vector2(max.X - Rad.Ctl, min.Y + (Stroke.Hair / 2f)),
                Col.U32(new Vector4(1f, 1f, 1f, 0.09f)), Stroke.Hair);
        }

        drawList.AddRect(
            min, max,
            Col.U32(selected
                ? Theme.Accent
                : hovered
                    ? Col.EdgeHover
                    : Col.EdgeRest with { W = 0.5f }),
            Rad.Ctl, ImDrawFlags.None, selected ? Stroke.Focus : Stroke.Hair);
    }

    /// How a cell's contents are coloured, so a label or a glyph inside one answers the pointer along with
    /// the body around it.
    public static Vector4 Ink(bool selected, bool hovered) =>
        selected ? Theme.Accent : hovered ? Col.Text : Col.TextDim;
}
