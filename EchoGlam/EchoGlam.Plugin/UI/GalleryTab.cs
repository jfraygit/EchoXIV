using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using EchoGlam.Game;
using EchoGlam.Shared;
using EchoGlam.UI.Controls;

namespace EchoGlam.UI;

/// The gallery: everyone's published glamours, and the one thing this plugin exists for.
public sealed class GalleryTab : IDisposable
{
    private readonly Plugin plugin;
    private readonly GalleryImages images = new();

    private GalleryQuery query = new();
    private GlamourPage? page;
    private bool loading;
    private string? error;

    /// Cancels the browse currently in flight.
    private CancellationTokenSource? browsing;

    private GlamourDetail? detail;
    private bool detailLoading;
    private string? detailError;
    private int detailImage;

    /// The report note box is a fixed width, so this can be derived rather than remembered.
    private static float ReportNoteWrap =>
        (280f * UiHelpers.Scale) - (ImGui.GetStyle().FramePadding.X * 2f) - (18f * UiHelpers.Scale);

    /// The picture sliding out, or -1 when nothing is moving.
    private int slideFrom = -1;

    /// How far through the slide, 0 to 1.
    private float slideProgress;

    /// +1 when stepping forward, -1 when stepping back.
    private int slideDirection = 1;

    /// How long a step takes.
    private const float SlideSeconds = 0.18f;

    /// Starts a slide to another picture.
    private void SlideTo(int index, int count)
    {
        if (count <= 0 || index == detailImage)
            return;

        var forward = ((index - detailImage) % count + count) % count;
        var backward = ((detailImage - index) % count + count) % count;

        slideDirection = forward <= backward ? 1 : -1;
        slideFrom = detailImage;
        slideProgress = 0f;
        detailImage = index;
    }

    /// Drops any slide in progress.
    private void StopSlide()
    {
        slideFrom = -1;
        slideProgress = 0f;
    }

    /// One screenshot in the detail frame, offset horizontally by dx.
    private void DrawDetailPicture(
        ImDrawListPtr drawList, GlamourSummary entry, int index,
        Vector2 pos, float width, float height, float rounding, float dx)
    {
        var url = GalleryClient.ImageUrl(entry, index, card: false);
        var texture = images.Get(url);

        if (texture != null)
        {
            var scaleToFit = MathF.Min(width / texture.Width, height / texture.Height);
            var drawn = new Vector2(texture.Width, texture.Height) * scaleToFit;
            var offset = (new Vector2(width, height) - drawn) / 2f + new Vector2(dx, 0f);

            drawList.AddImageRounded(
                texture.Handle, pos + offset, pos + offset + drawn, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), rounding);

            return;
        }

        var label = images.Failed(url) ? "That screenshot couldn't be loaded" : "Loading...";
        var measured = ImGui.CalcTextSize(label);

