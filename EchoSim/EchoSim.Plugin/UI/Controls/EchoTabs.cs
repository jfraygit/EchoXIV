using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;

namespace EchoSim.UI.Controls;

/// The plugin's tab strip: words with a lit underline that slides between them.
public sealed class EchoTabs
{
    /// How fast the underline chases the selected tab.
    private const float SlideSpeed = 22f;

    private const float FadeSeconds = 0.11f;

    private const float UnderlineHeight = 2.5f;

    /// Gap either side of a label, which is also the strip's click target padding.
    private const float LabelPadX = 11f;

    private const float StripPadY = 7f;

    private float underlineX;
    private float underlineWidth;
    private bool underlinePlaced;

    private int selected;
    private int? pending;
    private float contentAlpha = 1f;

    public int Selected => selected;

    /// Alpha the tab's content should be drawn at.
    public float ContentAlpha => contentAlpha;

    /// Jumps straight to a tab with no animation - for restoring state, not for clicks.
    public void SetImmediate(int index)
    {
        selected = index;
        pending = null;
        contentAlpha = 1f;
        underlinePlaced = false;
    }

    /// Draws the strip and returns the tab whose content should be drawn this frame.
    public int Draw(string id, IReadOnlyList<string> labels, IFontHandle? font = null, float badgeOn = -1, string? badgeText = null, bool fill = false)
    {
        var deltaTime = ImGui.GetIO().DeltaTime;
        AdvanceFade(deltaTime);

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var stripWidth = ImGui.GetContentRegionAvail().X;

        var labelPadX = UiHelpers.S(LabelPadX);
        var stripPadY = UiHelpers.S(StripPadY);
        var underlineHeight = UiHelpers.S(UnderlineHeight);

        using (font?.PushSafe())
        {
            var lineHeight = ImGui.GetTextLineHeight();
            var stripHeight = lineHeight + (stripPadY * 2) + underlineHeight;

            var slack = 0f;
            if (fill && labels.Count > 0)
            {
                var natural = (labelPadX * 2 * labels.Count);
                foreach (var label in labels)
                    natural += ImGui.CalcTextSize(label).X;

                slack = Math.Max(0f, stripWidth - natural) / labels.Count;
            }

            var x = origin.X;
            var targetX = underlineX;
            var targetWidth = underlineWidth;

            for (var i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                var textWidth = ImGui.CalcTextSize(label).X;
                var itemWidth = textWidth + (labelPadX * 2) + slack;

                var textOffset = (itemWidth - textWidth) * 0.5f;

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

                var textPos = new Vector2(x + textOffset, origin.Y + stripPadY);
                drawList.AddText(textPos, ImGui.GetColorU32(colour), label);

                if (hovered && !isActive)
                {
                    var stubY = origin.Y + stripPadY + lineHeight + UiHelpers.S(3f);
                    drawList.AddRectFilled(
                        new Vector2(x + textOffset, stubY),
                        new Vector2(x + textOffset + textWidth, stubY + UiHelpers.S(1.5f)),
                        ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.30f)),
                        1f);
                }

                if (isActive)
                {
                    targetX = x + textOffset;
                    targetWidth = textWidth;
                }

                if (badgeText is not null && System.Math.Abs(badgeOn - i) < 0.5f)
                    DrawBadge(drawList, new Vector2(x + itemWidth - 4f, origin.Y + stripPadY), badgeText);

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

            var baselineY = origin.Y + stripPadY + lineHeight + UiHelpers.S(3f);

            drawList.AddRectFilled(
                new Vector2(origin.X, baselineY),
                new Vector2(origin.X + stripWidth, baselineY + 1f),
                ImGui.GetColorU32(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0.5f)));

            var glow = new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.28f);
            drawList.AddRectFilled(
                new Vector2(underlineX - 1.5f, baselineY - 1f),
                new Vector2(underlineX + underlineWidth + 1.5f, baselineY + underlineHeight + UiHelpers.S(1.5f)),
                ImGui.GetColorU32(glow), 3f);

            drawList.AddRectFilled(
                new Vector2(underlineX, baselineY),
                new Vector2(underlineX + underlineWidth, baselineY + underlineHeight),
                ImGui.GetColorU32(Theme.Accent), 2f);

            ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + stripHeight));
        }

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

    private static void DrawBadge(ImDrawListPtr drawList, Vector2 anchor, string text)
    {
        var centre = new Vector2(anchor.X, anchor.Y + 2f);

        if (string.IsNullOrEmpty(text))
        {
            drawList.AddCircleFilled(centre, 4f, ImGui.GetColorU32(Theme.Accent));
            return;
        }

        var textSize = ImGui.CalcTextSize(text);
        var radius = MathF.Max(8f, (textSize.X / 2f) + 3f);

        drawList.AddCircleFilled(centre, radius, ImGui.GetColorU32(Theme.Accent));
        drawList.AddText(centre - (textSize / 2f), ImGui.GetColorU32(Theme.Background), text);
    }
}
