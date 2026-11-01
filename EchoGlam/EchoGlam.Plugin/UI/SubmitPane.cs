using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using EchoGlam.Game;
using EchoGlam.Shared;
using EchoGlam.UI.Controls;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI;

/// Publishing a look, and editing one already published.
public sealed class SubmitPane : IDisposable
{
    private readonly Plugin plugin;
    private readonly ImageCropDialog cropper = new();
    private readonly Screenshots screenshots;

    /// The gallery's own image cache, shared rather than a second one.
    private readonly GalleryImages images;

    /// The tag chooser, which owns the band it is showing and what has been typed into its search.
    private readonly TagPicker tagPicker = new("submit", TagPicker.Mode.Many);

    /// One screenshot in the form: either a new one on its way up, or one the glamour being edited already
    /// has.
    private sealed class Attachment : IDisposable
    {
        /// The finished JPEG, for a new screenshot.
        public byte[] Jpeg = [];

        /// Which of the glamour's existing screenshots this is, or -1 for a new one.
        public int Existing = GlamourSubmission.NewImage;

        /// Only ever the preview of a new screenshot, which this owns.
        public IDalamudTextureWrap? Preview;

        public void Dispose() => Preview?.Dispose();
    }

    private readonly List<Attachment> attachments = [];

    /// Which attachment the plate is showing.
    private int shown;

    private string title = string.Empty;
    private string description = string.Empty;

    /// The width the description box was last laid out to, so the breaks the wrapper added can be told from
    /// the author's own on the way out.
    private float descriptionWrap;

    /// Set when something other than typing puts text in the description, so the next frame that knows how
    /// wide the box is can wrap it.
    private bool descriptionUnwrapped;

    /// Whether the description box had the keyboard last frame.
    private bool descriptionActive;

    private readonly HashSet<string> chosenTags = [];
    private uint jobId;
    private bool includeAppearance;

    /// The entry being edited, or null when this is a new submission.
    private string? editingId;

    /// The entry being edited, kept for its id and image stamp - between them those build the URL of a
    /// screenshot it already has.
    private GlamourSummary? editing;

    /// The look being published.
    private Dictionary<GlamSlot, GlamEntry> look = [];
    private CustomizeSet? appearance;

    /// Who is publishing: the character's name and world, and the race the board filters by.
    private (string Name, string World, byte Race, byte Tribe, byte Sex) author;

    private bool capturing;
    private bool publishing;
    private string? problem;
    private string? done;

    /// The id of a submission that has just landed, handed from the sending task to the draw loop.
    private volatile string? finished;

    /// Whether that landing was a deletion rather than a save.
    private bool removed;

    private List<string> recentFiles = [];
    private readonly Dictionary<string, IDalamudTextureWrap?> recentThumbnails = [];
    private bool recentLoaded;

    public SubmitPane(Plugin plugin, Screenshots screenshots, GalleryImages images)
    {
        this.plugin = plugin;
        this.screenshots = screenshots;
        this.images = images;
    }

    /// Whether the pane is showing.
    public bool IsOpen { get; private set; }

    /// Whether anything has been put into the form.
    private bool HasContent =>
        attachments.Count > 0
        || !string.IsNullOrWhiteSpace(title)
        || !string.IsNullOrWhiteSpace(description)
        || chosenTags.Count > 0
        || jobId != 0;

    private GalleryState State => plugin.Gallery;

    public void Dispose()
    {
        foreach (var attachment in attachments)
            attachment.Dispose();

        foreach (var thumbnail in recentThumbnails.Values)
            thumbnail?.Dispose();

        attachments.Clear();
        recentThumbnails.Clear();

        cropper.Dispose();
    }

    /// Opens the form for the look currently on the character.
    public void OpenForCurrentLook()
    {
        if (editingId is null && HasContent)
        {
            IsOpen = true;
            return;
        }

        Reset();

        look = new Dictionary<GlamSlot, GlamEntry>(plugin.Wardrobe.Current);
        appearance = plugin.Wardrobe.CurrentAppearance;

        problem = look.Count == 0
            ? "There's nothing in your wardrobe slots yet. Put a look together in the Dressing Room first."
            : null;

        IsOpen = true;
    }

    /// Opens the form against something already published, to change it.
    public void OpenForEdit(GlamourDetail entry)
    {
        if (editingId == entry.Id)
        {
            IsOpen = true;
            return;
        }

        Reset();

        editingId = entry.Id;
        editing = entry;
        title = entry.Title;
        description = entry.Description;
        descriptionUnwrapped = true;
        jobId = entry.JobId;

        for (var i = 0; i < entry.ImageCount; i++)
            attachments.Add(new Attachment { Existing = i });

        foreach (var tag in entry.Tags)
            chosenTags.Add(tag);

        look = entry.Slots
            .Where(s => s.Slot is >= 0 and <= 11)
            .ToDictionary(s => (GlamSlot)s.Slot, s => new GlamEntry(s.ItemId, s.Stain0, s.Stain1));

        appearance = entry.Appearance is null ? null : CustomizeSet.FromBase64(entry.Appearance);
        includeAppearance = appearance is not null;

        IsOpen = true;
    }

    /// Puts the pane away and KEEPS what is in it.
    public void Close() => IsOpen = false;

    /// Starts again: empties the form and re-reads the look from the character.
    private void Clear()
    {
        var entry = editing;
        Reset();

        if (entry is GlamourDetail published)
        {
            OpenForEdit(published);
            return;
        }

        look = new Dictionary<GlamSlot, GlamEntry>(plugin.Wardrobe.Current);
        appearance = plugin.Wardrobe.CurrentAppearance;

        problem = look.Count == 0
            ? "There's nothing in your wardrobe slots yet. Put a look together in the Dressing Room first."
            : null;
    }

