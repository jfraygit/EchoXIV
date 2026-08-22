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

namespace EchoGlam.UI;

/// Publishing a look, and editing one already published.
public sealed class SubmitPane : IDisposable
{
    private readonly Plugin plugin;
    private readonly ImageCropDialog cropper = new();
    private readonly Screenshots screenshots;

    /// The gallery's own image cache, shared rather than a second one.
    private readonly GalleryImages images;

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
    private bool pickingFile;
    private string? problem;
    private string? done;

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

    private GalleryState State => plugin.Gallery;

    public void Dispose()
    {
        foreach (var attachment in attachments)
            attachment.Dispose();

        foreach (var thumbnail in recentThumbnails.Values)
            thumbnail?.Dispose();

        attachments.Clear();
        recentThumbnails.Clear();
    }

    /// Opens the form for the look currently on the character.
    public void OpenForCurrentLook()
    {
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

    public void Close()
    {
        IsOpen = false;
        Reset();
    }

    private void Reset()
    {
        foreach (var attachment in attachments)
            attachment.Dispose();

        attachments.Clear();
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

    public void Draw()
    {
        var scale = UiHelpers.Scale;
        State.EnsureSettings();
        RefreshAuthor();

        cropper.Draw();

        DrawHeader();
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (!ImGui.BeginChild("##submitscroll", ImGui.GetContentRegionAvail(), false))
        {
            ImGui.EndChild();
            return;
        }

        var available = ImGui.GetContentRegionAvail().X;
        var gap = 12f * scale;

        var columns = available >= (StackBelow * scale);
        var pictureWidth = columns ? MathF.Min(available * 0.42f, 460f * scale) : available;
        var formWidth = columns ? available - pictureWidth - gap : available;

        ImGui.BeginGroup();
        DrawScreenshots(pictureWidth);
        ImGui.EndGroup();

        if (columns)
            ImGui.SameLine(0f, gap);
        else
            ImGui.Dummy(new Vector2(0f, gap));

        ImGui.BeginGroup();

        DrawDetails(formWidth);
        ImGui.Dummy(new Vector2(0f, gap));

        DrawLookSummary(formWidth);
        ImGui.Dummy(new Vector2(0f, gap));

        DrawPublish(formWidth);

        ImGui.EndGroup();

        ImGui.EndChild();
    }

    /// Below this the two columns stack.
    private const float StackBelow = 760f;

    private void DrawHeader()
    {
        var scale = UiHelpers.Scale;

        if (EchoButton.Draw(
                "##submitback", "Back", new Vector2(0f, 26f * scale),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.ChevronLeft))
            Close();

        ImGui.SameLine(0f, 10f * scale);
        ImGui.AlignTextToFramePadding();

        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextUnformatted(editingId is null ? "Publish Outfit" : "Edit Outfit");
    }


    private void DrawScreenshots(float outerWidth)
    {
        var scale = UiHelpers.Scale;
        var maximum = Math.Max(1, State.Settings.MaximumImages);

        Theme.BeginPanel(outerWidth);

        var width = outerWidth - (28f * scale);

        Theme.SectionHeader("Screenshots", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(
            Theme.TextDim,
            $"At least one, up to {maximum}. The first is the one the grid shows.");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var height = 26f * scale;
        var full = attachments.Count >= maximum;

        if (EchoButton.Draw(
                "##capture", capturing ? "Taking..." : "Take Screenshot", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Camera,
                enabled: !capturing && !full,
                tooltip: full
                    ? "That's as many as one glamour can carry."
                    : "Hides EchoGlam, captures the game window, then brings you back here."))
            Capture();

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##pickshot", pickingFile ? "Hide Recent" : "Use Recent", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Images,
                selected: pickingFile,
                enabled: !full,
                tooltip: "Anything already in your screenshots folder - a GPose shot, for instance."))
        {
            pickingFile = !pickingFile;

            if (pickingFile)
                LoadRecent();
        }

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##browseshot", "Browse", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.FolderOpen,
                enabled: !full,
                tooltip: "Pick any image on your machine."))
            Browse();

