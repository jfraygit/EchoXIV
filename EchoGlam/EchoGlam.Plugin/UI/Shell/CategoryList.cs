using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Shell;

/// The master column of a master/detail screen: a vertical list of categories, one selected.
public sealed class CategoryList
{
    public readonly record struct Item(string Key, string Label, FontAwesomeIcon Icon, string Blurb);

    /// Eased offset of the selection marker, RELATIVE TO THE LIST'S TOP and never an absolute screen
    /// coordinate.
    private float markerOffset;

    private bool markerPlaced;

    /// Width of the column, in design units.
    public static float Width => 232f * UiHelpers.Scale;

    public static float RowHeight => 46f * UiHelpers.Scale;

    private static float Gap => Sp.Xs;

    /// Exactly the height this many rows occupy, for a caller putting something underneath.
    public static float HeightFor(int count) =>
        count <= 0 ? 0f : (RowHeight * count) + (Gap * (count - 1));

    /// Draws the list and returns the selected key, which differs from selectedKey on the frame a different
    /// row is clicked.
    public string Draw(
        Vector2 origin,
        IReadOnlyList<Item> items,
        string selectedKey,
        IFontHandle iconFont,
        Func<string, bool>? badged = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var picked = selectedKey;
        var selectedIndex = -1;

        for (var i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i].Key, selectedKey, StringComparison.Ordinal))
                selectedIndex = i;
        }

        var target = selectedIndex < 0 ? 0f : selectedIndex * (RowHeight + Gap);

        if (!markerPlaced)
        {
            markerOffset = target;
            markerPlaced = true;
        }
        else
        {
            markerOffset = UiHelpers.Lerp(
                markerOffset, target, Anim.SelectSpeed, Anim.Step(ImGui.GetIO().DeltaTime));
        }

        if (selectedIndex >= 0)
            PaintMarker(drawList, origin, markerOffset);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var min = new Vector2(origin.X, origin.Y + (i * (RowHeight + Gap)));
            var max = min + new Vector2(Width, RowHeight);
            var selected = i == selectedIndex;

            ImGui.SetCursorScreenPos(min);

            if (ImGui.InvisibleButton($"##cat{item.Key}", new Vector2(Width, RowHeight), ImGuiButtonFlags.MouseButtonLeft))
                picked = item.Key;

            var hovered = ImGui.IsItemHovered();

            if (hovered && !selected)
            {
                drawList.AddRectFilled(min, max, Col.U32(Col.CtlHover with { W = 0.5f }), Rad.Ctl);
            }

            var icon = min + new Vector2(Sp.Lg + (Metrics.TouchMin / 2f), RowHeight / 2f);

            using (iconFont.PushSafe())
            {
                UiHelpers.DrawScaledIcon(
                    drawList, item.Icon, icon,
                    Col.U32(selected ? Theme.Accent : hovered ? Col.Text : Col.TextDim));
            }

            var textLeft = min.X + Sp.Lg + Metrics.TouchMin + Sp.Lg;
            var pip = badged?.Invoke(item.Key) == true;
            var textRight = max.X - Sp.Lg - (pip ? Sp.Md : 0f);
            var room = MathF.Max(0f, textRight - textLeft);

            var captionLine = Text.CaptionSize * 1.15f;
            var block = ImGui.GetTextLineHeight() + Sp.Xxs + captionLine;
            var top = min.Y + ((RowHeight - block) / 2f);

            drawList.AddText(
                new Vector2(textLeft, top),
                Col.U32(selected ? Col.Text : hovered ? Col.Text : Col.TextDim),
                Text.Truncate(item.Label, room));

            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(textLeft, top + ImGui.GetTextLineHeight() + Sp.Xxs),
                    Col.U32(selected ? Col.TextDim : Col.TextMuted),
                    Text.Truncate(item.Blurb, room));
            }

            if (pip)
            {
                drawList.AddCircleFilled(
                    new Vector2(max.X - Sp.Lg, min.Y + (RowHeight / 2f)),
                    3f * UiHelpers.Scale, Col.U32(Theme.Accent), 12);
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(Width, HeightFor(items.Count)));

        return picked;
    }

    /// The selected row's body: a lit left rail and a tinted fill, drawn once at the eased position rather
    /// than per row.
    private static void PaintMarker(ImDrawListPtr drawList, Vector2 origin, float offset)
    {
        var min = new Vector2(origin.X, origin.Y + offset);
        var max = min + new Vector2(Width, RowHeight);

        Elev.Glow(drawList, min, max, Theme.Accent, 0.15f, Rad.Ctl, 8.4f);

        drawList.AddRectFilled(
            min, max, Col.U32(Vector4.Lerp(Col.Raised, Theme.Accent, 0.10f)), Rad.Ctl);

        drawList.AddLine(
            new Vector2(min.X + Rad.Ctl, min.Y + (Stroke.Hair / 2f)),
            new Vector2(max.X - Rad.Ctl, min.Y + (Stroke.Hair / 2f)),
            Col.U32(new Vector4(1f, 1f, 1f, 0.09f)), Stroke.Hair);

        drawList.AddRectFilled(
            min,
            new Vector2(min.X + (3f * UiHelpers.Scale), max.Y),
            Col.U32(Theme.Accent),
            Rad.Ctl,
            ImDrawFlags.RoundCornersLeft);
    }

    /// The key one place up or down from currentKey, or null if there is nowhere to move.
    public static string? StepKey(IReadOnlyList<Item> items, string currentKey, int delta)
    {
        if (items.Count == 0)
            return null;

        var current = -1;

        for (var i = 0; i < items.Count; i++)
        {
            if (!string.Equals(items[i].Key, currentKey, StringComparison.Ordinal))
                continue;

            current = i;
            break;
        }

        var next = current < 0 ? 0 : Math.Clamp(current + delta, 0, items.Count - 1);
        return next == current ? null : items[next].Key;
    }
}
