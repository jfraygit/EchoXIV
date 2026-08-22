using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;

namespace EchoMix.Plugin.UI.Controls;

/// The plugin's tab strip: words with a lit underline that slides between them.
public sealed class TabStrip
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

    private readonly bool selfContained;

    public TabStrip(bool selfContained = true) => this.selfContained = selfContained;

    public int Selected => selected;

    /// The tab index a fresh click requested THIS Draw() call, or null if none - set the instant a click
    /// registers, unlike Selected/Draw()'s own return value (which only updates once this strip's own
    /// content-crossfade finishes, ~FadeSeconds later).
    public int? JustClicked { get; private set; }

    /// Alpha the tab's content should be drawn at, for a caller that lets this strip own the whole tab-switch
    /// transition (e.g.
    public float ContentAlpha => contentAlpha;

    /// Jumps straight to a tab with no animation - for restoring state, not for clicks.
    public void SetImmediate(int index)
    {
        selected = index;
        pending = null;
        contentAlpha = 1f;
        underlinePlaced = false;
    }

    /// Corrects drift from some externally-driven selection this strip's tabs mirror (e.g.
    public void Sync(int index)
    {
        if (pending != null || index == selected)
            return;

        SetImmediate(index);
    }

    /// Draws the strip and returns the tab whose content should be drawn this frame.
    public int Draw(string id, IReadOnlyList<string> labels, IFontHandle? font = null, float badgeOn = -1, string? badgeText = null)
    {
        JustClicked = null;

        var deltaTime = ImGui.GetIO().DeltaTime;
        AdvanceFade(deltaTime);

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var stripWidth = ImGui.GetContentRegionAvail().X;

        using (font?.PushSafe())
        {
            var lineHeight = ImGui.GetTextLineHeight();
            var stripHeight = lineHeight + (StripPadY * 2) + UnderlineHeight;

            var x = origin.X;
            var targetX = underlineX;
            var targetWidth = underlineWidth;

            for (var i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                var textWidth = ImGui.CalcTextSize(label).X;
                var itemWidth = textWidth + (LabelPadX * 2);

                ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));
                var clicked = ImGui.InvisibleButton($"{id}##tab{i}", new Vector2(itemWidth, stripHeight));
                var hovered = ImGui.IsItemHovered();

                if (clicked)
                    Select(i);

                var isActive = i == selected;

                var dimText = new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.55f);
                var color = isActive
                    ? Theme.NeutralAccent
                    : hovered
                        ? Vector4.Lerp(dimText, Theme.Text, 0.85f)
                        : dimText;

                var textPos = new Vector2(x + LabelPadX, origin.Y + StripPadY);
                drawList.AddText(textPos, ImGui.GetColorU32(color), label);

                if (hovered && !isActive)
                {
                    var stubY = origin.Y + StripPadY + lineHeight + 3f;
                    drawList.AddRectFilled(
                        new Vector2(x + LabelPadX, stubY),
                        new Vector2(x + LabelPadX + textWidth, stubY + 1.5f),
                        ImGui.GetColorU32(new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.30f)),
                        1f);
                }

                if (isActive)
                {
                    targetX = x + LabelPadX;
                    targetWidth = textWidth;
                }

                if (badgeText is not null && MathF.Abs(badgeOn - i) < 0.5f)
                    DrawBadge(drawList, new Vector2(x + itemWidth - 4f, origin.Y + StripPadY), badgeText);

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

            var baselineY = origin.Y + StripPadY + lineHeight + 3f;

            drawList.AddRectFilled(
                new Vector2(origin.X, baselineY),
                new Vector2(origin.X + stripWidth, baselineY + 1f),
                ImGui.GetColorU32(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0.5f)));

            var glow = new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.28f);
            drawList.AddRectFilled(
                new Vector2(underlineX - 1.5f, baselineY - 1f),
                new Vector2(underlineX + underlineWidth + 1.5f, baselineY + UnderlineHeight + 1.5f),
                ImGui.GetColorU32(glow), 3f);

            drawList.AddRectFilled(
                new Vector2(underlineX, baselineY),
                new Vector2(underlineX + underlineWidth, baselineY + UnderlineHeight),
                ImGui.GetColorU32(Theme.NeutralAccent), 2f);

            ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + stripHeight));
        }

        return selected;
    }

    private void Select(int index)
    {
        if (index == selected || pending == index)
            return;

        JustClicked = index;

        if (selfContained)
            pending = index;
        else
            selected = index;
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
        var textSize = ImGui.CalcTextSize(text);
        var radius = MathF.Max(8f, (textSize.X / 2f) + 3f);
        var centre = new Vector2(anchor.X, anchor.Y + 2f);

        drawList.AddCircleFilled(centre, radius, ImGui.GetColorU32(Theme.NeutralAccent));
        drawList.AddText(centre - (textSize / 2f), ImGui.GetColorU32(Theme.Background), text);
    }
}
