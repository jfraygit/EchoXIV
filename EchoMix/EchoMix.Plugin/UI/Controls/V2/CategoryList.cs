using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Controls.V2;

/// The master column of a master/detail screen: a vertical list of categories, one selected.
public sealed class CategoryList
{
    public readonly record struct Item(string Key, string Label, FontAwesomeIcon Icon, string Blurb);

    private float markerOffset;
    private bool markerPlaced;

    /// Width the detail pane is laid out against.
    public static float DefaultWidth => MathF.Round(232f * Metrics.Scale);

    /// Exactly the height `count` rows occupy, for a screen that puts something else underneath the list
    /// rather than letting it have the whole column.
    public static float HeightFor(int count)
        => count <= 0 ? 0f : MathF.Round((RowHeight * count) + (Metrics.Sm * (count - 1)));

    private static float RowHeight => MathF.Round(Metrics.ControlXl + Metrics.Md);

    /// The key one place up or down from `currentKey`, or null if there is nowhere to move.
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

    /// Draws the list and returns the selected key, which differs from `selectedKey` on the frame a different
    /// row is clicked.
    public string Draw(
        string id,
        Vector2 origin,
        Vector2 size,
        IReadOnlyList<Item> items,
        string selectedKey,
        Func<string, bool>? badged = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var rowHeight = RowHeight;
        var stride = rowHeight + Metrics.Sm;
        var result = selectedKey;

        var selectedIndex = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Key == selectedKey)
            {
                selectedIndex = i;
                break;
            }
        }

        if (selectedIndex >= 0)
        {
            var target = selectedIndex * stride;
            if (!markerPlaced)
            {
                markerOffset = target;
                markerPlaced = true;
            }
            else
            {
                markerOffset = Motion.Approach(markerOffset, target, Motion.SpeedFast);
            }

            var min = Chrome.Snap(new Vector2(origin.X, origin.Y + markerOffset));
            var max = Chrome.Snap(min + new Vector2(size.X, rowHeight));
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(Elevation.Surface), Metrics.RadiusCard);
            drawList.AddRectFilled(min, new Vector2(min.X + MathF.Round(4f * Metrics.Scale), max.Y),
                ImGui.GetColorU32(Semantic.Primary), Metrics.RadiusCard, ImDrawFlags.RoundCornersLeft);
        }

        using var spacing = Sty.New().Var(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        foreach (var item in items)
        {
            var pos = ImGui.GetCursorScreenPos();

            if (ImGui.InvisibleButton($"{id}{item.Key}", new Vector2(size.X, rowHeight)))
                result = item.Key;

            var hovered = ImGui.IsItemHovered();
            var isSelected = item.Key == selectedKey;
            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (hovered && !isSelected)
                drawList.AddRectFilled(Chrome.Snap(pos), Chrome.Snap(pos + new Vector2(size.X, rowHeight)),
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.04f)), Metrics.RadiusCard);

            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, item.Icon,
                    Chrome.Snap(new Vector2(pos.X + Metrics.Xxl, pos.Y + (rowHeight * 0.5f))),
                    ImGui.GetColorU32(isSelected ? Semantic.Primary : Semantic.TextSecondary));

            var badgeRoom = 0f;
            if (badged?.Invoke(item.Key) == true)
            {
                var radius = MathF.Max(2.5f, 3.5f * Metrics.Scale);
                var centre = Chrome.Snap(new Vector2(
                    pos.X + size.X - Metrics.Lg - radius, pos.Y + (rowHeight * 0.5f)));

                var pulse = 0.65f + (Motion.Pulse(0.8f) * 0.35f);
                drawList.AddCircleFilled(centre, radius,
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.Warning, pulse)));

                badgeRoom = (radius * 2f) + Metrics.Md;
            }

            var textX = pos.X + Metrics.Xxl + Metrics.Xl;
            var textRoom = MathF.Max(Metrics.Xxl, size.X - (textX - pos.X) - Metrics.Lg - badgeRoom);

            var label = UiHelpers.TruncateToWidth(item.Label, textRoom);
            string[] blurbLines;
            using (TypeScale.Caption())
                blurbLines = UiHelpers.WrapToWidth(item.Blurb, textRoom, 2);

            var labelSize = TypeScale.Measure(TypeScale.Body, label);
            var blurbLineHeight = TypeScale.Measure(TypeScale.Caption, item.Blurb).Y;

            var block = labelSize.Y + Metrics.Xs + (blurbLines.Length * blurbLineHeight);
            var top = pos.Y + ((rowHeight - block) * 0.5f);

            using (TypeScale.Body())
                Chrome.Text(drawList, new Vector2(textX, top),
                    ImGui.GetColorU32(isSelected ? Semantic.TextPrimary : Semantic.TextSecondary), label);

            using (TypeScale.Caption())
            {
                for (var line = 0; line < blurbLines.Length; line++)
                {
                    Chrome.Text(drawList,
                        new Vector2(textX, top + labelSize.Y + Metrics.Xs + (line * blurbLineHeight)),
                        ImGui.GetColorU32(Semantic.TextTertiary), blurbLines[line]);
                }
            }

            ImGui.Dummy(new Vector2(0f, Metrics.Sm));
        }

        return result;
    }
}
