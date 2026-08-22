using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoSim.UI.Controls;

/// A pill switch, used everywhere the plugin would otherwise draw a checkbox.
public static class EchoToggle
{
    private const float Width = 34f;
    private const float Height = 18f;
    private const float LabelGap = 9f;
    private const float Speed = 20f;

    /// Per-control animation, keyed by the caller's id.
    private static readonly Dictionary<string, float> Positions = [];

    public static bool Draw(string id, string label, ref bool value, string? tooltip = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var labelSize = ImGui.CalcTextSize(label);

        var rowHeight = MathF.Max(Height, labelSize.Y);
        var totalWidth = Width + LabelGap + labelSize.X;

        var clicked = ImGui.InvisibleButton(id, new Vector2(totalWidth, rowHeight));
        var hovered = ImGui.IsItemHovered();

        if (clicked)
            value = !value;

        if (hovered && !string.IsNullOrEmpty(tooltip))
            UiHelpers.WrappedTooltip(tooltip);

        var target = value ? 1f : 0f;
        var position = Positions.TryGetValue(id, out var stored) ? stored : target;
        position = UiHelpers.Lerp(position, target, Speed, ImGui.GetIO().DeltaTime);
        Positions[id] = position;

        var trackMin = new Vector2(origin.X, origin.Y + ((rowHeight - Height) / 2f));
        var trackMax = trackMin + new Vector2(Width, Height);
        var rounding = Height / 2f;

        var off = new Vector4(0f, 0f, 0f, 0.34f);
        var on = Theme.Accent;
        var track = Vector4.Lerp(off, on, position);

        if (hovered)
            track = Vector4.Lerp(track, Vector4.One, 0.08f);

        drawList.AddRectFilled(trackMin, trackMax, ImGui.GetColorU32(track), rounding);

        var ring = Vector4.Lerp(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0.7f), on, position);
        drawList.AddRect(trackMin, trackMax, ImGui.GetColorU32(ring), rounding, ImDrawFlags.None, 1.2f);

        var knobRadius = (Height / 2f) - 2.5f;
        var knobX = trackMin.X + knobRadius + 2.5f + (position * (Width - (2 * (knobRadius + 2.5f))));
        var knobCentre = new Vector2(knobX, trackMin.Y + (Height / 2f));

        drawList.AddCircleFilled(knobCentre + new Vector2(0, 1f), knobRadius,
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.30f)));

        var knob = Vector4.Lerp(Theme.TextDim, Theme.Background, position);
        drawList.AddCircleFilled(knobCentre, knobRadius, ImGui.GetColorU32(knob));

        var labelColour = value ? Theme.Text : Theme.TextDim;
        drawList.AddText(
            new Vector2(trackMax.X + LabelGap, origin.Y + ((rowHeight - labelSize.Y) / 2f)),
            ImGui.GetColorU32(hovered ? Theme.Text : labelColour),
            label);

        return clicked;
    }
}
