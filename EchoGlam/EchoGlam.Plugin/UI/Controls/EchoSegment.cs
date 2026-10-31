using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Controls;

/// A row of mutually exclusive choices as one control rather than several.
public static class EchoSegment
{
    /// Air either side of a label.
    private const float PaddingDesign = 11f;

    /// Letter spacing on the labels, matching the masthead's index and the section headers.
    private const float TrackingDesign = 1.6f;

    /// Thickness of the mark under the active label.
    private static float MarkHeight => Stroke.Mark;

    /// How fast the fill travels.
    private const float SlideSpeed = Anim.SlideSpeed;

    /// Where each control's fill currently is, keyed by id.
    private static readonly Dictionary<string, float> Slide = [];

    /// Width this control needs for the given labels.
    public static float Width(ReadOnlySpan<string> labels)
    {
        var scale = UiHelpers.Scale;
        var total = 0f;

        using (Text.PushCaption())
        {
            foreach (var label in labels)
            {
                total += Text.TrackedWidth(label.ToUpperInvariant(), TrackingDesign)
                         + (PaddingDesign * 2f * scale);
            }
        }

        return total;
    }

    /// Draws the row and returns WHICH SEGMENT IS NOW SELECTED - the one just clicked, or the one that was
    /// already selected on a frame where nothing was clicked.
    public static int Draw(string id, ReadOnlySpan<string> labels, int selected, float height, string[]? tooltips = null)
    {
        var scale = UiHelpers.Scale;
        var padding = PaddingDesign * scale;

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

        using (Text.PushCaption())
        {
            for (var i = 0; i < labels.Length; i++)
            {
                starts[i] = x;
                widths[i] = Text.TrackedWidth(labels[i].ToUpperInvariant(), TrackingDesign) + (padding * 2f);
                x += widths[i];
            }
        }


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

        var markInset = padding * 0.65f;
        var markY = origin.Y + height - MarkHeight;

        var markMin = new Vector2(origin.X + fillStart + markInset, markY);
        var markMax = new Vector2(origin.X + fillStart + fillWidth - markInset, markY + MarkHeight);

        Elev.Glow(drawList, markMin, markMax, Theme.Accent, 0.44f, MarkHeight, 4f);
        drawList.AddRectFilled(markMin, markMax, Col.U32(Theme.Accent), MarkHeight / 2f);

        var chosen = -1;

        using (Text.PushCaption())
        {
            var lineHeight = Text.CaptionSize * 1.15f;

            for (var i = 0; i < labels.Length; i++)
            {
                var min = origin + new Vector2(starts[i], 0f);
                var max = min + new Vector2(widths[i], height);
                var over = inside && mouse.X >= min.X && mouse.X < max.X;

                var covered = Math.Clamp(1f - MathF.Abs(slide - i), 0f, 1f);
                var resting = over ? Col.Text : Col.TextDim;
                var label = labels[i].ToUpperInvariant();

                var width_ = Text.TrackedWidth(label, TrackingDesign);

                var at = new Vector2(
                    min.X + ((widths[i] - width_) / 2f),
                    min.Y + ((height - MarkHeight - lineHeight) / 2f));

                Text.Tracked(
                    drawList, at, label,
                    Col.U32(Vector4.Lerp(resting, Theme.Accent, covered)), TrackingDesign);

                if (!over)
                    continue;

                if (tooltips is not null && i < tooltips.Length)
                    UiHelpers.WrappedTooltip(tooltips[i]);

                if (pressed && i != selected)
                    chosen = i;
            }
        }

        return chosen >= 0 ? chosen : selected;
    }
}
