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
    /// Below this the two-column body folds back to one column.
    private const float TwoColumnMinWidth = 620f;

    /// How much larger than the Header font the hero name is drawn.
    private const float HeroNameScale = 1.45f;

    private readonly Plugin plugin;

    /// Set by the Edit button on your own profile, for Browse to pick up and switch category with.
    private DjProfileDetailDto? editRequest;

    private const string DeletePopupId = "##echomixdeletedjprofile";

    /// Set by the Delete action, consumed one frame later by DrawDeleteConfirm.
    private bool deleteConfirmRequested;

    /// 2.0's own Report This Profile dialog.
    private readonly ReportProfileDialog reportDialog;

    public DjProfileSection(Plugin plugin)
    {
        this.plugin = plugin;
        reportDialog = new ReportProfileDialog(plugin);
    }

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
        var profile = Resolve(profileId, isSample, out var noticeIcon, out var noticeMessage);

        if (DrawTopBar(profile, isSample))
            return false;

        if (profile == null)
        {
            DrawNotice(noticeIcon, noticeMessage);
            return true;
        }

        DrawProfile(profile, isSample);

        if (!isSample)
            reportDialog.Draw(profile.Id);

        return true;
    }

    /// The profile to draw, or null with a notice to show instead.
    private DjProfileDetailDto? Resolve(
        string profileId, bool isSample, out FontAwesomeIcon noticeIcon, out string noticeMessage)
    {
        noticeIcon = FontAwesomeIcon.User;
        noticeMessage = "Loading profile...";

#if DEBUG

        if (isSample)
        {
            if (BrowseSampleData.Detail(profileId) is { } sample)
                return sample;

            noticeMessage = "No sample profile for that DJ.";
            return null;
        }
#endif

        var detail = plugin.AudioHostClient.LatestDjProfileDetail;

        if (detail?.Profile == null)
        {
            if (detail?.Error is { } error)
            {
                noticeIcon = FontAwesomeIcon.ExclamationTriangle;
                noticeMessage = error;
            }

            return null;
        }

        return string.Equals(detail.Profile.Id, profileId, StringComparison.Ordinal)
            ? detail.Profile
            : null;
    }

    /// Back on the left, and on your own listing Delete and Edit on the right, all on one row.
    private bool DrawTopBar(DjProfileDetailDto? profile, bool isSample)
    {
        var width = Surfaces.ContentWidth;
        var rowOrigin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var height = MathF.Round(Metrics.ControlMd);

        var back = LinkAction.Back("djprofile", "Back", height);

        if (profile is { IsOwnProfile: true })
            DrawOwnerLinks(profile, rowOrigin, width, height, isSample);

        ImGui.SetCursorScreenPos(new Vector2(rowOrigin.X, rowOrigin.Y + height));
        Surfaces.Gap(Metrics.Md);

        return back;
    }

    /// The page itself, in one place so the DEBUG fixture walks exactly the real layout rather than a
    /// parallel copy of it that can drift.
    private void DrawProfile(DjProfileDetailDto profile, bool isSample)
    {
        var width = Surfaces.ContentWidth;

        DrawHero(profile, width, isSample);
        DrawStatRibbon(profile, width);
        DrawTrophyShelf(profile, width);
        DrawBody(profile, width);
        DrawDeleteConfirm(profile, isSample);
    }

    /// The confirmation in front of Delete.
    private void DrawDeleteConfirm(DjProfileDetailDto profile, bool isSample)
    {
        if (deleteConfirmRequested)
        {
            ImGui.OpenPopup(DeletePopupId);
            deleteConfirmRequested = false;
        }

        using var detached = Surfaces.Detach();

        if (!ImGui.BeginPopup(DeletePopupId))
            return;

        var content = MathF.Round(330f * Metrics.Scale);
        ImGui.Dummy(new Vector2(content, 0f));

        using (TypeScale.Heading())
            ImGui.TextColored(Semantic.TextPrimary, "Delete this listing?");

        Surfaces.Gap(Metrics.Md);

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content);
        ImGui.TextColored(Semantic.TextSecondary,
            "It comes off the DJ list for good, along with every like, follower, badge and show "
            + "statistic it has collected. The EchoMix 1.0 founder badge is closed and cannot be "
            + "earned again. Your saved venues and your mixes are untouched.");
        ImGui.PopTextWrapPos();

        Surfaces.Gap(Metrics.Xl);

        var buttonHeight = MathF.Round(Metrics.ControlMd);
        var keepWidth = MeasureAction("Keep It", false);
        var deleteWidth = MeasureAction("Delete", true);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX()
            + MathF.Max(0f, content - keepWidth - deleteWidth - Metrics.Md));

        if (Fields.Button("Keep It", Fields.ButtonStyle.Secondary, keepWidth,
                height: buttonHeight, idSuffix: profile.Id, pill: true))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0f, Metrics.Md);

        if (Fields.Button("Delete", Fields.ButtonStyle.Danger, deleteWidth,
                enabled: !isSample && !plugin.DjDeckWindow.IsDeletingDjProfile,
                icon: FontAwesomeIcon.TrashAlt, height: buttonHeight,
                idSuffix: $"confirm{profile.Id}", pill: true))
        {
            ImGui.CloseCurrentPopup();
            plugin.DjDeckWindow.DeleteDjProfile(profile.Id);
        }

        ImGui.EndPopup();
    }


    /// Artwork, name, and the actions - all one object.
    private void DrawHero(DjProfileDetailDto profile, float width, bool isSample)
    {
        var aspectHeight = width * (ShowImageProcessor.DjBannerHeight / (float)ShowImageProcessor.DjBannerWidth);
        var heroHeight = MathF.Round(Math.Clamp(aspectHeight, 190f * Metrics.Scale, 280f * Metrics.Scale));

        var avatar = MathF.Round(Math.Clamp(heroHeight * 0.46f, 96f * Metrics.Scale, 132f * Metrics.Scale));
        var overhang = MathF.Round(avatar * 0.42f);

        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();
        var frameColor = new Vector4(profile.FrameColorR, profile.FrameColorG, profile.FrameColorB, 1f);

        var heroMin = origin;
        var heroMax = origin + new Vector2(width, heroHeight);

        DrawHeroArtwork(profile, drawList, heroMin, heroMax, heroHeight, frameColor);

        var avatarPos = Chrome.Snap(new Vector2(
            origin.X + Metrics.Xxl,
            origin.Y + heroHeight - (avatar - overhang)));
        var avatarBox = new Vector2(avatar, avatar);
        var avatarRadius = avatar * 0.5f;

        var texture = plugin.DjDeckWindow.DjAvatarImage(profile.Id, profile.AvatarBase64);

        drawList.AddCircleFilled(Chrome.Snap(avatarPos + (avatarBox * 0.5f)),
            avatarRadius + (4f * Metrics.Scale), ImGui.GetColorU32(Elevation.Base), 64);

        if (texture != null)
        {
            drawList.AddImageRounded(texture.Handle, avatarPos, avatarPos + avatarBox,
                Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), avatarRadius);
        }
        else
        {
            drawList.AddCircleFilled(Chrome.Snap(avatarPos + (avatarBox * 0.5f)), avatarRadius,
                ImGui.GetColorU32(Elevation.Raised), 64);

            using (TypeScale.IconLarge())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.User,
                    Chrome.Snap(avatarPos + (avatarBox * 0.5f)), ImGui.GetColorU32(Semantic.TextDisabled));
        }

        DjCosmetics.DrawAvatarFrame(drawList, avatarPos, avatarBox, avatarRadius,
            frameColor, profile.FrameStyle, Metrics.Scale);

        var baseline = origin.Y + heroHeight - Metrics.Xl;
        var nameLimit = origin.X + width - Metrics.Xl;

        DrawHeroName(profile, drawList, avatarPos, avatar, nameLimit, baseline);
        DrawLivePill(profile, drawList, heroMin);

        var buttonHeight = MathF.Round(Metrics.ControlSm);
        var underBand = overhang + Metrics.Lg;
        var actionsY = heroMax.Y + MathF.Round((underBand - buttonHeight) * 0.5f);

        DrawSocialRow(profile, origin, width, actionsY, buttonHeight, isSample);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, heroHeight + overhang + Metrics.Lg));
    }

    /// The artwork layer: image or fallback, the DJ's colour wash, and the scrim.
    private void DrawHeroArtwork(
        DjProfileDetailDto profile,
        ImDrawListPtr drawList,
        Vector2 heroMin,
        Vector2 heroMax,
        float heroHeight,
        Vector4 frameColor)
    {
        var banner = plugin.DjDeckWindow.DjBannerImage(profile.Id, profile.BannerBase64);

        if (banner != null)
        {
            drawList.AddImageRounded(banner.Handle, heroMin, heroMax, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), Metrics.RadiusPanel, ImDrawFlags.RoundCornersTop);
        }
        else
        {
            drawList.AddRectFilled(heroMin, heroMax,
                ImGui.GetColorU32(Semantic.Alpha(frameColor, 0.28f)),
                Metrics.RadiusPanel, ImDrawFlags.RoundCornersTop);

            drawList.AddRectFilledMultiColor(
                heroMin + new Vector2(0f, heroHeight * 0.25f), heroMax,
                ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0f)),
                ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0f)),
                ImGui.GetColorU32(Semantic.Alpha(frameColor, 0.12f)),
                ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0f)));
        }


        drawList.AddRectFilledMultiColor(
            heroMin + new Vector2(0f, heroHeight * 0.38f), heroMax,
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0f)),
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0f)),
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.88f)),
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.88f)));

        drawList.AddLine(
            new Vector2(heroMin.X, heroMax.Y - Metrics.Hairline),
            new Vector2(heroMax.X, heroMax.Y - Metrics.Hairline),
            ImGui.GetColorU32(Semantic.Alpha(frameColor, 0.5f)), Metrics.Hairline * 2f);
    }

    /// The name, set large over the scrim, with the "also known as" line under it.
    private void DrawHeroName(
        DjProfileDetailDto profile,
        ImDrawListPtr drawList,
        Vector2 avatarPos,
        float avatar,
        float actionsLeft,
        float baseline)
    {
        var nameLeft = avatarPos.X + avatar + Metrics.Xl;
        var room = MathF.Max(Metrics.Xxxl, actionsLeft - nameLeft - Metrics.Lg);

        var shown = profile.DjName;
        var nameSize = TypeScale.Measure(TypeScale.Heading, shown) * HeroNameScale;

        if (nameSize.X > room)
        {
            using (TypeScale.Heading())
                shown = UiHelpers.TruncateToWidth(profile.DjName, room / HeroNameScale);

            nameSize = TypeScale.Measure(TypeScale.Heading, shown) * HeroNameScale;
        }

        var nameColor = new Vector4(profile.NameColorR, profile.NameColorG, profile.NameColorB, 1f);
        if (nameColor.X + nameColor.Y + nameColor.Z < 0.05f)
            nameColor = Semantic.TextPrimary;

        var hasAlso = profile.ShowLinkedCharacters && profile.LinkedCharacterNames.Count > 0;
        var effectDescent = DjCosmetics.NameEffectDescent(profile.NameEffect, Metrics.Scale);
        var alsoGap = (Metrics.Xs * 2f) + effectDescent;
        var alsoHeight = hasAlso ? ImGui.GetTextLineHeight() + alsoGap : 0f;

        var nameY = baseline - alsoHeight - nameSize.Y;

        ImGui.SetCursorScreenPos(Chrome.Snap(new Vector2(nameLeft, nameY)));
        DjCosmetics.DrawDjName(plugin.Fonts, shown, nameColor, profile.NameEffect, HeroNameScale, Metrics.Scale);

        if (!hasAlso)
            return;

        using (TypeScale.Caption())
        {
            var also = UiHelpers.TruncateToWidth(
                $"also {UiHelpers.JoinCapped(profile.LinkedCharacterNames, 2)}", room);

            Chrome.Text(drawList,
                Chrome.Snap(new Vector2(nameLeft, nameY + nameSize.Y + alsoGap)),
                ImGui.GetColorU32(Semantic.TextSecondary), also);
        }
    }

    /// LIVE as a glowing capsule in the hero's top-right corner.
    private static void DrawLivePill(DjProfileDetailDto profile, ImDrawListPtr drawList, Vector2 heroTopLeft)
    {
        if (!profile.IsLiveNow)
            return;

        var pulse = 0.55f + (Motion.Pulse(0.6f) * 0.45f);

        using (TypeScale.Caption())
        {
            const string label = "LIVE";
            var textSize = ImGui.CalcTextSize(label);
            var dot = 5f * Metrics.Scale;
            var padX = Metrics.Lg;

            var pillHeight = MathF.Round(textSize.Y + (Metrics.Sm * 1.8f));
            var pillWidth = MathF.Round(textSize.X + (padX * 2f) + (dot * 2f) + Metrics.Sm);

            var pillMin = Chrome.Snap(new Vector2(
                heroTopLeft.X + Metrics.Xl,
                heroTopLeft.Y + Metrics.Xl));
            var pillMax = pillMin + new Vector2(pillWidth, pillHeight);
            var radius = Metrics.Pill(pillHeight);

            drawList.AddRectFilled(pillMin, pillMax,
                ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.6f)), radius);
            drawList.AddRect(pillMin, pillMax,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Live, 0.5f + (pulse * 0.5f))),
                radius, ImDrawFlags.None, Metrics.Hairline);

            drawList.AddCircleFilled(
                Chrome.Snap(new Vector2(pillMin.X + padX, pillMin.Y + (pillHeight * 0.5f))),
                dot, ImGui.GetColorU32(Semantic.Alpha(Semantic.Live, pulse)));

            Chrome.Text(drawList,
                Chrome.CenterY(pillMin.X + padX + (dot * 2f) + Metrics.Sm, pillMin.Y, pillHeight, textSize.Y),
                ImGui.GetColorU32(Semantic.Live), label);
        }
    }


    /// All-time numbers as a bare ribbon: values, labels, hairline dividers, no panel.
    private static void DrawStatRibbon(DjProfileDetailDto profile, float width)
    {
        if (profile.Stats is not { } stats || stats.ShowsPlayed <= 0)
            return;

        var cells = new (string Label, string Value)[]
        {
            ("Shows", stats.ShowsPlayed.ToString("N0", CultureInfo.InvariantCulture)),
            ("On Air", StatsSection.FormatDuration(stats.OnAirSeconds)),
            ("Listener Hours", StatsSection.FormatDuration(stats.ListenerSeconds)),
            ("Biggest Room", stats.PeakListeners.ToString("N0", CultureInfo.InvariantCulture)),
            ("Days Active", stats.DaysActive.ToString("N0", CultureInfo.InvariantCulture)),
            ("People Reached", stats.UniqueListeners.ToString("N0", CultureInfo.InvariantCulture)),
        };

        var minCell = 112f * Metrics.Scale;
        var columns = Math.Clamp((int)(width / minCell), 2, cells.Length);
        var rows = (cells.Length + columns - 1) / columns;

        var cellWidth = width / columns;
        var valueHeight = TypeScale.Measure(TypeScale.Heading, "0").Y;
        var labelHeight = ImGui.GetTextLineHeight();
        var rowHeight = MathF.Round(valueHeight + labelHeight + (Metrics.Lg * 2f));

        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();
        var totalHeight = rowHeight * rows;

        for (var i = 0; i < cells.Length; i++)
        {
            var column = i % columns;
            var row = i / columns;
            var cellMin = Chrome.Snap(new Vector2(
                origin.X + (column * cellWidth),
                origin.Y + (row * rowHeight)));

            if (column > 0)
            {
                drawList.AddLine(
                    new Vector2(cellMin.X, cellMin.Y + Metrics.Lg),
                    new Vector2(cellMin.X, cellMin.Y + rowHeight - Metrics.Lg),
                    ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);
            }

            using (TypeScale.Heading())
            {
                var size = ImGui.CalcTextSize(cells[i].Value);
                Chrome.Text(drawList,
                    new Vector2(cellMin.X + ((cellWidth - size.X) * 0.5f), cellMin.Y + Metrics.Lg),
                    ImGui.GetColorU32(Semantic.TextPrimary), cells[i].Value);
            }

            using (TypeScale.Caption())
            {
                var shown = UiHelpers.TruncateToWidth(cells[i].Label, cellWidth - Metrics.Md);
                var size = ImGui.CalcTextSize(shown);
                Chrome.Text(drawList,
                    new Vector2(
                        cellMin.X + ((cellWidth - size.X) * 0.5f),
                        cellMin.Y + Metrics.Lg + valueHeight + (Metrics.Xs * 2f)),
                    ImGui.GetColorU32(Semantic.TextTertiary), shown);
            }
        }

        drawList.AddLine(origin, new Vector2(origin.X + width, origin.Y),
            ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);
        drawList.AddLine(
            new Vector2(origin.X, origin.Y + totalHeight),
            new Vector2(origin.X + width, origin.Y + totalHeight),
            ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, totalHeight));
        Surfaces.Gap(Metrics.Xl);
    }


    /// The tier a badge belongs to, from its id.
    private static Vector4 BadgeTint(string id)
    {
        if (id.EndsWith("-gold", StringComparison.Ordinal))
            return new Vector4(1f, 0.80f, 0.33f, 1f);

        if (id.EndsWith("-silver", StringComparison.Ordinal))
            return new Vector4(0.80f, 0.84f, 0.90f, 1f);

        if (id.EndsWith("-bronze", StringComparison.Ordinal))
            return new Vector4(0.85f, 0.56f, 0.33f, 1f);

        return Semantic.Primary;
    }

    /// The categories a monthly medal was won in, as its own block in the tooltip.
    private static string CategoryLines(DjBadgeDto badge)
    {
        if (badge.Categories.Count == 0)
            return string.Empty;

        var titles = new List<string>();
        foreach (var key in badge.Categories)
        {
            if (BadgeCatalog.CategoryTitle(key) is { } title)
                titles.Add(title);
        }

        if (titles.Count == 0)
            return string.Empty;

        return "\n\n" + string.Join("\n", titles);
    }

    /// What this DJ has been granted, as medals mounted in a recessed shelf.
    private void DrawTrophyShelf(DjProfileDetailDto profile, float width)
    {
        if (profile.Badges.Count == 0)
            return;

        var art = MathF.Round(Metrics.ControlXl * 1.25f);
        var pad = MathF.Round(Metrics.Lg);
        var gap = MathF.Round(Metrics.Lg);
        var stride = art + gap;

        var inner = MathF.Max(art, width - (pad * 2f));

        var perRow = Math.Max(1, (int)((inner + gap) / stride));

        var drawable = profile.Badges.Where(b => BadgeCatalog.Find(b.Id) != null).ToList();
        if (drawable.Count == 0)
            return;

        var rows = (drawable.Count + perRow - 1) / perRow;
        var shelfHeight = (rows * stride) - gap + (pad * 2f);

        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        Elevation.DrawSurface(drawList, origin, origin + new Vector2(width, shelfHeight),
            Elevation.Sunken, Metrics.RadiusCard, Elevation.ShadowSpec.None,
            topEdge: false, border: Elevation.Line);

        for (var i = 0; i < drawable.Count; i++)
        {
            var badge = drawable[i];
            if (BadgeCatalog.Find(badge.Id) is not { } entry)
                continue;

            var column = i % perRow;
            var row = i / perRow;

            var pos = new Vector2(
                origin.X + pad + (column * stride),
                origin.Y + pad + (row * stride));
            var box = new Vector2(art, art);

            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton($"##v2badge{i}", box);
            var hovered = ImGui.IsItemHovered();

            var tint = BadgeTint(badge.Id);
            var centre = Chrome.Snap(pos + (box * 0.5f));

            Elevation.DrawSurface(drawList, pos, pos + box,
                Semantic.Alpha(tint, hovered ? 0.14f : 0.07f),
                Metrics.RadiusCard, Elevation.ShadowSpec.None,
                topEdge: false,
                border: Semantic.Alpha(tint, hovered ? 0.85f : 0.45f));

#if DEBUG

            BadgeAlignTool.Record(badge.Id, pos, box);
#endif

            var artScale = entry.ArtScale;
            var artOffset = new Vector2(entry.OffsetX, entry.OffsetY) * Metrics.Scale;

#if DEBUG

            if (BadgeAlignTool.Enabled)
            {
                var nudge = BadgeAlignTool.For(badge.Id);
                artScale *= nudge.Z;
                artOffset += new Vector2(nudge.X, nudge.Y) * Metrics.Scale;
            }
#endif

            if (plugin.BadgeTextures.Get(entry.ResourceName) is { } texture)
            {
                var drawn = MathF.Round(art * 0.82f * artScale);
                var artMin = Chrome.Snap(centre - new Vector2(drawn * 0.5f, drawn * 0.5f) + artOffset);

                drawList.AddImage(texture.Handle, artMin, artMin + new Vector2(drawn, drawn),
                    Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(Semantic.Alpha(Vector4.One, hovered ? 1f : 0.92f)));
            }
            else
            {
                var well = MathF.Round(art * 0.82f);
                var wellMin = Chrome.Snap(centre - new Vector2(well * 0.5f, well * 0.5f));
                drawList.AddRectFilled(wellMin, wellMin + new Vector2(well, well),
                    ImGui.GetColorU32(Elevation.Base), Metrics.RadiusSoft);
            }

            if (hovered)
            {
                var awarded = badge.AwardedAtUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
                Tip.Hovered(entry.Title, $"{entry.Description}{CategoryLines(badge)}\n\nObtained {awarded}");
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, shelfHeight));

#if DEBUG
        BadgeAlignTool.Draw(drawable, width);
#endif

        Surfaces.Gap(Metrics.Xl);
    }


    /// About and Details on the left, the week on the right.
    private void DrawBody(DjProfileDetailDto profile, float width)
    {
        if (width < TwoColumnMinWidth * Metrics.Scale)
        {
            DrawAbout(profile, width);
            DrawWeek(profile, width);
            DrawDetails(profile, width);
            return;
        }

        var gap = Metrics.Xl;
        var leftWidth = MathF.Round((width - gap) * 0.58f);
        var rightWidth = width - gap - leftWidth;
        var start = Chrome.Snap(ImGui.GetCursorScreenPos());

        ImGui.SetCursorScreenPos(start);
        ImGui.BeginGroup();
        DrawAbout(profile, leftWidth);
        DrawDetails(profile, leftWidth);
        ImGui.EndGroup();
        var leftHeight = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorScreenPos(new Vector2(start.X + leftWidth + gap, start.Y));
        ImGui.BeginGroup();
        DrawWeek(profile, rightWidth);
        ImGui.EndGroup();
        var rightHeight = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(width, MathF.Max(leftHeight, rightHeight)));
    }

    /// The bio, with no panel around it.
    private static void DrawAbout(DjProfileDetailDto profile, float width)
    {
        if (string.IsNullOrWhiteSpace(profile.Bio))
            return;

        Surfaces.SectionHeader("About", width: width);
        Surfaces.Gap(Metrics.Md);

        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + width);
        using (TypeScale.Body())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.TextSecondary);
            ImGui.TextWrapped(profile.Bio);
        }

        ImGui.PopTextWrapPos();
        Surfaces.Gap(Metrics.Xl);
    }

    /// The week as a VERTICAL list - one row per day, time on the right.
    private static void DrawWeek(DjProfileDetailDto profile, float width)
    {
        if (profile.Availability.Count == 0)
            return;

        var zone = AvailabilitySlots.ZoneOf(profile.Availability);

        Surfaces.SectionHeader(zone == null ? "Usually On" : $"Usually On  Â·  {zone}", width: width);
        Surfaces.Gap(Metrics.Md);

        var labels = DjDeckWindow.DjAvailabilityDays;

        var today = (int)DateTime.Now.DayOfWeek;

        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();
        var rowHeight = MathF.Round(ImGui.GetTextLineHeight() + (Metrics.Md * 1.4f));
        var rowGap = Metrics.Xs * 2f;
        var labelWidth = MathF.Round(Metrics.Xxxl * 1.4f);

        for (var i = 0; i < 7; i++)
        {
            var day = i < profile.Availability.Count ? profile.Availability[i] : null;
            var on = day?.IsAvailable == true;
            var isToday = i == today;

            var rowMin = Chrome.Snap(new Vector2(origin.X, origin.Y + (i * (rowHeight + rowGap))));
            var rowMax = rowMin + new Vector2(width, rowHeight);
            var radius = Metrics.RadiusSoft;

            drawList.AddRectFilled(rowMin, rowMax,
                ImGui.GetColorU32(on ? Semantic.Alpha(Semantic.Primary, 0.14f) : Semantic.Alpha(Vector4.One, 0.025f)),
                radius);

            if (on)
            {
                drawList.AddRectFilled(rowMin, new Vector2(rowMin.X + (3f * Metrics.Scale), rowMax.Y),
                    ImGui.GetColorU32(Semantic.Primary), radius, ImDrawFlags.RoundCornersLeft);
            }

            if (isToday)
            {
                drawList.AddRect(rowMin, rowMax,
                    ImGui.GetColorU32(Semantic.Alpha(on ? Semantic.Primary : Semantic.TextSecondary, 0.8f)),
                    radius, ImDrawFlags.None, Metrics.Hairline);
            }

            using (TypeScale.Caption())
            {
                var label = labels[i];
                var labelSize = ImGui.CalcTextSize(label);
                Chrome.Text(drawList,
                    Chrome.CenterY(rowMin.X + Metrics.Lg, rowMin.Y, rowHeight, labelSize.Y),
                    ImGui.GetColorU32(on ? Semantic.TextPrimary
                        : isToday ? Semantic.TextSecondary : Semantic.TextDisabled), label);

                var slot = on ? AvailabilitySlots.WindowOf(day?.TimeNote) : "--";
                var slotRoom = MathF.Max(Metrics.Xxl, width - labelWidth - (Metrics.Lg * 2f));
                var shown = UiHelpers.TruncateToWidth(slot, slotRoom);
                var slotSize = ImGui.CalcTextSize(shown);

                Chrome.Text(drawList,
                    Chrome.CenterY(rowMax.X - Metrics.Lg - slotSize.X, rowMin.Y, rowHeight, slotSize.Y),
                    ImGui.GetColorU32(on ? Semantic.TextSecondary : Semantic.TextDisabled), shown);
            }
        }

        var totalHeight = (7f * rowHeight) + (6f * rowGap);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, totalHeight));
        Surfaces.Gap(Metrics.Xl);
    }

    /// Genres and venues, as a short list of facts.
    private static void DrawDetails(DjProfileDetailDto profile, float width)
    {
        var hasGenres = profile.Genres.Count > 0;
        var hasVenues = profile.SavedVenues.Count > 0;

        if (!hasGenres && !hasVenues)
            return;

        Surfaces.SectionHeader("Details", width: width);
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel(width);

        if (hasGenres)
        {
            DrawDetailRow("Genres", UiHelpers.JoinCapped(
                profile.Genres.Select(DjDeckWindow.TitleCaseGenre).ToList(), 3));
        }

        if (hasVenues)
        {
            if (hasGenres)
                Fields.Divider();

            DrawDetailRow("Venues", UiHelpers.JoinCapped(profile.SavedVenues.Select(v => v.Name).ToList(), 3));
        }

        Surfaces.EndPanel();
        Surfaces.Gap(Metrics.Xl);
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


    /// A width from the label itself, rather than one fixed number for every action.
    private static float MeasureAction(string label, bool hasIcon)
    {
        float textWidth;
        using (TypeScale.Body())
            textWidth = ImGui.CalcTextSize(label).X;

        var iconRoom = hasIcon ? Metrics.Xl + Metrics.Sm : 0f;
        return MathF.Round(textWidth + iconRoom + (Metrics.Lg * 2f));
    }

    /// Report, Like and Follow, right-aligned in the band under the banner.
    private void DrawSocialRow(
        DjProfileDetailDto profile, Vector2 origin, float width, float y, float height, bool isSample)
    {
        if (profile.IsOwnProfile)
            return;

        var likeLabel = profile.IsLikedByRequester ? $"Liked {profile.LikeCount}" : $"Like {profile.LikeCount}";
        var followLabel = profile.IsFollowedByRequester
            ? $"Following {profile.FollowerCount}"
            : $"Follow {profile.FollowerCount}";

        var reportWidth = Capsule.Width("Report", true);
        var likeWidth = Capsule.Width(likeLabel, true);
        var followWidth = Capsule.Width(followLabel, true);

        var gap = Metrics.Md;
        var total = reportWidth + likeWidth + followWidth + (gap * 2f);

        var listenWidth = 0f;
        var canListen = profile.IsLiveNow && !string.IsNullOrWhiteSpace(profile.LiveRoomCode);

        if (canListen)
        {
            listenWidth = MeasureAction("Listen", true);
            total += listenWidth + gap;
        }

        var x = origin.X + width - Metrics.Xl - total;

        ImGui.SetCursorScreenPos(Chrome.Snap(new Vector2(x, y)));
        if (Capsule.Draw($"report{profile.Id}", "Report", FontAwesomeIcon.Flag,
                active: false, enabled: !isSample, height: height))
        {
            reportDialog.Open();
        }

        x += reportWidth + gap;

        ImGui.SetCursorScreenPos(Chrome.Snap(new Vector2(x, y)));
        if (Capsule.Draw($"like{profile.Id}", likeLabel, FontAwesomeIcon.Heart,
                profile.IsLikedByRequester, Semantic.DeckB, height: height))
        {
            ToggleLike(profile, isSample);
        }

        x += likeWidth + gap;

        ImGui.SetCursorScreenPos(Chrome.Snap(new Vector2(x, y)));
        if (Capsule.Draw($"follow{profile.Id}", followLabel, FontAwesomeIcon.Bell,
                profile.IsFollowedByRequester, Semantic.Primary, height: height))
        {
            ToggleFollow(profile, isSample);
        }

        if (!canListen)
            return;

        x += followWidth + gap;
        var room = profile.LiveRoomCode!;

        ImGui.SetCursorScreenPos(Chrome.Snap(new Vector2(x, y)));
        if (Fields.Button("Listen", Fields.ButtonStyle.Primary, listenWidth, enabled: !isSample,
                icon: FontAwesomeIcon.Headphones, height: height,
                idSuffix: profile.Id, pill: true))
        {
            plugin.DjDeckWindow.JoinPublicShow(room, string.Empty);
        }
    }

    /// Managing your own listing, right-aligned on the top bar beside Back.
    private void DrawOwnerLinks(DjProfileDetailDto profile, Vector2 rowOrigin, float width, float height, bool isSample)
    {
        var deleting = plugin.DjDeckWindow.IsDeletingDjProfile;
        var deleteLabel = deleting ? "Deleting" : "Delete";

        var editWidth = LinkAction.Width("Edit", true);
        var deleteWidth = LinkAction.Width(deleteLabel, true);
        var gap = Metrics.Xxl;

        var x = rowOrigin.X + width - editWidth - gap - deleteWidth;

        ImGui.SetCursorScreenPos(Chrome.Snap(new Vector2(x, rowOrigin.Y)));
        if (LinkAction.Draw($"del{profile.Id}", deleteLabel, FontAwesomeIcon.TrashAlt,
                Semantic.Danger, enabled: !isSample && !deleting, height: height))
        {
            deleteConfirmRequested = true;
        }

        ImGui.SetCursorScreenPos(Chrome.Snap(new Vector2(x + deleteWidth + gap, rowOrigin.Y)));
        if (LinkAction.Draw($"edit{profile.Id}", "Edit", FontAwesomeIcon.Pen,
                enabled: !isSample, height: height))
        {
            editRequest = profile;
        }
    }

    private void ToggleLike(DjProfileDetailDto profile, bool isSample)
    {
        if (isSample)
        {
#if DEBUG
            BrowseSampleData.ToggleLike(profile.Id);
#endif
            return;
        }

        plugin.AudioHostClient.Send(MessageType.ToggleDjProfileLike,
            new ToggleDjProfileLikeMessage { ProfileId = profile.Id, CharacterName = CharacterName });
    }

    private void ToggleFollow(DjProfileDetailDto profile, bool isSample)
    {
        if (isSample)
        {
#if DEBUG
            BrowseSampleData.ToggleFollow(profile.Id);
#endif
            return;
        }

        plugin.AudioHostClient.Send(MessageType.ToggleDjProfileFollow,
            new ToggleDjProfileFollowMessage { ProfileId = profile.Id, CharacterName = CharacterName });
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
