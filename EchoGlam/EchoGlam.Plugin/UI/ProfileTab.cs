using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoGlam.Game;
using EchoGlam.Shared;
using EchoGlam.UI.Controls;

namespace EchoGlam.UI;

/// Somebody's page: who they are, what they have published, and - on your own - what you have saved.
public sealed class ProfileTab : IDisposable
{
    private readonly Plugin plugin;
    private readonly GalleryImages images;

    /// Whose page is showing, or null for your own.
    private string? viewingId;

    private ProfileDetail? viewing;
    private string? viewingError;

    /// Set while the display name and bio are being edited, so a half-typed name is not posted on every
    /// keystroke.
    private bool editing;
    private string draftName = string.Empty;
    private string draftBio = string.Empty;

    /// Set when the bio is loaded from the profile rather than typed, so it is laid out to the box on the
    /// first frame instead of on the first keystroke.
    private bool wrapBioOnNextDraw;

    /// The width the bio box was last laid out to, so the wrapper's breaks can be told from the author's own
    /// when it is saved.
    private float bioWrapWidth;

    /// The report note box is a fixed width, so this can be derived rather than remembered.
    private static float ReportNoteWrap =>
        (260f * UiHelpers.Scale) - (ImGui.GetStyle().FramePadding.X * 2f) - (18f * UiHelpers.Scale);
    private string draftWorld = string.Empty;
    private bool saving;
    private string? problem;

    private readonly ImageCropDialog cropper = new();

    /// Which of the two lists your own page is showing.
    private bool showingFavourites;

    public ProfileTab(Plugin plugin, GalleryImages images)
    {
        this.plugin = plugin;
        this.images = images;
    }

    public void Dispose() => cropper.Draw();

    private GalleryState State => plugin.Gallery;

    /// Raised when a glamour on a profile is opened, so the Gallery tab can show it.
    public event Action<string>? OpenGlamour;

    /// Where Back goes - the glamour this profile was opened from.
    private Action? returnTo;

    /// Points the tab at somebody else's page.
    public void View(string? profileId, Action? back = null)
    {
        returnTo = back;
        viewing = null;
        viewingError = null;
        editing = false;

        if (profileId is null || profileId == State.MyProfile?.Id)
        {
            viewingId = null;
            return;
        }

        viewingId = profileId;
        LoadViewing(profileId);
    }

    /// The frame this tab last drew on.
    private int lastDrawnFrame = -10;

    public void Draw()
    {
        State.EnsureSettings();
        cropper.Draw();

        var frame = ImGui.GetFrameCount();
        var returning = frame - lastDrawnFrame > 1;
        lastDrawnFrame = frame;

        if (viewingId is not null)
        {
            DrawSomebodyElse();
            return;
        }

        State.RefreshProfile(force: returning && !editing);
        DrawMine();
    }


