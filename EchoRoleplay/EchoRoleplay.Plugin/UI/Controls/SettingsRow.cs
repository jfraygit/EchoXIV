using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoRoleplay.UI.Controls;

/// One preference, drawn as a row: what it is on the left, what it does underneath, and the control that
/// changes it on the right.
public static class SettingsRow
{
    /// Air inside a row, around its contents.
    private static Vector2 Padding => new Vector2(10f, 9f) * UiHelpers.Scale;

    /// Between the words and the control.
    private static float Gutter => 18f * UiHelpers.Scale;

    /// Under a title, before its description.
    private static float TitleGap => 3f * UiHelpers.Scale;

    /// The narrowest the words are ever squeezed.
    private static float MinimumTextWidth => 120f * UiHelpers.Scale;

    /// A switch and its explanation.
    public static bool Toggle(string id, string title, string? description, ref bool value)
    {
        var control = EchoToggle.PillSize;
        var layout = Measure(title, description, control);

        Wash(layout);
        DrawWords(layout, title, description, Theme.Text);

        ImGui.SetCursorScreenPos(layout.Origin);

        var toggled = ImGui.InvisibleButton(
            $"{id}##row", new Vector2(ControlX(layout, control.X) - layout.Origin.X - Gutter, layout.Size.Y));

        var pillY = layout.Origin.Y + ((layout.Size.Y - control.Y) / 2f);
        ImGui.SetCursorScreenPos(new Vector2(ControlX(layout, control.X), pillY));

        if (EchoToggle.Pill($"{id}##pill", ref value))
            toggled = true;
        else if (toggled)
            value = !value;

        Close(layout);
        return toggled;
    }

    /// A row whose control the caller draws itself - a slider, a segmented control, a colour picker.
    public static Layout Begin(string title, string? description, float controlWidth, float controlHeight)
    {
        var layout = Measure(title, description, new Vector2(controlWidth, controlHeight));

        Wash(layout);
        DrawWords(layout, title, description, Theme.Text);

        ImGui.SetCursorScreenPos(new Vector2(
            ControlX(layout, controlWidth),
            layout.Origin.Y + ((layout.Size.Y - controlHeight) / 2f)));

        return layout;
    }

    /// Where a control of this width starts.
    private static float ControlX(Layout layout, float controlWidth) =>
        layout.Origin.X + layout.Size.X - Padding.X - controlWidth;

    public static void End(Layout layout) => Close(layout);

    /// A row that is only words - a note, or a reading with no control beside it.
    public static void Note(string title, string? description)
    {
        var layout = Measure(title, description, Vector2.Zero);

        DrawWords(layout, title, description, Theme.TextDim);
        Close(layout);
    }

    /// Between rows in the same card.
    public static void Divider()
    {
        var scale = UiHelpers.Scale;
        var width = Theme.ContentWidth;
        var inset = Padding.X;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.GetWindowDrawList().AddLine(
            new Vector2(origin.X + inset, origin.Y),
            new Vector2(origin.X + width - inset, origin.Y),
            ImGui.GetColorU32(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0.35f)),
            1f * scale);

        ImGui.Dummy(new Vector2(width, 1f * scale));
    }

    /// Where a row is and how big it turned out.
    public readonly record struct Layout(Vector2 Origin, Vector2 Size, float TextWidth);

    private static Layout Measure(string title, string? description, Vector2 control)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = Theme.ContentWidth;
        var padding = Padding;

        var reserved = control.X > 0f ? control.X + Gutter + padding.X : 0f;
        var textWidth = MathF.Max(MinimumTextWidth, width - (padding.X * 2f) - reserved);

        var height = ImGui.GetTextLineHeight();

        if (!string.IsNullOrEmpty(description))
            height += TitleGap + ImGui.CalcTextSize(description, false, textWidth).Y;

        height = MathF.Max(height, control.Y) + (padding.Y * 2f);

        return new Layout(origin, new Vector2(width, height), textWidth);
    }

    /// A faint wash under the pointer.
    private static void Wash(Layout layout)
    {
        if (!ImGui.IsMouseHoveringRect(layout.Origin, layout.Origin + layout.Size))
            return;

        ImGui.GetWindowDrawList().AddRectFilled(
            layout.Origin, layout.Origin + layout.Size,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.035f)), 6f * UiHelpers.Scale);
    }

    private static void DrawWords(Layout layout, string title, string? description, Vector4 titleColour)
    {
        var drawList = ImGui.GetWindowDrawList();
        var padding = Padding;
        var lineHeight = ImGui.GetTextLineHeight();

        var textHeight = lineHeight;

        if (!string.IsNullOrEmpty(description))
            textHeight += TitleGap + ImGui.CalcTextSize(description, false, layout.TextWidth).Y;

        var x = layout.Origin.X + padding.X;
        var y = layout.Origin.Y + ((layout.Size.Y - textHeight) / 2f);

        drawList.AddText(new Vector2(x, y), ImGui.GetColorU32(titleColour), title);

        if (string.IsNullOrEmpty(description))
            return;

        drawList.AddText(
            ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(x, y + lineHeight + TitleGap),
            ImGui.GetColorU32(Theme.TextDim), description, layout.TextWidth);
    }

    /// Puts the cursor at the row's foot regardless of what the caller drew, and leaves a zero-size item
    /// there so a surrounding group measures the row rather than its contents.
    private static void Close(Layout layout)
    {
        ImGui.SetCursorScreenPos(new Vector2(layout.Origin.X, layout.Origin.Y + layout.Size.Y));
        ImGui.Dummy(Vector2.Zero);
    }
}
