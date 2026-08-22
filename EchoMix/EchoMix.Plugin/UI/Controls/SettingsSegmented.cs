using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Controls;

/// A pill divided into equal segments with a sliding accent highlight behind whichever one is selected - the
/// same drawn-not-native language as SettingsToggle/SettingsSlider, sized for the handful of named-mode
/// pickers in Settings (Listen vs.
public static class SettingsSegmented
{
    private const float Height = 26f;
    private const float Speed = 20f;

    /// Per-control animation, keyed by the caller's id - same idiom as SettingsToggle.
    private static readonly Dictionary<string, float> Positions = new();

    public static bool Draw(string id, IReadOnlyList<string> labels, ref int selected, float width = 0f)
    {
        if (labels.Count == 0)
            return false;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var totalWidth = width > 0 ? width : ImGui.GetContentRegionAvail().X;
        var segmentWidth = totalWidth / labels.Count;

        var changed = false;
        ImGui.InvisibleButton(id, new Vector2(totalWidth, Height));
        var hovered = ImGui.IsItemHovered();

        if (hovered && ImGui.IsItemClicked())
        {
            var localX = ImGui.GetIO().MousePos.X - origin.X;
            var clickedIndex = Math.Clamp((int)(localX / segmentWidth), 0, labels.Count - 1);
            if (clickedIndex != selected)
            {
                selected = clickedIndex;
                changed = true;
            }
        }

        var target = (float)selected;
        var position = Positions.TryGetValue(id, out var stored) ? stored : target;
        position = UiHelpers.Lerp(position, target, Speed, ImGui.GetIO().DeltaTime);
        Positions[id] = position;

        var trackMin = origin;
        var trackMax = origin + new Vector2(totalWidth, Height);
        var rounding = Height / 2f;

        drawList.AddRectFilled(trackMin, trackMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.34f)), rounding);
        drawList.AddRect(trackMin, trackMax, ImGui.GetColorU32(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0.7f)), rounding, ImDrawFlags.None, 1.2f);

        var inset = 2.5f;
        var highlightMin = origin + new Vector2((position * segmentWidth) + inset, inset);
        var highlightMax = origin + new Vector2(((position + 1f) * segmentWidth) - inset, Height - inset);
        var highlightColor = hovered ? Vector4.Lerp(Theme.NeutralAccent, Vector4.One, 0.08f) : Theme.NeutralAccent;
        drawList.AddRectFilled(highlightMin, highlightMax, ImGui.GetColorU32(highlightColor), rounding - inset);

        var textDim = new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.5f);
        for (var i = 0; i < labels.Count; i++)
        {
            var labelSize = ImGui.CalcTextSize(labels[i]);
            var cellCentre = origin + new Vector2((i + 0.5f) * segmentWidth, Height / 2f);
            var textPos = cellCentre - (labelSize / 2f);

            var lit = Math.Clamp(1f - MathF.Abs(i - position), 0f, 1f);
            var color = Vector4.Lerp(textDim, Theme.Background, lit);
            drawList.AddText(textPos, ImGui.GetColorU32(color), labels[i]);
        }

        return changed;
    }
}
