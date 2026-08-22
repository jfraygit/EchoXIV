using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Controls;

/// A vertical hardware-style fader: a recessed housing panel, a lit center groove with tick marks, and a wide
/// draggable gradient cap - like a real mixer channel fader.
public static class Fader
{
    private const float CapHeight = 18f;

    private static readonly Dictionary<string, float> PendingByKey = new();

    /// activated - True on exactly the one frame this fader's drag/click starts (e.g.
    public static bool Draw(string id, ref float value, float min, float max, float defaultValue, Vector2 size, out bool activated, Vector4? accent = null, float? midValue = null)
    {
        var mid = midValue ?? ((min + max) / 2f);
        var accentColor = accent ?? Theme.CyanAccent;
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
            var mouseY = ImGui.GetIO().MousePos.Y;
            var travelTop = origin.Y + (CapHeight / 2f);
            var travelBottom = origin.Y + size.Y - (CapHeight / 2f);
            var t = Math.Clamp((travelBottom - mouseY) / MathF.Max(1f, travelBottom - travelTop), 0f, 1f);
            value = PositionToValue(t, min, mid, max);
            changed = true;
        }

        if (changed)
            PendingByKey[id] = value;

        const float panelRounding = 5f;
        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.16f, 0.16f, 0.2f, 1f)), panelRounding);
        drawList.AddRect(origin, origin + size, ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.4f)), panelRounding, ImDrawFlags.None, 1.2f);

        const float trackWidth = 5f;
        var trackX = origin.X + (size.X / 2f);
        drawList.AddRectFilled(
            new Vector2(trackX - (trackWidth / 2f), origin.Y + 6f),
            new Vector2(trackX + (trackWidth / 2f), origin.Y + size.Y - 6f),
            ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.03f, 1f)), 2f);
        drawList.AddRectFilled(
            new Vector2(trackX - 1f, origin.Y + 6f), new Vector2(trackX + 1f, origin.Y + size.Y - 6f),
            ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.3f)), 1f);

        const int ticks = 10;
        for (var i = 0; i <= ticks; i++)
        {
            var ty = origin.Y + 6f + ((size.Y - 12f) * i / ticks);
            var big = i % 5 == 0;
            var tickWidth = big ? size.X * 0.55f : size.X * 0.32f;
            var tickAlpha = big ? 0.35f : 0.18f;
            drawList.AddLine(
                new Vector2(trackX - (tickWidth / 2f), ty), new Vector2(trackX + (tickWidth / 2f), ty),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, tickAlpha)), 1f);
        }

        var midTravelTop = origin.Y + (CapHeight / 2f);
        var midTravelBottom = origin.Y + size.Y - (CapHeight / 2f);
        var midY = midTravelBottom - (0.5f * (midTravelBottom - midTravelTop));
        drawList.AddLine(
            new Vector2(trackX - (size.X * 0.7f / 2f), midY), new Vector2(trackX + (size.X * 0.7f / 2f), midY),
            ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.8f)), 1.5f);

        var travel = size.Y - CapHeight;
        var capY = origin.Y + ((1f - ValueToPosition(value, min, mid, max)) * travel);
        var capMin = new Vector2(origin.X + 2f, capY);
        var capMax = new Vector2(origin.X + size.X - 2f, capY + CapHeight);

        drawList.AddRectFilled(capMin + new Vector2(0f, 2f), capMax + new Vector2(0f, 2f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.4f)), 3f);

        var mixAmount = active ? 0.05f : hovered ? 0.35f : 0.55f;
        var capTop = Vector4.Lerp(accentColor, Vector4.One, active ? 0.35f : 0.15f);
        var capBottom = Vector4.Lerp(accentColor, new Vector4(0.12f, 0.12f, 0.15f, 1f), mixAmount);
        drawList.AddRectFilledMultiColor(capMin, capMax,
            ImGui.GetColorU32(capTop), ImGui.GetColorU32(capTop), ImGui.GetColorU32(capBottom), ImGui.GetColorU32(capBottom));
        drawList.AddRect(capMin, capMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)), 3f, ImDrawFlags.None, 1f);

        var grooveY = capY + (CapHeight / 2f);
        drawList.AddLine(new Vector2(capMin.X + 4f, grooveY + 1f), new Vector2(capMax.X - 4f, grooveY + 1f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)), 1.5f);
        drawList.AddLine(new Vector2(capMin.X + 4f, grooveY - 1f), new Vector2(capMax.X - 4f, grooveY - 1f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)), 1f);

        if (hovered || active)
            ImGui.SetTooltip(value.ToString("0.00"));

        return changed;
    }

    /// Maps physical fader position (0 bottom..1 top) to a value, using the bottom half of the throw for
    /// (min, mid) and the top half for (mid, max).
    private static float PositionToValue(float t, float min, float mid, float max)
    {
        return t <= 0.5f
            ? min + (t / 0.5f * (mid - min))
            : mid + ((t - 0.5f) / 0.5f * (max - mid));
    }

    /// Inverse of PositionToValue, used to place the cap for a given value.
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
