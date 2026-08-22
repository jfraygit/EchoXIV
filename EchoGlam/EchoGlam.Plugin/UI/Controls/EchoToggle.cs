using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Controls;

/// The suite's on/off switch: a pill that fills with the accent and slides its knob across.
public static class EchoToggle
{
    /// The pill's own geometry, in design pixels.
    private const float WidthDesign = 38f;
    private const float HeightDesign = 20f;
    private const float GapDesign = 10f;

    /// How fast the knob travels.
    private const float SlideSpeed = 18f;

    /// Knob position per control, keyed by id.
    private static readonly Dictionary<string, float> Slide = [];

    private static readonly Vector4 Off = new(0.24f, 0.24f, 0.29f, 1f);
    private static readonly Vector4 Knob = new(0.96f, 0.96f, 0.98f, 1f);

    /// How tall a switch draws, so a caller lining it up against something else can ask rather than assume.
    public static float Height => MathF.Max(HeightDesign * UiHelpers.Scale, ImGui.GetTextLineHeight());

    /// Draws the switch and its label.
    public static bool Draw(string id, string label, ref bool value, string? tooltip = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var scale = UiHelpers.Scale;
        var width = WidthDesign * scale;
        var height = HeightDesign * scale;
        var gap = GapDesign * scale;

        var labelSize = ImGui.CalcTextSize(label);
        var rowHeight = MathF.Max(height, labelSize.Y);

        var clicked = ImGui.InvisibleButton(id, new Vector2(width + gap + labelSize.X, rowHeight), ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        if (clicked)
            value = !value;

        if (hovered && !string.IsNullOrEmpty(tooltip))
            UiHelpers.WrappedTooltip(tooltip);

        Slide.TryGetValue(id, out var slide);
        var target = value ? 1f : 0f;
        slide = UiHelpers.Lerp(slide, target, SlideSpeed, ImGui.GetIO().DeltaTime);
        if (MathF.Abs(slide - target) < 0.01f)
            slide = target;

        Slide[id] = slide;

        var pillMin = new Vector2(origin.X, origin.Y + ((rowHeight - height) / 2f));
        var pillMax = pillMin + new Vector2(width, height);
        var radius = height / 2f;

        var body = Vector4.Lerp(Off, Theme.Accent, slide);
        if (hovered)
            body = Vector4.Lerp(body, Vector4.One, 0.10f);

        drawList.AddRectFilled(pillMin, pillMax, ImGui.GetColorU32(body), radius);

        if (slide < 0.99f)
        {
            drawList.AddRect(
                pillMin, pillMax,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f * (1f - slide))),
                radius, ImDrawFlags.None, 1f * scale);
        }

        var knobCentre = new Vector2(pillMin.X + radius + (slide * (width - height)), pillMin.Y + radius);
        drawList.AddCircleFilled(knobCentre, radius - (3f * scale), ImGui.GetColorU32(Knob), 24);

        drawList.AddText(
            new Vector2(pillMax.X + gap, origin.Y + ((rowHeight - labelSize.Y) / 2f)),
            ImGui.GetColorU32(Theme.Text),
            label);

        return clicked;
    }
}
