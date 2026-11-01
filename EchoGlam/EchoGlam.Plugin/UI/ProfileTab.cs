using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoGlam.Game;
using EchoGlam.Shared;
using EchoGlam.UI.Controls;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI;

/// Somebody's page: who they are and what they have made.
public sealed class ProfileTab : IDisposable
{
    private readonly Plugin plugin;
    private readonly GalleryImages images;

    /// Whose page is showing, or null for your own.
    private string? viewingId;

    private ProfileDetail? viewing;
    private string? viewingError;

    /// What went wrong with the last picture upload, said under the picture.
    private string? problem;

    private readonly ImageCropDialog cropper = new();

    /// Which of the two lists your own page is showing.
    private bool showingFavourites;

    public ProfileTab(Plugin plugin, GalleryImages images)
    {
        this.plugin = plugin;
        this.images = images;
    }

    /// Releases the crop dialog's decoded source picture.
    public void Dispose() => cropper.Dispose();

    private GalleryState State => plugin.Gallery;

    /// Raised when a glamour on a profile is opened, so the Gallery tab can show it.
    public event Action<string>? OpenGlamour;

    /// Raised when the page somebody asked for turns out to be their own.
    public event Action? WasMine;

    /// Whose page this instance is showing, or null when it is showing your own - or, for the guest instance,
    /// nothing at all.
    public string? ViewingId => viewingId;

    /// Whether this instance currently stands in for somebody else's page.
    public bool IsViewing => viewingId is not null;

    /// Puts a guest view away entirely, rather than falling back to your own page.
    public void Stop()
    {
        viewingId = null;
        viewing = null;
        viewingError = null;
        returnTo = null;
    }

    /// Where Back goes - the glamour this profile was opened from.
    private Action? returnTo;

    /// Points the tab at somebody else's page.
    public void View(string? profileId, Action? back = null)
    {
        returnTo = back;
        viewing = null;
        viewingError = null;

        if (profileId is not null && profileId == State.MyProfile?.Id)
        {
            viewingId = null;
            WasMine?.Invoke();
            return;
        }

        if (profileId is null)
        {
            viewingId = null;
            return;
        }

        viewingId = profileId;
        LoadViewing(profileId);
    }

    /// The frame this tab last drew on.
    private int lastDrawnFrame = -10;


    /// How tall the shelf of their best work is.
    private const float ShelfDesign = 150f;

    /// How many of their best the shelf holds.
    private const int ShelfSlots = 6;

    /// How big the picture at the head of the page is.
    private const float DiscDesign = 84f;


    /// Air kept clear at the right-hand end of the identity band.
    private const float GutterDesign = 16f;

    /// Narrowest a card in the grid may be.
    private const float CardDesign = 180f;

    /// Set by the fetch when the relay says the page somebody asked for is their own.
    private volatile bool landedMine;

    public void Draw()
    {
        State.EnsureSettings();
        cropper.Draw();

        if (landedMine)
        {
            landedMine = false;
            Stop();
            WasMine?.Invoke();
            return;
        }

        var frame = ImGui.GetFrameCount();
        var returning = frame - lastDrawnFrame > 1;
        lastDrawnFrame = frame;

        if (viewingId is not null)
        {
            DrawTheirs();
            return;
        }

        State.RefreshProfile(force: returning);
        DrawMine();
    }

    private void DrawMine()
    {
        if (State.ProfileError is { } failure)
        {
            Notice.Fail(ImGui.GetContentRegionAvail().X, "Your profile couldn't be loaded", failure);
            return;
        }

        if (State.MyProfile is not { } profile)
        {
            Notice.Busy(ImGui.GetContentRegionAvail().X, "Loading", "Fetching your profile.");
            return;
        }

        DrawPage(profile, mine: true);
    }