        if (capturing)
        {
            ImGui.SameLine(0f, 10f * scale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.TextDim, "Say cheese.");
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (pickingFile)
            DrawRecentStrip(width);

        DrawAttachments(width, maximum);

        Theme.EndPanel();
    }

    private void DrawRecentStrip(float width)
    {
        var scale = UiHelpers.Scale;

        if (!Screenshots.DirectoryExists)
        {
            ImGui.TextColored(Theme.TextDim, "No screenshots folder found. Take one with the button above.");
            return;
        }

        if (recentFiles.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, recentLoaded ? "No screenshots yet." : "Reading your screenshots...");
            return;
        }

        var thumbWidth = 120f * scale;
        var thumbHeight = thumbWidth * 9f / 16f;
        var gap = 6f * scale;
        var perRow = Math.Max(1, (int)((width + gap) / (thumbWidth + gap)));
        var drawList = ImGui.GetWindowDrawList();

        for (var i = 0; i < recentFiles.Count; i++)
        {
            if (i % perRow != 0)
                ImGui.SameLine(0f, gap);

            var path = recentFiles[i];
            var pos = ImGui.GetCursorScreenPos();

            if (ImGui.InvisibleButton($"##recent{i}", new Vector2(thumbWidth, thumbHeight), ImGuiButtonFlags.MouseButtonLeft))
                BeginCrop(path);

            var hovered = ImGui.IsItemHovered();
            if (hovered)
                UiHelpers.WrappedTooltip(System.IO.Path.GetFileName(path));

            drawList.AddRectFilled(
                pos, pos + new Vector2(thumbWidth, thumbHeight),
                ImGui.GetColorU32(Theme.Tinted(0.05f)), 4f * scale);

            if (recentThumbnails.TryGetValue(path, out var texture) && texture != null)
            {
                drawList.AddImageRounded(
                    texture.Handle, pos, pos + new Vector2(thumbWidth, thumbHeight),
                    Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 1f : 0.85f)),
                    4f * scale);
            }

            drawList.AddRect(
                pos, pos + new Vector2(thumbWidth, thumbHeight),
                ImGui.GetColorU32(hovered ? Theme.Accent : Theme.Border),
                4f * scale, ImDrawFlags.None, 1.2f * scale);
        }

        ImGui.Dummy(new Vector2(0f, 10f * scale));
    }

    /// How wide one screenshot thumbnail is, and how many of them fit on a row.
    private static (float Width, int PerRow) ThumbLayout(float width, int maximum, float gap)
    {
        var scale = UiHelpers.Scale;
        var perRow = Math.Max(1, maximum);
        var thumb = (width - (gap * (perRow - 1))) / perRow;

        while (perRow > 1 && thumb < 96f * scale)
        {
            perRow--;
            thumb = (width - (gap * (perRow - 1))) / perRow;
        }

        return (MathF.Max(MathF.Min(thumb, 150f * scale), 1f), perRow);
    }

    private void DrawAttachments(float width, int maximum)
    {
        var scale = UiHelpers.Scale;
        var gap = 8f * scale;
        var (thumbWidth, perRow) = ThumbLayout(width, maximum, gap);
        var thumbHeight = ImageProcessor.HeightFor(thumbWidth);

        if (attachments.Count == 0)
        {
            var slotWidth = thumbWidth;
            var slotHeight = thumbHeight;

            var placeholderList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(width, slotHeight));

            var min = pos;
            var max = pos + new Vector2(slotWidth, slotHeight);

            placeholderList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Tinted(0.05f)), 8f * scale);
            placeholderList.AddRect(
                min, max, ImGui.GetColorU32(Theme.Warning with { W = 0.5f }),
                8f * scale, ImDrawFlags.None, 1.4f * scale);

            using (plugin.Fonts.Icon.PushSafe())
            {
                UiHelpers.DrawScaledIcon(
                    placeholderList, FontAwesomeIcon.Camera,
                    new Vector2(min.X + (slotWidth / 2f), min.Y + (slotHeight / 2f) - (10f * scale)),
                    ImGui.GetColorU32(Theme.Warning with { W = 0.7f }));
            }

            var label = "No screenshot yet";
            var measured = ImGui.CalcTextSize(label);

            placeholderList.AddText(
                new Vector2(
                    min.X + ((slotWidth - measured.X) / 2f),
                    min.Y + (slotHeight / 2f) + (12f * scale)),
                ImGui.GetColorU32(Theme.Warning),
                label);

            return;
        }

        var drawList = ImGui.GetWindowDrawList();

        for (var i = 0; i < attachments.Count; i++)
        {
            if (i % perRow != 0)
                ImGui.SameLine(0f, gap);

            var pos = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(thumbWidth, thumbHeight));

            drawList.AddRectFilled(
                pos, pos + new Vector2(thumbWidth, thumbHeight),
                ImGui.GetColorU32(Theme.Tinted(0.05f)), 4f * scale);

            var slot = attachments[i].Existing;
            var texture = slot >= 0 && editing is { } summary
                ? images.Get(GalleryClient.ImageUrl(summary, slot, card: true))
                : attachments[i].Preview;

            if (texture != null)
            {
                drawList.AddImageRounded(
                    texture.Handle, pos, pos + new Vector2(thumbWidth, thumbHeight),
                    Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), 4f * scale);
            }

            drawList.AddRect(
                pos, pos + new Vector2(thumbWidth, thumbHeight), ImGui.GetColorU32(Theme.Border),
                4f * scale, ImDrawFlags.None, 1.2f * scale);

            if (i == 0)
            {
                drawList.AddText(
                    pos + new Vector2(6f * scale, 5f * scale),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.85f)), "Cover");
            }

            var removeSize = 20f * scale;
            var cursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(pos.X + thumbWidth - removeSize - (4f * scale), pos.Y + (4f * scale)));

            if (EchoButton.BareIcon(
                    $"##dropshot{i}", plugin.Fonts.Icon, FontAwesomeIcon.Times, removeSize,
                    "Remove this screenshot.", colourOverride: Theme.Bad))
            {
                attachments[i].Dispose();
                attachments.RemoveAt(i);
                ImGui.SetCursorScreenPos(cursor);
                return;
            }

            ImGui.SetCursorScreenPos(cursor);
        }
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
        var start = Screenshots.DirectoryExists
            ? Screenshots.Directory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        plugin.FileDialogs.OpenFileDialog(
            "Pick a screenshot",
            "Images{.png,.jpg,.jpeg,.bmp,.webp}",
            (chosen, paths) =>
            {
                if (chosen && paths.Count > 0)
                    BeginCrop(paths[0]);
            },
            1,
            start);
    }

    private void BeginCrop(string path)
    {
        pickingFile = false;

        _ = cropper.OpenAsync(
            path, ImageProcessor.ShotWidth, ImageProcessor.ShotHeight,
            "Frame your glamour",
            jpeg => _ = Plugin.Framework.RunOnTick(() => Attach(jpeg)));
    }

    private void Attach(byte[] jpeg)
    {
        var attachment = new Attachment { Jpeg = jpeg };
        attachments.Add(attachment);

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


    private void DrawDetails(float outerWidth)
    {
        var scale = UiHelpers.Scale;
        var settings = State.Settings;

        Theme.BeginPanel(outerWidth);

        var width = outerWidth - (28f * scale);

        Theme.SectionHeader("About it", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        var titleLimit = settings.MaximumTitle;
        var descriptionLimit = settings.MaximumDescription;

        FieldLabel("Title", title.Length, settings.MaximumTitle, width);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f * scale, 7f * scale));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Tinted(0.04f));

        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##submittitle", "Name this look...", ref title, titleLimit);

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        FieldLabel("Description", description.Length, settings.MaximumDescription, width);

        var wrapAt = width - (ImGui.GetStyle().FramePadding.X * 2f) - (18f * scale);

        if (descriptionUnwrapped || (!descriptionActive && MathF.Abs(wrapAt - descriptionWrap) > 0.5f))
        {
            description = UiHelpers.Rewrap(description, wrapAt);
            descriptionUnwrapped = false;
        }

        descriptionWrap = wrapAt;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f * scale);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Tinted(0.04f));

        UiHelpers.WrappingInputTextMultiline(
            "##submitdescription", ref description, descriptionLimit,
            new Vector2(width, 78f * scale), descriptionWrap);

        descriptionActive = ImGui.IsItemActive();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        FieldLabel("Tags", chosenTags.Count, settings.MaximumPerGlamour, width);
        ImGui.Dummy(new Vector2(0f, 2f * scale));

        var x = 0f;
        var height = 22f * scale;

        foreach (var tag in settings.Tags)
        {
            var tagWidth = EchoButton.ContentSize(tag).X;

            if (x > 0f && x + tagWidth <= width)
                ImGui.SameLine(0f, 5f * scale);
            else if (x > 0f)
                x = 0f;

            var chosen = chosenTags.Contains(tag);

            if (EchoButton.Draw(
                    $"##submittag{tag}", tag, new Vector2(0f, height),
                    accentOverride: chosen ? null : Theme.TextDisabled,
                    selected: chosen,
                    enabled: chosen || chosenTags.Count < settings.MaximumPerGlamour))
            {
                if (!chosenTags.Remove(tag))
                    chosenTags.Add(tag);
            }

            x += tagWidth + (5f * scale);
        }

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        var job = jobId == 0 ? null : JobList.Find(jobId);

        ImGui.TextColored(Theme.TextDim, "Built around");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        if (EchoButton.Draw(
                "##submitjob", job is { } j ? j.Name : "All Jobs", new Vector2(0f, 24f * scale),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Filter,
                selected: jobId != 0,
                tooltip: jobId != 0 ? "Right-click to clear it." : "Optional. It is what the job filter finds."))
            ImGui.OpenPopup(JobPopupId);

        if (EchoButton.RightClicked())
            jobId = 0;

        DrawJobPopup();

        Theme.EndPanel();
    }

    /// A field's name with its budget at the right end of the same line.
    private static void FieldLabel(string label, int used, int limit, float width)
    {
        var count = $"{used}/{limit}";
        var origin = ImGui.GetCursorScreenPos();

        ImGui.TextColored(Theme.TextDim, label);

        var colour = used >= limit ? Theme.Bad : used > limit * 0.85f ? Theme.Warning : Theme.TextDisabled;

        ImGui.GetWindowDrawList().AddText(
            new Vector2(origin.X + width - ImGui.CalcTextSize(count).X, origin.Y),
            ImGui.GetColorU32(colour), count);
    }

    private const string JobPopupId = "##echoglamsubmitjobs";

    private void DrawJobPopup()
    {
        if (!ImGui.BeginPopup(JobPopupId))
            return;

        var scale = UiHelpers.Scale;
        var size = 34f * scale;
        var drawList = ImGui.GetWindowDrawList();

        if (EchoButton.Draw("##nojob", "All Jobs", new Vector2(0f, 24f * scale), selected: jobId == 0))
        {
            jobId = 0;
            ImGui.CloseCurrentPopup();
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var index = 0;

        foreach (var job in JobList.All)
        {
            if (index > 0 && index % 9 != 0)
                ImGui.SameLine(0f, 4f * scale);

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
                    pos, pos + new Vector2(size, size), ImGui.GetColorU32(Theme.Accent),
                    4f * scale, ImDrawFlags.None, 2f * scale);
            }

            index++;
        }

        ImGui.EndPopup();
    }

    /// What is actually going to be published, listed out.
    private void DrawLookSummary(float outerWidth)
    {
        var scale = UiHelpers.Scale;

        Theme.BeginPanel(outerWidth);

        var width = outerWidth - (28f * scale);

        Theme.SectionHeader("What gets published", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        if (!plugin.Items.Ready)
        {
            ImGui.TextColored(Theme.TextDim, "Reading the item list...");
            Theme.EndPanel();
            return;
        }

        if (look.Count == 0)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextColored(Theme.Warning, "Nothing. Put a look together in the Dressing Room first.");
            ImGui.PopTextWrapPos();
            Theme.EndPanel();
            return;
        }

        var names = GlamSlots.All
            .Where(look.ContainsKey)
            .Select(slot => look[slot].ItemId == 0
                ? $"{slot.Label()}: nothing shown"
                : $"{slot.Label()}: {plugin.Items.Resolve(look[slot].ItemId).Name}")
            .ToList();

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(Theme.TextDim, string.Join("   ·   ", names));
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(
            Theme.TextDim,
            $"Published as {(Author.Name.Length > 0 ? Author.Name : "you")}, "
            + $"recorded as {RaceNames.Describe(author.Race, author.Tribe, author.Sex)} - a glamour reads "
            + "differently on different races, so the board lets people filter by it.");
        ImGui.PopTextWrapPos();

        if (appearance is not null)
        {
            ImGui.Dummy(new Vector2(0f, 8f * scale));

            EchoToggle.Draw(
                "##includeappearance", "Include my appearance", ref includeAppearance,
                "Your race, face, hair and colours travel with the outfit. Off by default - "
                + "someone wearing your glamour does not usually mean wearing your face.");
        }

        Theme.EndPanel();
    }

    private void DrawPublish(float outerWidth)
    {
        var scale = UiHelpers.Scale;
        var settings = State.Settings;

        Theme.BeginPanel(outerWidth, Theme.Accent);

        var width = outerWidth - (28f * scale);

        var missingImage = attachments.Count == 0;
        var missingTitle = string.IsNullOrWhiteSpace(title);
        var missingLook = look.Count == 0;

        var missingAuthor = Author.Name.Length == 0;

        var blocked = missingImage || missingTitle || missingLook || missingAuthor
            || publishing || !State.CanPublish;

        if (!State.CanPublish)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextColored(Theme.Warning, "The gallery isn't accepting submissions yet.");
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, 6f * scale));
        }

        var deleting = editingId is not null;
        var deleteWidth = deleting
            ? EchoButton.ContentSize("Delete Glamour", plugin.Fonts.Icon, FontAwesomeIcon.TrashAlt).X
            : 0f;

        if (EchoButton.Draw(
                "##publish",
                publishing ? "Publishing..." : editingId is null ? "Publish" : "Save Changes",
                new Vector2(width, 34f * scale),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.CloudUploadAlt,
                enabled: !blocked,
                tooltip: missingTitle ? "Give it a title first."
                    : missingImage ? "Attach at least one screenshot."
                    : missingLook ? "There is no look to publish."
                    : missingAuthor ? "Waiting for your character. Try again in a moment."
                    : "Everyone browsing the gallery will see this.",
                primary: !blocked))
            ImGui.OpenPopup(RulesPopupId);

        DrawRulesPopup();

        if (deleting)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, width - deleteWidth));

            if (EchoButton.Draw(
                    "##unpublish", "Delete Glamour", new Vector2(deleteWidth, 26f * scale),
                    accentOverride: Theme.Bad,
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.TrashAlt,
                    enabled: !publishing,
                    tooltip: "Takes it off the board for good. Your local outfit is untouched."))
                Unpublish();
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (problem is { } message)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextColored(Theme.Bad, message);
            ImGui.PopTextWrapPos();
        }

        if (done is { } success)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextColored(Theme.Good, success);
            ImGui.PopTextWrapPos();
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);

        ImGui.TextColored(
            Theme.TextDisabled,
            settings.MaximumPerOwner > 0
                ? $"You can have {settings.MaximumPerOwner} glamours published at once. Anything published here "
                  + "is public, and can be reported by anyone who sees it."
                : "Anything published here is public, and can be reported by anyone who sees it.");

        ImGui.PopTextWrapPos();

        Theme.EndPanel();
    }

    private const string RulesPopupId = "##echoglamrules";

    /// The rules, and the last chance to not do it.
    private void DrawRulesPopup()
    {
        var scale = UiHelpers.Scale;

        var content = 400f * scale;

        var centre = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(centre, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24f, 21f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.5f * scale);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.7f));

        var open = ImGui.BeginPopupModal(
            RulesPopupId, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove);

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);

        if (!open)
            return;

        ImGui.Dummy(new Vector2(content, 0f));

        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextUnformatted("Before You Publish");

        ImGui.Dummy(new Vector2(0f, 14f * scale));
        Divider(content);
        ImGui.Dummy(new Vector2(0f, 14f * scale));

        Rule(content, Theme.Accent, "No modded clothing or bodies.", "Modded hair and eyes are fine.");
        Rule(content, Theme.Accent, "Nothing NSFW.", "Reported means removed.");
        Rule(
            content, Theme.Warning,
            $"{State.Settings.BanThreshold} takedowns and you are done.",
            "Your published glamours stay. You cannot add more.");

        ImGui.Dummy(new Vector2(0f, 4f * scale));
        Divider(content);
        ImGui.Dummy(new Vector2(0f, 14f * scale));

        DrawRuleButtons(content);
        ImGui.EndPopup();
    }

    /// The two answers, sitting at the bottom right the way a dialog's do.
    private void DrawRuleButtons(float content)
    {
        var scale = UiHelpers.Scale;
        var height = 30f * scale;
        var gap = 8f * scale;

        var publishWidth = EchoButton.ContentSize("Publish it", plugin.Fonts.Icon, FontAwesomeIcon.Check).X;
        var cancelWidth = EchoButton.ContentSize("Not yet").X;

        var left = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(left + MathF.Max(0f, content - publishWidth - cancelWidth - gap));

        if (EchoButton.Draw(
                "##rulesno", "Not yet", new Vector2(0f, height),
                tooltip: "Back to the form. Nothing is sent."))
            ImGui.CloseCurrentPopup();

        ImGui.SameLine(0f, gap);

        if (EchoButton.Draw(
                "##rulesyes", "Publish it", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Check))
        {
            ImGui.CloseCurrentPopup();
            Publish();
        }
    }

    /// A hairline that fades out at both ends, matching the rule under a section header.
    private static void Divider(float width)
    {
        var scale = UiHelpers.Scale;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var edge = ImGui.GetColorU32(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0f));
        var middle = ImGui.GetColorU32(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0.9f));

        drawList.AddRectFilledMultiColor(
            origin, new Vector2(origin.X + (width / 2f), origin.Y + (1f * scale)), edge, middle, middle, edge);

        drawList.AddRectFilledMultiColor(
            new Vector2(origin.X + (width / 2f), origin.Y), new Vector2(origin.X + width, origin.Y + (1f * scale)),
            middle, edge, edge, middle);

        ImGui.Dummy(new Vector2(width, 1f * scale));
    }

    /// One rule: a mark, the line that matters, and the qualification under it.
    private static void Rule(float content, Vector4 colour, string rule, string detail)
    {
        var scale = UiHelpers.Scale;
        var indent = 20f * scale;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var left = ImGui.GetCursorPosX();

        drawList.AddCircleFilled(
            new Vector2(origin.X + (4f * scale), origin.Y + (ImGui.GetTextLineHeight() / 2f)),
            3.5f * scale, ImGui.GetColorU32(colour));

        ImGui.SetCursorPosX(left + indent);
        ImGui.PushTextWrapPos(left + content);
        ImGui.TextColored(Theme.Text, rule);

        ImGui.SetCursorPosX(left + indent);
        ImGui.TextColored(Theme.TextDim, detail);
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, 12f * scale));
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
                Published?.Invoke(result.Id);

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
                    Removed?.Invoke(id);
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
