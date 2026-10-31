using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// What happened to a row this frame, and where its contents may go.
public readonly record struct RowResult(
    Vector2 Min,
    Vector2 Max,
    ControlState State,
    bool Hovered,
    bool Clicked,
    bool RightClicked)
{
    public float Height => Max.Y - Min.Y;

    public float Width => Max.X - Min.X;

    /// Vertically centres something of this height within the row.
    public float CentreY(float height) => Min.Y + ((Height - height) / 2f);

    /// The left edge of the row's text, inside its padding.
    public float TextLeft => Min.X + Row.PadX;

    /// The right edge available to content, inside its padding.
    public float ContentRight => Max.X - Row.PadX;
}

/// A row in a list: slot, friend, outfit, rule, source.
public static class Row
{
    /// Default row height in design units.
    public const float HeightDesign = 46f;

    public static float Height => HeightDesign * UiHelpers.Scale;

    public static float PadX => Sp.Lg;

    /// Between the two lines of TwoLines.
    private static float LineGap => Sp.Xxs;

    /// Where TwoLines puts its first line, so a caller can put something else ON that line - a badge after
    /// the name, a differently coloured warning in place of it.
    public static float PrimaryTop(RowResult row) =>
        row.CentreY(ImGui.GetTextLineHeight() + LineGap + (Text.CaptionSize * 1.15f));

    /// Draws the row's body and returns what happened to it.
    public static RowResult Draw(
        string id,
        float width,
        float? height = null,
        ControlState forced = ControlState.Rest,
        Vector4? accent = null,
        Vector4? tone = null,
        bool selected = false,
        bool enabled = true)
    {
        var rowHeight = height ?? Height;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        ImGui.InvisibleButton(id, new Vector2(width, rowHeight), ImGuiButtonFlags.MouseButtonLeft);

        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var clicked = enabled && ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var rightClicked = enabled && ImGui.IsItemClicked(ImGuiMouseButton.Right);

        ImGui.SetItemAllowOverlap();

        var state = forced != ControlState.Rest
            ? forced
            : ControlStates.Of(hovered, selected: selected, enabled: enabled);

        var min = origin;
        var max = origin + new Vector2(width, rowHeight);
        var rounding = Rad.Panel;

        var fill = state switch
        {
            ControlState.Selected => Col.SelRest,
            ControlState.Hover => Col.CtlHover,
            ControlState.Disabled => Col.Disabled(Col.Surface),
            _ => Theme.Tinted(0.035f),
        };

        if (tone is { } t)
            fill = Vector4.Lerp(Col.Surface, t, 0.16f);

        drawList.AddRectFilled(min, max, Col.U32(fill), rounding);

        if (tone is { } edge)
        {
            drawList.AddRect(
                min, max, Col.U32(edge with { W = 0.6f }), rounding, ImDrawFlags.None, Stroke.Edge);
        }
        else if (state is ControlState.Hover or ControlState.Selected)
        {
            drawList.AddRect(
                min, max, Col.U32(Col.EdgeRest), rounding, ImDrawFlags.None, Stroke.Edge);
        }

        if (accent is { } rail)
        {
            drawList.AddRectFilled(
                min,
                new Vector2(min.X + (3f * UiHelpers.Scale), max.Y),
                Col.U32(rail),
                rounding,
                ImDrawFlags.RoundCornersLeft);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y));

        return new RowResult(min, max, state, hovered, clicked, rightClicked);
    }

    /// A name above a dimmer line of detail, vertically centred - what every row in the plugin puts on its
    /// left.
    public static void TwoLines(RowResult row, float textWidth, string primary, string secondary) =>
        TwoLines(row, row.TextLeft, textWidth, primary, secondary);

    /// The same block, starting wherever the caller says.
    public static void TwoLines(
        RowResult row, float left, float textWidth, string primary, string secondary)
    {
        var drawList = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeight();
        var gap = LineGap;
        var top = PrimaryTop(row);

        drawList.AddText(
            new Vector2(left, top),
            Col.U32(row.State == ControlState.Disabled ? Col.TextMuted : Col.Text),
            Text.Truncate(primary, textWidth));

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(left, top + lineHeight + gap),
                Col.U32(Col.TextDim),
                Text.Truncate(secondary, textWidth));
        }
    }

    /// Lays controls out from the row's right edge leftwards, which is how every row in the plugin places its
    /// buttons.
    public static float PlaceRight(ref float x, float width, RowResult row, float height)
    {
        x -= width;
        ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(height)));
        var placed = x;
        x -= Sp.Sm;
        return placed;
    }
}
