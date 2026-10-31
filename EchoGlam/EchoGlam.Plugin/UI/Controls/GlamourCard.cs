using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using EchoGlam.Game;
using EchoGlam.Shared;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Controls;

/// One glamour as a card: the photograph, with what it is written on it.
public static class GlamourCard
{
    /// Draws the card's contents into the slot the grid laid out.
    public static void Draw(
        ImDrawListPtr drawList, GalleryImages images, IFontHandle iconFont,
        GlamourSummary entry, CardSlot slot, bool showAuthor = true)
    {
        Paint(drawList, images, iconFont, entry, slot.Min, slot.Size, slot.Hovered, showAuthor);

        if (entry.PublishedUtc > 0
            && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(entry.PublishedUtc)
            < TimeSpan.FromDays(3))
        {
            Pill.DrawAt(slot.Min + new Vector2(Sp.Md, Sp.Md), "NEW", Pill.Tone.Accent);
        }
    }

    /// The picture and the caption, into a rectangle the caller owns.
    public static void Paint(
        ImDrawListPtr drawList, GalleryImages images, IFontHandle iconFont,
        GlamourSummary entry, Vector2 min, Vector2 size, bool hovered, bool showAuthor = true)
    {
        DrawImage(drawList, images, entry, min, size, Rad.Card, hovered);
        DrawCaption(drawList, iconFont, entry, min, size, Rad.Card, showAuthor);
    }

    /// The card's screenshot, or what stands in for it while there isn't one.
    private static void DrawImage(
        ImDrawListPtr drawList, GalleryImages images, GlamourSummary entry, Vector2 pos, Vector2 size,
        float rounding, bool hovered)
    {
        var max = pos + size;
        var url = entry.ImageCount > 0 ? GalleryClient.ImageUrl(entry, 0, card: true) : null;
        var texture = url is null ? null : images.Get(url);

        if (texture != null)
        {
            var (uv0, uv1) = Avatar.Cover(texture.Width, texture.Height, size.X, size.Y);

            if (hovered)
            {
                var inset = (uv1 - uv0) * (0.03f / 2f);
                uv0 += inset;
                uv1 -= inset;
            }

            drawList.AddImageRounded(
                texture.Handle, pos, max, uv0, uv1,
                Col.U32(Vector4.One), rounding, ImDrawFlags.RoundCornersAll);

            return;
        }

        drawList.AddRectFilled(
            pos, max, Col.U32(Theme.Tinted(0.06f)), rounding, ImDrawFlags.RoundCornersAll);

        var label = url is null || images.Failed(url) ? "No screenshot" : "Loading...";
        var measured = ImGui.CalcTextSize(label);

        drawList.AddText(pos + ((size - measured) / 2f), Col.U32(Col.TextMuted), label);
    }

    /// The title, author and counts, over the foot of a picture.
    private static void DrawCaption(
        ImDrawListPtr drawList, IFontHandle iconFont, GlamourSummary entry, Vector2 pos, Vector2 size,
        float rounding, bool showAuthor)
    {
        var clear = Col.U32(new Vector4(0f, 0f, 0f, 0f));
        var solid = Col.U32(new Vector4(0f, 0f, 0f, 0.82f));

        var lines = ImGui.GetTextLineHeight() + Sp.Xxs + (Text.CaptionSize * 1.15f)
            + (showAuthor ? Sp.Sm + (Text.CaptionSize * 1.15f) : 0f);

        var captionTop = MathF.Max(
            pos.Y + (size.Y * 0.4f), pos.Y + size.Y - lines - (Sp.Lg * 3f));

        drawList.PushClipRect(pos, pos + size, true);

        var foot = rounding * 3f;
        var fadeBottom = MathF.Max(captionTop, pos.Y + size.Y - foot);

        drawList.AddRectFilledMultiColor(
            new Vector2(pos.X, captionTop), new Vector2(pos.X + size.X, fadeBottom),
            clear, clear, solid, solid);

        drawList.AddRectFilled(
            new Vector2(pos.X, fadeBottom), pos + size, solid, rounding, ImDrawFlags.RoundCornersBottom);

        var left = pos.X + Sp.Lg;
        var room = size.X - (Sp.Lg * 2f);

        var countsY = pos.Y + size.Y - Sp.Lg - (Text.CaptionSize * 1.15f);
        var authorY = countsY - Sp.Sm - (Text.CaptionSize * 1.15f);
        var titleY = (showAuthor ? authorY : countsY) - Sp.Xxs - ImGui.GetTextLineHeight();

        drawList.AddText(
            new Vector2(left, titleY), Col.U32(Vector4.One), Text.Truncate(entry.Title, room));

        if (showAuthor)
        {
            var author = string.IsNullOrEmpty(entry.AuthorWorld)
                ? entry.AuthorName
                : $"{entry.AuthorName} · {entry.AuthorWorld}";

            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(left, authorY),
                    Col.U32(new Vector4(0.86f, 0.84f, 0.90f, 1f)),
                    Text.Truncate(author, room));
            }
        }

        DrawFooter(drawList, iconFont, entry, new Vector2(left, countsY), room);

        drawList.PopClipRect();
    }

    /// Race and job on the left, the two counts on the right.
    private static void DrawFooter(
        ImDrawListPtr drawList, IFontHandle iconFont, GlamourSummary entry, Vector2 origin, float width)
    {
        var lineHeight = ImGui.GetTextLineHeight();
        var centre = origin.Y + (lineHeight / 2f);
        var x = origin.X;

        if (entry.JobId > 0 && JobList.Find(entry.JobId) is { } job)
        {
            var texture = Plugin.TextureProvider
                .GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrDefault();

            if (texture != null)
            {
                var iconSize = lineHeight + (2f * UiHelpers.Scale);

                drawList.AddImage(
                    texture.Handle,
                    new Vector2(x, centre - (iconSize / 2f)),
                    new Vector2(x + iconSize, centre + (iconSize / 2f)),
                    Vector2.Zero, Vector2.One, Col.U32(new Vector4(1f, 1f, 1f, 0.9f)));

                x += iconSize + Sp.Sm;
            }
        }

        var countsWidth = UiHelpers.DrawGlamourCounts(
            drawList, iconFont, origin, width,
            entry.Votes, entry.Voted, entry.Favourites, entry.Favourited);

        var raceRoom = origin.X + width - countsWidth - x - Sp.Md;
        if (raceRoom <= 0f)
            return;

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(x, origin.Y),
                Col.U32(new Vector4(0.82f, 0.80f, 0.86f, 1f)),
                Text.Truncate(RaceNames.Race(entry.Race, entry.Sex), raceRoom));
        }
    }
}