    private void Reset()
    {
        foreach (var attachment in attachments)
            attachment.Dispose();

        attachments.Clear();
        shown = 0;
        title = string.Empty;
        description = string.Empty;
        descriptionUnwrapped = false;
        chosenTags.Clear();
        jobId = 0;
        includeAppearance = false;
        editingId = null;
        editing = null;
        appearance = null;
        look = [];
        problem = null;
        done = null;
        capturing = false;
        publishing = false;
    }

    /// Takes a reading of the character, if the game is currently in a position to give one.
    private void RefreshAuthor()
    {
        if (Plugin.ObjectTable.LocalPlayer is not { } player)
            return;

        author = (
            player.Name.TextValue,
            player.HomeWorld.Value.Name.ExtractText(),
            player.Customize[(int)CustomizeIndex.Race],
            player.Customize[(int)CustomizeIndex.Tribe],
            player.Customize[(int)CustomizeIndex.Sex]);
    }

    /// The name and world to publish under.
    private (string Name, string World) Author => author.Name.Length > 0
        ? (author.Name, author.World)
        : (State.MyProfile?.DisplayName ?? string.Empty, State.MyProfile?.HomeWorld ?? string.Empty);


    /// The picture column, which is the same width the gallery's detail page gives a glamour.
    private const float PlateDesign = 420f;

    /// One thumbnail in the rail under the plate.
    private const float ThumbDesign = 64f;

    public void Draw()
    {
        var scale = UiHelpers.Scale;
        State.EnsureSettings();
        RefreshAuthor();

        if (finished is { } landed)
        {
            var wasRemoval = removed;

            finished = null;
            removed = false;

            Reset();
            IsOpen = false;

            if (wasRemoval)
                Removed?.Invoke(landed);
            else
                Published?.Invoke(landed);

            return;
        }

        cropper.Draw();

        DrawRecentPopup();
        DrawDeletePopup();
        DrawClearPopup();
        DrawRulesPopup();

        if (DrawTopBar())
        {
            Close();
            return;
        }

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        var halo = EchoButton.GlowReach;

        var available = ImGui.GetContentRegionAvail() - new Vector2(halo, halo);
        var origin = ImGui.GetCursorScreenPos();

        var column = (PlateDesign * scale) + Sp.Xxl;
        var formWidth = MathF.Max(320f * scale, available.X - column);


        DrawPlate(column - Sp.Xxl, available.Y);

        var rule = origin.X + column - (Sp.Xxl / 2f);

        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(rule, origin.Y),
            new Vector2(rule + Stroke.Hair, origin.Y + available.Y),
            Col.U32(Col.EdgeRest with { W = 0.5f }));

        var left = origin.X + column;

        ImGui.SetCursorScreenPos(new Vector2(left, origin.Y));
        var describe = DrawDescribeBand(formWidth, available.Y);

        ImGui.SetCursorScreenPos(new Vector2(left, origin.Y + describe));
        var file = DrawFileBand(formWidth);

        var consumed = describe + file;

