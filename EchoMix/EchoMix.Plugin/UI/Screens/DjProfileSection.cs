using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Cosmetics;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// One DJ's profile, drilled into from the list.
public sealed class DjProfileSection
{
    private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    private readonly Plugin plugin;

    /// Set by the Edit button on your own profile, for Browse to pick up and switch category with.
    private DjProfileDetailDto? editRequest;

    public DjProfileSection(Plugin plugin) => this.plugin = plugin;

    private static string CharacterName => Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

    public DjProfileDetailDto? ConsumeEditRequest()
    {
        var request = editRequest;
        editRequest = null;
        return request;
    }

    /// Returns false when the caller should drop back to the list - the profile failed to load, or Back was
    /// pressed.
    public bool Draw(string profileId, bool isSample)
    {
        var detail = plugin.AudioHostClient.LatestDjProfileDetail;

        if (DrawBackBar())
            return false;

#if DEBUG

        if (isSample)
        {
            if (BrowseSampleData.Detail(profileId) is not { } sample)
            {
                DrawNotice(FontAwesomeIcon.User, "No sample profile for that DJ.");
                return true;
            }

            DrawHeader(sample, isSample);
            DrawBadges(sample);
            DrawAvailability(sample);
            Surfaces.Gap(Metrics.Xxl);

            if (!string.IsNullOrWhiteSpace(sample.Bio))
            {
                Surfaces.SectionHeader("About");
                Surfaces.Gap(Metrics.Md);
                Surfaces.BeginPanel();
                Surfaces.RowText(sample.Bio!, Semantic.TextSecondary);
                Surfaces.EndPanel();
                Surfaces.Gap(Metrics.Xxl);
            }

            DrawDetails(sample);
            return true;
        }
#endif

        if (detail?.Profile == null)
        {
            if (detail?.Error is { } error)
            {
                DrawNotice(FontAwesomeIcon.ExclamationTriangle, error);
                return true;
            }

            DrawNotice(FontAwesomeIcon.User, "Loading profile...");
            return true;
        }

        var profile = detail.Profile;

        if (!string.Equals(profile.Id, profileId, StringComparison.Ordinal))
        {
            DrawNotice(FontAwesomeIcon.User, "Loading profile...");
            return true;
        }

        DrawHeader(profile, isSample);

        DrawBadges(profile);

        DrawAvailability(profile);

        if (!string.IsNullOrWhiteSpace(profile.Bio))
        {
            Surfaces.Gap(Metrics.Xxl);
            Surfaces.SectionHeader("About");
            Surfaces.Gap(Metrics.Md);
            Surfaces.BeginPanel();
            Surfaces.RowText(profile.Bio!, Semantic.TextSecondary);
            Surfaces.EndPanel();
        }

        Surfaces.Gap(Metrics.Xxl);
        DrawDetails(profile);
        DrawStats(profile);

        plugin.DjDeckWindow.DrawLegacyDjProfileReportPopup(profile.Id);
        return true;
    }

    /// What this DJ has been granted, as a row of small marks under their picture.
    private void DrawBadges(DjProfileDetailDto profile)
    {
        if (profile.Badges.Count == 0)
        {
            Surfaces.Gap(Metrics.Xxl);
            return;
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);

        var size = MathF.Round(Metrics.ControlXl);
        var gap = Metrics.Md;
        var indent = Metrics.Xxl;
        var width = MathF.Max(size, Surfaces.ContentWidth - indent);

        var drawList = ImGui.GetWindowDrawList();
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos() + new Vector2(indent, 0f));

        var x = 0f;
        var y = 0f;
        var drawn = 0;