    private void DrawTheirs()
    {
        var origin = ImGui.GetCursorScreenPos();

        if (viewingId is { } target)
            DrawReportPopup(target);

        if (BackLink.Draw(
                ImGui.GetWindowDrawList(), plugin.Fonts.Icon, "##profileback",
                returnTo is null ? "My profile" : "Back", origin, Metrics.TouchMin))
        {
            GoBack();
            return;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + Metrics.TouchMin + Sp.Lg));

        if (viewingError is { } failure)
        {
            Notice.Fail(ImGui.GetContentRegionAvail().X, "That profile couldn't be opened", failure);
            return;
        }

        if (viewing is not { } profile)
        {
            Notice.Busy(ImGui.GetContentRegionAvail().X, "Loading", "Fetching that profile.");
            return;
        }

        DrawPage(profile, mine: false);
    }

    /// Back to wherever this page was opened from, or to your own if it was opened from nowhere in
    /// particular.
    private void GoBack()
    {
        var back = returnTo;
        returnTo = null;

        if (back is not null)
            back();
        else
            View(null);
    }


    /// The shelf, the person, the grid.
    private void DrawPage(ProfileDetail profile, bool mine)
    {
        var halo = EchoButton.GlowReach;

        var available = ImGui.GetContentRegionAvail() - new Vector2(halo, halo);
        var origin = ImGui.GetCursorScreenPos();

        var shelf = DrawShelf(profile, mine, available.X);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + shelf));
        var identity = DrawIdentity(profile, mine, available.X);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + shelf + identity));
        var band = DrawViewBand(profile, mine, available.X);

        var consumed = shelf + identity + band;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + consumed));

        DrawWork(profile, mine, available.X, MathF.Max(Metrics.RowTall * 2f, available.Y - consumed));
    }


    /// Their best work, as the page's header image, in place of the banner that used to be here.
    private float DrawShelf(ProfileDetail profile, bool mine, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, ShelfDesign * UiHelpers.Scale);
        var max = origin + size;

        drawList.AddRectFilled(origin, max, Col.U32(Col.Sunken), Rad.Card);

        var featured = Featured(profile);

        if (featured.Count == 0)
            DrawEmptyShelf(drawList, origin, size, mine);
        else
            DrawFeatured(drawList, featured, origin, size);

        drawList.AddRect(
            origin, max, Col.U32(Col.EdgeRest with { W = 0.45f }), Rad.Card, ImDrawFlags.None, Stroke.Hair);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        return size.Y + Sp.Xl;
    }

    /// The pictures on the shelf.
    private void DrawFeatured(
        ImDrawListPtr drawList, List<GlamourSummary> entries, Vector2 min, Vector2 size)
    {
        var inset = Sp.Xs;
        var gap = Sp.Xs;
        var slotHeight = size.Y - (inset * 2f);

        var slotWidth = (size.X - (inset * 2f) - (gap * (ShelfSlots - 1))) / ShelfSlots;
        var run = (slotWidth * entries.Count) + (gap * (entries.Count - 1));
        var left = min.X + ((size.X - run) / 2f);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var slot = new Vector2(slotWidth, slotHeight);
            var slotMin = new Vector2(left + (i * (slotWidth + gap)), min.Y + inset);

            var cursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(slotMin);

            var clicked = ImGui.InvisibleButton($"##featured{i}", slot, ImGuiButtonFlags.MouseButtonLeft);
            var hovered = ImGui.IsItemHovered();

            ImGui.SetCursorScreenPos(cursor);

            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            var warm = Motion.Toward($"featured{entry.Id}", hovered, 16f);

            drawList.AddRectFilled(slotMin, slotMin + slot, Col.U32(Col.Surface), Rad.Panel);

            var url = entry.ImageCount > 0 ? GalleryClient.ImageUrl(entry, 0, card: true) : null;
            var texture = url is null ? null : images.Get(url);

            if (texture != null)
            {
                var (uv0, uv1) = HeadCrop(texture.Width, texture.Height, slotWidth, slotHeight);

                drawList.AddImageRounded(
                    texture.Handle, slotMin, slotMin + slot, uv0, uv1,
                    Col.U32(new Vector4(1f, 1f, 1f, 0.84f + (0.16f * warm))), Rad.Panel);
            }
            else
            {
                using (Text.PushCaption())
                {
                    var label = url is null || images.Failed(url) ? "No screenshot" : "Loading...";

                    drawList.AddText(
                        slotMin + ((slot - new Vector2(Text.Width(label), Text.CaptionSize)) / 2f),
                        Col.U32(Col.TextMuted), label);
                }
            }

            DrawShelfCaption(drawList, entry, slotMin, slot);

            drawList.AddRect(
                slotMin, slotMin + slot,
                Col.U32(Vector4.Lerp(Col.EdgeRest with { W = 0.4f }, Theme.Accent, warm)),
                Rad.Panel, ImDrawFlags.None, warm > 0.5f ? Stroke.Edge : Stroke.Hair);

            if (clicked)
                OpenGlamour?.Invoke(entry.Id);
        }
    }

    /// The title and its tallies, on the picture.
    private void DrawShelfCaption(
        ImDrawListPtr drawList, GlamourSummary entry, Vector2 min, Vector2 size)
    {
        var clear = Col.U32(new Vector4(0f, 0f, 0f, 0f));
        var solid = Col.U32(new Vector4(0f, 0f, 0f, 0.82f));
        var rounding = Rad.Panel;

        var line = Text.CaptionSize * 1.15f;
        var band = line + (Sp.Md * 2f);
        var bandTop = min.Y + size.Y - band;

        var foot = rounding * 3f;
        var fadeBottom = MathF.Max(bandTop, min.Y + size.Y - foot);

        drawList.PushClipRect(min, min + size, true);

        drawList.AddRectFilledMultiColor(
            new Vector2(min.X, bandTop), new Vector2(min.X + size.X, fadeBottom), clear, clear, solid, solid);

        drawList.AddRectFilled(
            new Vector2(min.X, fadeBottom), min + size, solid, rounding, ImDrawFlags.RoundCornersBottom);

        var left = min.X + Sp.Md;
        var room = size.X - (Sp.Md * 2f);
        var textY = min.Y + size.Y - Sp.Md - line;

        using (Text.PushCaption())
        {
            var counts = UiHelpers.DrawGlamourCounts(
                drawList, plugin.Fonts.Icon, new Vector2(left, textY), room,
                entry.Votes, entry.Voted, entry.Favourites, entry.Favourited);

            drawList.AddText(
                new Vector2(left, textY), Col.U32(Vector4.One),
                Text.Truncate(entry.Title, MathF.Max(0f, room - counts - Sp.Sm)));
        }

        drawList.PopClipRect();
    }

    /// The shelf with nothing on it.
    private void DrawEmptyShelf(ImDrawListPtr drawList, Vector2 min, Vector2 size, bool mine)
    {
        var centre = min + (size / 2f);

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList, FontAwesomeIcon.Images,
                centre - new Vector2(0f, Sp.Xxl), Col.U32(Theme.AccentSoft));
        }

        var heading = mine ? "Your best work shows here" : "Nothing published yet";

        var detail = mine
            ? "Publish a look from the Gallery. Whatever people vote for fills this shelf."
            : "They haven't put a glamour on the board.";

        var leading = ImGui.GetTextLineHeight();

        drawList.AddText(
            new Vector2(centre.X - (Text.Width(heading) / 2f), centre.Y), Col.U32(Col.Text), heading);

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(centre.X - (Text.Width(detail) / 2f), centre.Y + leading + Sp.Xs),
                Col.U32(Col.TextMuted), detail);
        }
    }

    /// Cover UVs biased toward the top of the source.
    private static (Vector2 Uv0, Vector2 Uv1) HeadCrop(
        int textureWidth, int textureHeight, float width, float height)
    {
        var (uv0, uv1) = Avatar.Cover(textureWidth, textureHeight, width, height);

        if (uv1.Y - uv0.Y >= 0.999f)
            return (uv0, uv1);

        var shift = uv0.Y * 0.75f;
        return (uv0 with { Y = uv0.Y - shift }, uv1 with { Y = uv1.Y - shift });
    }

    /// Their best work, best first.
    private List<GlamourSummary> Featured(ProfileDetail profile)
    {
        if (ReferenceEquals(featuredFor, profile))
            return featured;

        featuredFor = profile;

        featured =
        [
            .. profile.Glamours
                .OrderByDescending(entry => entry.Votes)
                .ThenByDescending(entry => entry.Favourites)
                .ThenByDescending(entry => entry.PublishedUtc)
                .Take(ShelfSlots),
        ];

        var likes = 0;
        var saves = 0;

        foreach (var entry in profile.Glamours)
        {
            likes += entry.Votes;
            saves += entry.Favourites;
        }

        tallies = (likes, saves);

        return featured;
    }

    /// What their published work has earned, added up.
    private (int Likes, int Saves) Tallies(ProfileDetail profile)
    {
        Featured(profile);
        return tallies;
    }

    private ProfileDetail? featuredFor;
    private List<GlamourSummary> featured = [];
    private (int Likes, int Saves) tallies;


    /// Who they are, on one row: the picture, the name, the two counts, and what you can do about them.
    private float DrawIdentity(ProfileDetail profile, bool mine, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var scale = UiHelpers.Scale;

        var disc = DiscDesign * scale;
        var row = disc;
        var right = origin.X + width - (GutterDesign * scale);


        if (!mine)
        {
            var followWidth = MathF.Max(
                EchoButton.ContentSize("Following", plugin.Fonts.Icon, FontAwesomeIcon.UserCheck).X,
                EchoButton.ContentSize("Follow", plugin.Fonts.Icon, FontAwesomeIcon.UserPlus).X) + Sp.Xl;

            var run = followWidth + Sp.Sm + Metrics.Row;

            ImGui.SetCursorScreenPos(new Vector2(right - run, origin.Y + ((row - Metrics.Row) / 2f)));

            if (EchoButton.Draw(
                    "##follow", profile.IsFollowing ? "Following" : "Follow",
                    new Vector2(followWidth, Metrics.Row),
                    iconFont: plugin.Fonts.Icon,
                    icon: profile.IsFollowing ? FontAwesomeIcon.UserCheck : FontAwesomeIcon.UserPlus,
                    selected: profile.IsFollowing,
                    tooltip: profile.IsFollowing
                        ? "Stop following them."
                        : "Adds them to the people you follow.",
                    primary: !profile.IsFollowing))
                ToggleFollow(profile.Id);

            ImGui.SameLine(0f, Sp.Sm);

            if (EchoButton.Draw(
                    "##reportprofile", null, new Vector2(Metrics.Row, Metrics.Row),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Flag,
                    tooltip: "Report this profile."))
                ImGui.OpenPopup(ReportPopupId);

            right -= run + Sp.Xxl;
        }

        var (likes, saves) = Tallies(profile);

        var followers = (int)MathF.Round(Motion.Count($"followers{profile.Id}", profile.Followers));

        MetaCell[] counts =
        [
            new MetaCell("Followers", followers.ToString(), Tooltip: "People following this profile."),
            new MetaCell("Following", profile.Following.ToString(), Tooltip: "Profiles they follow."),
            new MetaCell(
                "Likes", likes.ToString(), Col.Vote,
                "Every vote their published glamours have between them."),
            new MetaCell(
                "Saves", saves.ToString(), Col.Favourite,
                "How many times somebody starred one of their glamours."),
        ];

        var tiles = Meta.Width(counts);

        ImGui.SetCursorScreenPos(
            new Vector2(right - tiles, origin.Y + ((row - Meta.TileHeight) / 2f)));

        Meta.Tiles(tiles, counts);

        right -= tiles + Sp.Xxl;


        var inset = Avatar.Halo;

        DrawPicture(drawList, profile, mine, origin with { X = origin.X + inset }, disc);

        var textLeft = origin.X + inset + disc + Sp.Xl;
        var room = MathF.Max(Sp.Xxxl, right - textLeft);

        var block = (Text.DisplaySize * 1.2f) + (Text.CaptionSize * 1.4f);
        var textTop = origin.Y + ((row - block) / 2f);

        var name = string.IsNullOrWhiteSpace(profile.DisplayName) ? "Unnamed" : profile.DisplayName;

        using (Text.PushDisplay())
        {
            drawList.AddText(
                new Vector2(textLeft, textTop), Col.U32(Col.Text), Text.Truncate(name, room));
        }

        var world = string.IsNullOrWhiteSpace(profile.HomeWorld) ? "Unknown World" : profile.HomeWorld;

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, new Vector2(textLeft, textTop + (Text.DisplaySize * 1.2f)),
                world.ToUpperInvariant(), Col.U32(Col.TextDim), 1.6f, room);
        }

        var bottom = origin.Y + row;

        if (mine && problem is { } issue)
        {
            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(origin.X, bottom + Sp.Sm), Col.U32(Col.Danger),
                    Text.Truncate(issue, width));

                bottom += Sp.Sm + (Text.CaptionSize * 1.4f);
            }
        }

        var ruleY = bottom + Sp.Lg;

        drawList.AddRectFilled(
            new Vector2(origin.X, ruleY), new Vector2(origin.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, row));

        return ruleY + Stroke.Hair + Sp.Lg - origin.Y;
    }

    /// Their picture, which on your own page is also the control that changes it.
    private void DrawPicture(
        ImDrawListPtr drawList, ProfileSummary profile, bool mine, Vector2 min, float disc)
    {
        var url = profile.HasAvatar ? GalleryClient.AvatarUrl(profile) : null;

        if (Avatar.Draw(drawList, images, url, profile.DisplayName, min, disc, interactive: mine))
            PickPicture();

        if (!mine)
            return;

        var centre = min + new Vector2(disc / 2f, disc / 2f);
        var radius = disc / 2f;

        var badge = disc * 0.3f;
        var offset = radius * 0.68f;
        var badgeCentre = centre + new Vector2(offset, offset);

        const float BadgeInkFill = 0.58f;

        const float BadgeOpticalNudge = 0.06f;

        drawList.AddCircleFilled(badgeCentre, (badge / 2f) + Stroke.Mark, Col.U32(Col.Window));
        drawList.AddCircleFilled(badgeCentre, badge / 2f, Col.U32(Theme.Accent));

        var inkBox = badge * BadgeInkFill;

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawFittedIcon(
                drawList, FontAwesomeIcon.Camera,
                badgeCentre with { X = badgeCentre.X + (inkBox * BadgeOpticalNudge) }, inkBox,
                Col.U32(Col.TextOnAccent));
        }

        if (!ImGui.IsMouseHoveringRect(min, min + new Vector2(disc, disc)))
            return;

        UiHelpers.WrappedTooltip(
            profile.HasAvatar
                ? "Click to pick a different picture. Right-click to take this one off."
                : "Click to add a picture. It shows on every glamour you publish.");

        if (profile.HasAvatar && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            SetPicture(null);
    }


    /// Which list, and how long it is.
    private float DrawViewBand(ProfileDetail profile, bool mine, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var row = Metrics.Row;

        if (mine)
        {
            ReadOnlySpan<string> views = ["Published", "Saved"];

            ImGui.SetCursorScreenPos(origin);

            showingFavourites = EchoSegment.Draw(
                "##mineviews", views, showingFavourites ? 1 : 0, row,
                [
                    "Glamours you have put on the board.",
                    "Glamours you starred. Only you see this list.",
                ]) == 1;
        }
        else
        {
            using (Text.PushCaption())
            {
                Text.Tracked(
                    drawList, new Vector2(origin.X, origin.Y + ((row - Text.CaptionSize) / 2f)),
                    "PUBLISHED", Col.U32(Col.TextMuted), 1.8f, width);
            }
        }

        var list = Showing(profile, mine);
        var count = list.Count == 1 ? "1 glamour" : $"{list.Count} glamours";

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(
                    origin.X + width - Text.Width(count), origin.Y + ((row - Text.CaptionSize) / 2f)),
                Col.U32(Col.TextMuted with { W = 0.75f }), count);
        }

        var ruleY = origin.Y + row + Sp.Md;

        drawList.AddRectFilled(
            new Vector2(origin.X, ruleY), new Vector2(origin.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, row));

        return ruleY + Stroke.Hair + Sp.Lg - origin.Y;
    }

    private List<GlamourSummary> Showing(ProfileDetail profile, bool mine) =>
        mine && showingFavourites ? profile.Favourites : profile.Glamours;

    /// The grid.
    private void DrawWork(ProfileDetail profile, bool mine, float width, float height)
    {
        var list = Showing(profile, mine);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var open = ImGui.BeginChild("##profilework", new Vector2(width, height), false);
        ImGui.PopStyleVar();

        if (open)
        {
            if (list.Count == 0)
            {
                var room = ImGui.GetContentRegionAvail().X;

                if (mine && showingFavourites)
                {
                    Notice.Empty(
                        room, FontAwesomeIcon.Star, "Nothing saved yet",
                        "The star on a glamour keeps it here.");
                }
                else if (mine)
                {
                    Notice.Empty(
                        room, FontAwesomeIcon.Images, "Nothing published yet",
                        "Build a look in the Dressing Room, then publish it from the Gallery.");
                }
                else
                {
                    Notice.Empty(
                        room, FontAwesomeIcon.Images, "Nothing published",
                        "They haven't put a glamour on the board yet.");
                }
            }
            else
            {
                CardGrid.Draw(
                    list, entry => $"prof{entry.Id}", DrawCard,
                    stripDesign: 0f, minCard: CardDesign * UiHelpers.Scale);
            }
        }

        ImGui.EndChild();
    }

    /// One card's contents.
    private void DrawCard(GlamourSummary entry, CardSlot slot)
    {
        GlamourCard.Draw(
            ImGui.GetWindowDrawList(), images, plugin.Fonts.Icon, entry, slot, showAuthor: false);

        if (slot.Clicked)
            OpenGlamour?.Invoke(entry.Id);
    }


    private void LoadViewing(string profileId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var loaded = await GalleryClient
                    .ProfileAsync(profileId, State.OwnerKey, CancellationToken.None)
                    .ConfigureAwait(false);

                if (loaded is null)
                {
                    viewingError = "It isn't there any more.";
                    return;
                }

                if (loaded.Mine)
                {
                    landedMine = true;
                    return;
                }

                viewing = loaded;
            }
            catch (Exception ex)
            {
                viewingError = ex.Message;
            }
        });
    }

    private void ToggleFollow(string profileId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await GalleryClient
                    .FollowAsync(profileId, State.OwnerKey, CancellationToken.None)
                    .ConfigureAwait(false);

                if (viewing is { } current && current.Id == profileId)
                {
                    current.IsFollowing = result.Active;
                    current.Followers = result.Count;
                }
            }
            catch (Exception ex)
            {
                viewingError = ex.Message;
            }
        });
    }


    /// Opens a file browser, then the cropper on whatever was chosen.
    private void PickPicture()
    {
        problem = null;

        var fallback = Screenshots.DirectoryExists
            ? Screenshots.Directory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        plugin.FileDialogs.OpenFileDialog(
            "Pick a profile picture",
            "Images{.png,.jpg,.jpeg,.bmp,.webp}",
            (chosen, paths) =>
            {
                if (!chosen || paths.Count == 0)
                    return;

                plugin.Configuration.RememberBrowseDirectory(paths[0]);

                _ = cropper.OpenAsync(
                    paths[0],
                    ImageProcessor.AvatarSize, ImageProcessor.AvatarSize,
                    "Frame your picture",
                    SetPicture);
            },
            1,
            plugin.Configuration.BrowseStartDirectory(fallback));
    }

    /// Sends a new picture, or null to take the current one off.
    private void SetPicture(byte[]? jpeg)
    {
        problem = null;

        _ = Task.Run(async () =>
        {
            try
            {
                var updated = await GalleryClient
                    .SetPictureAsync(true, jpeg, State.OwnerKey, CancellationToken.None)
                    .ConfigureAwait(false);

                images.Forget(GalleryClient.AvatarUrlPrefix(updated.Id));

                State.Adopt(updated);
                State.RefreshProfile(force: true);
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }
        });
    }



    private const string ReportPopupId = "##echoglamprofilereport";

    private string reportNote = string.Empty;
    private string reportReason = string.Empty;
    private bool reporting;
    private string? reportDone;

    /// The report dialog's width and the wrap inside it, both from Modal so this one and the gallery's cannot
    /// be different sizes for no reason - which they were, at 260 and 280.
    private static float ReportWidth => Modal.Width(ModalSize.Small);

    private static float ReportNoteWrap => Modal.WrapWidth(ModalSize.Small);

    private void DrawReportPopup(string profileId)
    {
        if (!Modal.Begin(ReportPopupId, ModalSize.Small))
            return;

        var width = ReportWidth;

        Field.Label("Report This Profile", width);

        foreach (var reason in State.Settings.ReportReasons)
        {
            if (EchoButton.Draw(
                    $"##preason{reason}", reason, new Vector2(width, Metrics.Row),
                    selected: reportReason == reason))
                reportReason = reason;

            ImGui.Dummy(new Vector2(0f, Sp.Xxs));
        }

        ImGui.Dummy(new Vector2(0f, Sp.Sm));

        Field.Label(
            "Anything Else Worth Knowing", reportNote.Length, ReportReasons.MaximumNoteLength, width);

        Field.PushStyle();

        UiHelpers.WrappingInputTextMultiline(
            "##pnote", ref reportNote, ReportReasons.MaximumNoteLength,
            new Vector2(width, Metrics.Field * 2f), ReportNoteWrap);

        Field.PopStyle();

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        var ready = !reporting && !string.IsNullOrEmpty(reportReason);

        if (EchoButton.Draw(
                "##psendreport", reporting ? "Sending..." : "Send", new Vector2(width, Metrics.Row),
                enabled: ready, primary: ready))
            SendReport(profileId);

        if (reportDone is { } message)
        {
            ImGui.Dummy(new Vector2(0f, Sp.Sm));

            using (Text.PushCaption())
            {
                ImGui.GetWindowDrawList().AddText(
                    ImGui.GetCursorScreenPos(), Col.U32(Col.Success), Text.Truncate(message, width));
            }

            ImGui.Dummy(new Vector2(width, Text.CaptionSize * 1.4f));
        }

        Modal.End();
    }

    private void SendReport(string profileId)
    {
        reporting = true;
        reportDone = null;

        var report = new ReportSubmission
        {
            OwnerKey = State.OwnerKey,
            TargetId = profileId,
            Reason = reportReason,
            Note = UiHelpers.Unwrap(reportNote, ReportNoteWrap).Trim(),
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await GalleryClient.ReportProfileAsync(report, CancellationToken.None).ConfigureAwait(false);
                reportDone = "Sent. Thanks - somebody will look at it.";
                reportNote = string.Empty;
                reportReason = string.Empty;
            }
            catch (Exception ex)
            {
                reportDone = ex.Message;
            }
            finally
            {
                reporting = false;
            }
        });
    }
}
