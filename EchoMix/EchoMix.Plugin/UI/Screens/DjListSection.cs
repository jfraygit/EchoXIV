using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Cosmetics;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// The DJ list: who exists, who's on air, and how to reach them.
public sealed class DjListSection
{
    private readonly Plugin plugin;
    private readonly DjProfileSection profileSection;

    /// Set while drilled into one DJ.
    private string? viewingProfileId;

    private string genreFilter = string.Empty;
    private List<string> genreOptions = new();
    private object? genreOptionsSource;
    private string search = string.Empty;
    private bool requested;

    public DjListSection(Plugin plugin)
    {
        this.plugin = plugin;
        profileSection = new DjProfileSection(plugin);
    }

    private static string CharacterName => Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

    /// Passes the profile view's Edit click up to Browse, which owns the category switch.
    public DjProfileDetailDto? ConsumeEditRequest() => profileSection.ConsumeEditRequest();

    public void Draw(bool isSample)
    {
        var client = plugin.AudioHostClient;

        if (viewingProfileId is { } open)
        {
            if (!profileSection.Draw(open, isSample))
                viewingProfileId = null;

            return;
        }

        if (!requested)
        {
            requested = true;
            plugin.DjDeckWindow.RefreshDjProfiles(CharacterName);
        }

        var snapshot = client.LatestDjProfiles;

        Surfaces.SectionHeader("DJs");
        Surfaces.Gap(Metrics.Md);

        if (isSample)
        {
            Surfaces.RowText(
                "Showing sample DJs for layout testing. Turn off Sample Browse Data in Settings > "
                + "Appearance to see the real list.",
                Semantic.Warning);
            Surfaces.Gap(Metrics.Md);
        }

        if (snapshot == null)
        {
            DrawNotice(FontAwesomeIcon.Users, "Loading the DJ list...");
            return;
        }

        if (!string.IsNullOrEmpty(snapshot.Error))
        {
            DrawNotice(FontAwesomeIcon.ExclamationTriangle, $"Couldn't reach the relay: {snapshot.Error}");
            return;
        }

        if (snapshot.Profiles.Count == 0)
        {
            DrawNotice(FontAwesomeIcon.Users, "No DJ listings yet. Yours could be the first.");
            return;
        }

        DrawFeatured(snapshot.Profiles, isSample);
        Surfaces.Gap(Metrics.Xxl);

        RefreshGenreOptions(snapshot);

        var matches = Filter(snapshot.Profiles);
        DrawListHeader(matches.Count == 1 ? "1 DJ" : $"{matches.Count} DJs");
        Surfaces.Gap(Metrics.Md);

        if (matches.Count == 0)
        {
            DrawNotice(FontAwesomeIcon.Filter, "Nobody matches that.");
            return;
        }

        DrawProfileGrid(matches, isSample);
    }