        drawList.AddText(
            pos + ((new Vector2(width, height) - measured) / 2f) + new Vector2(dx, 0f),
            ImGui.GetColorU32(Theme.TextDisabled), label);
    }

    private string search = string.Empty;

    /// The last search text a request was sent for, so typing does not fire one per frame.
    private string searchSent = string.Empty;
    private double searchIdleSince;

    /// How long the search box has to stand still before a request goes out.
    private const double SearchDebounceSeconds = 0.35;

    /// What the character looked like before the first try-on, so "Put my look back" puts back the outfit
    /// that was being built rather than stripping to the character's real gear.
    private Dictionary<GlamSlot, GlamEntry>? beforeTryOn;
    private CustomizeSet? beforeAppearance;

    /// The entry currently worn, if any.
    private string? tryingOn;

    private string? saveResult;
    private double saveResultAt;

    public GalleryTab(Plugin plugin) => this.plugin = plugin;

    private ItemCatalogue Items => plugin.Items;
    private DyeCatalogue Dyes => plugin.Dyes;
    private Wardrobe Wardrobe => plugin.Wardrobe;

    /// The narrowest a card may be before the grid drops a column.
    private const float MinimumCardDesign = 200f;

    private const float CardGapDesign = 12f;

    public void Dispose() => images.Dispose();

    /// The images cache, shared with the Profile tab so a card seen in both places is fetched and decoded
    /// once.
    public GalleryImages Images => images;

    /// Raised when the player asks to see somebody's profile.
    public event Action<string>? OpenProfile;

    /// Raised when the player asks to publish the look they are wearing.
    public event Action? Publish;

    /// Raised when the player asks to edit something they published.
    public event Action<GlamourDetail>? Edit;

    public void Draw()
    {
        images.Tick();
        plugin.EnsureCatalogues();
        plugin.Gallery.EnsureSettings();

        if (page is null && !loading && error is null)
            Refresh();

        if (detail is not null || detailLoading || detailError is not null)
        {
            DrawDetail();
            return;
        }

        DrawBrowse();
    }

    /// Opens an entry by id, for another tab to hand one over.
    public void Show(string id, Action? back = null) => Open(id, back);

    /// Forces the grid to refetch - after something is published, for instance.
    public void Reload() => Refresh();


    private void DrawBrowse()
    {
        var scale = UiHelpers.Scale;

        DrawToolbar();
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var available = ImGui.GetContentRegionAvail();

        var pagerHeight = 34f * scale;

        if (ImGui.BeginChild(
                "##gallerygrid", new Vector2(0f, available.Y - pagerHeight), false,
                ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (error is { } message)
            {
                DrawNotice(FontAwesomeIcon.PlugCircleExclamation, "The gallery couldn't be reached", message, "Try again", Refresh);
            }
            else if (page is null)
            {
                DrawNotice(FontAwesomeIcon.Spinner, "Loading the gallery", "Fetching the newest glamours.", null, null);
            }
            else if (page.Entries.Count == 0)
            {
                DrawEmpty();
            }
            else
            {
                DrawGrid(page.Entries);
            }
        }

        ImGui.EndChild();

        if (page is { Total: > 0 })
            DrawPager();
    }

    /// Sort, search and the three facets, in one row that reflows.
    private void DrawToolbar()
    {
        var scale = UiHelpers.Scale;
        var height = 28f * scale;

        var labels = new[] { SortLabel(GlamourSort.All[0]), SortLabel(GlamourSort.All[1]), SortLabel(GlamourSort.All[2]), SortLabel(GlamourSort.All[3]) };
        var tips = new[] { SortTooltip(GlamourSort.All[0]), SortTooltip(GlamourSort.All[1]), SortTooltip(GlamourSort.All[2]), SortTooltip(GlamourSort.All[3]) };

        var current = Array.IndexOf(GlamourSort.All, query.Sort);
        var chosen = EchoSegment.Draw("##gallerysort", labels, current, height, tips);

        if (chosen >= 0)
        {
            query.Sort = GlamourSort.All[chosen];
            query.Page = 0;
            Refresh();
        }

        var publishWidth = EchoButton.ContentSize("Publish Outfit", plugin.Fonts.Icon, FontAwesomeIcon.CloudUploadAlt).X;
        var segmentWidth = EchoSegment.Width(labels);
        var room = ImGui.GetContentRegionAvail().X;

        var gapToRight = room - segmentWidth - publishWidth;

        if (gapToRight > 12f * scale)
            ImGui.SameLine(0f, gapToRight);
        else
            ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (EchoButton.Draw(
                "##publishlook", "Publish Outfit", new Vector2(publishWidth, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.CloudUploadAlt,
                enabled: plugin.Gallery.CanPublish,
                tooltip: plugin.Gallery.CanPublish
                    ? "Put the look on your character onto the board."
                    : "The gallery isn't accepting submissions yet.",
                primary: plugin.Gallery.CanPublish))
            Publish?.Invoke();

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var jobLabel = query.JobId is { } job ? JobList.Find(job)?.Abbreviation ?? "Job" : "Any job";
        var raceLabel = query.Race is { } race and > 0 ? RaceNames.Race(race, query.Sex ?? 0) : "Any race";
        var tagLabel = query.Tag ?? "Any tag";

        var widths = FilterWidths();

        var filterWidth = widths.Job + widths.Race + widths.Tag + (18f * scale);

        var searchWidth = MathF.Max(120f * scale, ImGui.GetContentRegionAvail().X - filterWidth);

        UiHelpers.SearchField(
            "##gallerysearch", "Search titles, authors and tags...", ref search,
            searchWidth, height, plugin.Fonts.Icon);

        if (search != searchSent)
        {
            searchIdleSince = ImGui.GetTime();
            searchSent = search;
        }
        else if (searchIdleSince > 0d && ImGui.GetTime() - searchIdleSince > SearchDebounceSeconds)
        {
            searchIdleSince = 0d;
            query.Text = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            query.Page = 0;
            Refresh();
        }

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##galleryjob", jobLabel, new Vector2(widths.Job, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Filter,
                selected: query.JobId is not null,
                tooltip: FilterTooltip("Show only glamours built around one job.", query.JobId is not null)))
            ImGui.OpenPopup(JobPopupId);

        if (EchoButton.RightClicked() && query.JobId is not null)
        {
            query.JobId = null;
            query.Page = 0;
            Refresh();
        }

        DrawJobPopup();

        ImGui.SameLine(0f, 6f * scale);

        var raceFiltered = query.Race is not null || query.Sex is not null;

        if (EchoButton.Draw(
                "##galleryrace", raceLabel, new Vector2(widths.Race, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.User,
                selected: raceFiltered,
                tooltip: FilterTooltip(
                    "The same outfit reads very differently on a Lalafell and a Roegadyn.", raceFiltered)))
            ImGui.OpenPopup(RacePopupId);

        if (EchoButton.RightClicked() && raceFiltered)
        {
            query.Race = null;
            query.Sex = null;
            query.Page = 0;
            Refresh();
        }

        DrawRacePopup();

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##gallerytag", tagLabel, new Vector2(widths.Tag, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Tag,
                selected: query.Tag is not null,
                tooltip: FilterTooltip("Show only one kind of look.", query.Tag is not null)))
            ImGui.OpenPopup(TagPopupId);

        if (EchoButton.RightClicked() && query.Tag is not null)
        {
            query.Tag = null;
            query.Page = 0;
            Refresh();
        }

        DrawTagPopup();
    }

    private float filterWidthsAt = -1f;
    private int filterVocabularyAt = -1;
    private (float Job, float Race, float Tag) filterWidths;

    /// How wide each filter chip is, held fixed at the widest label it will ever show.
    private (float Job, float Race, float Tag) FilterWidths()
    {
        var scale = UiHelpers.Scale;
        var races = RaceNames.All;
        var tags = plugin.Gallery.Settings.Tags;
        var vocabulary = races.Length + tags.Length;

        if (MathF.Abs(filterWidthsAt - scale) < 0.001f && vocabulary == filterVocabularyAt)
            return filterWidths;

        var job = EchoButton.ContentSize("Any job", plugin.Fonts.Icon, FontAwesomeIcon.Filter).X;
        foreach (var info in JobList.All)
            job = MathF.Max(job, EchoButton.ContentSize(info.Abbreviation, plugin.Fonts.Icon, FontAwesomeIcon.Filter).X);

        var race = EchoButton.ContentSize("Any race", plugin.Fonts.Icon, FontAwesomeIcon.User).X;
        foreach (var (_, name) in races)
            race = MathF.Max(race, EchoButton.ContentSize(name, plugin.Fonts.Icon, FontAwesomeIcon.User).X);

        var tag = EchoButton.ContentSize("Any tag", plugin.Fonts.Icon, FontAwesomeIcon.Tag).X;
        foreach (var name in tags)
            tag = MathF.Max(tag, EchoButton.ContentSize(name, plugin.Fonts.Icon, FontAwesomeIcon.Tag).X);

        filterWidths = (job, race, tag);
        filterWidthsAt = scale;
        filterVocabularyAt = vocabulary;

        return filterWidths;
    }

    /// A filter's tooltip, which says how to undo it only while there is something to undo.
    private static string FilterTooltip(string what, bool active) =>
        active ? what + "\nRight-click to clear it." : what;

    private static string SortLabel(string sort) => sort switch
    {
        GlamourSort.Top => "Top",
        GlamourSort.Newest => "Newest",
        GlamourSort.Favourites => "Saved",
        _ => "Trending",
    };

    private static string SortTooltip(string sort) => sort switch
    {
        GlamourSort.Top => "The highest voted of all time.",
        GlamourSort.Newest => "Everything, newest first.",
        GlamourSort.Favourites => "Whatever the most people have saved.",
        _ => "Recent interest, so something posted today can still reach the top.",
    };

    private const string JobPopupId = "##echoglamgalleryjobs";
    private const string RacePopupId = "##echoglamgalleryraces";
    private const string TagPopupId = "##echoglamgallerytags";

    private void DrawJobPopup()
    {
        if (!ImGui.BeginPopup(JobPopupId))
            return;

        var scale = UiHelpers.Scale;
        var size = 34f * scale;

        if (EchoButton.Draw("##anyjob", "Any job", new Vector2(0f, 24f * scale), selected: query.JobId is null))
        {
            query.JobId = null;
            query.Page = 0;
            Refresh();
            ImGui.CloseCurrentPopup();
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var drawList = ImGui.GetWindowDrawList();
        var perRow = 9;
        var index = 0;

        foreach (var job in JobList.All)
        {
            if (index > 0 && index % perRow != 0)
                ImGui.SameLine(0f, 4f * scale);

            var pos = ImGui.GetCursorScreenPos();
            var selected = query.JobId == job.Id;

            if (ImGui.InvisibleButton($"##galjob{job.Id}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft))
            {
                query.JobId = selected ? null : job.Id;
                query.Page = 0;
                Refresh();
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

    private void DrawRacePopup()
    {
        if (!ImGui.BeginPopup(RacePopupId))
            return;

        var scale = UiHelpers.Scale;
        var height = 24f * scale;

        ImGui.TextColored(Theme.TextDim, "Gender");
        ImGui.Dummy(new Vector2(0f, 3f * scale));

        (string Label, byte? Value)[] sexes = [("Any", null), ("Male", 0), ("Female", 1)];

        foreach (var (label, value) in sexes)
        {
            if (EchoButton.Draw($"##sex{label}", label, new Vector2(0f, height), selected: query.Sex == value))
            {
                query.Sex = value;
                query.Page = 0;
                Refresh();
            }

            ImGui.SameLine(0f, 5f * scale);
        }

        ImGui.Dummy(new Vector2(0f, 0f));
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        ImGui.TextColored(Theme.TextDim, "Race");
        ImGui.Dummy(new Vector2(0f, 3f * scale));

        if (EchoButton.Draw("##anyrace", "Any race", new Vector2(0f, height), selected: query.Race is null))
        {
            query.Race = null;
            query.Page = 0;
            Refresh();
            ImGui.CloseCurrentPopup();
        }

        foreach (var (id, name) in RaceNames.All)
        {
            if (EchoButton.Draw($"##race{id}", name, new Vector2(0f, height), selected: query.Race == id))
            {
                query.Race = query.Race == id ? null : id;
                query.Page = 0;
                Refresh();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
    }

    private void DrawTagPopup()
    {
        if (!ImGui.BeginPopup(TagPopupId))
            return;

        var scale = UiHelpers.Scale;
        var height = 24f * scale;

        if (EchoButton.Draw("##anytag", "Any tag", new Vector2(0f, height), selected: query.Tag is null))
        {
            query.Tag = null;
            query.Page = 0;
            Refresh();
            ImGui.CloseCurrentPopup();
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var perRow = 5;
        var index = 0;

        foreach (var tag in plugin.Gallery.Settings.Tags)
        {
            if (index > 0 && index % perRow != 0)
                ImGui.SameLine(0f, 5f * scale);

            if (EchoButton.Draw($"##tag{tag}", tag, new Vector2(0f, height), selected: query.Tag == tag))
            {
                query.Tag = query.Tag == tag ? null : tag;
                query.Page = 0;
                Refresh();
                ImGui.CloseCurrentPopup();
            }

            index++;
        }

        ImGui.EndPopup();
    }

    /// The card grid.
    private void DrawGrid(List<GlamourSummary> entries)
    {
        var scale = UiHelpers.Scale;
        var gap = CardGapDesign * scale;
        var available = ImGui.GetContentRegionAvail().X;

        var columns = Math.Max(1, (int)((available + gap) / ((MinimumCardDesign * scale) + gap)));
        var cardWidth = (available - (gap * (columns - 1))) / columns;
        var imageHeight = ImageProcessor.HeightFor(cardWidth);
        var cardHeight = imageHeight + (72f * scale);

        for (var i = 0; i < entries.Count; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine(0f, gap);

            DrawCard(entries[i], new Vector2(cardWidth, cardHeight), imageHeight);
        }

        ImGui.Dummy(new Vector2(0f, 4f * scale));
    }

    private void DrawCard(GlamourSummary entry, Vector2 size, float imageHeight)
    {
        var scale = UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var rounding = Theme.CardRounding;

        var clicked = ImGui.InvisibleButton($"##card{entry.Id}", size, ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        var lift = hovered ? 1.9f : 1f;

        for (var i = 4; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 2f * scale * lift);
            var spread = (i - 1) * 0.8f * scale * lift;

            drawList.AddRectFilled(
                pos + offset - new Vector2(spread, 0f),
                pos + size + offset + new Vector2(spread, 0f),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.045f * i)), rounding);
        }

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Theme.Panel), rounding);

        DrawCardImage(drawList, entry, pos, new Vector2(size.X, imageHeight), rounding, hovered);

        var scrimHeight = 22f * scale;
        var scrimTop = pos.Y + imageHeight - scrimHeight;
        var panel = ImGui.GetColorU32(Theme.Panel);
        var clear = ImGui.GetColorU32(Theme.Panel with { W = 0f });

        drawList.AddRectFilledMultiColor(
            new Vector2(pos.X, scrimTop), new Vector2(pos.X + size.X, pos.Y + imageHeight),
            clear, clear, panel, panel);

        drawList.PushClipRect(pos, pos + size, true);

        var textX = pos.X + (12f * scale);
        var textWidth = size.X - (24f * scale);
        var y = pos.Y + imageHeight + (9f * scale);

        drawList.AddText(new Vector2(textX, y), ImGui.GetColorU32(Theme.Text), Truncate(entry.Title, textWidth));
        y += ImGui.GetTextLineHeight() + (3f * scale);

        var author = string.IsNullOrEmpty(entry.AuthorWorld)
            ? entry.AuthorName
            : $"{entry.AuthorName} · {entry.AuthorWorld}";

        drawList.AddText(new Vector2(textX, y), ImGui.GetColorU32(Theme.TextDim), Truncate(author, textWidth));
        y += ImGui.GetTextLineHeight() + (6f * scale);

        DrawCardFooter(drawList, entry, new Vector2(textX, y), textWidth);

        drawList.PopClipRect();

        drawList.AddRect(
            pos, pos + size,
            ImGui.GetColorU32(hovered
                ? new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.85f)
                : Theme.Border),
            rounding, ImDrawFlags.None, (hovered ? 1.8f : 1.2f) * scale);

        if (entry.PublishedUtc > 0
            && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(entry.PublishedUtc) < TimeSpan.FromDays(3))
        {
            const string label = "NEW";
            var padX = 7f * scale;
            var padY = 3f * scale;
            var text = ImGui.CalcTextSize(label);

            var badgeMin = pos + new Vector2(10f * scale, 10f * scale);
            var badgeMax = badgeMin + text + new Vector2(padX * 2f, padY * 2f);

            drawList.AddRectFilled(badgeMin, badgeMax, ImGui.GetColorU32(Theme.Accent), 5f * scale);
            drawList.AddText(badgeMin + new Vector2(padX, padY), ImGui.GetColorU32(Theme.Background), label);
        }

        if (clicked)
            Open(entry.Id);
    }

    /// The card's screenshot, or what stands in for it while there isn't one.
    private void DrawCardImage(
        ImDrawListPtr drawList, GlamourSummary entry, Vector2 pos, Vector2 size, float rounding, bool hovered = false)
    {
        var max = pos + size;

        if (entry.ImageCount > 0)
        {
            var url = GalleryClient.ImageUrl(entry, 0, card: true);
            var texture = images.Get(url);

            if (texture != null)
            {
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
                    var zoom = 0.03f;
                    var inset = (uv1 - uv0) * (zoom / 2f);
                    uv0 += inset;
                    uv1 -= inset;
                }

                drawList.AddImageRounded(
                    texture.Handle, pos, max, uv0, uv1,
                    ImGui.GetColorU32(Vector4.One), rounding, ImDrawFlags.RoundCornersTop);

                return;
            }
        }

        drawList.AddRectFilled(
            pos, max, ImGui.GetColorU32(Theme.Tinted(0.06f)), rounding, ImDrawFlags.RoundCornersTop);

        var failed = entry.ImageCount == 0 || images.Failed(GalleryClient.ImageUrl(entry, 0, card: true));
        var label = failed ? "No screenshot" : "Loading...";
        var measured = ImGui.CalcTextSize(label);

        drawList.AddText(
            pos + ((size - measured) / 2f), ImGui.GetColorU32(Theme.TextDisabled), label);
    }

    /// Race and job on the left, the two counts on the right.
    private void DrawCardFooter(ImDrawListPtr drawList, GlamourSummary entry, Vector2 origin, float width)
    {
        var scale = UiHelpers.Scale;
        var lineHeight = ImGui.GetTextLineHeight();
        var centre = origin.Y + (lineHeight / 2f);

        var x = origin.X;

        if (entry.JobId > 0 && JobList.Find(entry.JobId) is { } job)
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrDefault();
            if (texture != null)
            {
                var iconSize = lineHeight + (2f * scale);
                drawList.AddImage(
                    texture.Handle, new Vector2(x, centre - (iconSize / 2f)),
                    new Vector2(x + iconSize, centre + (iconSize / 2f)),
                    Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f)));

                x += iconSize + (6f * scale);
            }
        }

        var countsWidth = UiHelpers.DrawGlamourCounts(
            drawList, plugin.Fonts.Icon, origin, width,
            entry.Votes, entry.Voted, entry.Favourites, entry.Favourited);

        var raceRoom = origin.X + width - countsWidth - x - (8f * scale);
        if (raceRoom <= 0f)
            return;

        var race = RaceNames.Race(entry.Race, entry.Sex);
        drawList.AddText(new Vector2(x, origin.Y), ImGui.GetColorU32(Theme.TextDim), Truncate(race, raceRoom));
    }

    private void DrawPager()
    {
        if (page is not { } current)
            return;

        var scale = UiHelpers.Scale;
        var pages = Math.Max(1, (int)Math.Ceiling(current.Total / (double)Math.Max(1, current.PageSize)));
        var height = 24f * scale;

        var label = $"Page {current.Page + 1} of {pages}  ·  {current.Total} glamour{(current.Total == 1 ? "" : "s")}";
        var labelWidth = ImGui.CalcTextSize(label).X;

        var backWidth = EchoButton.ContentSize(null, plugin.Fonts.Icon, FontAwesomeIcon.ChevronLeft).X;
        var nextWidth = EchoButton.ContentSize(null, plugin.Fonts.Icon, FontAwesomeIcon.ChevronRight).X;

        var total = backWidth + nextWidth + labelWidth + (24f * scale);
        var start = MathF.Max(0f, (ImGui.GetContentRegionAvail().X - total) / 2f);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + start);

        var troughPad = new Vector2(10f * scale, 5f * scale);
        var troughMin = ImGui.GetCursorScreenPos() - troughPad;
        var troughMax = troughMin + new Vector2(total + (troughPad.X * 2f), height + (troughPad.Y * 2f));

        ImGui.GetWindowDrawList().AddRectFilled(
            troughMin, troughMax, ImGui.GetColorU32(Theme.Panel with { W = 0.55f }), (height / 2f) + troughPad.Y);

        if (EchoButton.Draw(
                "##pageback", null, new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.ChevronLeft,
                enabled: current.Page > 0))
        {
            query.Page = current.Page - 1;
            Refresh();
        }

        ImGui.SameLine(0f, 12f * scale);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextDim, label);
        ImGui.SameLine(0f, 12f * scale);

        if (EchoButton.Draw(
                "##pagenext", null, new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.ChevronRight,
                enabled: current.Page + 1 < pages))
        {
            query.Page = current.Page + 1;
            Refresh();
        }
    }

    private void DrawEmpty()
    {
        var filtered = query.JobId is not null || query.Race is not null || query.Sex is not null
                       || query.Tag is not null || !string.IsNullOrWhiteSpace(query.Text);

        if (filtered)
        {
            DrawNotice(
                FontAwesomeIcon.Search, "Nothing matches that",
                "No published glamour fits all of those filters yet.",
                "Clear filters", ClearFilters);

            return;
        }

        var canPublish = plugin.Gallery.CanPublish;

        DrawNotice(
            FontAwesomeIcon.Tshirt, "The gallery is empty",
            canPublish
                ? "Nobody has published a glamour yet. Put an outfit together in the Dressing Room "
                  + "and yours can be the first."
                : "Nobody has published a glamour yet, and submissions are closed at the moment.",
            canPublish ? "Publish a glamour" : null,
            canPublish ? () => Publish?.Invoke() : null);
    }

    private void ClearFilters()
    {
        query = new GalleryQuery { Sort = query.Sort };
        search = string.Empty;
        searchSent = string.Empty;
        Refresh();
    }

    /// A centred glyph, a heading and a sentence.
    private void DrawNotice(FontAwesomeIcon icon, string heading, string body, string? action, Action? onAction)
    {
        var scale = UiHelpers.Scale;
        var available = ImGui.GetContentRegionAvail();

        ImGui.Dummy(new Vector2(0f, MathF.Max(20f * scale, (available.Y * 0.28f) - (40f * scale))));

        var drawList = ImGui.GetWindowDrawList();
        var centreX = ImGui.GetCursorScreenPos().X + (available.X / 2f);

        using (plugin.Fonts.Icon.PushSafe())
        {
            var glyph = icon.ToIconString();
            var font = ImGui.GetFont();
            var glyphSize = 34f * scale;
            var measured = ImGui.CalcTextSize(glyph) * (glyphSize / ImGui.GetFontSize());

            drawList.AddText(
                font, glyphSize,
                new Vector2(centreX - (measured.X / 2f), ImGui.GetCursorScreenPos().Y),
                ImGui.GetColorU32(Theme.AccentSoft), glyph, 0f);
        }

        ImGui.Dummy(new Vector2(0f, 44f * scale));

        Centred(heading, Theme.Text);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        var wrapWidth = MathF.Min(
            MathF.Min(available.X - (40f * scale), 420f * scale),
            ImGui.CalcTextSize(body).X);

        var indent = MathF.Max(0f, (available.X - wrapWidth) / 2f);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapWidth);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
        ImGui.TextUnformatted(body);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();

        if (action is null || onAction is null)
            return;

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        var buttonWidth = EchoButton.ContentSize(action).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (available.X - buttonWidth) / 2f));

        if (EchoButton.Draw("##noticeaction", action, new Vector2(0f, 26f * scale)))
            onAction();
    }

    private static void Centred(string text, Vector4 colour)
    {
        var width = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (ImGui.GetContentRegionAvail().X - width) / 2f));
        ImGui.TextColored(colour, text);
    }

    /// Shortens text to fit a pixel width, with an ellipsis.
    private static string Truncate(string text, float width)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= width)
            return text;

        const string ellipsis = "...";
        var room = width - ImGui.CalcTextSize(ellipsis).X;

        if (room <= 0f)
            return ellipsis;

        var length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length]).X > room)
            length--;

        return text[..length].TrimEnd() + ellipsis;
    }


    private void DrawDetail()
    {
        var scale = UiHelpers.Scale;

        if (EchoButton.Draw(
                "##galleryback", "Back", new Vector2(0f, 26f * scale),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.ChevronLeft))
        {
            Close();
            return;
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (detailError is { } problem)
        {
            DrawNotice(FontAwesomeIcon.PlugCircleExclamation, "That glamour couldn't be opened", problem, null, null);
            return;
        }

        if (detail is not { } entry)
        {
            DrawNotice(FontAwesomeIcon.Spinner, "Opening", "Fetching the gear list.", null, null);
            return;
        }

        if (!ImGui.BeginChild("##detailscroll", ImGui.GetContentRegionAvail(), false))
        {
            ImGui.EndChild();
            return;
        }

        var available = ImGui.GetContentRegionAvail().X;

        var byHeight = MathF.Min(ImGui.GetContentRegionAvail().Y, 560f * scale) * ImageProcessor.ShotAspect;
        var imageWidth = MathF.Max(200f * scale, MathF.Min(byHeight, available * 0.45f));

        var listWidth = available - imageWidth - (14f * scale);
        var stacked = listWidth < 200f * scale;

        if (stacked)
        {
            imageWidth = available;
            listWidth = available;
        }

        DrawDetailImage(entry, imageWidth);

        if (!stacked)
            ImGui.SameLine(0f, 14f * scale);
        else
            ImGui.Dummy(new Vector2(0f, 12f * scale));

        ImGui.BeginGroup();
        DrawDetailInfo(entry, listWidth);
        ImGui.EndGroup();

        ImGui.EndChild();

        if (pendingClose)
        {
            pendingClose = false;
            Close();
            Refresh();
        }
    }

    private bool pendingClose;

    private void DrawDetailImage(GlamourDetail entry, float width)
    {
        var scale = UiHelpers.Scale;
        var height = ImageProcessor.HeightFor(width);

        ImGui.BeginGroup();

        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Theme.CardRounding;

        ImGui.Dummy(new Vector2(width, height));

        for (var i = 4; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 2.2f * scale);
            drawList.AddRectFilled(
                pos + offset, pos + new Vector2(width, height) + offset,
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.05f * i)), rounding);
        }

        drawList.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.GetColorU32(Theme.Tinted(0.05f)), rounding);

        if (entry.ImageCount > 0)
        {
            if (slideFrom >= 0)
            {
                slideProgress += MathF.Min(ImGui.GetIO().DeltaTime, 1f / 30f) / SlideSeconds;

                if (slideProgress >= 1f)
                    StopSlide();
            }

            var index = Math.Clamp(detailImage, 0, entry.ImageCount - 1);

            drawList.PushClipRect(pos, pos + new Vector2(width, height), true);

            if (slideFrom >= 0)
            {
                var t = 1f - MathF.Pow(1f - Math.Clamp(slideProgress, 0f, 1f), 3f);

                DrawDetailPicture(drawList, entry, slideFrom, pos, width, height, rounding, -slideDirection * width * t);
                DrawDetailPicture(drawList, entry, index, pos, width, height, rounding, slideDirection * width * (1f - t));
            }
            else
            {
                DrawDetailPicture(drawList, entry, index, pos, width, height, rounding, 0f);
            }

            drawList.PopClipRect();
        }

        drawList.AddRect(
            pos, pos + new Vector2(width, height), ImGui.GetColorU32(Theme.Border),
            rounding, ImDrawFlags.None, 1.2f * scale);


        if (entry.ImageCount > 1)
        {
            var arrow = 30f * scale;
            var inset = 10f * scale;
            var centreY = pos.Y + (height / 2f);

            if (DrawImageArrow(drawList, "##previmage", new Vector2(pos.X + inset, centreY - (arrow / 2f)), arrow, FontAwesomeIcon.ChevronLeft))
                SlideTo((detailImage - 1 + entry.ImageCount) % entry.ImageCount, entry.ImageCount);

            if (DrawImageArrow(drawList, "##nextimage", new Vector2(pos.X + width - inset - arrow, centreY - (arrow / 2f)), arrow, FontAwesomeIcon.ChevronRight))
                SlideTo((detailImage + 1) % entry.ImageCount, entry.ImageCount);
        }

        if (entry.ImageCount > 1)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));

            var thumbWidth = 64f * scale;
            var thumbHeight = ImageProcessor.HeightFor(thumbWidth);
            var thumbGap = 5f * scale;

            var stripWidth = (entry.ImageCount * thumbWidth) + ((entry.ImageCount - 1) * thumbGap);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (width - stripWidth) / 2f));

            for (var i = 0; i < entry.ImageCount; i++)
            {
                if (i > 0)
                    ImGui.SameLine(0f, thumbGap);

                var thumbPos = ImGui.GetCursorScreenPos();
                if (ImGui.InvisibleButton($"##thumb{i}", new Vector2(thumbWidth, thumbHeight), ImGuiButtonFlags.MouseButtonLeft))
                    SlideTo(i, entry.ImageCount);

                var thumb = images.Get(GalleryClient.ImageUrl(entry, i, card: true));
                if (thumb != null)
                {
                    drawList.AddImageRounded(
                        thumb.Handle, thumbPos, thumbPos + new Vector2(thumbWidth, thumbHeight),
                        Vector2.Zero, Vector2.One,
                        ImGui.GetColorU32(new Vector4(1f, 1f, 1f, i == detailImage ? 1f : 0.6f)),
                        4f * scale);
                }
                else
                {
                    drawList.AddRectFilled(
                        thumbPos, thumbPos + new Vector2(thumbWidth, thumbHeight),
                        ImGui.GetColorU32(Theme.Tinted(0.05f)), 4f * scale);
                }

                if (i == detailImage)
                {
                    var min = thumbPos - new Vector2(2f * scale, 2f * scale);
                    var max = thumbPos + new Vector2(thumbWidth, thumbHeight) + new Vector2(2f * scale, 2f * scale);

                    drawList.AddRect(
                        min, max, ImGui.GetColorU32(Theme.Accent), 5f * scale, ImDrawFlags.None, 1.8f * scale);

                    var barWidth = thumbWidth * 0.42f;
                    var barY = max.Y + (4f * scale);

                    drawList.AddRectFilled(
                        new Vector2(thumbPos.X + ((thumbWidth - barWidth) / 2f), barY),
                        new Vector2(thumbPos.X + ((thumbWidth + barWidth) / 2f), barY + (2f * scale)),
                        ImGui.GetColorU32(Theme.Accent), 1f * scale);
                }
            }
        }

        ImGui.EndGroup();
    }

    /// One of the arrows sitting on the picture.
    private bool DrawImageArrow(ImDrawListPtr drawList, string id, Vector2 min, float size, FontAwesomeIcon icon)
    {
        var cursor = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(min);

        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        ImGui.SetCursorScreenPos(cursor);

        var centre = min + new Vector2(size / 2f, size / 2f);
        var offset = MathF.Max(1f, 1.5f * UiHelpers.Scale);

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList, icon, centre + new Vector2(offset, offset),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)));

            UiHelpers.DrawScaledIcon(
                drawList, icon, centre,
                ImGui.GetColorU32(hovered ? Theme.Accent : new Vector4(1f, 1f, 1f, 0.85f)));
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return clicked;
    }

    /// The column beside the picture: who made it, what to do with it, what it says, and what is in it.
    private void DrawDetailInfo(GlamourDetail entry, float width)
    {
        var scale = UiHelpers.Scale;

        DrawIdentityCard(entry, width);

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawActionCard(entry, width);

        if (entry.Tags.Length > 0 || !string.IsNullOrWhiteSpace(entry.Description))
        {
            ImGui.Dummy(new Vector2(0f, 10f * scale));
            DrawAboutCard(entry, width);
        }

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawLookCard(entry, width);
    }

    /// Title, author and race, behind a monogram.
    private void DrawIdentityCard(GlamourDetail entry, float width)
    {
        var scale = UiHelpers.Scale;

        Theme.BeginPanel(width, Theme.Accent);

        var drawList = ImGui.GetWindowDrawList();
        var badge = 52f * scale;
        var origin = ImGui.GetCursorScreenPos();

        var initial = string.IsNullOrWhiteSpace(entry.AuthorName)
            ? "?"
            : entry.AuthorName.Trim()[..1].ToUpperInvariant();

        var centre = origin + new Vector2(badge / 2f, badge / 2f);

        for (var i = 3; i >= 1; i--)
            drawList.AddCircleFilled(centre, (badge / 2f) + (i * 2.2f * scale), ImGui.GetColorU32(Theme.Accent with { W = 0.045f }));

        drawList.AddCircleFilled(centre, badge / 2f, ImGui.GetColorU32(Theme.Accent with { W = 0.22f }));

        var avatar = string.IsNullOrEmpty(entry.AuthorId)
            ? null
            : images.Get(GalleryClient.AvatarUrl(entry.AuthorId), (int)MathF.Ceiling(badge * 2f));

        if (avatar != null)
        {
            var (uv0, uv1) = CentreCrop(avatar.Width, avatar.Height);

            drawList.AddImageRounded(
                avatar.Handle,
                centre - new Vector2(badge / 2f, badge / 2f),
                centre + new Vector2(badge / 2f, badge / 2f),
                uv0, uv1, ImGui.GetColorU32(Vector4.One), badge / 2f);
        }
        else
        {
            drawList.AddCircleFilled(
                centre - new Vector2(0f, badge * 0.12f), badge * 0.42f,
                ImGui.GetColorU32(Theme.Accent with { W = 0.10f }));

            using (plugin.Fonts.Header.PushSafe())
            {
                var glyph = ImGui.CalcTextSize(initial);
                drawList.AddText(centre - (glyph / 2f), ImGui.GetColorU32(Theme.Accent), initial);
            }
        }

        drawList.AddCircle(centre, badge / 2f, ImGui.GetColorU32(Theme.Accent with { W = 0.65f }), 0, 1.5f * scale);

        if (!string.IsNullOrEmpty(entry.AuthorId)
            && ImGui.IsMouseHoveringRect(centre - new Vector2(badge / 2f, badge / 2f), centre + new Vector2(badge / 2f, badge / 2f)))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                OpenProfile?.Invoke(entry.AuthorId);
        }

        ImGui.Dummy(new Vector2(badge, badge));

        float titleHeight;
        using (plugin.Fonts.Header.PushSafe())
            titleHeight = ImGui.GetTextLineHeight();

        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var textHeight = titleHeight + (2f * (ImGui.GetTextLineHeight() + spacing));

        ImGui.SetCursorScreenPos(new Vector2(
            origin.X + badge + (12f * scale),
            origin.Y + MathF.Max(0f, (badge - textHeight) / 2f)));

        ImGui.BeginGroup();

        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextUnformatted(Truncate(entry.Title, width - badge - (40f * scale)));

        var author = string.IsNullOrEmpty(entry.AuthorWorld)
            ? entry.AuthorName
            : $"{entry.AuthorName} · {entry.AuthorWorld}";

        var linkColour = ImGui.IsMouseHoveringRect(
            ImGui.GetCursorScreenPos(),
            ImGui.GetCursorScreenPos() + ImGui.CalcTextSize(author))
            ? Theme.AccentHover
            : Theme.Accent;

        ImGui.TextColored(linkColour, author);

        if (ImGui.IsItemClicked() && !string.IsNullOrEmpty(entry.AuthorId))
            OpenProfile?.Invoke(entry.AuthorId);

        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        ImGui.TextColored(Theme.TextDim, RaceNames.Describe(entry.Race, entry.Tribe, entry.Sex));

        if (entry.PublishedUtc > 0)
        {
            var published = Published(entry);
            var raceLine = ImGui.GetItemRectMin().Y;

            drawList.AddText(
                new Vector2(origin.X + width - (28f * scale) - ImGui.CalcTextSize(published).X, raceLine),
                ImGui.GetColorU32(Theme.TextDisabled), published);
        }

        ImGui.EndGroup();

        var contentBottom = MathF.Max(origin.Y + badge, ImGui.GetItemRectMax().Y);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, contentBottom));
        ImGui.Dummy(Vector2.Zero);

        Theme.EndPanel();
    }

    /// UVs that fill a square with the middle of a picture of any shape.
    private static (Vector2 Uv0, Vector2 Uv1) CentreCrop(int textureWidth, int textureHeight)
    {
        if (textureWidth <= 0 || textureHeight <= 0)
            return (Vector2.Zero, Vector2.One);

        var aspect = (float)textureWidth / textureHeight;

        if (aspect > 1f)
        {
            var half = 1f / aspect / 2f;
            return (new Vector2(0.5f - half, 0f), new Vector2(0.5f + half, 1f));
        }

        if (aspect < 1f)
        {
            var half = aspect / 2f;
            return (new Vector2(0f, 0.5f - half), new Vector2(1f, 0.5f + half));
        }

        return (Vector2.Zero, Vector2.One);
    }

    /// How long ago an entry went up, in the roughest terms that are still true.
    private static string Published(GlamourDetail entry)
    {
        var elapsed = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(entry.PublishedUtc);

        if (elapsed < TimeSpan.FromHours(1)) return "just now";
        if (elapsed < TimeSpan.FromDays(1)) return $"{MathF.Max(1, (int)elapsed.TotalHours)}h ago";
        if (elapsed < TimeSpan.FromDays(30)) return $"{(int)elapsed.TotalDays}d ago";
        if (elapsed < TimeSpan.FromDays(365)) return $"{(int)(elapsed.TotalDays / 30)}mo ago";

        return $"{(int)(elapsed.TotalDays / 365)}y ago";
    }

    private void DrawActionCard(GlamourDetail entry, float width)
    {
        Theme.BeginPanel(width);
        DrawDetailActions(entry, width - (28f * UiHelpers.Scale));
        Theme.EndPanel();
    }

    private void DrawAboutCard(GlamourDetail entry, float width)
    {
        var scale = UiHelpers.Scale;
        var inner = width - (28f * scale);

        Theme.BeginPanel(width);

        if (entry.Tags.Length > 0)
        {
            var x = 0f;
            foreach (var tag in entry.Tags)
            {
                var tagWidth = EchoButton.ContentSize(tag).X;

                if (x > 0f && x + tagWidth <= inner)
                    ImGui.SameLine(0f, 5f * scale);
                else if (x > 0f)
                    x = 0f;

                if (EchoButton.Draw($"##detailtag{tag}", tag, new Vector2(0f, 22f * scale)))
                {
                    query.Tag = tag;
                    query.Page = 0;
                    pendingClose = true;
                }

                x += tagWidth + (5f * scale);
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            if (entry.Tags.Length > 0)
                ImGui.Dummy(new Vector2(0f, 8f * scale));

            ImGui.TextUnformatted(entry.Description);
        }

        Theme.EndPanel();
    }

    /// The gear, as a grid of tiles.
    private void DrawLookCard(GlamourDetail entry, float width)
    {
        var scale = UiHelpers.Scale;
        var inner = width - (28f * scale);

        Theme.BeginPanel(width);

        var pieces = GlamSlots.All
            .Select(slot => (Slot: slot, Piece: entry.Slots.FirstOrDefault(s => s.Slot == (int)slot)))
            .Where(pair => pair.Piece is not null)
            .ToList();

        var summary = Summarise(pieces);
        var summaryWidth = ImGui.CalcTextSize(summary).X + (10f * scale);

        Theme.SectionHeader("The look", ruleWidth: MathF.Max(inner - summaryWidth, 60f * scale));

        var summaryPos = ImGui.GetCursorScreenPos()
            + new Vector2(inner - ImGui.CalcTextSize(summary).X, -ImGui.GetTextLineHeightWithSpacing());

        ImGui.GetWindowDrawList().AddText(summaryPos, ImGui.GetColorU32(Theme.TextDisabled), summary);

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (!Items.Ready || !Dyes.Ready)
        {
            ImGui.TextColored(Theme.TextDim, "Reading the item list...");
            Theme.EndPanel();
            return;
        }

        var gap = 8f * scale;
        var columns = inner >= (TileFloor * 2f * scale) + gap ? 2 : 1;
        var tileWidth = (inner - (gap * (columns - 1))) / columns;

        var layout = MeasureTiles(pieces, tileWidth);
        var missing = 0;

        for (var i = 0; i < pieces.Count; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine(0f, gap);

            if (DrawGearTile(pieces[i].Slot, pieces[i].Piece!, tileWidth, layout))
                missing++;
        }

        if (missing > 0)
        {
            ImGui.Dummy(new Vector2(0f, 8f * scale));
            ImGui.TextColored(
                Theme.Warning,
                missing == 1
                    ? "One piece can't be worn by your race or gender - it will show as smallclothes."
                    : $"{missing} pieces can't be worn by your race or gender - they will show as smallclothes.");
        }

        Theme.EndPanel();
    }

    /// The narrowest a tile is allowed to be before the grid gives up and stacks.
    private const float TileFloor = 168f;

    /// The pill column every tile in one glamour shares.
    private readonly record struct TileColumns(float Unit, float Total);

    /// Works out the shared pill column for a whole glamour, once, before any tile draws.
    private TileColumns MeasureTiles(List<(GlamSlot Slot, GlamourSlotDto? Piece)> pieces, float tileWidth)
    {
        var scale = UiHelpers.Scale;
        var pillGap = 4f * scale;

        var unit = 0f;
        var mostPills = 0;
        var widestSlot = 0f;

        foreach (var (slot, piece) in pieces)
        {
            if (piece is null)
                continue;

            widestSlot = MathF.Max(widestSlot, ImGui.CalcTextSize(slot.Label()).X);

            if (!Items.Ready || piece.ItemId == 0)
                continue;

            var pills = AvailabilityPills.For(Items.Resolve(piece.ItemId));
            mostPills = Math.Max(mostPills, pills.Count);

            foreach (var pill in pills)
                unit = MathF.Max(unit, AvailabilityPills.Width(pill.Label, scale));
        }

        if (mostPills == 0)
            return default;

        var total = (unit * mostPills) + (pillGap * (mostPills - 1));

        var textRoom = tileWidth - (8f * scale) - (34f * scale) - (9f * scale) - (8f * scale);

        return textRoom - total - (10f * scale) >= widestSlot
            ? new TileColumns(unit, total)
            : default;
    }

    /// The line at the right of the section rule: how many pieces, and how much of it is gettable.
    private string Summarise(List<(GlamSlot Slot, GlamourSlotDto? Piece)> pieces)
    {
        var parts = new List<string> { pieces.Count == 1 ? "1 piece" : $"{pieces.Count} pieces" };

        if (!Items.Ready)
            return parts[0];

        var resolved = pieces
            .Where(p => p.Piece is { ItemId: > 0 })
            .Select(p => Items.Resolve(p.Piece!.ItemId))
            .ToList();

        var market = resolved.Count(i => i.IsMarketable);
        var store = resolved.Count(i => i.IsStore);
        var dyed = pieces.Count(p => p.Piece is { } piece && (piece.Stain0 != 0 || piece.Stain1 != 0));

        if (market > 0) parts.Add($"{market} on the market");
        if (store > 0) parts.Add($"{store} from the store");
        if (dyed > 0) parts.Add($"{dyed} dyed");

        return string.Join(" · ", parts);
    }

    /// The action row: wear it, put your own look back, keep a copy.
    private void DrawDetailActions(GlamourDetail entry, float width)
    {
        var scale = UiHelpers.Scale;
        var height = 26f * scale;
        var wearing = tryingOn == entry.Id;

        var gap = 6f * scale;
        var barHeight = 32f * scale;

        var revertWidth = EchoButton.ContentSize("Revert", plugin.Fonts.Icon, FontAwesomeIcon.UndoAlt).X;
        var saveWidth = EchoButton.ContentSize("Save Outfit", plugin.Fonts.Icon, FontAwesomeIcon.Save).X;
        var tryWidth = width - revertWidth - saveWidth - (gap * 2f);

        var inline = tryWidth >= MathF.Max(revertWidth, 110f * scale);

        if (EchoButton.Draw(
                "##tryon", wearing ? "Wearing this" : "Try On",
                new Vector2(inline ? tryWidth : width, barHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Tshirt,
                selected: wearing,
                enabled: Items.Ready && !wearing,
                tooltip: "Puts this look on your character. Only you can see it.",
                primary: true))
            TryOn(entry);

        if (inline)
            ImGui.SameLine(0f, gap);
        else
            ImGui.Dummy(new Vector2(0f, gap));

        if (EchoButton.Draw(
                "##undotryon", "Revert", new Vector2(revertWidth, barHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.UndoAlt,
                enabled: beforeTryOn is not null,
                tooltip: "Goes back to whatever you were wearing before you started trying things on."))
            UndoTryOn();

        ImGui.SameLine(0f, gap);

        if (EchoButton.Draw(
                "##savecopy", "Save Outfit", new Vector2(saveWidth, barHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Save,
                enabled: Items.Ready && !plugin.Outfits.ReadOnly,
                tooltip: "Keeps a copy in the Dressing Room, where you can change it."))
            SaveCopy(entry);

        if (entry.Appearance is not null)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));

            if (EchoButton.Draw(
                    "##applylook", "Also use their appearance", new Vector2(0f, height),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.UserEdit,
                    enabled: Appearance.LayoutTrusted,
                    tooltip: Appearance.LayoutTrusted
                        ? "Changes your race, face, hair and colours to theirs. Undone by the button above."
                        : "The appearance editor is disabled on this build - see the Dressing Room."))
                ApplyAppearance(entry);
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        DrawSocialRow(entry, width);

        if (saveResult is { } message && ImGui.GetTime() - saveResultAt < 4d)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextColored(Theme.Good, message);
            ImGui.PopTextWrapPos();
        }
    }

    /// Vote, save, report - and, on your own entry, edit.
    private void DrawSocialRow(GlamourDetail entry, float width)
    {
        var scale = UiHelpers.Scale;
        var height = 26f * scale;

        if (EchoButton.Draw(
                "##vote", $"{entry.Votes}", new Vector2(0f, height),
                accentOverride: Theme.Vote,
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Heart,
                selected: entry.Voted,
                enabled: !entry.Mine && !busySocial,
                tooltip: entry.Mine
                    ? "You can't vote for your own."
                    : entry.Voted ? "Take your vote back." : "Vote for this glamour."))
            Vote(entry);

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##favourite", $"{entry.Favourites}", new Vector2(0f, height),
                accentOverride: Theme.Favourite,
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Star,
                selected: entry.Favourited,
                enabled: !busySocial,
                tooltip: entry.Favourited ? "Remove it from your saved list." : "Keep it on your profile."))
            Favourite(entry);

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##report", null, new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Flag,
                enabled: !entry.Mine,
                tooltip: entry.Mine ? "It's yours." : "Report this to moderation."))
            ImGui.OpenPopup(ReportPopupId);

        DrawReportPopup(entry.Id);

        if (!entry.Mine)
            return;

        ImGui.SameLine(0f, 6f * scale);

        if (EchoButton.Draw(
                "##editmine", "Edit", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Edit,
                tooltip: "Change the title, description, tags or screenshots."))
            Edit?.Invoke(entry);
    }

    private bool busySocial;

    private const string ReportPopupId = "##echoglamreport";

    private string reportReason = string.Empty;
    private string reportNote = string.Empty;
    private bool reporting;
    private string? reportDone;

    private void DrawReportPopup(string glamourId)
    {
        if (!ImGui.BeginPopup(ReportPopupId))
            return;

        var scale = UiHelpers.Scale;

        Theme.SectionHeader("Report this glamour", ruleWidth: 280f * scale);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        ImGui.TextColored(Theme.TextDim, "What's wrong with it?");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        foreach (var reason in plugin.Gallery.Settings.ReportReasons)
        {
            if (EchoButton.Draw(
                    $"##reason{reason}", reason, new Vector2(0f, 22f * scale),
                    selected: reportReason == reason))
                reportReason = reason;
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        ImGui.TextColored(Theme.TextDim, "Anything else worth knowing (optional)");
        UiHelpers.WrappingInputTextMultiline(
            "##reportnote", ref reportNote, 300, new Vector2(280f * scale, 52f * scale), ReportNoteWrap);

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (EchoButton.Draw(
                "##sendreport", reporting ? "Sending..." : "Send", new Vector2(0f, 24f * scale),
                enabled: !reporting && !string.IsNullOrEmpty(reportReason)))
            SendReport(glamourId);

        if (reportDone is { } message)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (280f * scale));
            ImGui.TextColored(Theme.Good, message);
            ImGui.PopTextWrapPos();
        }

        ImGui.EndPopup();
    }

    private void SendReport(string glamourId)
    {
        reporting = true;
        reportDone = null;

        var report = new ReportSubmission
        {
            OwnerKey = plugin.Gallery.OwnerKey,
            TargetId = glamourId,
            Reason = reportReason,
            Note = UiHelpers.Unwrap(reportNote, ReportNoteWrap).Trim(),
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await GalleryClient.ReportGlamourAsync(report, CancellationToken.None).ConfigureAwait(false);
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

    private void Vote(GlamourDetail entry)
    {
        busySocial = true;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await GalleryClient
                    .VoteAsync(entry.Id, plugin.Gallery.OwnerKey, CancellationToken.None)
                    .ConfigureAwait(false);

                entry.Voted = result.Active;
                entry.Votes = result.Count;
                Mirror(entry);
            }
            catch (Exception ex)
            {
                detailError = null;
                Plugin.Log.Warning($"[EchoGlam] Vote failed: {ex.Message}");
                saveResult = ex.Message;
                saveResultAt = ImGui.GetTime();
            }
            finally
            {
                busySocial = false;
            }
        });
    }

    private void Favourite(GlamourDetail entry)
    {
        busySocial = true;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await GalleryClient
                    .FavouriteAsync(entry.Id, plugin.Gallery.OwnerKey, CancellationToken.None)
                    .ConfigureAwait(false);

                entry.Favourited = result.Active;
                entry.Favourites = result.Count;
                Mirror(entry);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[EchoGlam] Favourite failed: {ex.Message}");
                saveResult = ex.Message;
                saveResultAt = ImGui.GetTime();
            }
            finally
            {
                busySocial = false;
            }
        });
    }

    /// Copies a changed count back onto the card in the grid behind, so going back does not show the old
    /// number until the next refresh.
    private void Mirror(GlamourDetail entry)
    {
        if (page?.Entries.FirstOrDefault(e => e.Id == entry.Id) is not { } card)
            return;

        card.Votes = entry.Votes;
        card.Voted = entry.Voted;
        card.Favourites = entry.Favourites;
        card.Favourited = entry.Favourited;
    }

    /// One row of the gear list.
    private bool DrawGearTile(GlamSlot slot, GlamourSlotDto piece, float width, TileColumns columns)
    {
        var scale = UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();
        var iconSize = 34f * scale;
        var pad = 8f * scale;
        var height = MathF.Max(iconSize + (pad * 2f), (ImGui.GetTextLineHeight() * 2f) + (pad * 2.6f));

        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));

        var item = piece.ItemId == 0 ? GlamItem.None : Items.Resolve(piece.ItemId);
        var blocked = piece.ItemId != 0 && !plugin.CanWear(item);
        var hovered = ImGui.IsMouseHoveringRect(pos, pos + new Vector2(width, height));

        drawList.AddRectFilled(
            pos, pos + new Vector2(width, height),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.055f : 0.03f)), 8f * scale);

        drawList.AddRect(
            pos, pos + new Vector2(width, height),
            ImGui.GetColorU32(blocked ? Theme.Warning with { W = 0.5f } : new Vector4(1f, 1f, 1f, hovered ? 0.14f : 0.07f)),
            8f * scale, ImDrawFlags.None, 1f * scale);

        var iconMin = new Vector2(pos.X + pad, pos.Y + ((height - iconSize) / 2f));
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        var iconRound = 6f * scale;

        drawList.AddRectFilled(iconMin, iconMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.30f)), iconRound);

        if (item.IconId != 0)
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(item.IconId)).GetWrapOrDefault();
            if (texture != null)
            {
                drawList.PushClipRect(iconMin, iconMax, true);
                drawList.AddImage(
                    texture.Handle, iconMin, iconMax,
                    Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, blocked ? 0.45f : 1f)));
                drawList.PopClipRect();
            }
        }

        drawList.AddRect(
            iconMin, iconMax,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.16f : 0.09f)),
            iconRound, ImDrawFlags.None, 1f * scale);

        var dyeCount = piece.Stain1 != 0 ? 2 : piece.Stain0 != 0 ? 1 : 0;
        var dyeHovered = false;

        if (dyeCount > 0)
        {
            var chip = 11f * scale;
            var chipGap = 2f * scale;
            var inset = 2f * scale;

            var x = iconMax.X - inset - (dyeCount * chip) - ((dyeCount - 1) * chipGap);
            var top = iconMax.Y - inset - chip;

            drawList.AddRectFilled(
                new Vector2(x - inset, top - inset), new Vector2(iconMax.X, iconMax.Y),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)), iconRound, ImDrawFlags.RoundCornersBottomRight);

            for (var i = 0; i < dyeCount; i++)
            {
                var dye = Dyes.Resolve(i == 0 ? piece.Stain0 : piece.Stain1);
                var min = new Vector2(x, top);
                var over = ImGui.IsMouseHoveringRect(min, min + new Vector2(chip, chip));

                UiHelpers.DrawSwatch(drawList, min, chip, dye.Colour, over ? Theme.Accent : null);

                if (over)
                {
                    UiHelpers.WrappedTooltip(dyeCount > 1 ? $"{dye.Name} (dye {i + 1})" : dye.Name);
                    dyeHovered = true;
                }

                x += chip + chipGap;
            }
        }

        var textX = pos.X + pad + iconSize + (9f * scale);
        var rightEdge = pos.X + width - pad;
        var lineOne = pos.Y + (height / 2f) - ImGui.GetTextLineHeight() - (1f * scale);
        var lineTwo = pos.Y + (height / 2f) + (1f * scale);

        var pillGap = 4f * scale;
        var pills = item.IsNone || columns.Total <= 0f ? [] : AvailabilityPills.For(item);

        var pillsSpan = pills.Count == 0
            ? 0f
            : (columns.Unit * pills.Count) + (pillGap * (pills.Count - 1));

        var name = piece.ItemId == 0 ? "Nothing shown" : item.IsNone ? $"Item #{piece.ItemId}" : item.Name;

        drawList.AddText(
            new Vector2(textX, lineOne),
            ImGui.GetColorU32(blocked ? Theme.Warning : piece.ItemId == 0 ? Theme.TextDisabled : Theme.Text),
            Truncate(name, rightEdge - textX));

        drawList.AddText(
            new Vector2(textX, lineTwo),
            ImGui.GetColorU32(Theme.TextDim),
            Truncate(slot.Label(), MathF.Max(rightEdge - textX - columns.Total - (10f * scale), 20f * scale)));

        var pillHovered = false;

        if (pills.Count > 0)
        {
            var pillHeight = AvailabilityPills.Height(scale);

            var x = rightEdge - pillsSpan;
            var top = lineTwo + (ImGui.GetTextLineHeight() / 2f) - (pillHeight / 2f);

            foreach (var pill in pills)
            {
                var min = new Vector2(x, top);

                if (AvailabilityPills.Draw(drawList, min, pill, scale, columns.Unit))
                {
                    pillHovered = true;
                    DrawPillTooltip(pill, item);
                }

                x += columns.Unit + pillGap;
            }
        }

        if (!dyeHovered && !pillHovered && hovered)
        {
            if (blocked)
                UiHelpers.WrappedTooltip("Your race or gender can't wear this piece. It will show as smallclothes.");
            else if (!item.IsNone && ImGui.CalcTextSize(name).X > rightEdge - textX)
                UiHelpers.WrappedTooltip(name);
        }

        return blocked;
    }

    /// What a pill says when you stop on it.
    private void DrawPillTooltip(AvailabilityPills.Pill pill, GlamItem item)
    {
        if (!pill.IsMarket)
        {
            UiHelpers.WrappedTooltip(pill.Tooltip);
            return;
        }

        var world = GalleryState.Character.World;

        if (string.IsNullOrEmpty(world))
        {
            UiHelpers.WrappedTooltip(pill.Tooltip);
            return;
        }

        var quote = plugin.Prices.Ask(item.ItemId, world);

        var line = quote.State switch
        {
            MarketState.Listed => $"{MarketPrices.Gil(quote.Cheapest)} on {world}",
            MarketState.Empty => $"None listed on {world} right now",
            MarketState.Loading => "Checking the market board...",
            MarketState.Unavailable => "No price for this on your world",
            _ => pill.Tooltip,
        };

        if (quote.State == MarketState.Listed && quote.Updated is { } updated)
            line += $"\nPrices last seen {MarketPrices.Age(updated)}";

        UiHelpers.WrappedTooltip(line);
    }


    private void TryOn(GlamourDetail entry)
    {
        beforeTryOn ??= new Dictionary<GlamSlot, GlamEntry>(Wardrobe.Current);
        beforeAppearance ??= Wardrobe.CurrentAppearance;

        Wardrobe.Wear(ToLook(entry));
        tryingOn = entry.Id;
    }

    private void UndoTryOn()
    {
        if (beforeTryOn is not { } previous)
            return;

        if (previous.Count == 0)
            Wardrobe.RevertAll();
        else
            Wardrobe.Wear(previous);

        if (beforeAppearance is { } appearance)
            Wardrobe.SetAppearance(appearance);
        else
            Wardrobe.ClearAppearance();

        beforeTryOn = null;
        beforeAppearance = null;
        tryingOn = null;
    }

    private void ApplyAppearance(GlamourDetail entry)
    {
        if (entry.Appearance is null || CustomizeSet.FromBase64(entry.Appearance) is not { } set)
            return;

        beforeTryOn ??= new Dictionary<GlamSlot, GlamEntry>(Wardrobe.Current);
        beforeAppearance ??= Wardrobe.CurrentAppearance;

        Wardrobe.SetAppearance(set);
    }

    private void SaveCopy(GlamourDetail entry)
    {
        var name = UniqueName(entry.Title);

        plugin.Outfits.Save(
            name, ToLook(entry),
            entry.Appearance is null ? null : CustomizeSet.FromBase64(entry.Appearance));

        saveResult = $"Saved as \"{name}\" in the Dressing Room.";
        saveResultAt = ImGui.GetTime();
    }

    /// A name that isn't already taken.
    private string UniqueName(string title)
    {
        var trimmed = string.IsNullOrWhiteSpace(title) ? "Gallery glamour" : title.Trim();

        if (!plugin.Outfits.Exists(trimmed))
            return trimmed;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{trimmed} ({i})";
            if (!plugin.Outfits.Exists(candidate))
                return candidate;
        }

        return trimmed;
    }

    private static Dictionary<GlamSlot, GlamEntry> ToLook(GlamourDetail entry)
    {
        var look = new Dictionary<GlamSlot, GlamEntry>();

        foreach (var piece in entry.Slots)
        {
            if (piece.Slot is < 0 or > 11)
                continue;

            look[(GlamSlot)piece.Slot] = new GlamEntry(piece.ItemId, piece.Stain0, piece.Stain1);
        }

        return look;
    }


    private void Refresh()
    {
        browsing?.Cancel();
        browsing?.Dispose();

        var token = new CancellationTokenSource();
        browsing = token;

        loading = true;
        error = null;

        var asked = query.Clone();
        var owner = plugin.Configuration.EnsureOwnerKey();

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await GalleryClient.BrowseAsync(asked, owner, token.Token).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                    return;

                page = result;
                error = null;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                    return;

                error = ex.Message;
                Plugin.Log.Warning($"[EchoGlam] Gallery browse failed: {ex.Message}");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    loading = false;
            }
        }, token.Token);
    }

    /// Where Back goes, when it is not the grid.
    private Action? returnTo;

    private void Open(string id, Action? back = null)
    {
        detail = null;
        detailError = null;
        detailLoading = true;
        detailImage = 0;
        StopSlide();
        returnTo = back;

        var owner = plugin.Configuration.EnsureOwnerKey();

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await GalleryClient.DetailAsync(id, owner, CancellationToken.None).ConfigureAwait(false);

                if (result is null)
                {
                    detailError = "It isn't on the board any more.";

                    Refresh();
                    return;
                }

                detail = result;
            }
            catch (Exception ex)
            {
                detailError = ex.Message;
                Plugin.Log.Warning($"[EchoGlam] Gallery detail failed: {ex.Message}");
            }
            finally
            {
                detailLoading = false;
            }
        });
    }

    private void Close()
    {
        detail = null;
        detailError = null;
        detailLoading = false;
        detailImage = 0;
        StopSlide();

        var back = returnTo;
        returnTo = null;
        back?.Invoke();
    }
}
