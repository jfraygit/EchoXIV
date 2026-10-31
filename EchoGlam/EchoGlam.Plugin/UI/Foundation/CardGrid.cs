using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

namespace EchoGlam.UI.Foundation;

/// One card's place in the grid, and what the pointer did to it.
public readonly record struct CardSlot(
    Vector2 Min,
    Vector2 Size,
    float ImageHeight,
    bool Hovered,
    bool Clicked,
    bool RightClicked)
{
    public Vector2 Max => Min + Size;

    /// The picture's rectangle, which is the top of the card.
    public Vector2 ImageMax => Min + new Vector2(Size.X, ImageHeight);

    /// Where the text under the picture starts, inset by the card's own padding.
    public Vector2 StripMin => Min + new Vector2(Sp.Lg, ImageHeight + Sp.Sm);

    /// How wide text in the strip may be.
    public float StripWidth => Size.X - (Sp.Lg * 2f);
}

/// The grid of glamour cards.
public static class CardGrid
{
    /// Height of the text strip under a card's picture, in design units.
    public const float StripDesign = 72f;

    /// Lays out the cards and draws each one's shell, calling back to fill it in.
    public static void Draw<T>(
        IReadOnlyList<T> items,
        Func<T, string> idOf,
        Action<T, CardSlot> draw,
        float stripDesign = StripDesign,
        float? minCard = null,
        float? gutter = null)
    {
        if (items.Count == 0)
            return;

        var gap = gutter ?? Sp.Lg;
        var available = ImGui.GetContentRegionAvail().X;

        var (columns, cardWidth) = Layout.Fit(available, minCard ?? Metrics.CardMin, gap);

        var imageHeight = Game.ImageProcessor.HeightFor(cardWidth);
        var cardHeight = imageHeight + (stripDesign * UiHelpers.Scale);
        var size = new Vector2(cardWidth, cardHeight);

        var drawList = ImGui.GetWindowDrawList();

        for (var i = 0; i < items.Count; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine(0f, gap);

            var item = items[i];
            var min = ImGui.GetCursorScreenPos();

            ImGui.InvisibleButton($"##card{idOf(item)}", size, ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();
            var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
            var rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);

            Elev.Shadow(drawList, min, min + size, Elev.Float, Rad.Card, hovered ? 1.9f : 1f);

            drawList.AddRectFilled(min, min + size, Col.U32(Col.Surface), Rad.Card);

            var slot = new CardSlot(min, size, imageHeight, hovered, clicked, rightClicked);
            draw(item, slot);

            drawList.AddRect(
                min, min + size,
                Col.U32(hovered ? Theme.Accent : Col.EdgeRest),
                Rad.Card, ImDrawFlags.None, hovered ? Stroke.Focus : Stroke.Edge);
        }

        ImGui.Dummy(new Vector2(0f, Sp.Xs));
    }

    /// Draws a picture filling the card's top, cropped rather than squashed.
    public static void CoverImage(
        ImDrawListPtr drawList, IDalamudTextureWrap texture, Vector2 min, Vector2 max, bool hovered = false)
    {
        var size = max - min;
        var wanted = size.X / size.Y;
        var actual = (float)texture.Width / texture.Height;

        var uv0 = Vector2.Zero;
        var uv1 = Vector2.One;

        if (actual > wanted)
        {
            var keep = wanted / actual;
            uv0.X = (1f - keep) / 2f;
            uv1.X = uv0.X + keep;
        }
        else if (actual < wanted)
        {
            var keep = actual / wanted;
            uv0.Y = (1f - keep) / 2f;
            uv1.Y = uv0.Y + keep;
        }

        if (hovered)
        {
            var inset = (uv1 - uv0) * 0.015f;
            uv0 += inset;
            uv1 -= inset;
        }

        drawList.AddImageRounded(
            texture.Handle, min, max, uv0, uv1,
            Col.U32(Vector4.One), Rad.Card, ImDrawFlags.RoundCornersTop);
    }

    /// The placeholder where a picture has not arrived, or where there is not one.
    public static void Placeholder(ImDrawListPtr drawList, Vector2 min, Vector2 max, string label)
    {
        drawList.AddRectFilled(min, max, Col.U32(Col.Sunken), Rad.Card, ImDrawFlags.RoundCornersTop);

        using (Text.PushCaption())
        {
            var size = max - min;
            drawList.AddText(
                min + new Vector2((size.X - Text.Width(label)) / 2f, (size.Y - Text.CaptionSize) / 2f),
                Col.U32(Col.TextMuted),
                label);
        }
    }
}