    /// The DJ list as a grid of cards.
    private void DrawProfileGrid(List<DjProfileSummaryDto> matches, bool isSample)
    {
        var width = Surfaces.ContentWidth;
        var gap = Metrics.Lg;
        var minCardWidth = 236f * Metrics.Scale;

        var columns = Math.Clamp((int)MathF.Floor((width + gap) / (minCardWidth + gap)), 1, 4);
        var cardWidth = MathF.Round((width - (gap * (columns - 1))) / columns);
        var cardHeight = CardHeight();

        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var rows = (matches.Count + columns - 1) / columns;

        for (var i = 0; i < matches.Count; i++)
        {
            ImGui.SetCursorScreenPos(new Vector2(
                origin.X + ((i % columns) * (cardWidth + gap)),
                origin.Y + ((i / columns) * (cardHeight + gap))));

            DrawProfileCard(matches[i], cardWidth, cardHeight, isSample);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, (rows * cardHeight) + ((rows - 1) * gap)));
    }

    private static float CardAvatarSize() => MathF.Round(68f * Metrics.Scale);

    /// Room above the avatar.
    private static float CardTopPad() => MathF.Round(20f * Metrics.Scale);

    private static float CardHeight()
    {
        var nameLine = TypeScale.Measure(TypeScale.Heading, "Ag").Y;
        var captionLine = ImGui.GetTextLineHeight();

        return MathF.Round(
            CardTopPad() + CardAvatarSize()
            + Metrics.Md + nameLine
            + Metrics.Xs + captionLine
            + Metrics.Lg + Metrics.Hairline + Metrics.Sm
            + MathF.Round(Metrics.ControlSm) + Metrics.Md);
    }

    /// Avatar, name, genres, and a like/follow strip along the bottom.
    private void DrawProfileCard(DjProfileSummaryDto profile, float width, float height, bool isSample)
    {
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var max = Chrome.Snap(origin + new Vector2(width, height));
        var drawList = ImGui.GetWindowDrawList();
        var frameColor = new Vector4(profile.FrameColorR, profile.FrameColorG, profile.FrameColorB, 1f);

        var avatar = CardAvatarSize();
        var statsHeight = MathF.Round(Metrics.ControlSm);

        var hitHeight = height - statsHeight - Metrics.Md;
        var open = ImGui.InvisibleButton($"##djCard{profile.Id}", new Vector2(width, hitHeight));
        var hovered = ImGui.IsItemHovered();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var live = profile.IsLiveNow;
        var pulse = 0.55f + (Motion.Pulse(0.6f) * 0.45f);
        var border = live
            ? Semantic.Alpha(Semantic.Live, 0.35f + (0.35f * pulse))
            : Semantic.Alpha(frameColor, hovered ? 0.5f : 0.22f);

        DrawCardSurface(drawList, origin, max, frameColor, border, hovered);

        if (live)
            DrawLivePill(drawList, max.X, origin.Y, pulse);

        var centreX = origin.X + (width * 0.5f);
        var avatarPos = Chrome.Snap(new Vector2(centreX - (avatar * 0.5f), origin.Y + CardTopPad()));

        DrawAvatar(drawList, profile, avatarPos, avatar, frameColor);

        var y = avatarPos.Y + avatar + Metrics.Md;
        var textWidth = width - (Metrics.Lg * 2f);

        var shownName = UiHelpers.TruncateToWidth(profile.DjName, textWidth);
        var nameWidth = TypeScale.Measure(TypeScale.Heading, shownName).X;
        ImGui.SetCursorScreenPos(new Vector2(centreX - (nameWidth * 0.5f), y));
        DrawCosmeticName(profile, textWidth, shownName);

        y += TypeScale.Measure(TypeScale.Heading, "Ag").Y + Metrics.Xs;

        var subtitle = profile.Genres.Count > 0
            ? UiHelpers.JoinCapped(profile.Genres, 2)
            : profile.Bio ?? string.Empty;

        using (TypeScale.Caption())
        {
            var shown = UiHelpers.TruncateToWidth(subtitle, textWidth);
            var size = ImGui.CalcTextSize(shown);
            Chrome.Text(drawList, new Vector2(centreX - (size.X * 0.5f), y),
                ImGui.GetColorU32(Semantic.TextTertiary), shown);
        }

        var statsTop = max.Y - Metrics.Md - statsHeight;

        drawList.AddLine(
            Chrome.Snap(new Vector2(origin.X + Metrics.Lg, statsTop - Metrics.Sm)),
            Chrome.Snap(new Vector2(max.X - Metrics.Lg, statsTop - Metrics.Sm)),
            ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);

        DrawStats(profile, new Vector2(origin.X, statsTop), width, statsHeight, isSample);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));

        if (!open)
            return;

        if (!isSample)
            plugin.DjDeckWindow.RequestDjProfileDetail(profile.Id, CharacterName);

        viewingProfileId = profile.Id;
    }

    /// The card's own surface: shadow, fill, and the DJ's colour washed down from the top.
    private static void DrawCardSurface(
        ImDrawListPtr drawList, Vector2 origin, Vector2 max, Vector4 tint, Vector4 border, bool hovered)
    {
        var rounding = Metrics.RadiusCard;
        var baseFill = hovered ? Elevation.Raised : Elevation.Surface;
        var topFill = Vector4.Lerp(baseFill, tint, hovered ? 0.3f : 0.22f);

        Elevation.DrawShadow(drawList, origin, max, rounding, Elevation.ShadowSpec.Low);
        drawList.AddRectFilled(origin, max, ImGui.GetColorU32(baseFill), rounding);

        var topU32 = ImGui.GetColorU32(topFill);
        var baseU32 = ImGui.GetColorU32(baseFill);
        var fadeBottom = origin.Y + ((max.Y - origin.Y) * 0.55f);

        drawList.AddRectFilled(origin, new Vector2(max.X, origin.Y + rounding), topU32,
            rounding, ImDrawFlags.RoundCornersTop);
        drawList.AddRectFilledMultiColor(
            new Vector2(origin.X, origin.Y + rounding), new Vector2(max.X, fadeBottom),
            topU32, topU32, baseU32, baseU32);

        drawList.AddRect(origin, max, ImGui.GetColorU32(border), rounding, ImDrawFlags.None, Metrics.Hairline);
    }

    /// A LIVE pill in the card's top-right corner.
    private static void DrawLivePill(ImDrawListPtr drawList, float rightEdge, float top, float pulse)
    {
        using (TypeScale.Caption())
        {
            const string label = "LIVE";
            var textSize = ImGui.CalcTextSize(label);
            var padX = Metrics.Md;
            var pillHeight = MathF.Round(textSize.Y + Metrics.Sm);
            var pillWidth = MathF.Round(textSize.X + (padX * 2f));
            var pillMin = Chrome.Snap(new Vector2(rightEdge - pillWidth - Metrics.Md, top + Metrics.Md));
            var pillMax = pillMin + new Vector2(pillWidth, pillHeight);

            drawList.AddRectFilled(pillMin, pillMax,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Live, 0.18f + (0.14f * pulse))),
                Metrics.Pill(pillHeight));
            drawList.AddRect(pillMin, pillMax,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Live, 0.45f + (0.35f * pulse))),
                Metrics.Pill(pillHeight), ImDrawFlags.None, Metrics.Hairline);

            Chrome.Text(drawList,
                Chrome.CenterY(pillMin.X + padX, pillMin.Y, pillHeight, textSize.Y),
                ImGui.GetColorU32(Semantic.Live), label);
        }
    }

    private List<DjProfileSummaryDto> Filter(IReadOnlyList<DjProfileSummaryDto> profiles)
    {
        var query = search.Trim();

        return profiles
            .Where(p => genreFilter.Length == 0
                || p.Genres.Any(g => string.Equals(g, genreFilter, StringComparison.OrdinalIgnoreCase)))
            .Where(p => query.Length == 0
                || p.DjName.Contains(query, StringComparison.OrdinalIgnoreCase))

            .OrderByDescending(p => p.IsLiveNow)
            .ThenByDescending(p => p.IsLiveNow ? p.ListenerCount : p.FollowerCount)
            .ToList();
    }

    private void RefreshGenreOptions(DjProfilesSnapshotMessage snapshot)
    {
        if (ReferenceEquals(genreOptionsSource, snapshot))
            return;

        genreOptionsSource = snapshot;
        genreOptions = DjDeckWindow.GenreOptions(snapshot.Profiles.Select(p => p.Genres));

        if (genreFilter.Length > 0 && !genreOptions.Contains(genreFilter))
            genreFilter = string.Empty;
    }

    /// The list's count, and its two filters, on one line - see Fields.BeginListHeader for why a filter lives
    /// on its list's own header rather than in a panel above it.
    private void DrawListHeader(string title)
    {
        var searchWidth = MathF.Round(150f * Metrics.Scale);
        var genreWidth = MathF.Round(160f * Metrics.Scale);

        var header = Fields.BeginListHeader(title, genreWidth + searchWidth + Metrics.Md);

        var options = new List<string> { "All Genres" };
        options.AddRange(genreOptions);
        var index = genreFilter.Length == 0 ? 0 : options.IndexOf(genreFilter);

        ImGui.SetCursorScreenPos(header.ControlMin);
        if (Fields.DropdownInline("##v2DjGenre", ref index, options, genreWidth, header.Height))
            genreFilter = index <= 0 ? string.Empty : options[index];

        ImGui.SetCursorScreenPos(new Vector2(header.ControlMin.X + genreWidth + Metrics.Md, header.ControlMin.Y));
        Fields.TextInput("##v2DjSearch", ref search, 32, searchWidth, "Search DJs");

        Fields.EndListHeader(header);
    }

    /// The top few by listener count, ranked left to right.
    private void DrawFeatured(IReadOnlyList<DjProfileSummaryDto> profiles, bool isSample)
    {
        var live = profiles
            .Where(p => p.IsLiveNow)
            .OrderByDescending(p => p.ListenerCount)
            .Take(3)
            .ToList();

        if (live.Count == 0)
            return;

        Surfaces.SectionHeader("Live Now", Semantic.Live);
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var width = Surfaces.ContentWidth;
        var gap = Metrics.Lg;
        var tileWidth = (width - (gap * (live.Count - 1))) / live.Count;
        var avatar = MathF.Round(56f * Metrics.Scale);
        var tileHeight = avatar + (Metrics.Md * 2f);
        var origin = ImGui.GetCursorScreenPos();

        for (var i = 0; i < live.Count; i++)
        {
            ImGui.SetCursorScreenPos(origin + new Vector2(i * (tileWidth + gap), 0f));
            DrawFeaturedTile(live[i], i + 1, new Vector2(tileWidth, tileHeight), avatar, isSample);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, tileHeight));

        Surfaces.EndPanel();
    }

    private void DrawFeaturedTile(DjProfileSummaryDto profile, int rank, Vector2 size, float avatar, bool isSample)
    {
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();
        var frameColor = new Vector4(profile.FrameColorR, profile.FrameColorG, profile.FrameColorB, 1f);

        var open = ImGui.InvisibleButton($"##featured{profile.Id}", size);
        var hovered = ImGui.IsItemHovered();

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            drawList.AddRectFilled(origin, origin + size,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.04f)), Metrics.RadiusSoft);
        }

        var avatarPos = Chrome.Snap(new Vector2(origin.X + Metrics.Md, origin.Y + Metrics.Md));
        DrawAvatar(drawList, profile, avatarPos, avatar, frameColor);

        DrawRankBadge(drawList, avatarPos, rank);

        var textLeft = avatarPos.X + avatar + Metrics.Md;
        var textWidth = MathF.Max(Metrics.Xxl, origin.X + size.X - textLeft - Metrics.Md);
        var lineHeight = ImGui.GetTextLineHeight();
        var block = (lineHeight * 2f) + Metrics.Xs;
        var y = origin.Y + ((size.Y - block) * 0.5f);

        ImGui.SetCursorScreenPos(new Vector2(textLeft, y));
        DrawCosmeticName(profile, textWidth);

        using (TypeScale.Caption())
            Chrome.Text(drawList, new Vector2(textLeft, y + lineHeight + Metrics.Xs),
                ImGui.GetColorU32(Semantic.Live), $"{profile.ListenerCount} listening");

        if (hovered)
            Tip.Hovered(profile.DjName, $"{profile.ListenerCount} listening right now. Click to open their profile.");

        if (!open)
            return;

        if (!isSample)
            plugin.DjDeckWindow.RequestDjProfileDetail(profile.Id, CharacterName);

        viewingProfileId = profile.Id;
    }

    /// A medal on the avatar's top-left corner.
    private static void DrawRankBadge(ImDrawListPtr drawList, Vector2 avatarPos, int rank)
    {
        var radius = MathF.Round(11f * Metrics.Scale);
        var centre = Chrome.Snap(avatarPos);

        var metal = rank switch
        {
            1 => new Vector4(0.96f, 0.80f, 0.36f, 1f),
            2 => new Vector4(0.80f, 0.83f, 0.88f, 1f),
            _ => new Vector4(0.82f, 0.56f, 0.34f, 1f),
        };

        drawList.AddCircleFilled(centre + new Vector2(0f, 1f * Metrics.Scale), radius,
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.45f)));
        drawList.AddCircleFilled(centre, radius, ImGui.GetColorU32(Elevation.Surface));
        drawList.AddCircleFilled(centre, radius - MathF.Max(1.5f, 2f * Metrics.Scale), ImGui.GetColorU32(metal));

        using (TypeScale.Caption())
        {
            var label = rank.ToString();
            var size = ImGui.CalcTextSize(label);
            Chrome.Text(drawList, Chrome.Snap(centre - (size * 0.5f)),
                ImGui.GetColorU32(Semantic.TextOnAccent), label);
        }
    }

    /// Like and follow along the bottom of a card - glyph and count side by side, each owning half the strip
    /// so the hit target is the whole half rather than the glyph alone.
    private void DrawStats(DjProfileSummaryDto profile, Vector2 pos, float width, float height, bool isSample)
    {
        var client = plugin.AudioHostClient;
        var half = width * 0.5f;

        DrawStat(pos.X, "like", FontAwesomeIcon.Heart, profile.IsLikedByRequester, profile.LikeCount,
            Semantic.DeckB, () => client.Send(MessageType.ToggleDjProfileLike,
                new ToggleDjProfileLikeMessage { ProfileId = profile.Id, CharacterName = CharacterName }));

        DrawStat(pos.X + half, "follow", FontAwesomeIcon.Bell, profile.IsFollowedByRequester, profile.FollowerCount,
            Semantic.Primary, () => client.Send(MessageType.ToggleDjProfileFollow,
                new ToggleDjProfileFollowMessage { ProfileId = profile.Id, CharacterName = CharacterName }));

        void DrawStat(float x, string kind, FontAwesomeIcon icon, bool on, int count, Vector4 accent, Action toggle)
        {
            var drawList = ImGui.GetWindowDrawList();

            ImGui.SetCursorScreenPos(new Vector2(x, pos.Y));
            var clicked = ImGui.InvisibleButton($"##{kind}{profile.Id}", new Vector2(half, height));
            var hovered = ImGui.IsItemHovered();

            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            var label = count.ToString();
            float labelWidth;
            using (TypeScale.Caption())
                labelWidth = ImGui.CalcTextSize(label).X;

            var glyphRoom = Metrics.Xl;
            var gap = Metrics.Md;
            var left = x + ((half - glyphRoom - gap - labelWidth) * 0.5f);
            var midY = pos.Y + (height * 0.5f);

            var tint = on ? accent : hovered ? Semantic.TextSecondary : Semantic.TextTertiary;

            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, icon,
                    Chrome.Snap(new Vector2(left + (glyphRoom * 0.5f), midY)), ImGui.GetColorU32(tint));

            using (TypeScale.Caption())
                Chrome.Text(drawList,
                    Chrome.CenterY(left + glyphRoom + gap, pos.Y, height, ImGui.GetTextLineHeight()),
                    ImGui.GetColorU32(on ? accent : Semantic.TextTertiary), label);

            if (!clicked)
                return;

            if (!isSample)
            {
                toggle();
                return;
            }

#if DEBUG
            if (kind == "like")
                BrowseSampleData.ToggleLike(profile.Id);
            else
                BrowseSampleData.ToggleFollow(profile.Id);
#endif
        }
    }

    private void DrawAvatar(ImDrawListPtr drawList, DjProfileSummaryDto profile, Vector2 pos, float size, Vector4 frameColor)
    {
        var texture = plugin.DjDeckWindow.DjAvatarImage(profile.Id, profile.AvatarBase64);
        var box = new Vector2(size, size);
        var rounding = Metrics.RadiusSoft;

        if (texture != null)
        {
            drawList.AddImageRounded(texture.Handle, pos, pos + box, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), rounding);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + box, ImGui.GetColorU32(Elevation.Sunken), rounding);

            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.User,
                    Chrome.Snap(pos + (box * 0.5f)), ImGui.GetColorU32(Semantic.TextDisabled));
        }

        DjCosmetics.DrawAvatarFrame(drawList, pos, box, rounding, frameColor, profile.FrameStyle, Metrics.Scale);
    }

    /// `preTruncated` is for a caller that already had to measure the name in order to place it - a centred
    /// card title - so the truncation isn't computed twice and can't disagree with the width the caller
    /// centred against.
    private void DrawCosmeticName(DjProfileSummaryDto profile, float maxWidth, string? preTruncated = null)
    {
        var color = new Vector4(profile.NameColorR, profile.NameColorG, profile.NameColorB, 1f);

        if (color.X + color.Y + color.Z < 0.05f)
            color = Semantic.TextPrimary;

        var shown = preTruncated ?? UiHelpers.TruncateToWidth(profile.DjName, maxWidth);
        DjCosmetics.DrawDjName(plugin.Fonts, shown, color, profile.NameEffect, 0.8f, Metrics.Scale);
    }

    private static void DrawNotice(FontAwesomeIcon icon, string message)
    {
        Surfaces.BeginPanel();

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();

        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, icon,
                Chrome.Snap(new Vector2(pos.X + Metrics.Md, pos.Y + (lineHeight * 0.5f))),
                ImGui.GetColorU32(Semantic.TextTertiary));

        ImGui.Dummy(new Vector2(Metrics.Xxl, 0f));
        ImGui.SameLine();

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextTertiary, message);

        Surfaces.EndPanel();
    }
}
