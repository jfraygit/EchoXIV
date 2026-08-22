using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Controls;

/// The sideways twin of Fader - a recessed housing panel, a lit center groove with tick marks, and a tall
/// draggable gradient cap, dragged left/right instead of up/down.
public static class HorizontalFader
{
    private const float CapWidth = 14f;

    private static readonly Dictionary<string, float> PendingByKey = new();

    /// activated - True on exactly the one frame this fader's drag/click starts (e.g.
    public static bool Draw(string id, ref float value, float min, float max, float defaultValue, Vector2 size, Vector4 leftAccent, Vector4 rightAccent, out bool activated, float? midValue = null)
    {
        var mid = midValue ?? ((min + max) / 2f);
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var tolerance = MathF.Max(0.0005f, (max - min) * 0.01f);
        if (PendingByKey.TryGetValue(id, out var pending))
        {
            if (MathF.Abs(value - pending) < tolerance)
                PendingByKey.Remove(id);            else
                value = pending;
        }

        ImGui.InvisibleButton(id, size);
        activated = ImGui.IsItemActivated();
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();

        var changed = false;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            value = defaultValue;
            changed = true;
        }
        else if (active)
        {
            var mouseX = ImGui.GetIO().MousePos.X;
            var travelLeft = origin.X + (CapWidth / 2f);
            var travelRight = origin.X + size.X - (CapWidth / 2f);
            var t = Math.Clamp((mouseX - travelLeft) / MathF.Max(1f, travelRight - travelLeft), 0f, 1f);
            value = PositionToValue(t, min, mid, max);
            changed = true;
        }

        if (changed)
            PendingByKey[id] = value;

        const float panelRounding = 5f;
        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.16f, 0.16f, 0.2f, 1f)), panelRounding);

        var borderMidX = origin.X + (size.X / 2f);
        drawList.PushClipRect(origin, new Vector2(borderMidX, origin.Y + size.Y), true);
        drawList.AddRect(origin, origin + size, ImGui.GetColorU32(new Vector4(leftAccent.X, leftAccent.Y, leftAccent.Z, 0.75f)), panelRounding, ImDrawFlags.None, 1.5f);
        drawList.PopClipRect();
        drawList.PushClipRect(new Vector2(borderMidX, origin.Y), origin + size, true);
        drawList.AddRect(origin, origin + size, ImGui.GetColorU32(new Vector4(rightAccent.X, rightAccent.Y, rightAccent.Z, 0.75f)), panelRounding, ImDrawFlags.None, 1.5f);
        drawList.PopClipRect();

        const float trackHeight = 5f;
        var trackY = origin.Y + (size.Y / 2f);
        var trackMidX = origin.X + (size.X / 2f);
        drawList.AddRectFilled(
            new Vector2(origin.X + 6f, trackY - (trackHeight / 2f)),
            new Vector2(origin.X + size.X - 6f, trackY + (trackHeight / 2f)),
            ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.03f, 1f)), 2f);
        drawList.AddRectFilled(
            new Vector2(origin.X + 6f, trackY - 1f), new Vector2(trackMidX, trackY + 1f),
            ImGui.GetColorU32(new Vector4(leftAccent.X, leftAccent.Y, leftAccent.Z, 0.4f)), 1f);
        drawList.AddRectFilled(
            new Vector2(trackMidX, trackY - 1f), new Vector2(origin.X + size.X - 6f, trackY + 1f),
            ImGui.GetColorU32(new Vector4(rightAccent.X, rightAccent.Y, rightAccent.Z, 0.4f)), 1f);

        const int ticks = 10;
        for (var i = 0; i <= ticks; i++)
        {
            var tx = origin.X + 6f + ((size.X - 12f) * i / ticks);
            var big = i % 5 == 0;
            var tickHeight = big ? size.Y * 0.55f : size.Y * 0.32f;
            var tickAlpha = big ? 0.35f : 0.18f;
            drawList.AddLine(
                new Vector2(tx, trackY - (tickHeight / 2f)), new Vector2(tx, trackY + (tickHeight / 2f)),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, tickAlpha)), 1f);
        }

        var travelLeft2 = origin.X + (CapWidth / 2f);
        var travelRight2 = origin.X + size.X - (CapWidth / 2f);
        var defaultT = ValueToPosition(defaultValue, min, mid, max);
        var defaultX = travelLeft2 + (defaultT * (travelRight2 - travelLeft2));
        drawList.AddLine(
            new Vector2(defaultX, trackY - (size.Y * 0.7f / 2f)), new Vector2(defaultX, trackY + (size.Y * 0.7f / 2f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.6f)), 1.5f);

        var travel = size.X - CapWidth;
        var t2 = ValueToPosition(value, min, mid, max);
        var capX = origin.X + (t2 * travel);
        var capMin = new Vector2(capX, origin.Y + 2f);
        var capMax = new Vector2(capX + CapWidth, origin.Y + size.Y - 2f);

        var capAccent = Vector4.Lerp(leftAccent, rightAccent, t2);

        drawList.AddRectFilled(capMin + new Vector2(2f, 0f), capMax + new Vector2(2f, 0f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.4f)), 3f);

        var mixAmount = active ? 0.05f : hovered ? 0.35f : 0.55f;
        var capLeft = Vector4.Lerp(capAccent, Vector4.One, active ? 0.35f : 0.15f);
        var capRight = Vector4.Lerp(capAccent, new Vector4(0.12f, 0.12f, 0.15f, 1f), mixAmount);
        drawList.AddRectFilledMultiColor(capMin, capMax,
            ImGui.GetColorU32(capLeft), ImGui.GetColorU32(capRight), ImGui.GetColorU32(capRight), ImGui.GetColorU32(capLeft));
        drawList.AddRect(capMin, capMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)), 3f, ImDrawFlags.None, 1f);

        var grooveX = capX + (CapWidth / 2f);
        drawList.AddLine(new Vector2(grooveX + 1f, capMin.Y + 3f), new Vector2(grooveX + 1f, capMax.Y - 3f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)), 1.5f);
        drawList.AddLine(new Vector2(grooveX - 1f, capMin.Y + 3f), new Vector2(grooveX - 1f, capMax.Y - 3f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)), 1f);

        if (hovered || active)
            ImGui.SetTooltip(value.ToString("0.00"));

        return changed;
    }

    /// Maps physical fader position (0 left..1 right) to a value, using the left half of the throw for (min,
    /// mid) and the right half for (mid, max) - same two-segment approach as the vertical Fader control, just
    /// left-to-right instead of bottom-to-top.
    private static float PositionToValue(float t, float min, float mid, float max)
    {
        return t <= 0.5f
            ? min + (t / 0.5f * (mid - min))
            : mid + ((t - 0.5f) / 0.5f * (max - mid));
    }

    /// Inverse of PositionToValue, used to place the cap (and the default- value detent mark) for a given
    /// value.
    private static float ValueToPosition(float value, float min, float mid, float max)
    {
        if (value <= mid)
        {
            var range = MathF.Max(1e-5f, mid - min);
            return Math.Clamp((value - min) / range, 0f, 1f) * 0.5f;
        }
        else
        {
            var range = MathF.Max(1e-5f, max - mid);
            return 0.5f + (Math.Clamp((value - mid) / range, 0f, 1f) * 0.5f);
        }
    }
}
