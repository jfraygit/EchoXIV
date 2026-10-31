using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Shell;

/// A destination in the index.
public readonly record struct Destination(string Label);

/// The wordmark and a numbered index of destinations.
public sealed class Masthead
{
    /// Width in design units.
    public const float WidthDesign = 190f;

    public static float Width => WidthDesign * UiHelpers.Scale;

    private const float RowDesign = 38f;

    private static float Row => RowDesign * UiHelpers.Scale;

    private int selected;
    private float fade = 1f;
    private int fading = -1;

    public int Selected => selected;

    /// True on the frame a destination was chosen, so the window can put a settings overlay away.
    public bool ClickedThisFrame { get; private set; }

    /// What the content should be drawn at while a destination change settles.
    public float ContentAlpha => fade;

    /// Jumps straight to a destination, for restoring the saved page on open - where easing in from whatever
    /// happened to be first is a transition nobody asked for.
    public void SetImmediate(int index)
    {
        selected = index;
        fade = 1f;
        fading = -1;
    }

    /// Draws the column into a screen rectangle and returns the selected index.
    public int Draw(
        Vector2 origin,
        float height,
        IReadOnlyList<Destination> items)
    {
        ClickedThisFrame = false;

        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorPos();
        var width = Width;
        var x = origin.X + Sp.Xl;
        var inner = width - (Sp.Xl * 2f);
        var y = origin.Y + Sp.Xl;

        using (Text.PushDisplay())
        {
            Text.Tracked(drawList, new Vector2(x, y), "ECHOGLAM", Col.U32(Col.Text), 2.4f, inner);
            y += Text.DisplaySize * 1.3f;
        }

        drawList.AddRectFilled(
            new Vector2(x, y), new Vector2(x + (inner * 0.52f), y + Stroke.Mark), Col.U32(Theme.Accent));

        drawList.AddRectFilled(
            new Vector2(x, y - UiHelpers.Scale),
            new Vector2(x + (inner * 0.52f), y + Stroke.Mark + UiHelpers.Scale),
            Col.U32(Theme.Accent with { W = 0.16f }));

        y += Sp.Xxl;

        for (var i = 0; i < items.Count; i++)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X, y));
            ImGui.InvisibleButton(
                $"##dest{i}", new Vector2(width, Row), ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                ClickedThisFrame = true;

                if (i != selected)
                    fading = i;
            }

            var active = i == selected;
            var lit = Motion.Toward($"dest{i}", active, 16f);
            var warm = Motion.Toward($"desthover{i}", hovered || active, 18f);

            if (lit > 0.01f)
            {
                var barTop = y + (Row * 0.2f);
                var barHeight = Row * 0.6f * lit;
                var bar = 2.5f * UiHelpers.Scale;

                for (var ring = 3; ring >= 1; ring--)
                {
                    var spread = ring * 2.2f * UiHelpers.Scale;
                    drawList.AddRectFilled(
                        new Vector2(origin.X - spread, barTop - spread),
                        new Vector2(origin.X + bar + spread, barTop + barHeight + spread),
                        Col.U32(Theme.Accent with { W = 0.045f * lit }),
                        spread);
                }

                drawList.AddRectFilled(
                    new Vector2(origin.X, barTop),
                    new Vector2(origin.X + bar, barTop + barHeight),
                    Col.U32(Theme.Accent), Rad.Chip);
            }

            var numberTint = active ? Theme.Accent with { W = 0.9f } : Col.TextMuted;
            var nameTint = active ? Col.Text : Vector4.Lerp(Col.TextDim, Col.Text, warm);
            var textY = y + ((Row - Text.CaptionSize) / 2f);

            using (Text.PushCaption())
            {
                Text.Tracked(drawList, new Vector2(x, textY), $"{i + 1:00}", Col.U32(numberTint), 1.6f);

                var nameX = x + (Sp.Xxl * 1.1f);
                Text.Tracked(
                    drawList, new Vector2(nameX, textY), items[i].Label.ToUpperInvariant(),
                    Col.U32(nameTint), 1.8f, (origin.X + width - Sp.Lg) - nameX);
            }

            y += Row;
        }

        ImGui.SetCursorPos(cursor);
        Advance();

        return selected;
    }

    /// Out, swap, back in.
    private void Advance()
    {
        var rate = Anim.Step(ImGui.GetIO().DeltaTime) / Anim.FadeSeconds;

        if (fading >= 0)
        {
            fade -= rate;

            if (fade <= 0f)
            {
                fade = 0f;
                selected = fading;
                fading = -1;
            }

            return;
        }

        if (fade < 1f)
            fade = MathF.Min(1f, fade + rate);
    }
}
