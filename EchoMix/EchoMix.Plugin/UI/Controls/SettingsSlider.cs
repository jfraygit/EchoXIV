using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Controls;

/// Ported from EchoSim's own EchoSlider - a thin recessed track, a lit accent fill, and a round grab that
/// grows and haloes as you take hold of it.
public static class SettingsSlider
{
    private const float TrackHeight = 6f;
    private const float GrabRadius = 8f;
    private const float ActiveGrabRadius = 10f;

    /// Row height.
    private const float RowHeight = 24f;

    /// Track geometry and grab offset, captured when a drag starts and held until it ends.
    private readonly record struct DragState(float TravelMin, float Travel, float GrabOffset);

    private static readonly Dictionary<string, DragState> Drags = new();

    /// Draws a slider spanning the available width (or `width`, if given).
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
        var trackWidth = width > 0 ? width : ImGui.GetContentRegionAvail().X;

        ImGui.InvisibleButton(id, new Vector2(trackWidth, RowHeight));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        var changed = false;
        var centreY = origin.Y + (RowHeight / 2f);

        var travelMin = origin.X + GrabRadius;
        var travelMax = origin.X + trackWidth - GrabRadius;
        var travel = MathF.Max(1f, travelMax - travelMin);

        if (ImGui.IsItemActivated())
        {
            var grabAt = travelMin + (((value - min) / (max - min)) * travel);
            var pressX = ImGui.GetIO().MousePos.X;

            var onGrab = MathF.Abs(pressX - grabAt) <= GrabRadius + 3f;
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

        if (resetTo is { } reset && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            value = reset;
            changed = true;
        }

        value = Math.Clamp(value, min, max);
        var fraction = (value - min) / (max - min);
        var grabX = travelMin + (fraction * travel);

        var trackMin = new Vector2(origin.X, centreY - (TrackHeight / 2f));
        var trackMax = new Vector2(origin.X + trackWidth, centreY + (TrackHeight / 2f));
        var rounding = TrackHeight / 2f;

        drawList.AddRectFilled(trackMin, trackMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.32f)), rounding);
        drawList.AddRectFilled(
            trackMin, new Vector2(trackMax.X, trackMin.Y + 1.5f),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.22f)), rounding);

        if (ticks is { Count: > 0 })
        {
            var unlitTick = new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.35f);
            foreach (var tick in ticks)
            {
                if (tick <= min || tick >= max)
                    continue;

                var tx = travelMin + (((tick - min) / (max - min)) * travel);
                var lit = tick <= value;

                drawList.AddRectFilled(
                    new Vector2(tx - 0.75f, centreY + rounding + 2f),
                    new Vector2(tx + 0.75f, centreY + rounding + 6f),
                    ImGui.GetColorU32(lit
                        ? new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.55f)
                        : unlitTick));
            }
        }

        if (fraction > 0.001f)
        {
            var fillMax = new Vector2(grabX, trackMax.Y);
            drawList.AddRectFilled(trackMin, fillMax, ImGui.GetColorU32(Theme.NeutralAccentActive), rounding);
            drawList.AddRectFilledMultiColor(
                new Vector2(trackMin.X + rounding, trackMin.Y), fillMax,
                ImGui.GetColorU32(Theme.NeutralAccentActive), ImGui.GetColorU32(Theme.NeutralAccent),
                ImGui.GetColorU32(Theme.NeutralAccent), ImGui.GetColorU32(Theme.NeutralAccentActive));
        }

        var radius = active ? ActiveGrabRadius : hovered ? GrabRadius + 1f : GrabRadius;
        var centre = new Vector2(grabX, centreY);

        if (hovered || active)
        {
            drawList.AddCircleFilled(centre, radius + (active ? 7f : 4f),
                ImGui.GetColorU32(new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, active ? 0.22f : 0.14f)));
        }

        drawList.AddCircleFilled(centre, radius, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)));
        drawList.AddCircleFilled(centre, radius - 1f, ImGui.GetColorU32(Theme.Panel));
        drawList.AddCircle(centre, radius - 1f, ImGui.GetColorU32(Theme.NeutralAccent), 0, active ? 2.4f : 1.8f);
        drawList.AddCircleFilled(centre, radius - 4f, ImGui.GetColorU32(active || hovered ? Theme.NeutralAccentHover : Theme.NeutralAccent));

        if (active && format is not null)
            DrawBubble(drawList, centre, radius, string.Format(format, value));

        return changed;
    }

    /// A value bubble above the grab, so the number is where the eye already is.
    private static void DrawBubble(ImDrawListPtr drawList, Vector2 grabCentre, float radius, string text)
    {
        var textSize = ImGui.CalcTextSize(text);
        var padding = new Vector2(7f, 3f);
        var size = textSize + (padding * 2f);
        var min = new Vector2(grabCentre.X - (size.X / 2f), grabCentre.Y - radius - size.Y - 7f);
        var max = min + size;

        drawList.AddRectFilled(min + new Vector2(0, 1.5f), max + new Vector2(0, 1.5f),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)), 5f);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Background), 5f);
        drawList.AddRect(min, max, ImGui.GetColorU32(Theme.NeutralAccent), 5f, ImDrawFlags.None, 1.2f);
        drawList.AddText(min + padding, ImGui.GetColorU32(Theme.Text), text);
    }

    /// Integer-valued convenience wrapper, for a settings value that's naturally a whole number.
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

    /// Right-click-to-reset gesture paired with a slider call - relies on ImGui.IsItemHovered() referring to
    /// the slider's own InvisibleButton, so it must be called immediately after a Draw()/DrawInt() call and
    /// nothing else in between.
    public static void ResetOnRightClick(Action reset)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Right-click to reset to default.");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                reset();
        }
    }
}
