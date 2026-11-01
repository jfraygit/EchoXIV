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
using EchoGlam.UI.Foundation;

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

    /// This month's most-liked, for the showcase.
    private GlamourPage? showcase;

    private bool showcaseAsked;

    /// Whether the showcase is folded away.
    private bool showcaseOpen = true;

    /// Whether the filter column is out.
    private bool filtersOpen;

    private GlamourDetail? detail;
    private bool detailLoading;
    private string? detailError;
    private int detailImage;

    /// How wide the report dialog is, and what text inside it wraps to.
    private static float ReportWidth => Modal.Width(ModalSize.Small);

    private static float ReportNoteWrap => Modal.WrapWidth(ModalSize.Small);

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

            var imageLeft = pos.X + offset.X;
            var top = pos.Y + offset.Y;

            var sliceLeft = MathF.Max(imageLeft, pos.X + MathF.Max(0f, dx));
            var sliceRight = MathF.Min(imageLeft + drawn.X, pos.X + width + MathF.Min(0f, dx));

            if (sliceRight <= sliceLeft)
                return;

            var corners = dx == 0f ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersNone;

            drawList.AddImageRounded(
                texture.Handle,
                new Vector2(sliceLeft, top),
                new Vector2(sliceRight, top + drawn.Y),
                new Vector2((sliceLeft - imageLeft) / drawn.X, 0f),
                new Vector2((sliceRight - imageLeft) / drawn.X, 1f),
                ImGui.GetColorU32(Vector4.One), rounding, corners);

            return;
        }

        var label = images.Failed(url) ? "That screenshot couldn't be loaded" : "Loading...";
        var measured = ImGui.CalcTextSize(label);

        drawList.AddText(
            pos + ((new Vector2(width, height) - measured) / 2f) + new Vector2(dx, 0f),
            ImGui.GetColorU32(Theme.TextDisabled), label);
    }

    /// Paints out everything inside a rectangle but outside its rounded shape - the four little "ears"
    /// between a square corner and the arc that is supposed to replace it.
    private static void MaskCorners(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float rounding)
    {
        if (rounding <= 0f)
            return;

        var half = rounding / 2f;

        drawList.AddRect(
            pos - new Vector2(half, half),
            pos + size + new Vector2(half, half),
            Col.U32(Theme.Background),
            rounding + half,
            ImDrawFlags.RoundCornersAll,
            rounding);
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

    /// The last thing that happened on the detail page, and WHAT IT MEANT.
    private Message saveResult = Message.None;

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
        plugin.EnsureCatalogues();
        plugin.Gallery.EnsureSettings();

        if (page is null && !loading && error is null)
            Refresh();

        if (!showcaseAsked)
        {
            showcaseAsked = true;
            RefreshShowcase();
        }

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


    /// The board.
    private void DrawBrowse()
    {
        DrawToolbar();
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        var available = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();

        var out01 = Motion.Toward("galleryfilters", filtersOpen, Anim.SelectSpeed);

        var columnWidth = FilterColumnDesign * UiHelpers.Scale * out01;
        var gap = Sp.Xxl * out01;

        if (columnWidth > 4f)
        {
            if (ImGui.BeginChild(
                    "##galleryfilters", new Vector2(columnWidth, available.Y), false,
                    ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                DrawFilterColumn(
                    (FilterColumnDesign * UiHelpers.Scale) - ImGui.GetStyle().ScrollbarSize);
            }

            ImGui.EndChild();

            var rule = origin.X + columnWidth + (gap / 2f);

            ImGui.GetWindowDrawList().AddRectFilled(
                new Vector2(rule, origin.Y),
                new Vector2(rule + Stroke.Hair, origin.Y + available.Y),
                Col.U32(Col.EdgeRest with { W = 0.5f * out01 }));

            ImGui.SameLine(0f, gap);
        }

        var boardWidth = MathF.Max(240f * UiHelpers.Scale, available.X - columnWidth - gap);

        if (ImGui.BeginChild(
                "##gallerygrid", new Vector2(boardWidth, available.Y), false,
                ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            if (returnToTop)
            {
                returnToTop = false;
                scrollingToTop = ImGui.GetScrollY() > 1f;
            }

            if (scrollingToTop)
            {
                if (MathF.Abs(ImGui.GetIO().MouseWheel) > 0f)
                {
                    scrollingToTop = false;
                }
                else
                {
                    var next = UiHelpers.Lerp(
                        ImGui.GetScrollY(), 0f, ScrollHomeSpeed, Anim.Step(ImGui.GetIO().DeltaTime));

                    if (next < 0.5f)
                    {
                        next = 0f;
                        scrollingToTop = false;
                    }

                    ImGui.SetScrollY(next);
                }
            }

            if (error is { } message)
            {
                if (Notice.Fail(ImGui.GetContentRegionAvail().X, "The gallery couldn't be reached", message, "Try again"))
                    Refresh();
            }
            else if (page is null)
            {
                Notice.Busy(ImGui.GetContentRegionAvail().X, "Loading the gallery", "Fetching the newest glamours.");
            }
            else
            {
                DrawShowcase(ImGui.GetContentRegionAvail().X);

                if (page.Entries.Count == 0)
                    DrawEmpty();
                else
                    DrawGrid(page.Entries);

                if (page is { Total: > 0 })
                {
                    ImGui.Dummy(new Vector2(0f, Sp.Xl));
                    DrawPager();
                    ImGui.Dummy(new Vector2(0f, Sp.Lg));
                }
            }
        }

        ImGui.EndChild();
    }

    /// Width of the filter column, in design units.
    private const float FilterColumnDesign = 232f;

    /// Set when the board's contents change under a scroll that is no longer where it was left.
    private bool returnToTop;

    /// Whether the board is currently travelling back to its first row.
    private bool scrollingToTop;

    /// How fast the board travels back up.
    private const float ScrollHomeSpeed = 16f;

    /// Asks the board to return to its first row.
    private void ReturnToTop() => returnToTop = true;

    /// Everything that decides what the board shows, on ONE LINE.
    private void DrawToolbar()
    {
        var scale = UiHelpers.Scale;

        var height = Metrics.RowTall;

        var labels = new[] { SortLabel(GlamourSort.All[0]), SortLabel(GlamourSort.All[1]), SortLabel(GlamourSort.All[2]), SortLabel(GlamourSort.All[3]) };

        var active = ActiveFilterCount();
        var filtersLabel = active > 0 ? $"Filters ({active})" : "Filters";

        var segmentWidth = EchoSegment.Width(labels);
        var filtersWidth = EchoButton.ContentSize(filtersLabel, plugin.Fonts.Icon, FontAwesomeIcon.SlidersH).X;

        var publishWidth = EchoButton.ContentSize("Publish", plugin.Fonts.Icon, FontAwesomeIcon.CloudUploadAlt).X;

        var reserved = segmentWidth + filtersWidth + publishWidth + (Sp.Lg * 3f) + EchoButton.GlowReach;

        var searchWidth = MathF.Max(
            160f * scale, ImGui.GetContentRegionAvail().X - reserved);

        var current = Array.IndexOf(GlamourSort.All, query.Sort);
        var chosen = EchoSegment.Draw("##gallerysort", labels, current, height);

        if (chosen >= 0 && chosen != current)
        {
            query.Sort = GlamourSort.All[chosen];
            query.Page = 0;
            ReturnToTop();
            Refresh();
        }

        ImGui.SameLine(0f, Sp.Lg);

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
            ReturnToTop();
            Refresh();
        }

        ImGui.SameLine(0f, Sp.Lg);

        if (EchoButton.Draw(
                "##galleryfilterstoggle", filtersLabel, new Vector2(filtersWidth, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.SlidersH,
                selected: filtersOpen || active > 0))
            filtersOpen = !filtersOpen;

        if (EchoButton.RightClicked() && active > 0)
            ClearFacets();

        ImGui.SameLine(0f, Sp.Lg);

        if (EchoButton.Draw(
                "##publishlook", "Publish", new Vector2(publishWidth, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.CloudUploadAlt,
                enabled: plugin.Gallery.CanPublish,

                tooltip: plugin.Gallery.CanPublish ? null : "The gallery isn't accepting submissions yet.",
                primary: plugin.Gallery.CanPublish))
            Publish?.Invoke();
    }

    /// How many of the three facets are set.
    private int ActiveFilterCount()
    {
        var count = 0;

        if (query.JobId is not null) count++;
        if (query.Race is not null || query.Sex is not null) count++;
        if (query.Tag is not null) count++;

        return count;
    }

    /// Clears the three facets and LEAVES THE SEARCH ALONE, which is what distinguishes it from ClearFilters
    /// below.
    private void ClearFacets()
    {
        query.JobId = null;
        query.Race = null;
        query.Sex = null;
        query.Tag = null;
        query.Page = 0;
        Refresh();
    }

    /// The facets, as a column rather than three popups.
    private void DrawFilterColumn(float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var active = ActiveFilterCount();

        var inner = width;

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, ImGui.GetCursorScreenPos(), "FILTERS",
                Col.U32(Col.TextMuted), 1.8f, inner);
        }

        ImGui.Dummy(new Vector2(inner, Text.CaptionSize * 1.8f));

        if (active > 0)
        {
            if (EchoButton.Draw(
                    "##clearfilters", $"Clear {active}", new Vector2(inner, Metrics.Row),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.TimesCircle))
                ClearFacets();

            ImGui.Dummy(new Vector2(0f, Sp.Lg));
        }

        Theme.SectionHeader("Job", ruleWidth: inner);
        ImGui.Dummy(new Vector2(0f, Sp.Sm));
        DrawJobFilterGrid(inner);

        ImGui.Dummy(new Vector2(0f, Sp.Xl));
        Theme.SectionHeader("Race", ruleWidth: inner);
        ImGui.Dummy(new Vector2(0f, Sp.Sm));
        DrawRaceFilterList(inner);

        ImGui.Dummy(new Vector2(0f, Sp.Xl));
        Theme.SectionHeader("Tag", ruleWidth: inner);
        ImGui.Dummy(new Vector2(0f, Sp.Sm));
        DrawTagFilterChips(inner);

        ImGui.Dummy(new Vector2(0f, Sp.Xl));
    }

    /// Every job as its own icon, six across.
    private void DrawJobFilterGrid(float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var gap = Sp.Xs;
        var size = MathF.Floor((width - (gap * 5f)) / 6f);
        var column = 0;

        if (EchoButton.Draw(
                "##jobany", "Any job", new Vector2(width, Metrics.Row),
                selected: query.JobId is null))
        {
            query.JobId = null;
            query.Page = 0;
            Refresh();
        }

        ImGui.Dummy(new Vector2(0f, Sp.Sm));

        foreach (var job in JobList.Groups.SelectMany(JobList.InGroup))
        {
            if (column > 0 && column % 6 != 0)
                ImGui.SameLine(0f, gap);

            var min = ImGui.GetCursorScreenPos();
            var selected = query.JobId == job.Id;

            if (ImGui.InvisibleButton($"##filterjob{job.Id}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft))
            {
                query.JobId = selected ? null : job.Id;
                query.Page = 0;
                Refresh();
            }

            var hovered = ImGui.IsItemHovered();

            if (hovered)
                UiHelpers.WrappedTooltip($"{job.Name} ({job.Abbreviation})");

            if (selected || hovered)
            {
                drawList.AddRectFilled(
                    min, min + new Vector2(size, size),
                    Col.U32(selected ? Col.SelRest : Col.CtlHover), Rad.Chip);
            }

            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrDefault();

            if (texture != null)
            {
                var inset = Stroke.Hair;

                drawList.AddImage(
                    texture.Handle, min + new Vector2(inset, inset),
                    min + new Vector2(size - inset, size - inset), Vector2.Zero, Vector2.One,
                    Col.U32(new Vector4(1f, 1f, 1f, selected ? 1f : hovered ? 0.95f : 0.55f)));
            }

            if (selected)
            {
                drawList.AddRect(
                    min, min + new Vector2(size, size), Col.U32(Theme.Accent),
                    Rad.Chip, ImDrawFlags.None, Stroke.Edge);
            }

            column++;
        }
    }

    /// The eight races, each as one row that carries both sexes.
    private void DrawRaceFilterList(float width)
    {
        if (EchoButton.Draw(
                "##raceany", "Any race", new Vector2(width, Metrics.Row),
                selected: query.Race is null && query.Sex is null))
        {
            query.Race = null;
            query.Sex = null;
            query.Page = 0;
            Refresh();
        }

        ImGui.Dummy(new Vector2(0f, Sp.Sm));

        var mark = 24f * UiHelpers.Scale;
        var nameWidth = MathF.Max(70f * UiHelpers.Scale, width - ((mark + Sp.Xs) * 2f));

        var drawList = ImGui.GetWindowDrawList();

        for (byte race = 1; race <= 8; race++)
        {
            var chosen = query.Race == race;

            var rowOrigin = ImGui.GetCursorScreenPos();

            if (EchoButton.Draw(
                    $"##race{race}", RaceNames.Race(race, 0), new Vector2(nameWidth, Metrics.Row),
                    selected: chosen && query.Sex is null))
            {
                query.Race = chosen && query.Sex is null ? null : race;
                query.Sex = null;
                query.Page = 0;
                ReturnToTop();
                Refresh();
            }

            for (byte sex = 0; sex <= 1; sex++)
            {
                ImGui.SameLine(0f, Sp.Xs);

                var set = chosen && query.Sex == sex;

                var min = new Vector2(
                    rowOrigin.X + nameWidth + Sp.Xs + (sex * (mark + Sp.Xs)),
                    rowOrigin.Y + ((Metrics.Row - mark) / 2f));

                ImGui.SetCursorScreenPos(min);

                var hit = ImGui.InvisibleButton(
                    $"##race{race}s{sex}", new Vector2(mark, mark), ImGuiButtonFlags.MouseButtonLeft);

                var hovered = ImGui.IsItemHovered();

                if (hovered)
                    UiHelpers.WrappedTooltip(RaceNames.Race(race, sex));

                if (hit)
                {
                    query.Race = set ? null : race;
                    query.Sex = set ? null : sex;
                    query.Page = 0;
                    ReturnToTop();
                    Refresh();
                }

                var max = min + new Vector2(mark, mark);

                drawList.AddRectFilled(
                    min, max,
                    Col.U32(set ? Col.SelRest : hovered ? Col.CtlHover : Col.Raised),
                    Rad.Chip);

                drawList.AddRect(
                    min, max,
                    Col.U32(set ? Theme.Accent : Col.EdgeRest with { W = 0.5f }),
                    Rad.Chip, ImDrawFlags.None, set ? Stroke.Edge : Stroke.Hair);

                using (Text.PushCaption())
                {
                    var glyph = sex == 0 ? "M" : "F";
                    var size = ImGui.CalcTextSize(glyph);

                    drawList.AddText(
                        min + ((new Vector2(mark, mark) - size) / 2f),
                        Col.U32(set ? Theme.Accent : hovered ? Col.Text : Col.TextDim),
                        glyph);
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(rowOrigin.X, rowOrigin.Y + Metrics.Row + Sp.Xxs));
        }

        ImGui.Dummy(new Vector2(width, 0f));
    }

    /// The curated vocabulary, as chips that wrap.
    private void DrawTagFilterChips(float width)
    {
        var tags = plugin.Gallery.Settings?.Tags ?? GalleryTags.All;

        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (query.Tag is { } current)
            chosen.Add(current);

        var origin = ImGui.GetCursorScreenPos();

        if (tagFilter.Field(origin, width, chosen, tags, 1, plugin.Fonts.Icon))
        {
            query.Tag = chosen.FirstOrDefault();
            query.Page = 0;
            Refresh();
        }

        ImGui.Dummy(new Vector2(width, TagPicker.FieldHeight));
    }

    /// The filter column's own picker, so its band and its search survive the panel closing and do not follow
    /// the publish form's around.
    private readonly TagPicker tagFilter = new("galleryfilter", TagPicker.Mode.One);

    private static string SortLabel(string sort) => sort switch
    {
        GlamourSort.Top => "Top",
        GlamourSort.Newest => "Newest",
        GlamourSort.Favourites => "Most Saved",
        _ => "Trending",
    };



    /// The card grid.
    private void DrawShowcase(float width)
    {
        if (showcase is not { Entries.Count: > 0 } top)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var headRow = Metrics.TouchMin;

        DrawShowcaseHead(drawList, origin, width, headRow);

        if (!showcaseOpen)
        {
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, headRow + Sp.Xl));
            return;
        }

        var gap = Sp.Lg;
        var count = Math.Min(ShowcaseCount, top.Entries.Count);
        var card = MathF.Round(188f * UiHelpers.Scale);
        var height = Game.ImageProcessor.HeightFor(card);
        var stride = card + gap;
        var top0 = origin.Y + headRow + Sp.Md;

        var stripMin = new Vector2(origin.X, top0);
        var stripMax = new Vector2(origin.X + width, top0 + height);

        var loop = stride * count;

        var fits = (count * stride) - gap <= width;

        if (fits)
        {
            showcaseOffset = 0f;
            showcaseFling = 0f;
            showcaseDragging = false;
        }

        var delta = ImGui.GetIO().DeltaTime;
        var step = Anim.Step(delta);

        if (!fits && !showcaseDragging && !ImGui.IsMouseHoveringRect(stripMin, stripMax))
            showcaseOffset += ShowcaseSpeed * UiHelpers.Scale * step;

        if (!fits && !showcaseDragging)
        {
            if (MathF.Abs(showcaseFling) > FlingFloorDesign * UiHelpers.Scale)
            {
                showcaseOffset += showcaseFling * step;
                showcaseFling *= MathF.Exp(-FlingDecay * step);
            }
            else
            {
                showcaseFling = 0f;
            }
        }

        if (showcaseDragging)
        {
            var moved = ImGui.GetIO().MouseDelta.X;

            showcaseOffset -= moved;
            showcaseDragMoved += MathF.Abs(moved);

            showcaseHeldFor += delta;

            showcaseFling = 0f;

            if (delta > 0f)
                showcaseThrow = UiHelpers.Lerp(showcaseThrow, -moved / delta, ThrowSmoothing, delta);
        }

        showcaseOffset = ((showcaseOffset % loop) + loop) % loop;

        var wasDrag = showcaseDragMoved;
        var wasHeld = showcaseHeldFor;

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (showcaseDragging && showcaseDragMoved >= ClickSlackDesign * UiHelpers.Scale)
            {
                var cap = FlingMaxDesign * UiHelpers.Scale;
                showcaseFling = Math.Clamp(showcaseThrow, -cap, cap);
            }

            showcaseDragging = false;
            showcaseDragMoved = 0f;
            showcaseHeldFor = 0f;
            showcaseThrow = 0f;
        }

        drawList.PushClipRect(stripMin, stripMax, true);

        var first = (int)MathF.Floor(showcaseOffset / stride);
        var shift = showcaseOffset - (first * stride);

        var slots = Math.Min(count - 1, (int)MathF.Ceiling(width / stride) + 1);

        var content = (count * stride) - gap;
        var lead = fits ? MathF.Max(0f, (width - content) / 2f) : 0f;

        for (var slot = 0; slot <= slots; slot++)
        {
            var index = (((first + slot) % count) + count) % count;
            var entry = top.Entries[index];

            var min = new Vector2(origin.X + lead + (slot * stride) - shift, top0);
            var size = new Vector2(card, height);

            var hitMin = new Vector2(min.X - (gap / 2f), min.Y);

            ImGui.SetCursorScreenPos(hitMin);

            if (ImGui.InvisibleButton(
                    $"##showcase{slot}", new Vector2(stride, height), ImGuiButtonFlags.MouseButtonLeft)
                && wasDrag < ClickSlackDesign * UiHelpers.Scale
                && wasHeld < TapSeconds)
            {
                Open(entry.Id);
            }

            if (!fits && ImGui.IsItemActive())
                showcaseDragging = true;

            var hovered = ImGui.IsItemHovered()
                          && ImGui.IsMouseHoveringRect(min, min + size);

            if (!fits && (ImGui.IsItemHovered() || ImGui.IsItemActive()))
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

            Elev.Shadow(drawList, min, min + size, Elev.Float, Rad.Card, hovered ? 1.9f : 1f);
            drawList.AddRectFilled(min, min + size, Col.U32(Col.Surface), Rad.Card);

            GlamourCard.Paint(drawList, images, plugin.Fonts.Icon, entry, min, size, hovered);

            DrawRank(drawList, min, index + 1);

            drawList.AddRect(
                min, min + size,
                Col.U32(hovered ? Theme.Accent : Col.EdgeRest),
                Rad.Card, ImDrawFlags.None, hovered ? Stroke.Focus : Stroke.Edge);
        }

        drawList.PopClipRect();

        var fade = Sp.Xxxl;
        var solid = Col.U32(Theme.Background);
        var clear = Col.U32(Theme.Background with { W = 0f });

        drawList.AddRectFilledMultiColor(
            stripMin, new Vector2(stripMin.X + fade, stripMax.Y), solid, clear, clear, solid);

        drawList.AddRectFilledMultiColor(
            new Vector2(stripMax.X - fade, stripMin.Y), stripMax, clear, solid, solid, clear);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, headRow + Sp.Md + height + Sp.Xxl));
    }

    /// The showcase's heading, its month, and the control that folds it.
    private void DrawShowcaseHead(ImDrawListPtr drawList, Vector2 origin, float width, float headRow)
    {
        var centre = origin.Y + (headRow / 2f);

        var textEnd = origin.X;

        using (Text.PushCaption())
        {
            var y = centre - (Text.CaptionSize * 0.575f);

            var used = Text.Tracked(
                drawList, new Vector2(origin.X, y), "THIS MONTH'S TOP",
                Col.U32(Theme.Accent), 1.8f, width);

            var month = DateTime.UtcNow.ToString("MMMM").ToUpperInvariant();

            var monthUsed = Text.Tracked(
                drawList, new Vector2(origin.X + used + Sp.Lg, y), month,
                Col.U32(Col.TextMuted), 1.8f, width);

            textEnd = origin.X + used + Sp.Lg + monthUsed;
        }

        var ruleLeft = textEnd + Sp.Xl;
        var ruleRight = origin.X + width - Metrics.TouchMin - Sp.Lg;

        if (ruleRight > ruleLeft)
        {
            drawList.AddRectFilled(
                new Vector2(ruleLeft, centre),
                new Vector2(ruleRight, centre + Stroke.Hair),
                Col.U32(Col.EdgeRest with { W = 0.5f }));
        }

        var at = new Vector2(origin.X + width - Metrics.TouchMin, origin.Y);
        ImGui.SetCursorScreenPos(at);

        ImGui.InvisibleButton(
            "##showcasefold", new Vector2(Metrics.TouchMin, headRow), ImGuiButtonFlags.MouseButtonLeft);

        var hovered = ImGui.IsItemHovered();

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            showcaseOpen = !showcaseOpen;

        var warm = Motion.Toward("showcasefold", hovered, 20f);

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList,
                showcaseOpen ? FontAwesomeIcon.AngleUp : FontAwesomeIcon.AngleDown,
                at + new Vector2(Metrics.TouchMin / 2f, headRow / 2f),
                Col.U32(Vector4.Lerp(Col.TextDim, Theme.Accent, warm)));
        }
    }

    /// A big numeral in the corner of a showcase card.
    private void DrawRank(ImDrawListPtr drawList, Vector2 min, int rank)
    {
        var at = min + new Vector2(Sp.Lg, Sp.Md);

        using (Text.PushDisplay())
        {
            var label = rank.ToString();

            drawList.AddText(at + new Vector2(Stroke.Mark, Stroke.Mark), Col.U32(new Vector4(0f, 0f, 0f, 0.65f)), label);
            drawList.AddText(at, Col.U32(Vector4.One), label);
        }
    }

    private void DrawGrid(List<GlamourSummary> entries) =>
        CardGrid.Draw(entries, entry => entry.Id, DrawCard, stripDesign: 0f);

    /// One card's contents.
    private void DrawCard(GlamourSummary entry, CardSlot slot)
    {
        GlamourCard.Draw(ImGui.GetWindowDrawList(), images, plugin.Fonts.Icon, entry, slot);

        if (slot.Clicked)
            Open(entry.Id);
    }


    /// Numbered pages with a window around the one you are on.
    private void DrawPager()
    {
        if (page is not { } current)
            return;

        var pages = Math.Max(1, (int)Math.Ceiling(current.Total / (double)Math.Max(1, current.PageSize)));

        if (pages <= 1)
            return;

        var height = Metrics.Row;
        var step = MathF.Round(30f * UiHelpers.Scale);
        var gap = Sp.Xs;

        var numbers = PageWindow(current.Page, pages);

        var run = (step + gap) * 2f;

        foreach (var number in numbers)
            run += (number < 0 ? step * 0.7f : step) + gap;

        var start = MathF.Max(0f, (ImGui.GetContentRegionAvail().X - run) / 2f);
        var origin = ImGui.GetCursorScreenPos() + new Vector2(start, 0f);
        var x = origin.X;

        if (DrawPageStep("##pageback", FontAwesomeIcon.ChevronLeft, origin with { X = x }, step, height,
                enabled: current.Page > 0, tip: "Previous page"))
        {
            GoToPage(current.Page - 1);
        }

        x += step + gap;

        foreach (var number in numbers)
        {
            if (number < 0)
            {
                using (Text.PushCaption())
                {
                    ImGui.GetWindowDrawList().AddText(
                        new Vector2(x + (step * 0.2f), origin.Y + ((height - Text.CaptionSize) / 2f)),
                        Col.U32(Col.TextMuted), "...");
                }

                x += (step * 0.7f) + gap;
                continue;
            }

            if (DrawPageNumber(number, current.Page, origin with { X = x }, step, height))
                GoToPage(number);

            x += step + gap;
        }

        if (DrawPageStep("##pagenext", FontAwesomeIcon.ChevronRight, origin with { X = x }, step, height,
                enabled: current.Page + 1 < pages, tip: "Next page"))
        {
            GoToPage(current.Page + 1);
        }

        ImGui.SetCursorScreenPos(origin with { X = origin.X - start });
        ImGui.Dummy(new Vector2(run + start, height));

        using (Text.PushCaption())
        {
            var note = $"{current.Total} glamour{(current.Total == 1 ? "" : "s")}";
            var noteWidth = Text.Width(note);

            ImGui.GetWindowDrawList().AddText(
                new Vector2(
                    origin.X - start + ((ImGui.GetContentRegionAvail().X - noteWidth) / 2f),
                    origin.Y + height + Sp.Sm),
                Col.U32(Col.TextMuted), note);
        }

        ImGui.Dummy(new Vector2(0f, Text.CaptionSize + Sp.Sm));
    }

    private void GoToPage(int index)
    {
        query.Page = Math.Max(0, index);
        ReturnToTop();
        Refresh();
    }

    /// Which page numbers to show, with -1 standing for a break.
    private static List<int> PageWindow(int current, int pages)
    {
        const int span = 2;
        var shown = new List<int>();

        for (var i = 0; i < pages; i++)
        {
            var keep = i == 0
                       || i == pages - 1
                       || (i >= current - span && i <= current + span);

            if (!keep)
                continue;

            if (shown.Count > 0 && shown[^1] >= 0 && i - shown[^1] > 1)
                shown.Add(-1);

            shown.Add(i);
        }

        return shown;
    }

    private bool DrawPageNumber(int index, int current, Vector2 min, float width, float height)
    {
        var drawList = ImGui.GetWindowDrawList();
        var on = index == current;

        ImGui.SetCursorScreenPos(min);

        var pressed = ImGui.InvisibleButton(
            $"##page{index}", new Vector2(width, height), ImGuiButtonFlags.MouseButtonLeft) && !on;

        var hovered = ImGui.IsItemHovered();
        var max = min + new Vector2(width, height);

        if (on)
            Elev.Glow(drawList, min, max, Theme.Accent, 0.24f, Rad.Ctl, 7.2f);

        if (on || hovered)
        {
            drawList.AddRectFilled(
                min, max,
                Col.U32(on ? Vector4.Lerp(Col.Raised, Theme.Accent, 0.14f) : Col.CtlHover),
                Rad.Ctl);
        }

        drawList.AddRect(
            min, max,
            Col.U32(on ? Theme.Accent : Col.EdgeRest with { W = hovered ? 0.8f : 0.4f }),
            Rad.Ctl, ImDrawFlags.None, on ? Stroke.Edge : Stroke.Hair);

        var label = (index + 1).ToString();
        var size = ImGui.CalcTextSize(label);

        drawList.AddText(
            min + ((new Vector2(width, height) - size) / 2f),
            Col.U32(on ? Theme.Accent : hovered ? Col.Text : Col.TextDim),
            label);

        return pressed;
    }

    private bool DrawPageStep(
        string id, FontAwesomeIcon glyph, Vector2 min, float width, float height, bool enabled, string tip)
    {
        var drawList = ImGui.GetWindowDrawList();

        ImGui.SetCursorScreenPos(min);

        var pressed = ImGui.InvisibleButton(id, new Vector2(width, height), ImGuiButtonFlags.MouseButtonLeft)
                      && enabled;

        var hovered = enabled && ImGui.IsItemHovered();

        if (hovered)
            UiHelpers.WrappedTooltip(tip);

        var max = min + new Vector2(width, height);

        if (hovered)
            drawList.AddRectFilled(min, max, Col.U32(Col.CtlHover), Rad.Ctl);

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList, glyph, min + (new Vector2(width, height) / 2f),
                Col.U32(!enabled ? Col.Disabled(Col.TextMuted) : hovered ? Theme.Accent : Col.TextDim));
        }

        return pressed;
    }

    private void DrawEmpty()
    {
        var filtered = query.JobId is not null || query.Race is not null || query.Sex is not null
                       || query.Tag is not null || !string.IsNullOrWhiteSpace(query.Text);

        var width = ImGui.GetContentRegionAvail().X;

        if (filtered)
        {
            if (Notice.Empty(
                    width, FontAwesomeIcon.Search, "Nothing matches that",
                    "No published glamour fits all of those filters yet.",
                    "Clear filters"))
            {
                ClearFilters();
            }

            return;
        }

        var canPublish = plugin.Gallery.CanPublish;

        if (Notice.Empty(
                width, FontAwesomeIcon.Tshirt, "The gallery is empty",
                canPublish
                    ? "Nobody has published a glamour yet. Put an outfit together in the Dressing Room "
                      + "and yours can be the first."
                    : "Nobody has published a glamour yet, and submissions are closed at the moment.",
                canPublish ? "Publish a glamour" : null)
            && canPublish)
        {
            Publish?.Invoke();
        }
    }

    private void ClearFilters()
    {
        query = new GalleryQuery { Sort = query.Sort };
        search = string.Empty;
        searchSent = string.Empty;
        Refresh();
    }


    private static string Truncate(string text, float width) => Text.Truncate(text, width);


    private void DrawDetail()
    {
        var scale = UiHelpers.Scale;

        if (DrawTopBar(detail))
        {
            Close();
            return;
        }

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        if (detailError is { } problem)
        {
            Notice.Fail(ImGui.GetContentRegionAvail().X, "That glamour couldn't be opened", problem);
            return;
        }

        if (detail is not { } entry)
        {
            Notice.Busy(ImGui.GetContentRegionAvail().X, "Opening", "Fetching the gear list.");
            return;
        }

        var available = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();

        var fold = Motion.Toward("gallerypicturefold", PictureFolded, Anim.ResizeSpeed);
        var column = ((PictureDesign * scale) + Sp.Xxl) * (1f - fold);

        var listWidth = MathF.Max(260f * scale, available.X - column);

        var forPicture = available.Y - StripHeight(entry.ImageCount);

        var imageWidth = MathF.Max(
            0f,
            MathF.Min(column - Sp.Xxl, forPicture * ImageProcessor.ShotAspect));


        DrawReportPopup(entry.Id);

        if (imageWidth > Sp.Xxl)
        {
            DrawDetailImage(entry, imageWidth);

            var rule = origin.X + column - (Sp.Xxl / 2f);

            ImGui.GetWindowDrawList().AddRectFilled(
                new Vector2(rule, origin.Y),
                new Vector2(rule + Stroke.Hair, origin.Y + available.Y),
                Col.U32(Col.EdgeRest with { W = 0.5f * (1f - fold) }));
        }


        var listLeft = origin.X + column;

        ImGui.SetCursorScreenPos(new Vector2(listLeft, origin.Y));
        var headerHeight = DrawDetailHeader(entry, listWidth);

        ImGui.SetCursorScreenPos(new Vector2(listLeft, origin.Y + headerHeight));
        var bandHeight = DrawActionBand(entry, listWidth);

        var consumed = headerHeight + bandHeight;

        ImGui.SetCursorScreenPos(new Vector2(listLeft, origin.Y + consumed));

        var infoHeight = MathF.Max(Metrics.RowTall, available.Y - consumed);

        if (ImGui.BeginChild(
                "##detailinfo", new Vector2(listWidth, infoHeight), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            DrawDetailInfo(entry, ImGui.GetContentRegionAvail().X, infoHeight);

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

        Elev.Shadow(drawList, pos, pos + new Vector2(width, height), Elev.Float, rounding);

        drawList.AddRectFilled(pos, pos + new Vector2(width, height), Col.U32(Col.Sunken), rounding);

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

                MaskCorners(drawList, pos, new Vector2(width, height), rounding);
            }
            else
            {
                DrawDetailPicture(drawList, entry, index, pos, width, height, rounding, 0f);
            }

            drawList.PopClipRect();
        }

        drawList.AddRect(
            pos, pos + new Vector2(width, height), Col.U32(Col.EdgeRest with { W = 0.7f }),
            rounding, ImDrawFlags.None, Stroke.Hair);


        DrawPictureOverlay(drawList, entry, pos, new Vector2(width, height), rounding);

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
            ImGui.Dummy(new Vector2(0f, Sp.Sm));

            var thumbWidth = StripThumbWidth(entry.ImageCount, width);
            var thumbHeight = ImageProcessor.HeightFor(thumbWidth);
            var thumbGap = Sp.Xs;
            var thumbRound = Rad.Chip;

            var stripWidth = (entry.ImageCount * thumbWidth) + ((entry.ImageCount - 1) * thumbGap);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (width - stripWidth) / 2f));

            for (var i = 0; i < entry.ImageCount; i++)
            {
                if (i > 0)
                    ImGui.SameLine(0f, thumbGap);

                var thumbPos = ImGui.GetCursorScreenPos();
                var thumbMax = thumbPos + new Vector2(thumbWidth, thumbHeight);

                if (ImGui.InvisibleButton($"##thumb{i}", new Vector2(thumbWidth, thumbHeight), ImGuiButtonFlags.MouseButtonLeft))
                    SlideTo(i, entry.ImageCount);

                var active = i == detailImage;
                var over = ImGui.IsItemHovered();

                if (over)
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (active)
                    Elev.Glow(drawList, thumbPos, thumbMax, Theme.Accent, 0.30f, thumbRound, 7.2f);

                var thumb = images.Get(GalleryClient.ImageUrl(entry, i, card: true));

                if (thumb != null)
                {
                    var tint = active ? 1f : over ? 0.88f : 0.55f;

                    drawList.AddImageRounded(
                        thumb.Handle, thumbPos, thumbMax,
                        Vector2.Zero, Vector2.One,
                        Col.U32(new Vector4(1f, 1f, 1f, tint)),
                        thumbRound);
                }
                else
                {
                    drawList.AddRectFilled(thumbPos, thumbMax, Col.U32(Col.Sunken), thumbRound);
                }

                drawList.AddRect(
                    thumbPos, thumbMax,
                    Col.U32(active ? Theme.Accent : over ? Col.EdgeHover : Col.EdgeRest with { W = 0.5f }),
                    thumbRound, ImDrawFlags.None, active ? Stroke.Mark : Stroke.Hair);
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

    /// Everything about the glamour that is not its name or its buttons: what the author said, what it is
    /// tagged, and what it is made of.
    private void DrawDetailInfo(GlamourDetail entry, float width, float height)
    {
        var pieces = Pieces(entry);
        var fit = Fit(entry, pieces, width, height);

        if (entry.Tags.Length > 0 || !string.IsNullOrWhiteSpace(entry.Description))
        {
            DrawAboutSection(entry, width, fit.DescriptionLines);
            ImGui.Dummy(new Vector2(0f, Sp.Xl));
        }

        DrawLookSection(entry, width, pieces, fit.GearColumns);
    }

    /// The pieces a glamour actually has, in slot order.
    private List<(GlamSlot Slot, GlamourSlotDto? Piece)> Pieces(GlamourDetail entry) =>
        GlamSlots.All
            .Select(slot => (Slot: slot, Piece: entry.Slots.FirstOrDefault(s => s.Slot == (int)slot)))
            .Where(pair => pair.Piece is not null)
            .ToList();

    /// How the info region divides itself up at this size.
    private readonly record struct InfoFit(int GearColumns, int DescriptionLines);

    /// How tall one gear tile is.
    private static float GearTileHeight =>
        MathF.Max(
            (34f * UiHelpers.Scale) + (Sp.Md * 2f),
            (ImGui.GetTextLineHeight() * 2f) + (Sp.Md * 2.6f));


    /// Divides the info region between the gear grid and the description.
    private InfoFit Fit(
        GlamourDetail entry, List<(GlamSlot Slot, GlamourSlotDto? Piece)> pieces, float width, float height)
    {
        var pieceCount = pieces.Count;
        var lineHeight = ImGui.GetTextLineHeight();

        var gap = Sp.Md;
        var spacing = ImGui.GetStyle().ItemSpacing.Y;

        var headBlock = lineHeight + spacing + Sp.Md + spacing;

        var hasDescription = !string.IsNullOrWhiteSpace(entry.Description);
        var hasAbout = entry.Tags.Length > 0 || hasDescription;

        var aboutFixed = hasAbout ? headBlock + Sp.Xl + spacing : 0f;

        if (hasDescription)
            aboutFixed += spacing;

        var wanted = hasDescription
            ? (int)MathF.Ceiling(ImGui.CalcTextSize(entry.Description, false, width).Y / lineHeight)
            : 0;

        var blocked = Items.Ready && pieces.Any(p => p.Piece is { ItemId: > 0 } piece && !plugin.CanWear(Items.Resolve(piece.ItemId)))
            ? Sp.Md + spacing + lineHeight + spacing
            : 0f;

        var widest = width >= (TileFloor * 3f * UiHelpers.Scale) + (gap * 2f) ? 3
            : width >= (TileFloor * 2f * UiHelpers.Scale) + gap ? 2
            : 1;

        var best = widest;
        var lines = 0;

        for (var columns = Math.Min(2, widest); columns <= widest; columns++)
        {
            var rows = (int)MathF.Ceiling(pieceCount / (float)columns);
            var gear = headBlock + (rows * (GearTileHeight + spacing));

            var room = height - gear - blocked - aboutFixed;
            var available = (int)MathF.Floor(room / lineHeight);

            best = columns;
            lines = Math.Clamp(available, 0, wanted);

            if (lines >= wanted)
                break;
        }

        return new InfoFit(best, lines);
    }

    /// Width of the picture column on the detail page, in design units.
    internal const float PictureDesign = 420f;

    /// How much narrower the window gets when the picture folds away.
    internal const float CollapseSaving = PictureDesign + GutterDesign;

    /// The gap between the picture and the gear list.
    private const float GutterDesign = 24f;

    /// Whether the picture is folded away, which it is exactly while you are wearing the glamour you are
    /// looking at.
    public bool PictureFolded => detail is { } entry && tryingOn == entry.Id;

    /// How wide one thumbnail wants to be, in design units.
    private const float ThumbDesign = 64f;

    /// What a thumbnail actually gets, once the whole strip has to fit under the picture.
    private static float StripThumbWidth(int count, float width)
    {
        var asked = ThumbDesign * UiHelpers.Scale;

        if (count <= 1)
            return asked;

        var room = (width - (Sp.Xs * (count - 1))) / count;

        return MathF.Max(Sp.Xxxl, MathF.Min(asked, room));
    }

    /// How tall the strip under the picture is.
    private static float StripHeight(int count) =>
        count > 1 ? Sp.Sm + ImageProcessor.HeightFor(ThumbDesign * UiHelpers.Scale) : 0f;

    /// The band above the spread: the way back on the left, and whose entry this is on the right.
    private bool DrawTopBar(GlamourDetail? entry)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Metrics.TouchMin;

        var clicked = DrawBackLink(drawList, min, height);

        if (entry is not { Mine: true })
            return clicked;

        var editWidth = Capsule.Width("Edit", FontAwesomeIcon.PencilAlt);
        var yoursWidth = Capsule.Width("Yours");

        var top = min.Y + ((height - Capsule.Height) / 2f);

        Capsule.Marker(
            drawList, new Vector2(min.X + width - editWidth - Sp.Sm - yoursWidth, top),
            "Yours", tooltip: "You published this. Only you can see the Edit button.");

        if (Capsule.Button(
                "##editmine", new Vector2(min.X + width - editWidth, top), "Edit",
                FontAwesomeIcon.PencilAlt,
                "Change the title, description, tags or screenshots - or take it off the board."))
            Edit?.Invoke(entry);

        return clicked;
    }


    /// The way back to the board.
    private bool DrawBackLink(ImDrawListPtr drawList, Vector2 min, float height)
    {
        return BackLink.Draw(
            drawList, plugin.Fonts.Icon, "##galleryback",
            returnTo is null ? "All glamours" : "Back", min, height);
    }

    /// The counts and the report, ON the photograph rather than under it.
    private void DrawPictureOverlay(
        ImDrawListPtr drawList, GlamourDetail entry, Vector2 pos, Vector2 size, float rounding)
    {
        var clear = Col.U32(new Vector4(0f, 0f, 0f, 0f));
        var solid = Col.U32(new Vector4(0f, 0f, 0f, 0.78f));

        var band = Metrics.TouchMin + (Sp.Lg * 2f);
        var foot = rounding * 3f;
        var bandTop = pos.Y + size.Y - band;
        var fadeBottom = MathF.Max(bandTop, pos.Y + size.Y - foot);

        drawList.AddRectFilledMultiColor(
            new Vector2(pos.X, bandTop),
            new Vector2(pos.X + size.X, fadeBottom),
            clear, clear, solid, solid);

        drawList.AddRectFilled(
            new Vector2(pos.X, fadeBottom), pos + size, solid, rounding, ImDrawFlags.RoundCornersBottom);

        var centreY = pos.Y + size.Y - Sp.Lg - (Metrics.TouchMin / 2f);
        var x = pos.X + Sp.Lg;

        var quiet = new Vector4(0.92f, 0.90f, 0.95f, 1f);

        if (entry.Mine)
        {
            x += DrawTally(
                drawList, null, new Vector2(x, centreY), FontAwesomeIcon.Heart,
                $"{entry.Votes}", Theme.Vote, live: false);
        }
        else
        {
            x += DrawTally(
                drawList, "##vote", new Vector2(x, centreY), FontAwesomeIcon.Heart,
                $"{entry.Votes}", Theme.Vote, live: !busySocial, clicked: out var voted,
                on: entry.Voted, quiet: quiet,
                tooltip: entry.Voted ? "Take your vote back." : "Vote for this glamour.");

            if (voted)
                Vote(entry);
        }

        x += Sp.Lg;

        DrawTally(
            drawList, "##favourite", new Vector2(x, centreY), FontAwesomeIcon.Star,
            $"{entry.Favourites}", Theme.Favourite, live: !busySocial, clicked: out var favourited,
            on: entry.Favourited, quiet: quiet,
            tooltip: entry.Favourited ? "Remove it from your saved list." : "Keep it on your profile.");

        if (favourited)
            Favourite(entry);

        if (entry.Mine)
            return;

        DrawTally(
            drawList, "##report",
            new Vector2(pos.X + size.X - Sp.Lg - Metrics.TouchMin, centreY), FontAwesomeIcon.Flag,
            null, Col.Danger, live: true, clicked: out var reported,
            quiet: quiet with { W = 0.6f },
            tooltip: "Report this to moderation.");

        if (reported)
            ImGui.OpenPopup(ReportPopupId);
    }

    /// A glyph and a number that light up together.
    private float DrawTally(
        ImDrawListPtr drawList, string? id, Vector2 centreLeft, FontAwesomeIcon icon,
        string? count, Vector4 colour, bool live, out bool clicked,
        bool on = false, string? tooltip = null, Vector4? quiet = null)
    {
        clicked = false;

        var glyphRoom = Metrics.TouchMin;
        var textWidth = string.IsNullOrEmpty(count) ? 0f : Text.Width(count);
        var width = glyphRoom + (textWidth > 0f ? Sp.Xs + textWidth : 0f);

        var min = new Vector2(centreLeft.X, centreLeft.Y - (glyphRoom / 2f));
        var hovered = false;

        if (live && id is not null)
        {
            var cursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(min);

            clicked = ImGui.InvisibleButton(id, new Vector2(width, glyphRoom), ImGuiButtonFlags.MouseButtonLeft);
            hovered = ImGui.IsItemHovered();

            ImGui.SetCursorScreenPos(cursor);

            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (!string.IsNullOrEmpty(tooltip))
                    UiHelpers.WrappedTooltip(tooltip);
            }
        }

        var warm = id is null ? 0f : Motion.Toward($"tally{id}", hovered, 20f);

        var unlit = quiet ?? Col.TextDim;

        var resting = on
            ? colour
            : id is null
                ? colour with { W = 0.75f }
                : unlit;

        var lit = on ? Vector4.Lerp(colour, Vector4.One, 0.3f) : colour;
        var tint = Col.U32(Vector4.Lerp(resting, lit, warm));

        using (plugin.Fonts.Icon.PushSafe())
        {
            if (quiet is not null)
            {
                var drop = MathF.Max(1f, Stroke.Hair);

                UiHelpers.DrawScaledIcon(
                    drawList, icon,
                    new Vector2(min.X + (glyphRoom / 2f) + drop, centreLeft.Y + drop),
                    Col.U32(new Vector4(0f, 0f, 0f, 0.55f)));
            }

            UiHelpers.DrawScaledIcon(drawList, icon, new Vector2(min.X + (glyphRoom / 2f), centreLeft.Y), tint);
        }

        if (textWidth > 0f)
        {
            var textPos = new Vector2(min.X + glyphRoom + Sp.Xs, centreLeft.Y - (ImGui.GetTextLineHeight() / 2f));
            var bright = quiet is { } q ? Vector4.Lerp(q, Vector4.One, 0.5f) : Col.Text;

            if (quiet is not null)
            {
                drawList.AddText(
                    textPos + new Vector2(MathF.Max(1f, Stroke.Hair), MathF.Max(1f, Stroke.Hair)),
                    Col.U32(new Vector4(0f, 0f, 0f, 0.55f)), count!);
            }

            drawList.AddText(
                textPos,
                Col.U32(Vector4.Lerp(on ? bright : unlit, bright, warm)),
                count!);
        }

        return width;
    }

    /// The overload for a readout, where there is nothing to click and therefore no click to report.
    private float DrawTally(
        ImDrawListPtr drawList, string? id, Vector2 centreLeft, FontAwesomeIcon icon,
        string? count, Vector4 colour, bool live) =>
        DrawTally(drawList, id, centreLeft, icon, count, colour, live, clicked: out _);

    /// What this is and whose it is, above the scroll rather than inside it.
    private float DrawDetailHeader(GlamourDetail entry, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var badge = 44f * UiHelpers.Scale;

        float titleHeight;

        using (Text.PushDisplay())
        {
            titleHeight = Text.DisplaySize * 1.2f;

            drawList.AddText(
                origin, Col.U32(Col.Text), Text.Truncate(entry.Title, width));
        }

        var row = origin.Y + titleHeight + Sp.Sm;

        DrawAuthorBadge(entry, new Vector2(origin.X, row), badge);

        var textLeft = origin.X + badge + Sp.Lg;
        var room = MathF.Max(0f, width - badge - Sp.Lg);

        var author = string.IsNullOrEmpty(entry.AuthorWorld)
            ? entry.AuthorName
            : $"{entry.AuthorName} · {entry.AuthorWorld}";

        var lineHeight = ImGui.GetTextLineHeight();
        var captionLine = Text.CaptionSize * 1.15f;
        var block = lineHeight + Sp.Xxs + captionLine;
        var textTop = row + ((badge - block) / 2f);

        var jobRoom = 0f;

        if (entry.JobId > 0 && JobList.Find(entry.JobId) is { } job)
        {
            var glyph = lineHeight + (2f * UiHelpers.Scale);
            var jobWidth = Text.Width(job.Name);
            jobRoom = glyph + Sp.Sm + jobWidth + Sp.Lg;

            var jobLeft = origin.X + width - jobWidth;

            drawList.AddText(new Vector2(jobLeft, textTop), Col.U32(Col.TextDim), job.Name);

            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrDefault();
            if (texture != null)
            {
                var iconTop = textTop + ((lineHeight - glyph) / 2f);

                drawList.AddImage(
                    texture.Handle,
                    new Vector2(jobLeft - Sp.Sm - glyph, iconTop),
                    new Vector2(jobLeft - Sp.Sm, iconTop + glyph),
                    Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f)));
            }
        }

        var authorRoom = MathF.Max(0f, room - jobRoom);
        var authorWidth = Text.Width(author);

        var overAuthor = !string.IsNullOrEmpty(entry.AuthorId)
                         && ImGui.IsMouseHoveringRect(
                             new Vector2(textLeft, textTop),
                             new Vector2(textLeft + MathF.Min(authorWidth, authorRoom), textTop + lineHeight));

        drawList.AddText(
            new Vector2(textLeft, textTop),
            Col.U32(overAuthor ? Theme.AccentHover : Theme.Accent),
            Text.Truncate(author, authorRoom));

        if (overAuthor)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                OpenProfile?.Invoke(entry.AuthorId);
        }

        using (Text.PushCaption())
        {
            var describing = RaceNames.Describe(entry.Race, entry.Tribe, entry.Sex);

            var edited = entry.UpdatedUtc > entry.PublishedUtc + 60;

            var when = entry.PublishedUtc > 0
                ? edited ? $"edited {Elapsed(entry.UpdatedUtc)}" : Published(entry)
                : string.Empty;

            var whenWidth = when.Length > 0 ? Text.Width(when) + Sp.Lg : 0f;

            drawList.AddText(
                new Vector2(textLeft, textTop + lineHeight + Sp.Xxs),
                Col.U32(Col.TextDim),
                Text.Truncate(describing, MathF.Max(0f, room - whenWidth)));

            if (when.Length > 0)
            {
                drawList.AddText(
                    new Vector2(origin.X + width - Text.Width(when), textTop + lineHeight + Sp.Xxs),
                    Col.U32(Col.TextMuted), when);
            }
        }

        var ruleY = row + badge + Sp.Lg;

        drawList.AddRectFilled(
            new Vector2(origin.X, ruleY),
            new Vector2(origin.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        return ruleY + Stroke.Hair + Sp.Lg - origin.Y;
    }

    /// The author's picture, or their initial.
    private void DrawAuthorBadge(GlamourDetail entry, Vector2 min, float badge)
    {
        var unknown = string.IsNullOrEmpty(entry.AuthorId);

        var clicked = Avatar.Draw(
            ImGui.GetWindowDrawList(), images,
            unknown ? null : GalleryClient.AvatarUrl(entry.AuthorId),
            entry.AuthorName, min, badge, interactive: !unknown);

        if (clicked)
            OpenProfile?.Invoke(entry.AuthorId);
    }

    /// How long ago an entry went up, in the roughest terms that are still true.
    private static string Published(GlamourDetail entry) => Elapsed(entry.PublishedUtc);

    /// The same phrasing for any unix stamp, so "3d ago" and "edited 3d ago" cannot drift into two different
    /// roundings of the same gap.
    private static string Elapsed(long unixSeconds)
    {
        var elapsed = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        if (elapsed < TimeSpan.FromHours(1)) return "just now";
        if (elapsed < TimeSpan.FromDays(1)) return $"{MathF.Max(1, (int)elapsed.TotalHours)}h ago";
        if (elapsed < TimeSpan.FromDays(30)) return $"{(int)elapsed.TotalDays}d ago";
        if (elapsed < TimeSpan.FromDays(365)) return $"{(int)(elapsed.TotalDays / 30)}mo ago";

        return $"{(int)(elapsed.TotalDays / 365)}y ago";
    }


    private void DrawAboutSection(GlamourDetail entry, float width, int lines)
    {
        var room = MathF.Max(Sp.Xxxl, width - Theme.HeaderRun("About") - Sp.Xl);
        var (tagsShown, tagsRun) = FitTags(entry.Tags, room);

        Theme.SectionHeader(
            "About",
            ruleWidth: tagsShown > 0 ? MathF.Max(width - tagsRun - Sp.Lg, Sp.Xxxl) : width);

        if (tagsShown > 0)
        {
            DrawTags(
                entry, tagsShown,
                ImGui.GetCursorScreenPos()
                + new Vector2(width - tagsRun, -ImGui.GetTextLineHeightWithSpacing()));
        }

        ImGui.Dummy(new Vector2(0f, Sp.Md));

        if (!string.IsNullOrWhiteSpace(entry.Description) && lines > 0)
        {
            var shown = Text.ClampLines(entry.Description, width, lines, out var cut);

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextUnformatted(shown);
            ImGui.PopTextWrapPos();

            if (cut && ImGui.IsItemHovered())
                UiHelpers.WrappedTooltip(entry.Description);
        }
    }

    /// How many tags fit on one line in room, and how wide that run comes out.
    private static (int Shown, float Width) FitTags(string[] tags, float room)
    {
        if (tags.Length == 0)
            return (0, 0f);

        var shown = 0;
        var total = 0f;

        for (var i = 0; i < tags.Length; i++)
        {
            var candidate = total + (i > 0 ? Sp.Lg : 0f) + Text.Width($"#{tags[i]}");
            var remaining = tags.Length - i - 1;
            var needed = candidate + (remaining > 0 ? Sp.Lg + Text.Width($"+{remaining}") : 0f);

            if (i > 0 && needed > room)
                break;

            total = candidate;
            shown = i + 1;
        }

        var hidden = tags.Length - shown;

        return (shown, hidden > 0 ? total + Sp.Lg + Text.Width($"+{hidden}") : total);
    }


    /// The tags, as links.
    private void DrawTags(GlamourDetail entry, int shown, Vector2 origin)
    {
        var drawList = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeight();

        var x = origin.X;
        var y = origin.Y;

        for (var i = 0; i < shown && i < entry.Tags.Length; i++)
        {
            var tag = entry.Tags[i];
            var label = $"#{tag}";
            var measured = Text.Width(label);

            var min = new Vector2(x, y);
            var hovered = ImGui.IsMouseHoveringRect(min, min + new Vector2(measured, lineHeight));
            var warm = Motion.Toward($"detailtag{tag}", hovered, 20f);
            var tint = Vector4.Lerp(Col.TextDim, Theme.Accent, warm);

            drawList.AddText(min, Col.U32(tint), label);

            if (warm > 0.01f)
            {
                var underline = y + lineHeight - Stroke.Hair;

                drawList.AddRectFilled(
                    new Vector2(x, underline),
                    new Vector2(x + (measured * warm), underline + Stroke.Hair),
                    Col.U32(tint with { W = warm }));
            }

            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                UiHelpers.WrappedTooltip($"Show everything tagged {tag}.");

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    query.Tag = tag;
                    query.Page = 0;
                    pendingClose = true;
                }
            }

            x += measured + Sp.Lg;
        }

        var hidden = entry.Tags.Length - shown;

        if (hidden <= 0)
            return;

        var marker = $"+{hidden}";
        var markerMin = new Vector2(x, y);

        drawList.AddText(markerMin, Col.U32(Col.TextMuted), marker);

        if (ImGui.IsMouseHoveringRect(markerMin, markerMin + new Vector2(Text.Width(marker), lineHeight)))
            UiHelpers.WrappedTooltip($"Also tagged {string.Join(", ", entry.Tags.Skip(shown))}.");
    }

    /// The gear, as a grid of tiles.
    private void DrawLookSection(
        GlamourDetail entry, float width, List<(GlamSlot Slot, GlamourSlotDto? Piece)> pieces, int columns)
    {
        var scale = UiHelpers.Scale;
        var inner = width;

        var summary = Summarise(pieces);
        var summaryWidth = Text.Width(summary) + Sp.Lg;

        Theme.SectionHeader("The Look", ruleWidth: MathF.Max(inner - summaryWidth, 60f * scale));

        var summaryPos = ImGui.GetCursorScreenPos()
            + new Vector2(inner - Text.Width(summary), -ImGui.GetTextLineHeightWithSpacing());

        ImGui.GetWindowDrawList().AddText(summaryPos, Col.U32(Col.TextMuted), summary);

        ImGui.Dummy(new Vector2(0f, Sp.Md));

        if (!Items.Ready || !Dyes.Ready)
        {
            ImGui.TextColored(Theme.TextDim, "Reading the item list...");
            return;
        }

        var gap = Sp.Md;
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
            ImGui.Dummy(new Vector2(0f, Sp.Md));
            ImGui.TextColored(
                Theme.Warning,
                missing == 1
                    ? "1 piece won't show on your character."
                    : $"{missing} pieces won't show on your character.");
        }

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

    /// What you can do with this look, under the name of it.
    private float DrawActionBand(GlamourDetail entry, float width)
    {
        var scale = UiHelpers.Scale;
        var start = ImGui.GetCursorScreenPos();

        ImGui.BeginGroup();

        var gap = Sp.Sm;
        var height = Metrics.Row;
        var wearing = tryingOn == entry.Id;
        var canUndo = beforeTryOn is not null;

        var used = 0f;

        void Place(float next)
        {
            if (used <= 0f)
            {
                used = next;
                return;
            }

            if (used + gap + next <= width)
            {
                ImGui.SameLine(0f, gap);
                used += gap + next;
                return;
            }

            ImGui.Dummy(new Vector2(0f, gap));
            used = next;
        }

        var tryLabel = wearing ? "Wearing This" : "Try On";
        Place(EchoButton.ContentSize(tryLabel, plugin.Fonts.Icon, FontAwesomeIcon.Tshirt).X);

        if (EchoButton.Draw(
                "##tryon", tryLabel, new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Tshirt,
                selected: wearing,
                enabled: Items.Ready && !wearing,
                tooltip: "Puts this look on your character. Only you can see it.",
                primary: true))
            TryOn(entry);

        Place(EchoButton.ContentSize("Revert", plugin.Fonts.Icon, FontAwesomeIcon.UndoAlt).X);

        if (EchoButton.Draw(
                "##undotryon", "Revert", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.UndoAlt,
                accentOverride: canUndo ? Col.Caution : null,
                enabled: canUndo,
                tooltip: "Goes back to whatever you were wearing before you started trying things on."))
            UndoTryOn();

        Place(EchoButton.ContentSize("Save Outfit", plugin.Fonts.Icon, FontAwesomeIcon.Save).X);

        if (EchoButton.Draw(
                "##savecopy", "Save Outfit", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Save,
                enabled: Items.Ready && !plugin.Outfits.ReadOnly,
                tooltip: entry.Mine
                    ? "Keeps an editable copy in the Dressing Room."
                    : "Keeps a copy in the Dressing Room, where you can change it."))
            SaveCopy(entry);

        if (entry.Appearance is not null)
        {
            Place(EchoButton.ContentSize("Copy Appearance", plugin.Fonts.Icon, FontAwesomeIcon.UserEdit).X);

            if (EchoButton.Draw(
                    "##applylook", "Copy Appearance", new Vector2(0f, height),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.UserEdit,
                    enabled: Appearance.LayoutTrusted,
                    tooltip: Appearance.LayoutTrusted
                        ? entry.Mine
                            ? "Puts your character's race, face, hair and colours back to how they were in this glamour. Undone by Revert."
                            : "Changes your character's race, face, hair and colours to theirs. Undone by Revert."
                        : "The appearance editor is disabled on this build - see the Dressing Room."))
                ApplyAppearance(entry);
        }

        if (saveResult.Any && ImGui.GetTime() - saveResultAt < 4d)
        {
            ImGui.Dummy(new Vector2(0f, Sp.Md));
            saveResult.Draw(width);
        }

        ImGui.EndGroup();

        var ruleY = ImGui.GetItemRectMax().Y + Sp.Lg;

        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(start.X, ruleY),
            new Vector2(start.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        return ruleY + Stroke.Hair + Sp.Lg - start.Y;
    }

    private bool busySocial;

    private const string ReportPopupId = "##echoglamreport";

    private string reportReason = string.Empty;
    private string reportNote = string.Empty;
    private bool reporting;
    private Message reportDone = Message.None;

    private void DrawReportPopup(string glamourId)
    {
        if (!ImGui.BeginPopup(ReportPopupId))
            return;

        var scale = UiHelpers.Scale;

        Theme.SectionHeader("Report this glamour", ruleWidth: ReportWidth);
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
            "##reportnote", ref reportNote, 300, new Vector2(ReportWidth, 52f * scale), ReportNoteWrap);

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (EchoButton.Draw(
                "##sendreport", reporting ? "Sending..." : "Send", new Vector2(0f, 24f * scale),
                enabled: !reporting && !string.IsNullOrEmpty(reportReason)))
            SendReport(glamourId);

        if (reportDone.Any)
        {
            ImGui.Dummy(new Vector2(0f, Sp.Sm));
            reportDone.Draw(ReportWidth);
        }

        ImGui.EndPopup();
    }

    private void SendReport(string glamourId)
    {
        reporting = true;
        reportDone = Message.None;

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
                reportDone = Message.Ok("Sent. Thanks - somebody will look at it.");
                reportNote = string.Empty;
                reportReason = string.Empty;
            }
            catch (Exception ex)
            {
                reportDone = Message.Failed(ex.Message);
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
                saveResult = Message.Failed(ex.Message);
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
                saveResult = Message.Failed(ex.Message);
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
        var pad = Sp.Md;

        var height = GearTileHeight;

        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));

        var item = piece.ItemId == 0 ? GlamItem.None : Items.Resolve(piece.ItemId);
        var blocked = piece.ItemId != 0 && !plugin.CanWear(item);
        var hovered = ImGui.IsMouseHoveringRect(pos, pos + new Vector2(width, height));

        var max = pos + new Vector2(width, height);
        var rounding = Rad.Panel;

        Elev.Shadow(drawList, pos, max, Elev.Block, rounding);

        drawList.AddRectFilled(pos, max, Col.U32(hovered ? Col.RaisedHigh : Col.Raised), rounding);

        drawList.AddLine(
            new Vector2(pos.X + rounding, pos.Y + (Stroke.Hair / 2f)),
            new Vector2(max.X - rounding, pos.Y + (Stroke.Hair / 2f)),
            Col.U32(new Vector4(1f, 1f, 1f, 0.07f)),
            Stroke.Hair);

        drawList.AddRect(
            pos, max,
            Col.U32(blocked
                ? Col.Caution with { W = 0.55f }
                : hovered ? Col.EdgeHover : Col.EdgeRest with { W = 0.6f }),
            rounding, ImDrawFlags.None, blocked ? Stroke.Edge : Stroke.Hair);

        var iconMin = new Vector2(pos.X + pad, pos.Y + ((height - iconSize) / 2f));
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        var iconRound = Rad.Ctl;

        drawList.AddRectFilled(iconMin, iconMax, Col.U32(Col.Sunken), iconRound);

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
            Col.U32(new Vector4(1f, 1f, 1f, hovered ? 0.16f : 0.09f)),
            iconRound, ImDrawFlags.None, Stroke.Hair);

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

        var pillGutter = columns.Total > 0f ? columns.Total + Sp.Xl : 0f;
        var textRoom = MathF.Max(40f * scale, rightEdge - textX - pillGutter);

        drawList.AddText(
            new Vector2(textX, lineOne),
            ImGui.GetColorU32(blocked ? Theme.Warning : piece.ItemId == 0 ? Theme.TextDisabled : Theme.Text),
            Truncate(name, textRoom));

        drawList.AddText(
            new Vector2(textX, lineTwo),
            ImGui.GetColorU32(Theme.TextDim),
            Truncate(slot.Label(), textRoom));

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

        saveResult = Message.Ok($"Saved as \"{name}\" in the Dressing Room.");
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


    /// Asks for this month's top five.
    private void RefreshShowcase()
    {
        var owner = plugin.Configuration.EnsureOwnerKey();

        var asked = new GalleryQuery
        {
            Sort = GlamourSort.Month,
            PageSize = ShowcaseCount,
        };

        _ = Task.Run(async () =>
        {
            try
            {
                showcase = await GalleryClient.BrowseAsync(asked, owner, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning($"[EchoGlam] Showcase unavailable: {ex.Message}");
            }
        });
    }

    /// How many the showcase ranks.
    private const int ShowcaseCount = 10;

    /// How fast the showcase travels, in design units a second.
    private const float ShowcaseSpeed = 22f;

    /// How far the strip has travelled, in real pixels.
    private float showcaseOffset;

    /// Whether a card is being held and dragged along the strip.
    private bool showcaseDragging;

    /// How far the pointer has moved since it went down, in pixels.
    private float showcaseDragMoved;

    /// How far the pointer can travel and still count as a click rather than a drag, in design units.
    private const float ClickSlackDesign = 4f;

    /// How long a card may be held down and still open on release.
    private const float TapSeconds = 0.25f;

    /// How long the current press has lasted, in seconds.
    private float showcaseHeldFor;

    /// The strip's speed while a hand is on it, in pixels a second, smoothed.
    private float showcaseThrow;

    /// What the strip is coasting at after a flick, in pixels a second.
    private float showcaseFling;

    /// How fast the measured drag speed follows the pointer.
    private const float ThrowSmoothing = 18f;

    /// How quickly a throw runs out, per second.
    private const float FlingDecay = 2.8f;

    /// The fastest a throw may leave, in design units a second.
    private const float FlingMaxDesign = 3000f;

    /// Below this, in design units a second, a throw is over.
    private const float FlingFloorDesign = 6f;

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
