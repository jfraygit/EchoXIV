using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Controls;

/// A digital-styled progress bar for the deck display: dark track, tick marks, a glowing cyan fill, and a lit
/// playhead dot.
public static class SeekBar
{
    /// cueValue - If given, draws a small flag marker on the track at this position (the deck's cue point) -
    /// distinct from the playhead so both are visible at once.
    public static bool Draw(string id, ref float value, float min, float max, Vector2 size, Vector4? accent = null, float? cueValue = null, bool interactive = true)
    {
        var accentColor = accent ?? Theme.CyanAccent;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton(id, size);
        var active = interactive && ImGui.IsItemActive();
        var hovered = interactive && ImGui.IsItemHovered();
        var released = interactive && ImGui.IsItemDeactivated();

        if (active || released)
        {
            var mouseX = ImGui.GetIO().MousePos.X;
            var t = Math.Clamp((mouseX - origin.X) / size.X, 0f, 1f);
            value = min + (t * (max - min));
        }

        var range = MathF.Max(max - min, 0.0001f);
        var fillT = Math.Clamp((value - min) / range, 0f, 1f);
        var rounding = size.Y / 2f;

        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.03f, 0.03f, 0.045f, 1f)), rounding);
        drawList.AddRect(origin, origin + size, ImGui.GetColorU32(Theme.Border), rounding, ImDrawFlags.None, 1f);

        for (var i = 1; i < 10; i++)
        {
            var x = origin.X + (size.X * i / 10f);
            drawList.AddLine(new Vector2(x, origin.Y + 2f), new Vector2(x, origin.Y + size.Y - 2f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)), 1f);
        }

        var fillWidth = size.X * fillT;
        if (fillWidth > 1f)
        {
            var fillFlags = fillT >= 0.995f ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersLeft;
            drawList.AddRectFilled(origin, origin + new Vector2(fillWidth, size.Y),
                ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.9f)), rounding, fillFlags);
        }

        var headCenter = new Vector2(origin.X + fillWidth, origin.Y + (size.Y / 2f));
        var headColor = active || hovered ? Theme.Text : accentColor;
        drawList.AddCircleFilled(headCenter, size.Y * 1.1f, ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.3f)));
        drawList.AddCircleFilled(headCenter, size.Y * 0.55f, ImGui.GetColorU32(headColor));

        if (cueValue.HasValue)
        {
            var cueT = Math.Clamp((cueValue.Value - min) / range, 0f, 1f);
            var cueX = origin.X + (cueT * size.X);
            var flagColor = ImGui.GetColorU32(Theme.Text);
            var flagTop = origin.Y - 7f;
            var flagPoint = new Vector2(cueX, origin.Y);
            drawList.AddTriangleFilled(new Vector2(cueX - 4f, flagTop), new Vector2(cueX + 4f, flagTop), flagPoint, flagColor);
        }

        return released;
    }
}
