using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EchoGlam.UI.Foundation;

/// One labelled fact.
public readonly record struct MetaCell(
    string Label,
    string Value,
    Vector4? Colour = null,
    string? Tooltip = null,
    FontAwesomeIcon? Icon = null);

/// Labelled facts, laid out as tiles or as a run.
public static class Meta
{
    /// The gap kept between one tile and the next.
    private static float Gutter => Sp.Xl;

    /// How tall a tile is: a caption label over a title-sized number.
    public static float TileHeight =>
        (Text.CaptionSize * 1.15f) + Sp.Xxs + (Text.TitleSize * 1.15f);

    /// How wide a block of these tiles comes out.
    public static float Width(params MetaCell[] cells)
    {
        if (cells.Length == 0)
            return 0f;

        var widest = 0f;

        using (Text.PushCaption())
        {
            foreach (var cell in cells)
                widest = MathF.Max(widest, Text.Width(cell.Label.ToUpperInvariant()));
        }

        return (cells.Length * (widest + Gutter)) - Gutter;
    }

    /// Tiles across a width, sharing it equally.
    public static void Tiles(float width, params MetaCell[] cells)
    {
        if (cells.Length == 0)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var column = (width + Gutter) / cells.Length;
        var labelHeight = Text.CaptionSize * 1.15f;
        var height = TileHeight;
        var room = MathF.Max(0f, column - Gutter);

        for (var i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            var x = origin.X + (i * column);

            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(x, origin.Y),
                    Col.U32(Col.TextMuted),
                    Text.Truncate(cell.Label.ToUpperInvariant(), room));
            }

            using (Text.PushTitle())
            {
                drawList.AddText(
                    new Vector2(x, origin.Y + labelHeight + Sp.Xxs),
                    Col.U32(cell.Colour ?? Col.Text),
                    Text.Truncate(cell.Value, room));
            }
        }

        ImGui.Dummy(new Vector2(width, height));

        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i].Tooltip is not { } tooltip)
                continue;

            var min = new Vector2(origin.X + (i * column), origin.Y);

            if (ImGui.IsMouseHoveringRect(min, min + new Vector2(column, height)))
                UiHelpers.WrappedTooltip(tooltip);
        }
    }

    /// The same facts on one line, for somewhere too short for tiles - a card footer, a row.
    public static void Inline(float width, params MetaCell[] cells)
    {
        if (cells.Length == 0)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var x = origin.X;
        var baseline = origin.Y;

        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                using (Text.PushCaption())
                {
                    drawList.AddText(new Vector2(x, baseline), Col.U32(Col.TextMuted), "·");
                    x += Text.Width("·") + Sp.Sm;
                }
            }

            var cell = cells[i];
            var text = string.IsNullOrEmpty(cell.Label) ? cell.Value : $"{cell.Value} {cell.Label}";

            using (Text.PushCaption())
            {
                var measured = Text.Width(text);
                if (x + measured > origin.X + width)
                    break;

                drawList.AddText(new Vector2(x, baseline), Col.U32(cell.Colour ?? Col.TextDim), text);
                x += measured + Sp.Sm;
            }
        }

        ImGui.Dummy(new Vector2(width, Text.CaptionSize * 1.3f));
    }
}
