using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// One column in a split.
public readonly record struct ColumnSpec(float Weight, float MinDesign = 0f);

/// A responsive multi-column layout.
public sealed class ColumnSet : IDisposable
{
    private readonly List<(float Width, bool Dropped)> columns = [];
    private readonly float startX;
    private readonly float startY;
    private readonly float gutter;

    private int next;
    private float bottom;
    private bool inColumn;

    internal ColumnSet(IReadOnlyList<ColumnSpec> specs, float available, float gutter)
    {
        this.gutter = gutter;
        startX = ImGui.GetCursorPosX();
        startY = ImGui.GetCursorPosY();
        bottom = startY;

        var totalWeight = 0f;
        foreach (var spec in specs)
            totalWeight += spec.Weight;

        if (totalWeight <= 0f)
            totalWeight = 1f;

        var room = available - (gutter * Math.Max(0, specs.Count - 1));

        foreach (var spec in specs)
        {
            var width = room * (spec.Weight / totalWeight);

            if (Build.Diagnostics && spec.MinDesign > 0f && width < spec.MinDesign * UiHelpers.Scale)
            {
                Plugin.Log.Warning(
                    "[EchoGlam] Column wants {0} design units and the layout gives it {1}.",
                    spec.MinDesign,
                    width / MathF.Max(0.01f, UiHelpers.Scale));
            }

            columns.Add((width, false));
        }
    }

    /// Moves to the next column.
    public bool Next(out float width)
    {
        CloseColumn();

        while (next < columns.Count && columns[next].Dropped)
            next++;

        if (next >= columns.Count)
        {
            width = 0f;
            return false;
        }

        var x = startX;
        for (var i = 0; i < next; i++)
        {
            if (!columns[i].Dropped)
                x += columns[i].Width + gutter;
        }

        width = columns[next].Width;
        next++;

        ImGui.SetCursorPos(new Vector2(x, startY));
        ImGui.BeginGroup();
        inColumn = true;

        return true;
    }

    /// Whether the column at this index survived the current width.
    public bool Has(int index) => index < columns.Count && !columns[index].Dropped;

    private void CloseColumn()
    {
        if (!inColumn)
            return;

        ImGui.EndGroup();
        inColumn = false;

        bottom = MathF.Max(bottom, ImGui.GetCursorPosY());
    }

    public void Dispose()
    {
        CloseColumn();
        ImGui.SetCursorPos(new Vector2(startX, bottom));
    }
}

public static class Columns
{
    /// Opens a responsive split across the remaining content width.
    public static ColumnSet Begin(params ColumnSpec[] specs) =>
        Begin(ImGui.GetContentRegionAvail().X, Sp.Lg, specs);

    public static ColumnSet Begin(float available, float gutter, params ColumnSpec[] specs) =>
        new(specs, available, gutter);
}
