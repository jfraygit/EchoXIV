using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.Integrations;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Finding other people's shows, and being findable yourself.
public sealed class BrowseScreen
{
    private enum Category
    {
        LiveShows,
        Djs,
        Stats,
        YourListing,
    }

    private static readonly CategoryList.Item[] Categories =
    {
        new(nameof(Category.LiveShows), "Live Shows", FontAwesomeIcon.Compass, "Who's playing now"),
        new(nameof(Category.Djs), "DJs", FontAwesomeIcon.Users, "Browse the DJ list"),
        new(nameof(Category.Stats), "Stats", FontAwesomeIcon.ChartBar, "Shows and listeners"),
        new(nameof(Category.YourListing), "Your Listing", FontAwesomeIcon.IdBadge, "How you appear"),
    };

    private readonly Plugin plugin;
    private readonly CategoryList categoryList = new();
    private readonly DjListSection djList;
    private readonly ListingEditor listingEditor;
    private readonly StatsSection stats;
    private Category selected = Category.LiveShows;

    private string genreFilter = string.Empty;
    private List<string> genreOptions = new();
    private object? genreOptionsSource;

    private string pendingJoinRoomCode = string.Empty;
    private string joinPasswordBuffer = string.Empty;

    private readonly RelayPoll showsPoll = new();

    /// True while the grid is showing fixtures.
    private bool IsSample
    {
#if DEBUG
        get => plugin.Configuration.UseSampleBrowseData;
#else
        get => false;
#endif
    }

    public BrowseScreen(Plugin plugin)
    {
        this.plugin = plugin;
        djList = new DjListSection(plugin);
        listingEditor = new ListingEditor(plugin);
        stats = new StatsSection(plugin);
    }

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var listWidth = CategoryList.DefaultWidth;
        var origin = ImGui.GetCursorScreenPos();

        var picked = categoryList.Draw("##browseCat", origin, new Vector2(listWidth, avail.Y),
            Categories, selected.ToString());
        if (Enum.TryParse<Category>(picked, out var next))
            Go(next);

        ImGui.SetCursorScreenPos(origin + new Vector2(listWidth + Metrics.Xxl, 0f));
        var detailWidth = MathF.Max(1f, avail.X - listWidth - Metrics.Xxl);

        Surfaces.ReserveScrollbar = true;

        ImGui.BeginChild("##browseDetail", new Vector2(detailWidth, avail.Y), false);
        switch (selected)
        {
            case Category.Djs:
                djList.Draw(IsSample);
                break;
            case Category.Stats:
                stats.Draw(IsSample);
                break;
            case Category.YourListing:
                listingEditor.Draw(IsSample);
                break;
            default:
                DrawLiveShows();
                break;
        }
        ImGui.EndChild();

        Surfaces.ReserveScrollbar = false;

