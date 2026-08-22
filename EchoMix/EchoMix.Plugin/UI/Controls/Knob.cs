using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Controls;

/// A procedurally-drawn circular dial (no image assets) - a complete ring plus a pointer line via ImDrawList,
/// click-and-drag left/right to adjust, right-click to reset to a default value.
public static class Knob
{
    private const float StartAngle = 3f * MathF.PI / 4f;    private const float SweepAngle = 3f * MathF.PI / 2f;
    private static readonly Dictionary<string, float> PendingByKey = new();

    private static readonly Dictionary<string, float> SnapBackValueByKey = new();

    /// accent2 - When given, the ring is split into two halves (left = accent, right = accent2) instead of a
    /// single color, and the pointer blends between them by the knob's current position - used for the master
    /// volume knob to represent "both decks" instead of either deck's own accent.
    public static bool Draw(string id, ref float value, float min, float max, float defaultValue, float radius = 20f, Vector4? accent = null, Vector4? accent2 = null)
    {
        var accentColor = accent ?? Theme.CyanAccent;
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var diameter = radius * 2f;
        var center = pos + new Vector2(radius, radius);

        var tolerance = MathF.Max(0.0005f, (max - min) * 0.01f);
        if (PendingByKey.TryGetValue(id, out var pending))
        {
            if (MathF.Abs(value - pending) < tolerance)
                PendingByKey.Remove(id);            else
                value = pending;
        }

        ImGui.InvisibleButton(id, new Vector2(diameter, diameter));
        var activated = ImGui.IsItemActivated();
        var deactivated = ImGui.IsItemDeactivated();
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();

        if (activated && ImGui.GetIO().KeyShift)
            SnapBackValueByKey[id] = value;

        var changed = false;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            value = defaultValue;
            changed = true;
        }
        else if (active)
        {
            var dragDeltaX = ImGui.GetIO().MouseDelta.X;
            if (dragDeltaX != 0f)
            {
                var sensitivity = (max - min) / 200f;                value = Math.Clamp(value + (dragDeltaX * sensitivity), min, max);
                changed = true;
            }
        }

        if (deactivated && SnapBackValueByKey.Remove(id, out var snapBackValue))
        {
            value = snapBackValue;
            changed = true;
        }

        if (changed)
            PendingByKey[id] = value;

        var t = Math.Clamp((value - min) / (max - min), 0f, 1f);
        var valueAngle = StartAngle + (t * SweepAngle);

        var bodyColor = active ? Theme.AccentActive : hovered ? Theme.AccentHover : Theme.Panel;
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(bodyColor));

        Vector4 pointerColor;
        if (accent2.HasValue)
        {
            var ringAlpha = active || hovered ? 1f : 0.6f;
            drawList.PathArcTo(center, radius, MathF.PI / 2f, 3f * MathF.PI / 2f, 24);
            drawList.PathStroke(ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, ringAlpha)), ImDrawFlags.None, 1.5f);
            drawList.PathArcTo(center, radius, -MathF.PI / 2f, MathF.PI / 2f, 24);
            drawList.PathStroke(ImGui.GetColorU32(new Vector4(accent2.Value.X, accent2.Value.Y, accent2.Value.Z, ringAlpha)), ImDrawFlags.None, 1.5f);
            pointerColor = Vector4.Lerp(accentColor, accent2.Value, t);
        }
        else
        {
            var ringColor = active || hovered ? accentColor : Theme.Border;
            drawList.AddCircle(center, radius, ImGui.GetColorU32(ringColor), 0, 1.5f);
            pointerColor = accentColor;
        }

        var dir = new Vector2(MathF.Cos(valueAngle), MathF.Sin(valueAngle));
        drawList.AddLine(center + (dir * radius * 0.3f), center + (dir * radius * 0.85f), ImGui.GetColorU32(pointerColor), 2f);

        if (hovered)
            ImGui.SetTooltip(value.ToString("0.00"));

        return changed;
    }
}
