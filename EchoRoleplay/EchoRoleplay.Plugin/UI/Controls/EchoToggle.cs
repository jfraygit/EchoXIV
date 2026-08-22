using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoRoleplay.UI.Controls;

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

    /// Just the pill, at its own size, for a caller doing its own layout - see SettingsRow, which puts the
    /// label and its explanation on the other side of the row and needs the switch placed rather than
    /// followed.
    public static Vector2 PillSize => new Vector2(WidthDesign, HeightDesign) * UiHelpers.Scale;

    /// Draws the switch and its label.
    public static bool Draw(string id, string label, ref bool value, string? tooltip = null)
    {
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

        var pillMin = new Vector2(origin.X, origin.Y + ((rowHeight - height) / 2f));

        DrawPill(id, pillMin, value, hovered);

        ImGui.GetWindowDrawList().AddText(
            new Vector2(pillMin.X + width + gap, origin.Y + ((rowHeight - labelSize.Y) / 2f)),
            ImGui.GetColorU32(Theme.Text),
            label);

        return clicked;
    }

    /// A bare switch, submitted at the cursor with no label.
    public static bool Pill(string id, ref bool value)
    {
        var origin = ImGui.GetCursorScreenPos();
        var size = PillSize;

        var clicked = ImGui.InvisibleButton(id, size, ImGuiButtonFlags.MouseButtonLeft);

        if (clicked)
            value = !value;

        DrawPill(id, origin, value, ImGui.IsItemHovered() || RowHovered(origin, size));

        return clicked;
    }

    /// Whether the pointer is anywhere on the line the pill sits on.
    private static bool RowHovered(Vector2 pillOrigin, Vector2 size) =>
        ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)
        && ImGui.GetIO().MousePos.Y >= pillOrigin.Y
        && ImGui.GetIO().MousePos.Y <= pillOrigin.Y + size.Y;

    /// The pill itself: track, ring, and the knob that slides across it.
    private static void DrawPill(string id, Vector2 min, bool value, bool hovered)
    {
        var scale = UiHelpers.Scale;
        var width = WidthDesign * scale;
        var height = HeightDesign * scale;
        var radius = height / 2f;

        var drawList = ImGui.GetWindowDrawList();

        Slide.TryGetValue(id, out var slide);
        var target = value ? 1f : 0f;
        slide = UiHelpers.Lerp(slide, target, SlideSpeed, ImGui.GetIO().DeltaTime);
        if (MathF.Abs(slide - target) < 0.01f)
            slide = target;

        Slide[id] = slide;

        var max = min + new Vector2(width, height);
        var body = Vector4.Lerp(Off, Theme.Accent, slide);

        if (hovered)
            body = Vector4.Lerp(body, Vector4.One, 0.10f);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(body), radius);

        if (slide < 0.99f)
        {
            drawList.AddRect(
                min, max,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f * (1f - slide))),
                radius, ImDrawFlags.None, 1f * scale);
        }

        var knobCentre = new Vector2(min.X + radius + (slide * (width - height)), min.Y + radius);
        drawList.AddCircleFilled(knobCentre, radius - (3f * scale), ImGui.GetColorU32(Knob), 24);
    }
}