        ImGui.SetCursorScreenPos(new Vector2(left, origin.Y + consumed));
        DrawSendBand(formWidth, MathF.Max(Metrics.RowTall * 3f, available.Y - consumed));
    }

    /// The chrome band: the way out, what state the form is in, and the controls that undo rather than do.
    private bool DrawTopBar()
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Metrics.TouchMin;

        var leaving = DrawBackLink(drawList, min, height);

        var top = min.Y + ((height - Capsule.Height) / 2f);
        var right = min.X + width;

        if (editingId is not null)
        {
            var deleteWidth = Capsule.Width("Delete", FontAwesomeIcon.TrashAlt);
            right -= deleteWidth;

            if (Capsule.Button(
                    "##unpublish", new Vector2(right, top), "Delete", FontAwesomeIcon.TrashAlt,
                    "Takes this glamour off the board for good. Your local outfit is untouched.",
                    tone: Col.Danger,
                    enabled: !publishing))
                ImGui.OpenPopup(DeletePopupId);

            right -= Sp.Sm;
        }

        var label = editingId is null ? "Clear" : "Reset";
        var clearWidth = Capsule.Width(label, FontAwesomeIcon.Eraser);
        right -= clearWidth;

        if (Capsule.Button(
                "##clearform", new Vector2(right, top), label, FontAwesomeIcon.Eraser,
                editingId is null
                    ? "Empties the form and starts again from the look you have on."
                    : "Throws away your unsaved changes and goes back to what is published.",
                enabled: !publishing && HasContent))
            ImGui.OpenPopup(ClearPopupId);

        if (editingId is null && HasContent)
        {
            var draftWidth = Capsule.Width("Draft");
            right -= Sp.Sm + draftWidth;

            Capsule.Marker(
                drawList, new Vector2(right, top), "Draft",
                tooltip: "Kept from last time, including the look it was started from. Clear starts again.");
        }

        return leaving;
    }

    /// The way back, as a link rather than a button.
    private bool DrawBackLink(ImDrawListPtr drawList, Vector2 min, float height) =>
        BackLink.Draw(drawList, plugin.Fonts.Icon, "##submitback", "Back", min, height);


    /// The cover picture at the size it deserves, the rail that chooses it, and the three ways to add one.
    private void DrawPlate(float width, float height)
    {
        var scale = UiHelpers.Scale;
        var maximum = Math.Max(1, State.Settings.MaximumImages);
        var origin = ImGui.GetCursorScreenPos();

        var railHeight = RailHeight();
        var sources = Metrics.Row;

        var forPlate = height - railHeight - sources - (Sp.Lg * 2f);
        var plateWidth = MathF.Max(0f, MathF.Min(width, forPlate * ImageProcessor.ShotAspect));
        var plateHeight = ImageProcessor.HeightFor(plateWidth);

        var plateLeft = origin.X + ((width - plateWidth) / 2f);
        var plate = new Vector2(plateLeft, origin.Y);

        if (plateWidth > Sp.Xxl)
            DrawCover(plate, new Vector2(plateWidth, plateHeight), maximum);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + plateHeight + Sp.Lg));
        DrawRail(width, maximum);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + plateHeight + Sp.Lg + railHeight + Sp.Lg));
        DrawSources(width, maximum);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, MathF.Min(height, plateHeight + railHeight + sources + (Sp.Lg * 2f))));
    }

    /// How tall the rail under the plate is.
    private static float RailHeight() =>
        ImageProcessor.HeightFor(ThumbDesign * UiHelpers.Scale) + Sp.Sm + Stroke.Mark;

    /// The selected screenshot, large, with what you can do to it on a scrim at its foot.
    private void DrawCover(Vector2 min, Vector2 size, int maximum)
    {
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Rad.Card;
        var max = min + size;

        if (attachments.Count == 0)
        {
            DrawEmptyPlate(drawList, min, size, rounding, maximum);
            return;
        }

        shown = Math.Clamp(shown, 0, attachments.Count - 1);

        drawList.AddRectFilled(min, max, Col.U32(Col.Sunken), rounding);

        var texture = TextureFor(attachments[shown], card: false);

        if (texture != null)
        {
            drawList.AddImageRounded(
                texture.Handle, min, max, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), rounding);
        }
        else
        {
            using (Text.PushCaption())
            {
                var label = "Decoding...";

                drawList.AddText(
                    min + ((size - new Vector2(Text.Width(label), Text.CaptionSize)) / 2f),
                    Col.U32(Col.TextMuted), label);
            }
        }

        drawList.AddRect(
            min, max, Col.U32(Col.EdgeRest with { W = 0.7f }), rounding, ImDrawFlags.None, Stroke.Hair);

        DrawCoverActions(drawList, min, size, rounding);
    }

    /// What you can do to the picture you are looking at, on the picture.
    private void DrawCoverActions(ImDrawListPtr drawList, Vector2 min, Vector2 size, float rounding)
    {
        var clear = Col.U32(new Vector4(0f, 0f, 0f, 0f));
        var solid = Col.U32(new Vector4(0f, 0f, 0f, 0.78f));

        var band = Capsule.Height + (Sp.Lg * 2f);
        var foot = rounding * 3f;
        var bandTop = min.Y + size.Y - band;
        var fadeBottom = MathF.Max(bandTop, min.Y + size.Y - foot);

        drawList.AddRectFilledMultiColor(
            new Vector2(min.X, bandTop), new Vector2(min.X + size.X, fadeBottom), clear, clear, solid, solid);

        drawList.AddRectFilled(
            new Vector2(min.X, fadeBottom), min + size, solid, rounding, ImDrawFlags.RoundCornersBottom);

        var top = min.Y + size.Y - Sp.Lg - Capsule.Height;

        if (shown == 0)
        {
            Capsule.Marker(
                drawList, new Vector2(min.X + Sp.Lg, top), "Cover", FontAwesomeIcon.Star,
                "The first screenshot is the one the gallery grid shows.");
        }
        else if (Capsule.Button(
                     "##makecover", new Vector2(min.X + Sp.Lg, top), "Make Cover", FontAwesomeIcon.Star,
                     "Moves this one to the front, so it is the picture the grid shows."))
        {
            var moving = attachments[shown];
            attachments.RemoveAt(shown);
            attachments.Insert(0, moving);
            shown = 0;
        }

        var dropWidth = Capsule.Width("Remove", FontAwesomeIcon.Times);

        if (Capsule.Button(
                "##dropshot", new Vector2(min.X + size.X - Sp.Lg - dropWidth, top), "Remove",
                FontAwesomeIcon.Times, "Takes this screenshot off the submission.", tone: Col.Danger))
        {
            attachments[shown].Dispose();
            attachments.RemoveAt(shown);
            shown = Math.Max(0, shown - 1);
        }
    }

    /// The frame with nothing in it.
    private void DrawEmptyPlate(ImDrawListPtr drawList, Vector2 min, Vector2 size, float rounding, int maximum)
    {
        var max = min + size;
        var centre = min + (size / 2f);

        drawList.AddRectFilled(min, max, Col.U32(Col.Sunken), rounding);

        drawList.AddRect(
            min, max, Col.U32(Col.Caution with { W = 0.45f }), rounding, ImDrawFlags.None, Stroke.Edge);

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList, FontAwesomeIcon.Camera,
                centre - new Vector2(0f, Sp.Xxl), Col.U32(Col.Caution with { W = 0.6f }));
        }

        var heading = "A screenshot is required";
        var detail = $"Take one now, or use one you already have. Up to {maximum}.";

        var leading = ImGui.GetTextLineHeight();

        drawList.AddText(
            new Vector2(centre.X - (Text.Width(heading) / 2f), centre.Y),
            Col.U32(Col.Caution), heading);

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(centre.X - (Text.Width(detail) / 2f), centre.Y + leading + Sp.Xs),
                Col.U32(Col.TextMuted), detail);
        }
    }

    /// Every slot the board allows, filled or not.
    private void DrawRail(float width, int maximum)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var thumb = ThumbDesign * UiHelpers.Scale;
        var thumbHeight = ImageProcessor.HeightFor(thumb);
        var gap = Sp.Sm;

        var run = (thumb * maximum) + (gap * (maximum - 1));
        var x = origin.X + MathF.Max(0f, (width - run) / 2f);

        for (var i = 0; i < maximum; i++)
        {
            var min = new Vector2(x + (i * (thumb + gap)), origin.Y);
            var max = min + new Vector2(thumb, thumbHeight);
            var filled = i < attachments.Count;
            var selected = filled && i == Math.Clamp(shown, 0, Math.Max(0, attachments.Count - 1));

            var cursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(min);

            var clicked = ImGui.InvisibleButton(
                $"##railslot{i}", new Vector2(thumb, thumbHeight), ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();
            ImGui.SetCursorScreenPos(cursor);

            if (filled && hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                UiHelpers.WrappedTooltip(i == 0 ? "The cover. Click to show it above." : "Show this one above.");
            }

            if (filled && clicked)
                shown = i;

            drawList.AddRectFilled(min, max, Col.U32(Col.Sunken), Rad.Ctl);

            if (filled)
            {
                var texture = TextureFor(attachments[i], card: true);

                if (texture != null)
                {
                    drawList.AddImageRounded(
                        texture.Handle, min, max, Vector2.Zero, Vector2.One,
                        ImGui.GetColorU32(new Vector4(1f, 1f, 1f, selected || hovered ? 1f : 0.7f)), Rad.Ctl);
                }
            }
            else
            {
                using (plugin.Fonts.Icon.PushSafe())
                {
                    UiHelpers.DrawScaledIcon(
                        drawList, FontAwesomeIcon.Plus, (min + max) / 2f,
                        Col.U32(Col.TextMuted with { W = 0.4f }));
                }
            }

            drawList.AddRect(
                min, max,
                Col.U32(selected ? Theme.Accent with { W = 0.85f } : Col.EdgeRest with { W = 0.5f }),
                Rad.Ctl, ImDrawFlags.None, selected ? Stroke.Edge : Stroke.Hair);

            if (!selected)
                continue;

            drawList.AddRectFilled(
                new Vector2(min.X, max.Y + Sp.Sm),
                new Vector2(max.X, max.Y + Sp.Sm + Stroke.Mark),
                Col.U32(Theme.Accent), Rad.Chip);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, RailHeight()));
    }

    /// The three routes to a picture, none of which leaves the game.
    private void DrawSources(float width, int maximum)
    {
        var full = attachments.Count >= maximum;
        var origin = ImGui.GetCursorScreenPos();

        var take = EchoButton.ContentSize(
            capturing ? "Taking..." : "Take Screenshot", plugin.Fonts.Icon, FontAwesomeIcon.Camera).X;

        var recent = EchoButton.ContentSize("Use Recent", plugin.Fonts.Icon, FontAwesomeIcon.Images).X;
        var browse = EchoButton.ContentSize("Browse", plugin.Fonts.Icon, FontAwesomeIcon.FolderOpen).X;

        var run = take + recent + browse + (Sp.Sm * 2f);
        var x = origin.X + MathF.Max(0f, (width - run) / 2f);

        ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));

        if (EchoButton.Draw(
                "##capture", capturing ? "Taking..." : "Take Screenshot", new Vector2(take, Metrics.Row),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Camera,
                enabled: !capturing && !full,
                tooltip: full
                    ? "That's as many as one glamour can carry."
                    : "Hides EchoGlam, captures the game window, then brings you back here.",
                primary: !full && !capturing))
            Capture();

        ImGui.SameLine(0f, Sp.Sm);

        if (EchoButton.Draw(
                "##pickshot", "Use Recent", new Vector2(recent, Metrics.Row),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Images,
                enabled: !full,
                tooltip: "Anything already in your screenshots folder - a GPose shot, for instance."))
        {
            LoadRecent();
            ImGui.OpenPopup(RecentPopupId);
        }

        ImGui.SameLine(0f, Sp.Sm);

        if (EchoButton.Draw(
                "##browseshot", "Browse", new Vector2(browse, Metrics.Row),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.FolderOpen,
                enabled: !full,
                tooltip: "Pick any image on your machine."))
            Browse();

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, Metrics.Row));
    }

    /// The decoded picture for one attachment, at the size the caller is drawing it.
    private IDalamudTextureWrap? TextureFor(Attachment attachment, bool card) =>
        attachment.Existing >= 0 && editing is { } summary
            ? images.Get(GalleryClient.ImageUrl(summary, attachment.Existing, card))
            : attachment.Preview;


    /// How tall the description box gets.
    private float DescriptionHeight(float width, float pane)
    {
        var limit = Math.Max(1, State.Settings.MaximumDescription);
        var line = ImGui.GetTextLineHeight();

        var em = Text.Width("abcdefghijklmnopqrstuvwxyz") / 26f;
        var room = MathF.Max(em, width - (ImGui.GetStyle().FramePadding.X * 2f) - (18f * UiHelpers.Scale));
        var perLine = MathF.Max(1f, room / em);

        var wanted = (MathF.Ceiling(limit / perLine) + 1f) * line;

        return Math.Clamp(
            wanted + (ImGui.GetStyle().FramePadding.Y * 2f),
            line * 4f,
            MathF.Max(line * 4f, pane * 0.3f));
    }

    private float DrawDescribeBand(float width, float pane)
    {
        var start = ImGui.GetCursorScreenPos();
        var settings = State.Settings;

        ImGui.BeginGroup();

        Field.Label("Title", title.Length, settings.MaximumTitle, width);

        var titleLimit = settings.MaximumTitle;
        var descriptionLimit = settings.MaximumDescription;

        Field.PushStyle();
        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##submittitle", "Name this look...", ref title, titleLimit);
        Field.PopStyle();

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        Field.Label("Description", description.Length, settings.MaximumDescription, width);

        var wrapAt = width - (ImGui.GetStyle().FramePadding.X * 2f) - (18f * UiHelpers.Scale);

        if (descriptionUnwrapped || (!descriptionActive && MathF.Abs(wrapAt - descriptionWrap) > 0.5f))
        {
            description = UiHelpers.Rewrap(description, wrapAt);
            descriptionUnwrapped = false;
        }

        descriptionWrap = wrapAt;

        Field.PushStyle();

        UiHelpers.WrappingInputTextMultiline(
            "##submitdescription", ref description, descriptionLimit,
            new Vector2(width, DescriptionHeight(width, pane)), descriptionWrap);

        descriptionActive = ImGui.IsItemActive();
        Field.PopStyle();

        ImGui.EndGroup();
        return CloseBand(start, width);
    }


    private float DrawFileBand(float width)
    {
        var start = ImGui.GetCursorScreenPos();
        var settings = State.Settings;
        var vocabulary = settings.Tags.Length > 0 ? settings.Tags : GalleryTags.All;
        var maximum = Math.Max(1, settings.MaximumPerGlamour);

        ImGui.BeginGroup();

        Field.Label("Tags", chosenTags.Count, maximum, width);

        var tagsTop = ImGui.GetCursorScreenPos();
        tagPicker.Field(tagsTop, width, chosenTags, vocabulary, maximum, plugin.Fonts.Icon);

        ImGui.Dummy(new Vector2(width, TagPicker.FieldHeight));

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        var job = jobId == 0 ? null : JobList.Find(jobId);
        Field.Label("Built Around", width);

        if (EchoButton.Draw(
                "##submitjob", job is { } j ? j.Name : "Any Job",
                new Vector2(EchoButton.ContentSize("Any Job", plugin.Fonts.Icon, FontAwesomeIcon.Filter).X, Metrics.Row),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Filter,
                selected: jobId != 0,
                tooltip: jobId != 0
                    ? "Right-click to clear it."
                    : "Optional. It is what the job filter on the board finds."))
            ImGui.OpenPopup(JobPopupId);

        if (EchoButton.RightClicked())
            jobId = 0;

        DrawJobPopup();

        ImGui.EndGroup();
        return CloseBand(start, width);
    }


    /// What is actually going to be published, and the button that does it.
    private void DrawSendBand(float width, float height)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var settings = State.Settings;
        var line = ImGui.GetTextLineHeight();

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, origin, "WHAT GETS PUBLISHED", Col.U32(Col.TextMuted), 1.8f, width);
        }

        var y = origin.Y + (Text.CaptionSize * 1.6f) + Sp.Sm;


        if (!plugin.Items.Ready)
        {
            drawList.AddText(new Vector2(origin.X, y), Col.U32(Col.TextDim), "Reading the item list...");
            y += line;
        }
        else if (look.Count == 0)
        {
            drawList.AddText(
                new Vector2(origin.X, y), Col.U32(Col.Caution),
                "Nothing. Put a look together in the Dressing Room first.");

            y += line;
        }
        else
        {
            var pieces = GlamSlots.All.Where(look.ContainsKey).ToList();
            var count = $"{pieces.Count} {(pieces.Count == 1 ? "piece" : "pieces")}";
            var countWidth = Text.Width(count) + Sp.Lg;

            drawList.AddText(new Vector2(origin.X, y), Col.U32(Col.Text), count);

            var names = string.Join(" · ", pieces.Select(s => s.Label()));

            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(origin.X + countWidth, y + ((line - Text.CaptionSize) / 2f)),
                    Col.U32(Col.TextMuted),
                    Text.Truncate(names, MathF.Max(0f, width - countWidth)));
            }

            if (ImGui.IsMouseHoveringRect(new Vector2(origin.X, y), new Vector2(origin.X + width, y + line)))
            {
                UiHelpers.WrappedTooltip(string.Join(
                    "\n",
                    pieces.Select(slot => look[slot].ItemId == 0
                        ? $"{slot.Label()}: nothing shown"
                        : $"{slot.Label()}: {plugin.Items.Resolve(look[slot].ItemId).Name}")));
            }

            y += line + Sp.Xs;
        }

        using (Text.PushCaption())
        {
            var who = Author.Name.Length > 0
                ? $"As {Author.Name}{(Author.World.Length > 0 ? " · " + Author.World : string.Empty)}"
                : "Waiting for your character";

            var asRace = RaceNames.Describe(author.Race, author.Tribe, author.Sex);

            drawList.AddText(
                new Vector2(origin.X, y),
                Col.U32(Author.Name.Length > 0 ? Col.TextDim : Col.Caution),
                Text.Truncate($"{who}  ·  recorded as {asRace}", width));

            y += Text.CaptionSize * 1.4f;
        }

        if (appearance is not null)
        {
            y += Sp.Sm;
            ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

            EchoToggle.Draw(
                "##includeappearance", "Include my appearance", ref includeAppearance,
                "Your race, face, hair and colours travel with the outfit. Off by default - "
                + "someone wearing your glamour does not usually mean wearing your face.");

            y = ImGui.GetItemRectMax().Y;
        }


        var missingImage = attachments.Count == 0;
        var missingTitle = string.IsNullOrWhiteSpace(title);
        var missingLook = look.Count == 0;

        var missingAuthor = Author.Name.Length == 0;

        var blocked = missingImage || missingTitle || missingLook || missingAuthor
            || publishing || !State.CanPublish;

        var message = problem ?? done;
        var caption = Text.CaptionSize * 1.4f;

        var shut = State.CanPublish ? null : "The gallery isn't accepting submissions at the moment.";

        var sendHeight = Metrics.RowTall
            + (shut is null ? 0f : caption + Sp.Sm)
            + (message is null ? 0f : Sp.Md + caption)
            + Sp.Sm + caption;

        var sendTop = MathF.Max(y + (Sp.Xxl * 2f), origin.Y + height - sendHeight);

        var ruleY = sendTop - Sp.Xl;

        if (ruleY > y + Sp.Sm)
        {
            drawList.AddRectFilled(
                new Vector2(origin.X, ruleY),
                new Vector2(origin.X + width, ruleY + Stroke.Hair),
                Col.U32(Col.EdgeRest with { W = 0.55f }));
        }

        var sendY = sendTop;

        if (shut is { } closed)
        {
            using (Text.PushCaption())
                drawList.AddText(new Vector2(origin.X, sendY), Col.U32(Col.Caution), Text.Truncate(closed, width));

            sendY += caption + Sp.Sm;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, sendY));

        if (EchoButton.Draw(
                "##publish",
                publishing ? "Publishing..." : editingId is null ? "Publish To Gallery" : "Save Changes",
                new Vector2(width, Metrics.RowTall),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.CloudUploadAlt,
                enabled: !blocked,
                tooltip: missingTitle ? "Give it a title first."
                    : missingImage ? "Attach at least one screenshot."
                    : missingLook ? "There is no look to publish."
                    : missingAuthor ? "Waiting for your character. Try again in a moment."
                    : "Everyone browsing the gallery will see this.",
                primary: !blocked))
            ImGui.OpenPopup(RulesPopupId);

        var footY = ImGui.GetItemRectMax().Y;

        if (message is not null)
        {
            footY += Sp.Md;

            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(origin.X, footY), Col.U32(problem is null ? Col.Success : Col.Danger),
                    Text.Truncate(message, width));
            }

            footY += Text.CaptionSize * 1.4f;
        }

        footY += Sp.Sm;

        using (Text.PushCaption())
        {
            var small = settings.MaximumPerOwner > 0
                ? $"Public once published, and reportable by anyone who sees it. {settings.MaximumPerOwner} at a time."
                : "Public once published, and reportable by anyone who sees it.";

            drawList.AddText(
                new Vector2(origin.X, footY), Col.U32(Col.TextMuted), Text.Truncate(small, width));
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }


    /// Closes a band: the rule under it, and the height it took.
    private static float CloseBand(Vector2 start, float width)
    {
        var ruleY = ImGui.GetItemRectMax().Y + Sp.Lg;

        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(start.X, ruleY),
            new Vector2(start.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        return ruleY + Stroke.Hair + Sp.Lg - start.Y;
    }



    private const string RecentPopupId = "##echoglamrecentshots";

    /// The screenshots folder, in a dialog.
    private void DrawRecentPopup()
    {
        var scale = UiHelpers.Scale;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18f, 16f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rad.Panel);

        var open = ImGui.BeginPopup(RecentPopupId);
        ImGui.PopStyleVar(2);

        if (!open)
            return;

        var width = Modal.Width(ModalSize.Large);
        ImGui.Dummy(new Vector2(width, 0f));

        using (Text.PushCaption())
        {
            Text.Tracked(
                ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), "YOUR SCREENSHOTS",
                Col.U32(Col.TextMuted), 1.8f);
        }

        ImGui.Dummy(new Vector2(width, Text.CaptionSize * 1.6f));

        if (!Screenshots.DirectoryExists)
        {
            ImGui.TextColored(Col.TextDim, "No screenshots folder found. Take one from the form instead.");
            ImGui.EndPopup();
            return;
        }

        if (recentFiles.Count == 0)
        {
            ImGui.TextColored(
                Col.TextDim, recentLoaded ? "No screenshots yet." : "Reading your screenshots...");

            ImGui.EndPopup();
            return;
        }

        var gap = Sp.Sm;
        var thumbWidth = (width - (gap * 3f)) / 4f;
        var thumbHeight = thumbWidth * 9f / 16f;
        var drawList = ImGui.GetWindowDrawList();

        for (var i = 0; i < recentFiles.Count; i++)
        {
            if (i % 4 != 0)
                ImGui.SameLine(0f, gap);

            var path = recentFiles[i];
            var pos = ImGui.GetCursorScreenPos();
            var size = new Vector2(thumbWidth, thumbHeight);

            if (ImGui.InvisibleButton($"##recent{i}", size, ImGuiButtonFlags.MouseButtonLeft))
            {
                ImGui.CloseCurrentPopup();
                BeginCrop(path);
            }

            var hovered = ImGui.IsItemHovered();

            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                UiHelpers.WrappedTooltip(System.IO.Path.GetFileName(path));
            }

            drawList.AddRectFilled(pos, pos + size, Col.U32(Col.Sunken), Rad.Ctl);

            if (recentThumbnails.TryGetValue(path, out var texture) && texture != null)
            {
                drawList.AddImageRounded(
                    texture.Handle, pos, pos + size, Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 1f : 0.8f)), Rad.Ctl);
            }

            drawList.AddRect(
                pos, pos + size, Col.U32(hovered ? Col.EdgeHover : Col.EdgeRest with { W = 0.5f }),
                Rad.Ctl, ImDrawFlags.None, Stroke.Hair);
        }

        ImGui.EndPopup();
    }

    private void Capture()
    {
        capturing = true;
        problem = null;

        _ = Task.Run(async () =>
        {
            try
            {
                var (path, failure) = await screenshots.CaptureAsync().ConfigureAwait(false);

                if (path is null)
                {
                    problem = $"{failure} You can still use Browse or Use Recent.";
                    return;
                }

                await Plugin.Framework.RunOnTick(() => BeginCrop(path)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                problem = $"Couldn't take that screenshot: {ex.Message}";
            }
            finally
            {
                capturing = false;
            }
        });
    }

    private void Browse()
    {
        var fallback = Screenshots.DirectoryExists
            ? Screenshots.Directory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        plugin.FileDialogs.OpenFileDialog(
            "Pick a screenshot",
            "Images{.png,.jpg,.jpeg,.bmp,.webp}",
            (chosen, paths) =>
            {
                if (!chosen || paths.Count == 0)
                    return;

                plugin.Configuration.RememberBrowseDirectory(paths[0]);

                BeginCrop(paths[0]);
            },
            1,
            plugin.Configuration.BrowseStartDirectory(fallback));
    }

    private void BeginCrop(string path)
    {
        _ = cropper.OpenAsync(
            path, ImageProcessor.ShotWidth, ImageProcessor.ShotHeight,
            "Frame your glamour",
            jpeg => _ = Plugin.Framework.RunOnTick(() => Attach(jpeg)));
    }

    private void Attach(byte[] jpeg)
    {
        var attachment = new Attachment { Jpeg = jpeg };
        attachments.Add(attachment);

        shown = attachments.Count - 1;

        _ = Task.Run(async () =>
        {
            try
            {
                attachment.Preview = await Plugin.TextureProvider
                    .CreateFromImageAsync(jpeg, "EchoGlam submission")
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[EchoGlam] Couldn't preview an attachment: {ex.Message}");
            }
        });
    }

    private void LoadRecent()
    {
        recentLoaded = false;

        _ = Task.Run(async () =>
        {
            var files = Screenshots.Recent(12);
            recentFiles = files;
            recentLoaded = true;

            foreach (var path in files)
            {
                if (recentThumbnails.ContainsKey(path))
                    continue;

                try
                {
                    var bytes = await System.IO.File.ReadAllBytesAsync(path).ConfigureAwait(false);
                    recentThumbnails[path] = await Plugin.TextureProvider
                        .CreateFromImageAsync(bytes, "EchoGlam recent")
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    recentThumbnails[path] = null;
                }
            }
        });
    }


    private const string JobPopupId = "##echoglamsubmitjobs";

    private void DrawJobPopup()
    {
        if (!ImGui.BeginPopup(JobPopupId))
            return;

        var size = 34f * UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();

        if (EchoButton.Draw("##nojob", "Any Job", new Vector2(0f, Metrics.Row), selected: jobId == 0))
        {
            jobId = 0;
            ImGui.CloseCurrentPopup();
        }

        ImGui.Dummy(new Vector2(0f, Sp.Sm));

        var index = 0;

        foreach (var job in JobList.All)
        {
            if (index > 0 && index % 9 != 0)
                ImGui.SameLine(0f, Sp.Xs);

            var pos = ImGui.GetCursorScreenPos();
            var selected = jobId == job.Id;

            if (ImGui.InvisibleButton($"##submitjob{job.Id}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft))
            {
                jobId = selected ? 0 : job.Id;
                ImGui.CloseCurrentPopup();
            }

            var hovered = ImGui.IsItemHovered();
            if (hovered)
                ImGui.SetTooltip(job.Name);

            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrDefault();
            if (texture != null)
            {
                drawList.AddImage(
                    texture.Handle, pos, pos + new Vector2(size, size), Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, selected || hovered ? 1f : 0.72f)));
            }

            if (selected)
            {
                drawList.AddRect(
                    pos, pos + new Vector2(size, size), Col.U32(Theme.Accent),
                    Rad.Chip, ImDrawFlags.None, Stroke.Mark);
            }

            index++;
        }

        ImGui.EndPopup();
    }


    private const string DeletePopupId = "##echoglamdelete";

    /// The confirmation before a glamour leaves the board.
    private void DrawDeletePopup()
    {
        var scale = UiHelpers.Scale;

        var centre = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(centre, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24f, 21f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rad.Panel);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, Stroke.Focus);
        ImGui.PushStyleColor(ImGuiCol.Border, Col.Danger with { W = 0.7f });

        var open = ImGui.BeginPopupModal(
            DeletePopupId,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove);

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);

        if (!open)
            return;

        var content = Modal.Width(ModalSize.Small);
        ImGui.Dummy(new Vector2(content, 0f));

        using (Text.PushTitle())
            ImGui.TextUnformatted("Delete this glamour?");

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content);
        ImGui.TextColored(
            Col.TextDim,
            "It comes off the board for good, with every vote and save it has collected. "
            + "The outfit in your Dressing Room is untouched.");

        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Sp.Xl));

        var deleteWidth = EchoButton.ContentSize("Delete", plugin.Fonts.Icon, FontAwesomeIcon.TrashAlt).X;
        var keepWidth = EchoButton.ContentSize("Keep It").X;
        var left = ImGui.GetCursorPosX();

        ImGui.SetCursorPosX(left + MathF.Max(0f, content - deleteWidth - keepWidth - Sp.Md));

        if (EchoButton.Draw("##keepit", "Keep It", new Vector2(keepWidth, Metrics.Field)))
            ImGui.CloseCurrentPopup();

        ImGui.SameLine(0f, Sp.Md);

        if (EchoButton.Draw(
                "##confirmdelete", "Delete", new Vector2(deleteWidth, Metrics.Field),
                accentOverride: Col.Danger,
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.TrashAlt,
                enabled: !publishing))
        {
            ImGui.CloseCurrentPopup();
            Unpublish();
        }

        ImGui.EndPopup();
    }

    private const string ClearPopupId = "##echoglamclearform";

    /// The confirmation before a draft is thrown away.
    private void DrawClearPopup()
    {
        var scale = UiHelpers.Scale;
        var editingNow = editingId is not null;

        var centre = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(centre, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24f, 21f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rad.Panel);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, Stroke.Focus);
        ImGui.PushStyleColor(ImGuiCol.Border, Col.Caution with { W = 0.7f });

        var open = ImGui.BeginPopupModal(
            ClearPopupId,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove);

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);

        if (!open)
            return;

        var content = Modal.Width(ModalSize.Small);
        ImGui.Dummy(new Vector2(content, 0f));

        using (Text.PushTitle())
            ImGui.TextUnformatted(editingNow ? "Throw away your changes?" : "Clear this draft?");

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content);
        ImGui.TextColored(
            Col.TextDim,
            editingNow
                ? "The form goes back to what is published. Nothing on the board changes."
                : "The title, description, tags and screenshots go, and the form starts again from "
                  + "the look you have on now.");

        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Sp.Xl));

        var clearWidth = EchoButton.ContentSize(editingNow ? "Reset" : "Clear", plugin.Fonts.Icon, FontAwesomeIcon.Eraser).X;
        var keepWidth = EchoButton.ContentSize("Keep It").X;
        var left = ImGui.GetCursorPosX();

        ImGui.SetCursorPosX(left + MathF.Max(0f, content - clearWidth - keepWidth - Sp.Md));

        if (EchoButton.Draw("##keepdraft", "Keep It", new Vector2(keepWidth, Metrics.Field)))
            ImGui.CloseCurrentPopup();

        ImGui.SameLine(0f, Sp.Md);

        if (EchoButton.Draw(
                "##confirmclear", editingNow ? "Reset" : "Clear", new Vector2(clearWidth, Metrics.Field),
                accentOverride: Col.Caution,
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Eraser))
        {
            ImGui.CloseCurrentPopup();
            Clear();
        }

        ImGui.EndPopup();
    }

    private const string RulesPopupId = "##echoglamrules";

    /// The rules, and the last chance to not do it.
    private void DrawRulesPopup()
    {
        var scale = UiHelpers.Scale;

        var content = Modal.Width(ModalSize.Medium);

        var centre = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(centre, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24f, 21f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rad.Panel);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, Stroke.Focus);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Accent with { W = 0.7f });

        var open = ImGui.BeginPopupModal(
            RulesPopupId, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove);

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);

        if (!open)
            return;

        ImGui.Dummy(new Vector2(content, 0f));

        using (Text.PushTitle())
            ImGui.TextUnformatted("Before You Publish");

        ImGui.Dummy(new Vector2(0f, Sp.Lg));
        Divider(content);
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        Rule(content, Theme.Accent, "No modded clothing or bodies.", "Modded hair and eyes are fine.");
        Rule(content, Theme.Accent, "Nothing NSFW.", "Reported means removed.");
        Rule(
            content, Col.Caution,
            $"{State.Settings.BanThreshold} takedowns and you are done.",
            "Your published glamours stay. You cannot add more.");

        ImGui.Dummy(new Vector2(0f, Sp.Xs));
        Divider(content);
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        DrawRuleButtons(content);
        ImGui.EndPopup();
    }

    /// The two answers, sitting at the bottom right the way a dialog's do.
    private void DrawRuleButtons(float content)
    {
        var publishWidth = EchoButton.ContentSize("Publish It", plugin.Fonts.Icon, FontAwesomeIcon.Check).X;
        var cancelWidth = EchoButton.ContentSize("Not Yet").X;

        var left = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(left + MathF.Max(0f, content - publishWidth - cancelWidth - Sp.Md));

        if (EchoButton.Draw(
                "##rulesno", "Not Yet", new Vector2(cancelWidth, Metrics.Field),
                tooltip: "Back to the form. Nothing is sent."))
            ImGui.CloseCurrentPopup();

        ImGui.SameLine(0f, Sp.Md);

        if (EchoButton.Draw(
                "##rulesyes", "Publish It", new Vector2(publishWidth, Metrics.Field),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Check,
                primary: true))
        {
            ImGui.CloseCurrentPopup();
            Publish();
        }
    }

    /// A hairline that fades out at both ends, matching the rule under a section header.
    private static void Divider(float width)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var edge = Col.U32(Col.EdgeRest with { W = 0f });
        var middle = Col.U32(Col.EdgeRest with { W = 0.9f });

        drawList.AddRectFilledMultiColor(
            origin, new Vector2(origin.X + (width / 2f), origin.Y + Stroke.Hair), edge, middle, middle, edge);

        drawList.AddRectFilledMultiColor(
            new Vector2(origin.X + (width / 2f), origin.Y), new Vector2(origin.X + width, origin.Y + Stroke.Hair),
            middle, edge, edge, middle);

        ImGui.Dummy(new Vector2(width, Stroke.Hair));
    }

    /// One rule: a mark, the line that matters, and the qualification under it.
    private static void Rule(float content, Vector4 colour, string rule, string detail)
    {
        var indent = Sp.Xl + Sp.Xs;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var left = ImGui.GetCursorPosX();

        drawList.AddCircleFilled(
            new Vector2(origin.X + Sp.Xs, origin.Y + (ImGui.GetTextLineHeight() / 2f)),
            3.5f * UiHelpers.Scale, Col.U32(colour));

        ImGui.SetCursorPosX(left + indent);
        ImGui.PushTextWrapPos(left + content);
        ImGui.TextColored(Col.Text, rule);

        ImGui.SetCursorPosX(left + indent);
        ImGui.TextColored(Col.TextDim, detail);
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, Sp.Lg));
    }


    /// What is sent.
    private GlamourSubmission Compose()
    {
        return new GlamourSubmission
        {
            Id = editingId ?? string.Empty,
            OwnerKey = State.OwnerKey,
            Title = title.Trim(),

            Description = UiHelpers.Unwrap(description, descriptionWrap).Trim(),
            AuthorName = Author.Name,
            AuthorWorld = Author.World,
            JobId = jobId,
            Race = author.Race,
            Tribe = author.Tribe,
            Sex = author.Sex,
            Tags = [.. chosenTags],
            Slots =
            [
                .. look.Select(kv => new GlamourSlotDto
                {
                    Slot = (int)kv.Key,
                    ItemId = kv.Value.ItemId,
                    Stain0 = kv.Value.Stain0,
                    Stain1 = kv.Value.Stain1,
                }),
            ],
            Appearance = includeAppearance ? appearance?.ToBase64() : null,

            Images = editingId is null ? null : [.. attachments.Select(a => a.Existing)],
        };
    }

    private void Publish()
    {
        publishing = true;
        problem = null;
        done = null;

        var submission = Compose();

        var uploads = attachments.Where(a => a.Existing < 0).Select(a => a.Jpeg).ToList();

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await GalleryClient
                    .SubmitAsync(submission, uploads, CancellationToken.None)
                    .ConfigureAwait(false);

                if (!result.Accepted)
                {
                    problem = result.Reason;
                    return;
                }

                done = editingId is null ? "Published. It's on the board now." : "Saved.";

                finished = result.Id;

                State.RefreshProfile(force: true);
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }
            finally
            {
                publishing = false;
            }
        });
    }

    private void Unpublish()
    {
        if (editingId is not { } id)
            return;

        publishing = true;
        problem = null;

        _ = Task.Run(async () =>
        {
            try
            {
                if (await GalleryClient.DeleteAsync(id, State.OwnerKey, CancellationToken.None).ConfigureAwait(false))
                {
                    done = "Removed from the gallery.";

                    removed = true;
                    finished = id;

                    State.RefreshProfile(force: true);
                }
                else
                {
                    problem = "That couldn't be removed.";
                }
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }
            finally
            {
                publishing = false;
            }
        });
    }

    /// Raised when something is published or saved, so the grid behind can refresh.
    public event Action<string>? Published;

    /// Raised when an entry is taken off the board.
    public event Action<string>? Removed;
}