    private void DrawMine()
    {
        var scale = UiHelpers.Scale;

        if (State.ProfileError is { } failure)
        {
            DrawNotice(FontAwesomeIcon.PlugCircleExclamation, "Your profile couldn't be loaded", failure);
            return;
        }

        if (State.MyProfile is not { } profile)
        {
            DrawNotice(FontAwesomeIcon.Spinner, "Loading", "Fetching your profile.");
            return;
        }

        DrawHeader(profile, mine: true);
        ImGui.Dummy(new Vector2(0f, 10f * scale));

        var height = 24f * scale;

        if (EchoButton.Draw(
                "##mineglam", $"Published ({profile.Glamours.Count})", new Vector2(0f, height),
                selected: !showingFavourites))
            showingFavourites = false;

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##minefav", $"Saved ({profile.Favourites.Count})", new Vector2(0f, height),
                selected: showingFavourites))
            showingFavourites = true;

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var list = showingFavourites ? profile.Favourites : profile.Glamours;

        if (list.Count == 0)
        {
            ImGui.TextColored(
                Theme.TextDim,
                showingFavourites
                    ? "Nothing saved yet. The star on a glamour keeps it here."
                    : "Nothing published yet. Build a look in the Dressing Room, then publish it from the Gallery.");

            return;
        }

        DrawGrid(list, mine: !showingFavourites);
    }

    /// The header: banner, avatar, name, counts, and the controls that belong to whoever is looking.
    private void DrawHeader(ProfileSummary profile, bool mine)
    {
        var scale = UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;

        var contentLeft = ImGui.GetCursorPosX();

        var bannerHeight = MathF.Min(180f * scale, width / 4f);
        var origin = ImGui.GetCursorScreenPos();

        ImGui.Dummy(new Vector2(width, bannerHeight));

        drawList.AddRectFilled(
            origin, origin + new Vector2(width, bannerHeight),
            ImGui.GetColorU32(Theme.Tinted(0.10f)), Theme.CardRounding);

        if (profile.HasBanner && images.Get(GalleryClient.BannerUrl(profile)) is { } banner)
        {
            var (uv0, uv1) = Cover(banner.Width, banner.Height, width, bannerHeight);

            drawList.AddImageRounded(
                banner.Handle, origin, origin + new Vector2(width, bannerHeight),
                uv0, uv1, ImGui.GetColorU32(Vector4.One), Theme.CardRounding);
        }

        drawList.AddRect(
            origin, origin + new Vector2(width, bannerHeight), ImGui.GetColorU32(Theme.Border),
            Theme.CardRounding, ImDrawFlags.None, 1.2f * scale);

        var avatarSize = 130f * scale;
        var avatarMin = new Vector2(origin.X + (16f * scale), origin.Y + bannerHeight - (avatarSize / 2f));

        drawList.AddRectFilled(
            avatarMin - new Vector2(3f * scale, 3f * scale),
            avatarMin + new Vector2(avatarSize + (3f * scale), avatarSize + (3f * scale)),
            ImGui.GetColorU32(Theme.Background), 10f * scale);

        drawList.AddRectFilled(
            avatarMin, avatarMin + new Vector2(avatarSize, avatarSize),
            ImGui.GetColorU32(Theme.Tinted(0.16f)), 8f * scale);

        if (profile.HasAvatar && images.Get(GalleryClient.AvatarUrl(profile)) is { } avatar)
        {
            drawList.AddImageRounded(
                avatar.Handle, avatarMin, avatarMin + new Vector2(avatarSize, avatarSize),
                Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 8f * scale);
        }
        else
        {
            using (plugin.Fonts.Icon.PushSafe())
            {
                UiHelpers.DrawScaledIcon(
                    drawList, FontAwesomeIcon.User,
                    avatarMin + new Vector2(avatarSize / 2f, avatarSize / 2f),
                    ImGui.GetColorU32(Theme.TextDisabled));
            }
        }

        drawList.AddRect(
            avatarMin, avatarMin + new Vector2(avatarSize, avatarSize), ImGui.GetColorU32(Theme.Accent),
            8f * scale, ImDrawFlags.None, 1.5f * scale);

        ImGui.Dummy(new Vector2(0f, (avatarSize / 2f) + (6f * scale)));

        var textX = ImGui.GetCursorPosX() + (16f * scale) + avatarSize + (14f * scale);
        var textTop = ImGui.GetCursorPosY() - (avatarSize / 2f) - (2f * scale);

        ImGui.SetCursorPos(new Vector2(textX, textTop));
        ImGui.BeginGroup();

        var name = string.IsNullOrWhiteSpace(profile.DisplayName) ? "Unnamed" : profile.DisplayName;
        float nameHeight;

        using (plugin.Fonts.Header.PushSafe())
        {
            nameHeight = ImGui.GetTextLineHeight();
            ImGui.TextUnformatted(name);
        }

        if (mine && !editing)
        {
            var buttonHeight = 24f * scale;
            var editWidth = EchoButton.ContentSize("Edit", plugin.Fonts.Icon, FontAwesomeIcon.UserEdit).X;

            ImGui.SameLine(0f, 0f);
            ImGui.SetCursorPosX(contentLeft + width - editWidth);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + MathF.Max(0f, (nameHeight - buttonHeight) / 2f));

            if (EchoButton.Draw(
                    "##editprofile", "Edit", new Vector2(0f, buttonHeight),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.UserEdit,
                    tooltip: "Your bio, avatar and banner."))
            {
                draftName = profile.DisplayName;
                draftBio = State.MyProfile?.Bio ?? string.Empty;
                wrapBioOnNextDraw = true;
                draftWorld = profile.HomeWorld;
                editing = true;
                problem = null;
            }
        }

        var where = string.IsNullOrWhiteSpace(profile.HomeWorld) ? "" : profile.HomeWorld + "  ·  ";
        ImGui.TextColored(
            Theme.TextDim,
            $"{where}{profile.Published} published  ·  {profile.Followers} follower{(profile.Followers == 1 ? "" : "s")}"
            + $"  ·  {profile.Following} following");

        if (!mine)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));
            DrawTheirControls(profile);
        }

        ImGui.EndGroup();
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (mine)
            DrawMyControls();

        if (viewing is { Bio.Length: > 0 } theirs && !mine)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextUnformatted(theirs.Bio);
            ImGui.PopTextWrapPos();
        }
    }

    private void DrawMyControls()
    {
        var scale = UiHelpers.Scale;
        var height = 24f * scale;
        var profile = State.MyProfile!;

        if (!editing)
        {
            if (!string.IsNullOrWhiteSpace(profile.Bio))
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
                ImGui.TextUnformatted(profile.Bio);
                ImGui.PopTextWrapPos();
            }

            if (problem is { } issue)
            {
                ImGui.Dummy(new Vector2(0f, 4f * scale));
                ImGui.TextColored(Theme.Bad, issue);
            }

            return;
        }

        var width = ImGui.GetContentRegionAvail().X;

        ImGui.TextColored(Theme.TextDim, $"Bio  ({draftBio.Length}/400)");

        var bioWrap = width - (ImGui.GetStyle().FramePadding.X * 2f) - (18f * scale);

        if (wrapBioOnNextDraw)
        {
            draftBio = UiHelpers.Rewrap(draftBio, bioWrap);
            wrapBioOnNextDraw = false;
        }

        bioWrapWidth = bioWrap;

        UiHelpers.WrappingInputTextMultiline(
            "##profilebio", ref draftBio, 400, new Vector2(width, 64f * scale), bioWrap);

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        ImGui.TextColored(Theme.TextDim, "Pictures");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        if (EchoButton.Draw(
                "##setavatar", profile.HasAvatar ? "Change Avatar" : "Add Avatar", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Image,
                tooltip: profile.HasAvatar ? "Right-click to remove it." : "Pick a picture."))
            PickPicture(avatar: true);

        if (EchoButton.RightClicked() && profile.HasAvatar)
            ClearPicture(avatar: true);

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##setbanner", profile.HasBanner ? "Change Banner" : "Add Banner", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Images,
                tooltip: profile.HasBanner ? "Right-click to remove it." : "Pick a picture."))
            PickPicture(avatar: false);

        if (EchoButton.RightClicked() && profile.HasBanner)
            ClearPicture(avatar: false);

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        if (EchoButton.Draw("##saveprofile", saving ? "Saving..." : "Save", new Vector2(0f, height), enabled: !saving))
            SaveProfile();

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw("##cancelprofile", "Cancel", new Vector2(0f, height), enabled: !saving))
        {
            editing = false;
            problem = null;
        }

        if (problem is { } message)
        {
            ImGui.Dummy(new Vector2(0f, 4f * scale));
            ImGui.TextColored(Theme.Bad, message);
        }
    }

    private void DrawTheirControls(ProfileSummary profile)
    {
        var scale = UiHelpers.Scale;
        var height = 24f * scale;

        if (EchoButton.Draw(
                "##follow", profile.IsFollowing ? "Following" : "Follow", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon,
                icon: profile.IsFollowing ? FontAwesomeIcon.UserCheck : FontAwesomeIcon.UserPlus,
                selected: profile.IsFollowing))
            ToggleFollow(profile.Id);

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##reportprofile", null, new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Flag,
                tooltip: "Report this profile."))
            ImGui.OpenPopup(ReportPopupId);

        DrawReportPopup(profile.Id);
    }


    private void DrawSomebodyElse()
    {
        var scale = UiHelpers.Scale;

        DrawBack();
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (viewingError is { } failure)
        {
            DrawNotice(FontAwesomeIcon.PlugCircleExclamation, "That profile couldn't be opened", failure);
            return;
        }

        if (viewing is not { } profile)
        {
            DrawNotice(FontAwesomeIcon.Spinner, "Loading", "Fetching that profile.");
            return;
        }

        DrawHeader(profile, mine: false);
        ImGui.Dummy(new Vector2(0f, 12f * scale));

        if (profile.Glamours.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "Nothing published.");
            return;
        }

        Theme.SectionHeader($"Published ({profile.Glamours.Count})");
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawGrid(profile.Glamours, mine: false);
    }

    /// Back to wherever this page was opened from, or to your own if it was opened from nowhere in
    /// particular.
    private void DrawBack()
    {
        if (!EchoButton.Draw(
                "##profileback", "Back", new Vector2(0f, 26f * UiHelpers.Scale),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.ChevronLeft))
            return;

        var back = returnTo;
        returnTo = null;

        if (back is not null)
            back();
        else
            View(null);
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
                    viewingId = null;
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


    /// A compact grid of glamour cards.
    private void DrawGrid(System.Collections.Generic.List<GlamourSummary> entries, bool mine)
    {
        var scale = UiHelpers.Scale;
        var gap = 10f * scale;
        var available = ImGui.GetContentRegionAvail().X;

        var columns = Math.Max(1, (int)((available + gap) / ((200f * scale) + gap)));
        var cardWidth = (available - (gap * (columns - 1))) / columns;
        var imageHeight = ImageProcessor.HeightFor(cardWidth);
        var cardHeight = imageHeight + (46f * scale);

        var drawList = ImGui.GetWindowDrawList();

        for (var i = 0; i < entries.Count; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine(0f, gap);

            var entry = entries[i];
            var pos = ImGui.GetCursorScreenPos();
            var size = new Vector2(cardWidth, cardHeight);

            var clicked = ImGui.InvisibleButton($"##profcard{entry.Id}", size, ImGuiButtonFlags.MouseButtonLeft);
            var hovered = ImGui.IsItemHovered();

            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Theme.Panel), Theme.CardRounding);

            if (entry.ImageCount > 0 && images.Get(GalleryClient.ImageUrl(entry, 0, card: true)) is { } texture)
            {
                drawList.AddImageRounded(
                    texture.Handle, pos, pos + new Vector2(cardWidth, imageHeight),
                    Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One),
                    Theme.CardRounding, ImDrawFlags.RoundCornersTop);
            }
            else
            {
                drawList.AddRectFilled(
                    pos, pos + new Vector2(cardWidth, imageHeight), ImGui.GetColorU32(Theme.Tinted(0.06f)),
                    Theme.CardRounding, ImDrawFlags.RoundCornersTop);
            }

            drawList.PushClipRect(pos, pos + size, true);
            drawList.AddText(
                pos + new Vector2(10f * scale, imageHeight + (7f * scale)),
                ImGui.GetColorU32(Theme.Text),
                UiHelpers.Truncate(entry.Title, cardWidth - (20f * scale)));

            UiHelpers.DrawGlamourCounts(
                drawList, plugin.Fonts.Icon,
                pos + new Vector2(10f * scale, imageHeight + (7f * scale) + ImGui.GetTextLineHeight()),
                cardWidth - (20f * scale),
                entry.Votes, entry.Voted, entry.Favourites, entry.Favourited);

            drawList.PopClipRect();

            drawList.AddRect(
                pos, pos + size,
                ImGui.GetColorU32(hovered ? Theme.Accent : Theme.Border),
                Theme.CardRounding, ImDrawFlags.None, (hovered ? 1.8f : 1.2f) * scale);

            if (clicked)
                OpenGlamour?.Invoke(entry.Id);

        }

    }

    private const string ReportPopupId = "##echoglamprofilereport";

    private string reportNote = string.Empty;
    private string reportReason = string.Empty;
    private bool reporting;
    private string? reportDone;

    private void DrawReportPopup(string profileId)
    {
        if (!ImGui.BeginPopup(ReportPopupId))
            return;

        var scale = UiHelpers.Scale;

        Theme.SectionHeader("Report this profile", ruleWidth: 260f * scale);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        foreach (var reason in State.Settings.ReportReasons)
        {
            if (EchoButton.Draw($"##preason{reason}", reason, new Vector2(0f, 22f * scale), selected: reportReason == reason))
                reportReason = reason;
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.TextColored(Theme.TextDim, "Anything else worth knowing (optional)");
        UiHelpers.WrappingInputTextMultiline(
            "##pnote", ref reportNote, 300, new Vector2(260f * scale, 50f * scale), ReportNoteWrap);

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (EchoButton.Draw(
                "##psendreport", reporting ? "Sending..." : "Send", new Vector2(0f, 24f * scale),
                enabled: !reporting && !string.IsNullOrEmpty(reportReason)))
        {
            SendReport(profileId);
        }

        if (reportDone is { } message)
        {
            ImGui.Dummy(new Vector2(0f, 4f * scale));
            ImGui.TextColored(Theme.Good, message);
        }

        ImGui.EndPopup();
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

    private void SaveProfile()
    {
        saving = true;
        problem = null;

        var (characterName, characterWorld) = GalleryState.Character;

        var submission = new ProfileSubmission
        {
            OwnerKey = State.OwnerKey,
            DisplayName = string.IsNullOrEmpty(characterName) ? State.MyProfile?.DisplayName ?? string.Empty : characterName,
            Bio = UiHelpers.Unwrap(draftBio, bioWrapWidth).Trim(),
            HomeWorld = string.IsNullOrEmpty(characterName) ? State.MyProfile?.HomeWorld ?? string.Empty : characterWorld,
        };

        _ = Task.Run(async () =>
        {
            try
            {
                var updated = await GalleryClient
                    .UpdateProfileAsync(submission, CancellationToken.None)
                    .ConfigureAwait(false);

                State.Adopt(updated);
                State.RefreshProfile(force: true);
                editing = false;
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }
            finally
            {
                saving = false;
            }
        });
    }

    /// Opens a file browser, then the cropper on whatever was chosen.
    private void PickPicture(bool avatar)
    {
        problem = null;

        var start = Screenshots.DirectoryExists
            ? Screenshots.Directory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        plugin.FileDialogs.OpenFileDialog(
            avatar ? "Pick an avatar" : "Pick a banner",
            "Images{.png,.jpg,.jpeg,.bmp,.webp}",
            (chosen, paths) =>
            {
                if (!chosen || paths.Count == 0)
                    return;

                _ = cropper.OpenAsync(
                    paths[0],
                    avatar ? ImageProcessor.AvatarSize : ImageProcessor.BannerWidth,
                    avatar ? ImageProcessor.AvatarSize : ImageProcessor.BannerHeight,
                    avatar ? "Frame your avatar" : "Frame your banner",
                    jpeg => UploadPicture(avatar, jpeg));
            },
            1,
            start);
    }

    private void UploadPicture(bool avatar, byte[]? jpeg)
    {
        problem = null;

        _ = Task.Run(async () =>
        {
            try
            {
                var updated = await GalleryClient
                    .SetPictureAsync(avatar, jpeg, State.OwnerKey, CancellationToken.None)
                    .ConfigureAwait(false);

                State.Adopt(updated);
                State.RefreshProfile(force: true);
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }
        });
    }

    private void ClearPicture(bool avatar) => UploadPicture(avatar, null);

    private void DrawNotice(FontAwesomeIcon icon, string heading, string body)
    {
        var scale = UiHelpers.Scale;
        var available = ImGui.GetContentRegionAvail();

        ImGui.Dummy(new Vector2(0f, MathF.Max(20f * scale, (available.Y * 0.25f) - (40f * scale))));

        var drawList = ImGui.GetWindowDrawList();
        var centreX = ImGui.GetCursorScreenPos().X + (available.X / 2f);

        using (plugin.Fonts.Icon.PushSafe())
        {
            var glyph = icon.ToIconString();
            var font = ImGui.GetFont();
            var glyphSize = 30f * scale;
            var measured = ImGui.CalcTextSize(glyph) * (glyphSize / ImGui.GetFontSize());

            drawList.AddText(
                font, glyphSize,
                new Vector2(centreX - (measured.X / 2f), ImGui.GetCursorScreenPos().Y),
                ImGui.GetColorU32(Theme.AccentSoft), glyph, 0f);
        }

        ImGui.Dummy(new Vector2(0f, 40f * scale));

        Centred(heading, Theme.Text);
        ImGui.Dummy(new Vector2(0f, 4f * scale));
        Centred(body, Theme.TextDim);
    }

    /// UVs that fill a rectangle with the largest centred piece of a source that fits it - CSS
    /// background-size:cover, as texture coordinates.
    private static (Vector2 Min, Vector2 Max) Cover(int sourceWidth, int sourceHeight, float width, float height)
    {
        var wanted = width / height;
        var actual = (float)sourceWidth / sourceHeight;

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

        return (uv0, uv1);
    }

    private static void Centred(string text, Vector4 colour)
    {
        var width = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (ImGui.GetContentRegionAvail().X - width) / 2f));
        ImGui.TextColored(colour, text);
    }
}