        if (djList.ConsumeEditRequest() is { } profile)
        {
            listingEditor.OpenFor(profile);
            Go(Category.YourListing);
        }
    }

    /// Switches category, giving the listing editor the chance to save on the way out.
    private void Go(Category next)
    {
        if (next == selected)
            return;

        if (selected == Category.YourListing)
            listingEditor.OnLeaving();

        selected = next;

    }


    private void DrawLiveShows()
    {
        var client = plugin.AudioHostClient;
        var snapshot = client.LatestPublicShows;

        if (!IsSample && showsPoll.Due(client.IsConnected))
            plugin.DjDeckWindow.RefreshPublicShows();

#if DEBUG

        if (plugin.Configuration.UseSampleBrowseData)
            snapshot = BrowseSampleData.Shows;
#endif

        Surfaces.SectionHeader("Live Shows");
        Surfaces.Gap(Metrics.Md);

        if (IsSample)
        {
            Surfaces.RowText(
                "Showing sample shows for layout testing. Turn off Sample Browse Data in Settings > "
                + "Appearance to see the real list.",
                Semantic.Warning);
            Surfaces.Gap(Metrics.Md);
        }

        if (snapshot == null)
        {
            DrawNotice(FontAwesomeIcon.Compass, "Looking for live shows...");
            return;
        }

        if (!string.IsNullOrEmpty(snapshot.Error))
        {
            DrawNotice(FontAwesomeIcon.ExclamationTriangle, $"Couldn't reach the relay: {snapshot.Error}");
            return;
        }

        if (!ReferenceEquals(genreOptionsSource, snapshot))
        {
            genreOptionsSource = snapshot;
            genreOptions = DjDeckWindow.GenreOptions(snapshot.Shows.Select(s => s.Genres));

            if (genreFilter.Length > 0 && !genreOptions.Contains(genreFilter))
            {
                genreOptions.Add(genreFilter);
                genreOptions.Sort(StringComparer.OrdinalIgnoreCase);
            }
        }

        var shows = genreFilter.Length == 0
            ? snapshot.Shows
            : snapshot.Shows
                .Where(s => s.Genres.Any(g => string.Equals(g, genreFilter, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        DrawListHeader(shows.Count == 1 ? "1 Show" : $"{shows.Count} Shows");
        Surfaces.Gap(Metrics.Md);

        if (snapshot.Shows.Count == 0)
        {
            DrawNotice(FontAwesomeIcon.Music, "Nothing is live right now. Checking every few seconds.");
            return;
        }

        if (shows.Count == 0)
        {
            DrawNotice(FontAwesomeIcon.Filter, $"No live shows under \"{genreFilter}\" right now.");
            return;
        }

        var rowHeight = MathF.Round(RowHeight());

        Surfaces.BeginPanel();

        var rowWidth = Surfaces.ContentWidth;

        for (var i = 0; i < shows.Count; i++)
        {
            if (i > 0)
            {
                Fields.Divider();
                Surfaces.Gap(Metrics.Xs);
            }

            DrawShowRow(shows[i], rowWidth, rowHeight);
        }

        Surfaces.EndPanel();
    }

    /// Count, rule, Refresh and the genre filter - the same band DjListSection draws above its own list, so
    /// the two Browse lists read as the same kind of thing.
    private void DrawListHeader(string title)
    {
        var refreshSize = MathF.Round(Metrics.ControlSm);
        var genreWidth = MathF.Round(160f * Metrics.Scale);

        var header = Fields.BeginListHeader(title, genreWidth + refreshSize + Metrics.Md);

        ImGui.SetCursorScreenPos(new Vector2(
            header.ControlMin.X, header.ControlMin.Y + ((header.Height - refreshSize) * 0.5f)));

        if (Fields.IconButton("##v2refreshShows", FontAwesomeIcon.Sync, refreshSize,
                "Refresh", "Updates on its own. Press to check now."))
        {
            showsPoll.Stamp();
            plugin.DjDeckWindow.RefreshPublicShows();
        }

        var options = new List<string> { "All Genres" };
        options.AddRange(genreOptions);
        var index = genreFilter.Length == 0 ? 0 : options.IndexOf(genreFilter);

        ImGui.SetCursorScreenPos(new Vector2(header.ControlMin.X + refreshSize + Metrics.Md, header.ControlMin.Y));
        if (Fields.DropdownInline("##v2ShowGenre", ref index, options, genreWidth, header.Height))
            genreFilter = index <= 0 ? string.Empty : options[index];

        Fields.EndListHeader(header);
    }

    /// A row is as tall as its thumbnail, which is the tallest thing in it.
    private static float RowHeight() => MathF.Round(ThumbWidth() * 9f / 16f) + (Metrics.Md * 2f);

    private static float ThumbWidth() => MathF.Round(132f * Metrics.Scale);

    private void DrawShowRow(PublicShowEntryDto show, float width, float height)
    {
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();
        var accent = show.IsVenueShow ? Semantic.FixedB : Semantic.FixedA;

        var thumbWidth = ThumbWidth();
        var thumbHeight = MathF.Round(thumbWidth * 9f / 16f);
        var actionsWidth = MathF.Round(118f * Metrics.Scale);

        var hitWidth = MathF.Max(Metrics.Xxl, width - actionsWidth - Metrics.Xl - Metrics.Md);
        ImGui.InvisibleButton($"##showRow{show.RoomCode}", new Vector2(hitWidth, height));
        if (ImGui.IsItemHovered())
            drawList.AddRectFilled(origin, origin + new Vector2(width, height),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.03f)), Metrics.RadiusSoft);

        var thumbPos = Chrome.Snap(new Vector2(origin.X, origin.Y + ((height - thumbHeight) * 0.5f)));
        DrawThumb(drawList, show, thumbPos, new Vector2(thumbWidth, thumbHeight), accent);

        var textLeft = origin.X + thumbWidth + Metrics.Lg;
        var textWidth = MathF.Max(Metrics.Xxl, width - thumbWidth - actionsWidth - (Metrics.Lg * 3f));
        var lineHeight = ImGui.GetTextLineHeight();
        var block = (lineHeight * 3f) + (Metrics.Xs * 2f);
        var y = origin.Y + ((height - block) * 0.5f);

        var elapsed = DateTime.UtcNow - show.LiveSinceUtc;
        var meta = elapsed.TotalHours >= 1
            ? $"{show.ListenerCount} listening  ·  {(int)elapsed.TotalHours}h {elapsed.Minutes}m"
            : $"{show.ListenerCount} listening  ·  {elapsed.Minutes}m";

        float metaWidth;
        using (TypeScale.Caption())
            metaWidth = ImGui.CalcTextSize(meta).X;

        using (TypeScale.Caption())
            Chrome.Text(drawList, new Vector2(textLeft + textWidth - metaWidth, y),
                ImGui.GetColorU32(Semantic.TextTertiary), meta);

        var lockRoom = 0f;
        if (show.HasPassword)
        {
            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.Lock,
                    Chrome.Snap(new Vector2(textLeft + (Metrics.Md * 0.5f), y + (lineHeight * 0.5f))),
                    ImGui.GetColorU32(Semantic.Warning));

            lockRoom = Metrics.Xl;
        }

        using (TypeScale.Body())
        {
            var title = show.ShowName ?? show.DjName ?? "Live Show";
            Chrome.Text(drawList, new Vector2(textLeft + lockRoom, y),
                ImGui.GetColorU32(Semantic.TextPrimary),
                UiHelpers.TruncateToWidth(title, textWidth - metaWidth - lockRoom - Metrics.Lg));
        }

        y += lineHeight + Metrics.Xs;

        using (TypeScale.Caption())
            Chrome.Text(drawList, new Vector2(textLeft, y),
                ImGui.GetColorU32(Semantic.TextSecondary),
                UiHelpers.TruncateToWidth(show.DjName ?? "Unknown DJ", textWidth));

        y += lineHeight + Metrics.Xs;

        var (line1, line2) = DjDeckWindow.VenueAddressLines(show);
        var where = show.IsVenueShow
            ? string.Join("  ", new[] { show.VenueName, line1, line2 }.Where(l => !string.IsNullOrWhiteSpace(l)))
            : "Global Audio";

        using (TypeScale.Caption())
            Chrome.Text(drawList, new Vector2(textLeft, y),
                ImGui.GetColorU32(show.IsVenueShow ? Semantic.Alpha(accent, 0.85f) : Semantic.TextTertiary),
                UiHelpers.TruncateToWidth(where, textWidth - Metrics.Lg));

        DrawRowActions(show, origin, width, height, actionsWidth);
    }

    private void DrawThumb(ImDrawListPtr drawList, PublicShowEntryDto show, Vector2 pos, Vector2 size, Vector4 accent)
    {
        var texture = plugin.DjDeckWindow.PublicShowImage(show);

        if (texture != null)
        {
            drawList.AddImageRounded(texture.Handle, pos, pos + size, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), Metrics.RadiusSoft);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Elevation.Sunken), Metrics.RadiusSoft);

            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.Music,
                    Chrome.Snap(pos + (size * 0.5f)), ImGui.GetColorU32(Semantic.TextDisabled));
        }

        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(Semantic.Alpha(accent, 0.55f)),
            Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline);
    }

    private void DrawRowActions(PublicShowEntryDto show, Vector2 origin, float width, float height, float actionsWidth)
    {
        var canVisit = show.IsVenueShow && LifestreamIntegration.HasVisitableLocation(
            show.VenueWorld, show.VenueHousingArea, show.VenueWard, show.VenuePlot);

        var buttonHeight = MathF.Round(Metrics.ControlSm);

        var left = origin.X + width - actionsWidth - Metrics.Xl;
        var stackHeight = canVisit ? (buttonHeight * 2f) + Metrics.Sm : buttonHeight;
        var top = origin.Y + ((height - stackHeight) * 0.5f);

        ImGui.SetCursorScreenPos(new Vector2(left, top));
        if (Fields.Button(show.HasPassword ? "Join" : "Listen", Fields.ButtonStyle.Primary, actionsWidth,
                enabled: !IsSample, height: buttonHeight, idSuffix: show.RoomCode))
        {
            if (show.HasPassword)
            {
                pendingJoinRoomCode = show.RoomCode;
                joinPasswordBuffer = string.Empty;
                ImGui.OpenPopup(JoinPopupId);
            }
            else
            {
                plugin.DjDeckWindow.JoinPublicShow(show.RoomCode, string.Empty);
            }
        }

        if (canVisit)
        {
            ImGui.SetCursorScreenPos(new Vector2(left, top + buttonHeight + Metrics.Sm));
            if (Fields.Button("Visit", Fields.ButtonStyle.Secondary, actionsWidth,
                    enabled: !IsSample, height: buttonHeight, idSuffix: show.RoomCode))
            {
                LifestreamIntegration.TryVisit(show.VenueWorld!, show.VenueHousingArea!,
                    show.VenueWard!, show.VenuePlot!, show.VenueIsApartment, show.VenueSubdivision);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));

        if (pendingJoinRoomCode == show.RoomCode)
            DrawJoinPasswordPopup(show);
    }

    private const string JoinPopupId = "##v2joinShow";

    private void DrawJoinPasswordPopup(PublicShowEntryDto show)
    {
        using var detached = Surfaces.Detach();

        var popupWidth = MathF.Round(280f * Metrics.Scale);
        ImGui.SetNextWindowSize(new Vector2(popupWidth, 0f));

        using var style = Sty.Popup()
            .Var(ImGuiStyleVar.WindowPadding, new Vector2(Metrics.Lg, Metrics.Lg))
            .Var(ImGuiStyleVar.ItemSpacing, new Vector2(Metrics.Md, Metrics.Md));

        if (!ImGui.BeginPopup(JoinPopupId, ImGuiWindowFlags.NoScrollbar | Sty.PopupFlags))
            return;

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "This show needs a password.");

        Fields.TextInput("##v2joinPassword", ref joinPasswordBuffer, 48,
            popupWidth - (Metrics.Lg * 2f), "Password", password: true);

        if (Fields.Button("Join", Fields.ButtonStyle.Primary, idSuffix: "joinPopup"))
        {
            plugin.DjDeckWindow.JoinPublicShow(show.RoomCode, joinPasswordBuffer);
            pendingJoinRoomCode = string.Empty;
            joinPasswordBuffer = string.Empty;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
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

    /// Moves the category selection one place, for the Up/Down hotkeys while the nav rail is collapsed - see
    /// EchoMixShellWindow.StepRailSelection.
    public bool StepCategory(int delta)
    {
        if (CategoryList.StepKey(Categories, selected.ToString(), delta) is not { } key
            || !Enum.TryParse<Category>(key, out var next))
        {
            return false;
        }

        Go(next);
        return true;
    }

}