        for (var i = 0; i < profile.Badges.Count; i++)
        {
            var badge = profile.Badges[i];

            if (BadgeCatalog.Find(badge.Id) is not { } entry)
                continue;

            if (x > 0f && x + size > width)
            {
                x = 0f;
                y += size + gap;
            }

            var pos = Chrome.Snap(new Vector2(origin.X + x, origin.Y + y));
            var box = new Vector2(size, size);

            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton($"##v2badge{i}", box);
            var hovered = ImGui.IsItemHovered();

            if (plugin.BadgeTextures.Get(entry.ResourceName) is { } texture)
            {
                drawList.AddImage(texture.Handle, pos, pos + box, Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(Semantic.Alpha(Vector4.One, hovered ? 1f : 0.88f)));
            }
            else
            {
                drawList.AddRectFilled(pos, pos + box,
                    ImGui.GetColorU32(Elevation.Sunken), Metrics.RadiusSoft);
            }

            if (hovered)
            {
                var awarded = badge.AwardedAtUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
                Tip.Hovered(entry.Title, $"{entry.Description}\n\nObtained {awarded}");
            }

            x += size + gap;
            drawn++;
        }

        if (drawn == 0)
            return;

        ImGui.SetCursorScreenPos(origin - new Vector2(indent, 0f));
        ImGui.Dummy(new Vector2(Surfaces.ContentWidth, y + size));
        Surfaces.Gap(Metrics.Sm);
    }

    /// This DJ's all-time numbers, as a strip of tiles.
    private static void DrawStats(DjProfileDetailDto profile)
    {
        if (profile.Stats is not { } stats || stats.ShowsPlayed <= 0)
            return;

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("All Time");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var tiles = new (string Label, string Value)[]
        {
            ("Shows", stats.ShowsPlayed.ToString("N0", CultureInfo.InvariantCulture)),
            ("On Air", StatsSection.FormatDuration(stats.OnAirSeconds)),
            ("Listener Hours", StatsSection.FormatDuration(stats.ListenerSeconds)),
            ("Biggest Room", stats.PeakListeners.ToString("N0", CultureInfo.InvariantCulture)),
            ("Days Active", stats.DaysActive.ToString("N0", CultureInfo.InvariantCulture)),
            ("People Reached", stats.UniqueListeners.ToString("N0", CultureInfo.InvariantCulture)),
        };

        var width = Surfaces.ContentWidth;
        var gap = Metrics.Md;
        const int columns = 3;
        var tileWidth = MathF.Round((width - (gap * (columns - 1))) / columns);
        var tileHeight = MathF.Round(Metrics.ControlXl + ImGui.GetTextLineHeight());
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        for (var i = 0; i < tiles.Length; i++)
        {
            var pos = Chrome.Snap(new Vector2(
                origin.X + ((i % columns) * (tileWidth + gap)),
                origin.Y + ((i / columns) * (tileHeight + gap))));

            var box = new Vector2(tileWidth, tileHeight);

            Elevation.DrawSurface(drawList, pos, pos + box, Elevation.Sunken,
                Metrics.RadiusSoft, Elevation.ShadowSpec.None, topEdge: false, Elevation.Line);

            using (TypeScale.Heading())
            {
                var size = ImGui.CalcTextSize(tiles[i].Value);
                Chrome.Text(drawList,
                    new Vector2(pos.X + ((tileWidth - size.X) * 0.5f), pos.Y + Metrics.Lg),
                    ImGui.GetColorU32(Semantic.TextPrimary), tiles[i].Value);
            }

            using (TypeScale.Caption())
            {
                var shown = UiHelpers.TruncateToWidth(tiles[i].Label, tileWidth - Metrics.Md);
                var size = ImGui.CalcTextSize(shown);
                Chrome.Text(drawList,
                    new Vector2(pos.X + ((tileWidth - size.X) * 0.5f), pos.Y + tileHeight - size.Y - Metrics.Md),
                    ImGui.GetColorU32(Semantic.TextTertiary), shown);
            }
        }

        var rows = (tiles.Length + columns - 1) / columns;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, (rows * tileHeight) + ((rows - 1) * gap)));

        Surfaces.EndPanel();
    }

    private bool DrawBackBar()
    {
        var back = Fields.Button("Back to DJs", Fields.ButtonStyle.Secondary,
            icon: FontAwesomeIcon.ArrowLeft, height: MathF.Round(Metrics.ControlSm));

        Surfaces.Gap(Metrics.Lg);
        return back;
    }

    /// Banner, avatar and name.
    private void DrawHeader(DjProfileDetailDto profile, bool isSample)
    {
        var width = Surfaces.ContentWidth;
        var bannerHeight = MathF.Round(width * (ShowImageProcessor.DjBannerHeight / (float)ShowImageProcessor.DjBannerWidth));

        var avatar = MathF.Round(Math.Clamp(bannerHeight * 1.05f, 112f * Metrics.Scale, 168f * Metrics.Scale));

        var headerHeight = bannerHeight + (avatar * 0.5f) + Metrics.Lg;

        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();
        var frameColor = new Vector4(profile.FrameColorR, profile.FrameColorG, profile.FrameColorB, 1f);

        var banner = plugin.DjDeckWindow.DjBannerImage(profile.Id, profile.BannerBase64);
        var bannerSize = new Vector2(width, bannerHeight);

        if (banner != null)
        {
            drawList.AddImageRounded(banner.Handle, origin, origin + bannerSize, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), Metrics.RadiusCard, ImDrawFlags.RoundCornersTop);
        }
        else
        {
            drawList.AddRectFilled(origin, origin + bannerSize,
                ImGui.GetColorU32(Semantic.Alpha(frameColor, 0.18f)), Metrics.RadiusCard, ImDrawFlags.RoundCornersTop);
        }

        drawList.AddRectFilledMultiColor(
            new Vector2(origin.X, origin.Y + (bannerHeight * 0.45f)), origin + bannerSize,
            0x00000000u, 0x00000000u,
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.75f)), ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.75f)));

        var avatarPos = Chrome.Snap(new Vector2(origin.X + Metrics.Xxl, origin.Y + bannerHeight - (avatar * 0.5f)));
        var avatarBox = new Vector2(avatar, avatar);
        var texture = plugin.DjDeckWindow.DjAvatarImage(profile.Id, profile.AvatarBase64);

        if (texture != null)
        {
            drawList.AddImageRounded(texture.Handle, avatarPos, avatarPos + avatarBox, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), Metrics.RadiusCard);
        }
        else
        {
            drawList.AddRectFilled(avatarPos, avatarPos + avatarBox,
                ImGui.GetColorU32(Elevation.Raised), Metrics.RadiusCard);

            using (TypeScale.IconLarge())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.User,
                    Chrome.Snap(avatarPos + (avatarBox * 0.5f)), ImGui.GetColorU32(Semantic.TextDisabled));
        }

        DjCosmetics.DrawAvatarFrame(drawList, avatarPos, avatarBox, Metrics.RadiusCard,
            frameColor, profile.FrameStyle, Metrics.Scale);

        var nameLeft = avatarPos.X + avatar + Metrics.Lg;

        var nameSize = TypeScale.Measure(TypeScale.Heading, profile.DjName);
        var nameY = origin.Y + bannerHeight - nameSize.Y - Metrics.Md;

        var nameColor = new Vector4(profile.NameColorR, profile.NameColorG, profile.NameColorB, 1f);
        if (nameColor.X + nameColor.Y + nameColor.Z < 0.05f)
            nameColor = Semantic.TextPrimary;

        ImGui.SetCursorScreenPos(new Vector2(nameLeft, nameY));
        DjCosmetics.DrawDjName(plugin.Fonts, profile.DjName, nameColor, profile.NameEffect, 1f, Metrics.Scale);

        if (profile.IsLiveNow)
        {
            var pulse = 0.55f + (Motion.Pulse(0.6f) * 0.45f);

            var dotCentreY = nameY + (nameSize.Y * 0.5f);

            drawList.AddCircleFilled(
                Chrome.Snap(new Vector2(nameLeft + nameSize.X + Metrics.Lg, dotCentreY)),
                5f * Metrics.Scale, ImGui.GetColorU32(Semantic.Alpha(Semantic.Live, pulse)));

            using (TypeScale.Caption())
                Chrome.Text(drawList,
                    Chrome.CenterY(nameLeft + nameSize.X + Metrics.Xxl, nameY, nameSize.Y, ImGui.GetTextLineHeight()),
                    ImGui.GetColorU32(Semantic.Live), "LIVE");
        }

        DrawHeaderActions(profile, origin, width, bannerHeight, isSample);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, headerHeight));
    }

    private void DrawHeaderActions(DjProfileDetailDto profile, Vector2 origin, float width, float bannerHeight, bool isSample)
    {
        var buttonHeight = MathF.Round(Metrics.ControlSm);
        var buttonWidth = MathF.Round(96f * Metrics.Scale);
        var y = origin.Y + bannerHeight + Metrics.Md;
        var right = origin.X + width - Metrics.Lg;

        if (profile.IsOwnProfile)
        {
            ImGui.SetCursorScreenPos(new Vector2(right - buttonWidth, y));
            if (plugin.DjDeckWindow.IsDeletingDjProfile)
            {
                using (TypeScale.Caption())
                    ImGui.TextColored(Semantic.TextTertiary, "Deleting...");
            }
            else if (Fields.Button("Delete", Fields.ButtonStyle.Danger, buttonWidth,
                         enabled: !isSample, height: buttonHeight, idSuffix: profile.Id))
            {
                plugin.DjDeckWindow.DeleteDjProfile(profile.Id);
            }

            ImGui.SetCursorScreenPos(new Vector2(right - (buttonWidth * 2f) - Metrics.Md, y));
            if (Fields.Button("Edit", Fields.ButtonStyle.Primary, buttonWidth,
                    enabled: !isSample, height: buttonHeight, idSuffix: profile.Id))
            {
                editRequest = profile;
            }

            return;
        }

        ImGui.SetCursorScreenPos(new Vector2(right - buttonWidth, y));
        if (Fields.Button("Report", Fields.ButtonStyle.Secondary, buttonWidth,
                enabled: !isSample, height: buttonHeight, idSuffix: profile.Id))
        {
            plugin.DjDeckWindow.OpenDjProfileReport();
        }

        var client = plugin.AudioHostClient;
        var likeWidth = MathF.Round(104f * Metrics.Scale);

        ImGui.SetCursorScreenPos(new Vector2(right - buttonWidth - likeWidth - Metrics.Md, y));
        if (Fields.Button(
                profile.IsFollowedByRequester ? $"Following {profile.FollowerCount}" : $"Follow {profile.FollowerCount}",
                profile.IsFollowedByRequester ? Fields.ButtonStyle.Toggled : Fields.ButtonStyle.Secondary,
                likeWidth, height: buttonHeight, idSuffix: $"follow{profile.Id}",
                icon: profile.IsFollowedByRequester ? FontAwesomeIcon.Bell : null,
                toggleAccent: Semantic.Primary))
        {
            if (isSample)
            {
#if DEBUG
                BrowseSampleData.ToggleFollow(profile.Id);
#endif
            }
            else
            {
                client.Send(MessageType.ToggleDjProfileFollow,
                    new ToggleDjProfileFollowMessage { ProfileId = profile.Id, CharacterName = CharacterName });
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(right - buttonWidth - (likeWidth * 2f) - (Metrics.Md * 2f), y));
        if (Fields.Button(
                profile.IsLikedByRequester ? $"Liked {profile.LikeCount}" : $"Like {profile.LikeCount}",
                profile.IsLikedByRequester ? Fields.ButtonStyle.Toggled : Fields.ButtonStyle.Secondary,
                likeWidth, height: buttonHeight, idSuffix: $"like{profile.Id}",
                icon: profile.IsLikedByRequester ? FontAwesomeIcon.Heart : null,
                toggleAccent: Semantic.DeckB))
        {
            if (isSample)
            {
#if DEBUG
                BrowseSampleData.ToggleLike(profile.Id);
#endif
            }
            else
            {
                client.Send(MessageType.ToggleDjProfileLike,
                    new ToggleDjProfileLikeMessage { ProfileId = profile.Id, CharacterName = CharacterName });
            }
        }
    }

    private void DrawDetails(DjProfileDetailDto profile)
    {
        Surfaces.SectionHeader("Details");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var any = false;

        if (profile.Genres.Count > 0)
        {
            DrawDetailRow("Genres", UiHelpers.JoinCapped(
                profile.Genres.Select(DjDeckWindow.TitleCaseGenre).ToList(), 3));
            any = true;
        }

        if (profile.SavedVenues.Count > 0)
        {
            if (any)
                Fields.Divider();

            DrawDetailRow("Venues", UiHelpers.JoinCapped(profile.SavedVenues.Select(v => v.Name).ToList(), 3));
            any = true;
        }

        if (profile.ShowLinkedCharacters && profile.LinkedCharacterNames.Count > 0)
        {
            if (any)
                Fields.Divider();

            DrawDetailRow("Also Known As", UiHelpers.JoinCapped(profile.LinkedCharacterNames, 3));
            any = true;
        }

        if (!any)
            Surfaces.RowText("This DJ hasn't filled anything in yet.", Semantic.TextTertiary);

        Surfaces.EndPanel();
    }

    private static void DrawDetailRow(string label, string value)
    {
        var row = Fields.BeginRow($"##profile{label}", label, null, 0f, rowClickable: false);

        var drawList = ImGui.GetWindowDrawList();
        var width = Surfaces.ContentWidth;
        var labelWidth = TypeScale.Measure(TypeScale.Body, label).X + (Metrics.Lg * 2f);
        var valueWidth = MathF.Max(Metrics.Xxl, width - labelWidth - Metrics.Lg);

        using (TypeScale.Caption())
        {
            var shown = UiHelpers.TruncateToWidth(value, valueWidth);
            var size = ImGui.CalcTextSize(shown);
            Chrome.Text(drawList,
                Chrome.CenterY(row.ControlMin.X + row.ControlWidth - size.X, row.ControlMin.Y, row.ControlHeight, size.Y),
                ImGui.GetColorU32(Semantic.TextSecondary), shown);
        }

        Fields.EndRow(row);
    }

    /// Seven days as a strip.
    private void DrawAvailability(DjProfileDetailDto profile)
    {
        if (profile.Availability.Count == 0)
            return;

        var zone = AvailabilitySlots.ZoneOf(profile.Availability);

        Surfaces.SectionHeader(zone == null ? "Usually On" : $"Usually On  ·  {zone}");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var width = Surfaces.ContentWidth;
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        var gap = Metrics.Sm;
        var cellWidth = (width - (gap * 6f)) / 7f;
        var lineHeight = ImGui.GetTextLineHeight();

        var cellHeight = MathF.Round((lineHeight * 2f) + (Metrics.Sm * 3f));

        for (var i = 0; i < 7; i++)
        {
            var day = i < profile.Availability.Count ? profile.Availability[i] : null;
            var on = day?.IsAvailable == true;
            var pos = Chrome.Snap(new Vector2(origin.X + (i * (cellWidth + gap)), origin.Y));
            var box = new Vector2(cellWidth, cellHeight);

            drawList.AddRectFilled(pos, pos + box,
                ImGui.GetColorU32(on ? Semantic.Alpha(Semantic.Primary, 0.22f) : Elevation.Sunken),
                Metrics.RadiusSoft);

            if (on)
                drawList.AddRect(pos, pos + box, ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.55f)),
                    Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline);

            var slot = on ? AvailabilitySlots.WindowOf(day?.TimeNote) : string.Empty;

            using (TypeScale.Caption())
            {
                var label = DayNames[i];
                var size = ImGui.CalcTextSize(label);

                var labelY = slot.Length == 0
                    ? pos.Y + ((cellHeight - size.Y) * 0.5f)
                    : pos.Y + Metrics.Sm;

                Chrome.Text(drawList, new Vector2(pos.X + ((cellWidth - size.X) * 0.5f), labelY),
                    ImGui.GetColorU32(on ? Semantic.TextPrimary : Semantic.TextDisabled), label);

                if (slot.Length == 0)
                    continue;

                var shown = UiHelpers.TruncateToWidth(slot, cellWidth - Metrics.Sm);
                var slotSize = ImGui.CalcTextSize(shown);
                Chrome.Text(drawList,
                    new Vector2(pos.X + ((cellWidth - slotSize.X) * 0.5f), pos.Y + lineHeight + (Metrics.Sm * 1.5f)),
                    ImGui.GetColorU32(Semantic.TextTertiary), shown);
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, cellHeight));

        Surfaces.EndPanel();
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
