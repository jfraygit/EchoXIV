using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Controls;

/// A row of mutually exclusive choices as one control rather than several.
public static class EchoSegment
{
    private const float PaddingDesign = 15f;

    /// How fast the fill travels.
    private const float SlideSpeed = 18f;

    /// The unlit track, matching the toggle's off state exactly.
    private static readonly Vector4 Track = new(0.24f, 0.24f, 0.29f, 1f);

    /// Where each control's fill currently is, keyed by id.
    private static readonly Dictionary<string, float> Slide = [];

    /// Width this control needs for the given labels.
    public static float Width(ReadOnlySpan<string> labels)
    {
        var scale = UiHelpers.Scale;
        var total = 0f;

        foreach (var label in labels)
            total += ImGui.CalcTextSize(label).X + (PaddingDesign * 2f * scale);

        return total;
    }

    /// Draws the row and returns the index just chosen, or -1.
    public static int Draw(string id, ReadOnlySpan<string> labels, int selected, float height, string[]? tooltips = null)
    {
        var scale = UiHelpers.Scale;
        var padding = PaddingDesign * scale;
        var radius = height / 2f;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = Width(labels);

        ImGui.InvisibleButton(id, new Vector2(width, height), ImGuiButtonFlags.MouseButtonLeft);

        var pressed = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var mouse = ImGui.GetMousePos();
        var inside = ImGui.IsItemHovered();

        Span<float> starts = stackalloc float[labels.Length];
        Span<float> widths = stackalloc float[labels.Length];

        var x = 0f;
        for (var i = 0; i < labels.Length; i++)
        {
            starts[i] = x;
            widths[i] = ImGui.CalcTextSize(labels[i]).X + (padding * 2f);
            x += widths[i];
        }

        drawList.AddRectFilled(origin, origin + new Vector2(width, height), ImGui.GetColorU32(Track), radius);

        var target = (float)Math.Clamp(selected, 0, labels.Length - 1);

        if (!Slide.TryGetValue(id, out var slide))
            slide = target;

        slide = UiHelpers.Lerp(slide, target, SlideSpeed, ImGui.GetIO().DeltaTime);
        if (MathF.Abs(slide - target) < 0.005f)
            slide = target;

        Slide[id] = slide;

        var lower = Math.Clamp((int)MathF.Floor(slide), 0, labels.Length - 1);
        var upper = Math.Clamp(lower + 1, 0, labels.Length - 1);
        var t = slide - lower;

        var fillStart = starts[lower] + ((starts[upper] - starts[lower]) * t);
        var fillWidth = widths[lower] + ((widths[upper] - widths[lower]) * t);

        var inset = 2f * scale;
        drawList.AddRectFilled(
            origin + new Vector2(fillStart + inset, inset),
            origin + new Vector2(fillStart + fillWidth - inset, height - inset),
            ImGui.GetColorU32(Theme.Accent), radius - inset);

        var chosen = -1;

        for (var i = 0; i < labels.Length; i++)
        {
            var min = origin + new Vector2(starts[i], 0f);
            var max = min + new Vector2(widths[i], height);
            var over = inside && mouse.X >= min.X && mouse.X < max.X;

            var covered = Math.Clamp(1f - MathF.Abs(slide - i), 0f, 1f);

            var text = ImGui.CalcTextSize(labels[i]);
            var resting = over ? Theme.Text : Theme.TextDim;

            drawList.AddText(
                min + ((max - min - text) / 2f),
                ImGui.GetColorU32(Vector4.Lerp(resting, Theme.Background, covered)),
                labels[i]);

            if (!over)
                continue;

            if (tooltips is not null && i < tooltips.Length)
                UiHelpers.WrappedTooltip(tooltips[i]);

            if (pressed && i != selected)
                chosen = i;
        }

        return chosen;
    }
}
