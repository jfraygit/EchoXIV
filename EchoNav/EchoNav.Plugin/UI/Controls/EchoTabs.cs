using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoNav.UI.Controls;

/// The suite's tab strip: words with a lit underline that slides between them.
public sealed class EchoTabs
{
    /// Animation rates, not lengths.
    private const float SlideSpeed = 22f;
    private const float FadeSeconds = 0.11f;

    private const float UnderlineHeightDesign = 2.5f;

    /// Gap either side of a label, which is also the click target's padding.
    private const float LabelPadXDesign = 11f;

    private const float StripPadYDesign = 7f;

    private float underlineX;
    private float underlineWidth;
    private bool underlinePlaced;

    private int selected;
    private int? pending;
    private float contentAlpha = 1f;

    public int Selected => selected;

    /// Alpha the tab's content should be drawn at, so a switch fades rather than cuts.
    public float ContentAlpha => contentAlpha;

    /// Draws the strip and returns the tab whose content belongs on screen this frame.
    public int Draw(string id, IReadOnlyList<string> labels, int dotOn = -1)
    {
        var deltaTime = ImGui.GetIO().DeltaTime;
        AdvanceFade(deltaTime);

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var stripWidth = ImGui.GetContentRegionAvail().X;

        var scale = UiHelpers.Scale;
        var labelPadX = LabelPadXDesign * scale;
        var stripPadY = StripPadYDesign * scale;
        var underlineHeight = UnderlineHeightDesign * scale;

        var lineHeight = ImGui.GetTextLineHeight();
        var stripHeight = lineHeight + (stripPadY * 2) + underlineHeight;

        var x = origin.X;
        var targetX = underlineX;
        var targetWidth = underlineWidth;

        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            var textWidth = ImGui.CalcTextSize(label).X;
            var itemWidth = textWidth + (labelPadX * 2);

            ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
            var clicked = ImGui.InvisibleButton($"{id}##tab{i}", new Vector2(itemWidth, stripHeight));
            var hovered = ImGui.IsItemHovered();

            if (clicked)
                Select(i);

            var isActive = i == selected;

            var colour = isActive
                ? Theme.Accent
                : hovered
                    ? Vector4.Lerp(Theme.TextDim, Theme.Text, 0.85f)
                    : Theme.TextDim;

            drawList.AddText(new Vector2(x + labelPadX, origin.Y + stripPadY), ImGui.GetColorU32(colour), label);

            if (hovered && !isActive)
            {
                var stubY = origin.Y + stripPadY + lineHeight + (3f * scale);
                drawList.AddRectFilled(
                    new Vector2(x + labelPadX, stubY),
                    new Vector2(x + labelPadX + textWidth, stubY + (1.5f * scale)),
                    ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.30f)),
                    1f * scale);
            }

            if (isActive)
            {
                targetX = x + labelPadX;
                targetWidth = textWidth;
            }

            if (i == dotOn)
            {
                drawList.AddCircleFilled(
                    new Vector2(x + itemWidth - (4f * scale), origin.Y + stripPadY + (2f * scale)),
                    3.5f * scale, ImGui.GetColorU32(Theme.Accent));
            }

            x += itemWidth;
        }

        if (!underlinePlaced)
        {
            underlineX = targetX;
            underlineWidth = targetWidth;
            underlinePlaced = true;
        }
        else
        {
            underlineX = UiHelpers.Lerp(underlineX, targetX, SlideSpeed, deltaTime);
            underlineWidth = UiHelpers.Lerp(underlineWidth, targetWidth, SlideSpeed, deltaTime);
        }

        var baselineY = origin.Y + stripPadY + lineHeight + (3f * scale);

        drawList.AddRectFilled(
            new Vector2(origin.X, baselineY),
            new Vector2(origin.X + stripWidth, baselineY + (1f * scale)),
            ImGui.GetColorU32(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0.5f)));

        drawList.AddRectFilled(
            new Vector2(underlineX - (1.5f * scale), baselineY - (1f * scale)),
            new Vector2(underlineX + underlineWidth + (1.5f * scale), baselineY + underlineHeight + (1.5f * scale)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.28f)), 3f * scale);

        drawList.AddRectFilled(
            new Vector2(underlineX, baselineY),
            new Vector2(underlineX + underlineWidth, baselineY + underlineHeight),
            ImGui.GetColorU32(Theme.Accent), 2f * scale);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + stripHeight));
        return selected;
    }

    private void Select(int index)
    {
        if (index == selected || pending == index)
            return;

        pending = index;
    }

    /// Fades out, swaps, fades back in.
    private void AdvanceFade(float deltaTime)
    {
        var step = deltaTime / FadeSeconds;

        if (pending is { } target)
        {
            contentAlpha = MathF.Max(0f, contentAlpha - step);
            if (contentAlpha <= 0f)
            {
                selected = target;
                pending = null;
            }

            return;
        }

        contentAlpha = MathF.Min(1f, contentAlpha + step);
    }
}
