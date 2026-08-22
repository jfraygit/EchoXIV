using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoRoleplay.UI.Controls;

/// The suite's slider, ported from EchoSim rather than reinvented: a thin recessed track, a lit accent fill,
/// and a round grab that grows and haloes as you take hold of it.
public static class EchoSlider
{
    /// The slider's own geometry, in design pixels.
    private const float TrackHeightDesign = 6f;
    private const float GrabRadiusDesign = 8f;
    private const float ActiveGrabRadiusDesign = 10f;

    /// Row height.
    private const float RowHeightDesign = 24f;

    /// How tall a slider draws, for a caller centring it against something else.
    public static float Height => RowHeightDesign * UiHelpers.Scale;

    /// Track geometry and grab offset, captured when a drag starts and held until it ends.
    private readonly record struct DragState(float TravelMin, float Travel, float GrabOffset);

    private static readonly Dictionary<string, DragState> Drags = [];

    /// Draws a slider spanning the available width.
    public static bool Draw(
        string id,
        ref float value,
        float min,
        float max,
        float width = 0f,
        string? format = null,
        float? resetTo = null,
        IReadOnlyList<float>? ticks = null)
    {
        if (max <= min)
            return false;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var scale = UiHelpers.Scale;
        var trackHeight = TrackHeightDesign * scale;
        var grabRadius = GrabRadiusDesign * scale;
        var rowHeight = RowHeightDesign * scale;

        var trackWidth = width > 0 ? width : ImGui.GetContentRegionAvail().X;

        ImGui.InvisibleButton(id, new Vector2(trackWidth, rowHeight), ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        var changed = false;
        var centreY = origin.Y + (rowHeight / 2f);

        var travelMin = origin.X + grabRadius;
        var travelMax = origin.X + trackWidth - grabRadius;
        var travel = MathF.Max(1f, travelMax - travelMin);

        if (ImGui.IsItemActivated())
        {
            var grabAt = travelMin + (((value - min) / (max - min)) * travel);
            var pressX = ImGui.GetIO().MousePos.X;

            var onGrab = MathF.Abs(pressX - grabAt) <= grabRadius + (3f * scale);
            Drags[id] = new DragState(travelMin, travel, onGrab ? pressX - grabAt : 0f);
        }

        if (active)
        {
            var drag = Drags.TryGetValue(id, out var stored)
                ? stored
                : new DragState(travelMin, travel, 0f);

            var mouseX = ImGui.GetIO().MousePos.X - drag.GrabOffset;
            var t = Math.Clamp((mouseX - drag.TravelMin) / drag.Travel, 0f, 1f);
            var target = min + (t * (max - min));

            value = ImGui.GetIO().KeyCtrl ? value + ((target - value) * 0.12f) : target;
            changed = true;
        }

        if (ImGui.IsItemDeactivated())
            Drags.Remove(id);

        if (resetTo is { } reset
            && ImGui.IsItemHovered()
            && (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
        {
            value = reset;
            changed = true;
        }

        value = Math.Clamp(value, min, max);
        var fraction = (value - min) / (max - min);
        var grabX = travelMin + (fraction * travel);

        var trackMin = new Vector2(origin.X, centreY - (trackHeight / 2f));
        var trackMax = new Vector2(origin.X + trackWidth, centreY + (trackHeight / 2f));
        var rounding = trackHeight / 2f;

        drawList.AddRectFilled(trackMin, trackMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.32f)), rounding);
        drawList.AddRectFilled(
            trackMin, new Vector2(trackMax.X, trackMin.Y + (1.5f * scale)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.22f)), rounding);

        if (ticks is { Count: > 0 })
        {
            foreach (var tick in ticks)
            {
                if (tick <= min || tick >= max)
                    continue;

                var tx = travelMin + (((tick - min) / (max - min)) * travel);
                var lit = tick <= value;

                drawList.AddRectFilled(
                    new Vector2(tx - (0.75f * scale), centreY + rounding + (2f * scale)),
                    new Vector2(tx + (0.75f * scale), centreY + rounding + (6f * scale)),
                    ImGui.GetColorU32(lit
                        ? new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.55f)
                        : new Vector4(Theme.TextDisabled.X, Theme.TextDisabled.Y, Theme.TextDisabled.Z, 0.5f)));
            }
        }

        if (fraction > 0.001f)
        {
            var fillMax = new Vector2(grabX, trackMax.Y);
            drawList.AddRectFilled(trackMin, fillMax, ImGui.GetColorU32(Theme.AccentActive), rounding);
            drawList.AddRectFilledMultiColor(
                new Vector2(trackMin.X + rounding, trackMin.Y), fillMax,
                ImGui.GetColorU32(Theme.AccentActive), ImGui.GetColorU32(Theme.Accent),
                ImGui.GetColorU32(Theme.Accent), ImGui.GetColorU32(Theme.AccentActive));
        }

        var radius = active ? ActiveGrabRadiusDesign * scale : hovered ? grabRadius + (1f * scale) : grabRadius;
        var centre = new Vector2(grabX, centreY);

        if (hovered || active)
        {
            drawList.AddCircleFilled(centre, radius + ((active ? 7f : 4f) * scale),
                ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, active ? 0.22f : 0.14f)));
        }

        drawList.AddCircleFilled(centre, radius, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)));
        drawList.AddCircleFilled(centre, radius - (1f * scale), ImGui.GetColorU32(Theme.Panel));
        drawList.AddCircle(centre, radius - (1f * scale), ImGui.GetColorU32(Theme.Accent), 0, (active ? 2.4f : 1.8f) * scale);
        drawList.AddCircleFilled(centre, radius - (4f * scale), ImGui.GetColorU32(active || hovered ? Theme.AccentHover : Theme.Accent));

        if (active && format is not null)
            DrawBubble(drawList, centre, radius, string.Format(format, value), scale);

        return changed;
    }

    /// A value bubble above the grab, so the number is where the eye already is.
    private static void DrawBubble(ImDrawListPtr drawList, Vector2 grabCentre, float radius, string text, float scale)
    {
        var textSize = ImGui.CalcTextSize(text);
        var padding = new Vector2(7f, 3f) * scale;
        var size = textSize + (padding * 2f);
        var min = new Vector2(grabCentre.X - (size.X / 2f), grabCentre.Y - radius - size.Y - (7f * scale));
        var max = min + size;

        drawList.AddRectFilled(min + new Vector2(0, 1.5f * scale), max + new Vector2(0, 1.5f * scale),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)), 5f * scale);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Background), 5f * scale);
        drawList.AddRect(min, max, ImGui.GetColorU32(Theme.Accent), 5f * scale, ImDrawFlags.None, 1.2f * scale);
        drawList.AddText(min + padding, ImGui.GetColorU32(Theme.Text), text);
    }

    /// Integer-valued convenience wrapper, since most sliders in the suite are whole numbers.
    public static bool DrawInt(
        string id,
        ref int value,
        int min,
        int max,
        float width = 0f,
        string? format = null,
        int? resetTo = null,
        IReadOnlyList<float>? ticks = null)
    {
        var asFloat = (float)value;
        if (!Draw(id, ref asFloat, min, max, width, format, resetTo, ticks))
            return false;

        var rounded = (int)MathF.Round(asFloat);
        if (rounded == value)
            return false;

        value = rounded;
        return true;
    }
}
