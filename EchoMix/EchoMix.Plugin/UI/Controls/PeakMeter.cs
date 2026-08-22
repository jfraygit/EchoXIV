using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Controls;

/// A classic hardware-style LED peak meter: discrete rectangular segments stacked with visible gaps between
/// them (rather than one continuous gradient bar), lighting up bottom to top - green, through yellow, to red
/// - as the smoothed peak level rises.
public static class PeakMeter
{
    private static readonly Dictionary<string, float> SmoothedByKey = new();

    public static void Draw(string id, Vector2 size, float peak)
    {
        var dt = ImGui.GetIO().DeltaTime;
        SmoothedByKey.TryGetValue(id, out var level);

        var target = Math.Clamp(peak * 1.4f, 0f, 1f);
        var speed = target > level ? 40f : 6f;        level = UiHelpers.Lerp(level, target, speed, dt);
        SmoothedByKey[id] = level;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);

        const int segmentCount = 14;
        const float segGap = 3f;
        var segHeight = (size.Y - (segGap * (segmentCount - 1))) / segmentCount;
        var litCount = (int)MathF.Round(level * segmentCount);

        for (var i = 0; i < segmentCount; i++)
        {
            var segBottom = origin.Y + size.Y - (i * (segHeight + segGap));
            var segTop = segBottom - segHeight;

            var segT = i / (float)(segmentCount - 1);            var litColor = segT < 0.6f
                ? Vector4.Lerp(new Vector4(0.2f, 0.85f, 0.3f, 1f), new Vector4(0.95f, 0.85f, 0.15f, 1f), segT / 0.6f)
                : Vector4.Lerp(new Vector4(0.95f, 0.85f, 0.15f, 1f), new Vector4(0.95f, 0.2f, 0.2f, 1f), (segT - 0.6f) / 0.4f);

            Vector4 color;
            if (i < litCount)
                color = litColor;
            else if (i == 0)
                color = new Vector4(litColor.X, litColor.Y, litColor.Z, 0.3f);            else
                color = new Vector4(0.09f, 0.09f, 0.11f, 1f);
            drawList.AddRectFilled(new Vector2(origin.X, segTop), new Vector2(origin.X + size.X, segBottom), ImGui.GetColorU32(color), 1f);
        }

        drawList.AddRect(origin, origin + size, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)), 2f, ImDrawFlags.None, 1f);
    }
}
