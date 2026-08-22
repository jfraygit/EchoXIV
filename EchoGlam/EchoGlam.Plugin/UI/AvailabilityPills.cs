using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoGlam.Game;

namespace EchoGlam.UI;

/// The little labels that say how you would get a piece, and the one word each of them is.
public static class AvailabilityPills
{
    public readonly record struct Pill(string Label, Vector4 Colour, string Tooltip, bool IsMarket);

    /// Every pill this item earns, in the order they should be drawn.
    public static List<Pill> For(GlamItem item)
    {
        var pills = new List<Pill>(2);

        if (item.IsStore)
        {
            pills.Add(new Pill(
                "Store", Theme.Warning,
                "Sold on the online store.", false));
        }

        if (item.IsSeasonal)
        {
            pills.Add(new Pill(
                "Seasonal", Theme.Vote,
                "From a seasonal event. Only available while the event is running.", false));
        }

        if (item.IsExclusive)
        {
            pills.Add(new Pill(
                "Exclusive", Theme.Accent,
                "A promotional, collaboration or pre-order item.", false));
        }

        if (item.IsMarketable)
        {
            pills.Add(new Pill(
                "Market", Theme.Good,
                "On the market board.", true));
        }

        return pills;
    }

    private static Vector2 Padding(float scale) => new(6f * scale, 2f * scale);

    public static float Width(string label, float scale) =>
        ImGui.CalcTextSize(label).X + (Padding(scale).X * 2f);

    public static float Height(float scale) =>
        ImGui.GetTextLineHeight() + (Padding(scale).Y * 2f);

    /// Draws one pill and says whether the pointer is on it.
    public static bool Draw(ImDrawListPtr drawList, Vector2 min, Pill pill, float scale, float minWidth = 0f)
    {
        var size = new Vector2(MathF.Max(Width(pill.Label, scale), minWidth), Height(scale));
        var max = min + size;
        var hovered = ImGui.IsMouseHoveringRect(min, max);

        var fill = pill.Colour with { W = hovered ? 0.28f : 0.16f };
        var edge = pill.Colour with { W = hovered ? 0.75f : 0.45f };

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), 4f * scale);
        drawList.AddRect(min, max, ImGui.GetColorU32(edge), 4f * scale, ImDrawFlags.None, 1f * scale);

        var text = ImGui.CalcTextSize(pill.Label);
        drawList.AddText(min + ((size - text) / 2f), ImGui.GetColorU32(pill.Colour), pill.Label);

        return hovered;
    }
}
