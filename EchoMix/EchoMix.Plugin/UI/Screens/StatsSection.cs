using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Cosmetics;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Show statistics - presented as Stats, deliberately not as a leaderboard.
public sealed class StatsSection
{
    /// Board keys are stable identifiers chosen by the relay; the wording lives here, so retitling one is a
    /// plugin change rather than a relay deploy.
    private static readonly (string Key, string Title, string Blurb)[] Boards =
    {
        ("Followers", "Most Followed", "All time, not this month"),
        ("Likes", "Most Liked", "All time, not this month"),
        ("ShowsPlayed", "Shows Played", "Sets that ran long enough to count"),
        ("OnAirSeconds", "Time On Air", "Total hours broadcasting"),
        ("ListenerSeconds", "Listener Hours", "Time spent listening, summed across everyone"),
        ("PeakListeners", "Biggest Room", "Most people in at once"),
        ("DaysActive", "Days Active", "Days with at least one show"),
        ("UniqueListeners", "People Reached", "Different listeners who tuned in"),
    };

    private static readonly string[] WindowLabels = { "This Month", "All Time" };

    private readonly Plugin plugin;

    /// Index into WindowLabels.
    private int window;

    /// Which window the in-flight request was for, so a snapshot arriving after the toggle moved again can be
    /// ignored rather than rendered under the wrong heading.
    private int requestedWindow = -1;

    public StatsSection(Plugin plugin) => this.plugin = plugin;

    private static string CharacterName => Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

    private bool MonthOnly => window == 0;

    public void Draw(bool isSample)
    {
        if (requestedWindow != window)
        {
            requestedWindow = window;
            plugin.DjDeckWindow.RefreshDjStats(CharacterName, MonthOnly);
        }

        var snapshot = plugin.AudioHostClient.LatestDjStats;

#if DEBUG

        if (isSample)
            snapshot = BrowseSampleData.Stats(MonthOnly);
#endif

        DrawHeader();
        Surfaces.Gap(Metrics.Lg);

        if (isSample)
        {
            Surfaces.RowText(
                "Showing sample stats for layout testing. Turn off Sample Browse Data in Settings > "
                + "Appearance to see the real numbers.",
                Semantic.Warning);
            Surfaces.Gap(Metrics.Md);
        }

        if (snapshot == null)
        {
            DrawNotice(FontAwesomeIcon.ChartBar, "Loading stats...");
            return;
        }

        if (!string.IsNullOrEmpty(snapshot.Error))
        {
            DrawNotice(FontAwesomeIcon.ExclamationTriangle, $"Couldn't reach the relay: {snapshot.Error}");
            return;
        }

        DrawYours(snapshot);
        Surfaces.Gap(Metrics.Xxl);
        DrawBoards(snapshot);
    }

    /// Title plus the window toggle.
    private void DrawHeader()
    {
        var toggleWidth = MathF.Round(190f * Metrics.Scale);
        var header = Fields.BeginListHeader("Stats", toggleWidth);

        ImGui.SetCursorScreenPos(header.ControlMin);
        var picked = window;
        if (Fields.Segmented("##v2StatsWindow", ref picked, WindowLabels, toggleWidth, header.Height))
            window = picked;

        Fields.EndListHeader(header);
    }

    /// Your own six numbers, as a row of tiles.
    private void DrawYours(DjStatsSnapshotMessage snapshot)
    {
        Surfaces.SectionHeader(snapshot.IsMonth ? $"Your {MonthName(snapshot.MonthKey)} Stats" : "Your Lifetime Stats");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        if (snapshot.You is not { } you)
        {
            Surfaces.RowText(
                "You don't have a DJ listing yet, so there's nothing to count. Create one under "
                + "Your Listing and your shows start being recorded from then on.",
                Semantic.TextTertiary);

            Surfaces.EndPanel();
            return;
        }

        var tiles = new (string Label, string Value)[]
        {
            ("Shows", you.ShowsPlayed.ToString(CultureInfo.InvariantCulture)),
            ("On Air", Duration(you.OnAirSeconds)),
            ("Listener Hours", Duration(you.ListenerSeconds)),
            ("Biggest Room", you.PeakListeners.ToString(CultureInfo.InvariantCulture)),
            ("Days Active", you.DaysActive.ToString(CultureInfo.InvariantCulture)),
            ("People Reached", you.UniqueListeners.ToString(CultureInfo.InvariantCulture)),
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

        Surfaces.Gap(Metrics.Md);
        Surfaces.RowText(
            $"A show counts once it has run {snapshot.QualifyingMinutes} minutes.",
            Semantic.TextTertiary);

        Surfaces.EndPanel();
    }

    /// The community boards, as a responsive grid of small panels.
    private void DrawBoards(DjStatsSnapshotMessage snapshot)
    {
        Surfaces.SectionHeader("Across the Community");
        Surfaces.Gap(Metrics.Md);

        var lookup = new Dictionary<string, DjStatBoardDto>(StringComparer.Ordinal);
        foreach (var board in snapshot.Boards)
            lookup[board.Key] = board;

        var width = Surfaces.AutoWidth();
        var gap = Metrics.Lg;

        var columns = Math.Clamp((int)MathF.Floor((width + gap) / ((280f * Metrics.Scale) + gap)), 1, 3);
        var panelWidth = MathF.Round((width - (gap * (columns - 1))) / columns);

        var blurbWrap = Surfaces.ContentWidthFor(panelWidth);
        var blurbHeight = 0f;

        foreach (var (key, _, blurb) in Boards)
        {
            if (!lookup.TryGetValue(key, out var measured) || measured.Entries.Count == 0)
                continue;

            using (TypeScale.Caption())
                blurbHeight = MathF.Max(blurbHeight, ImGui.CalcTextSize(blurb, false, blurbWrap).Y);
        }

        var drawn = 0;
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var rowHeight = 0f;
        var rowTop = origin.Y;

        foreach (var (key, title, blurb) in Boards)
        {
            if (!lookup.TryGetValue(key, out var board) || board.Entries.Count == 0)
                continue;

            var column = drawn % columns;
            if (column == 0 && drawn > 0)
            {
                rowTop += rowHeight + gap;
                rowHeight = 0f;
            }

            var pos = new Vector2(origin.X + (column * (panelWidth + gap)), rowTop);
            ImGui.SetCursorScreenPos(pos);

            var height = DrawBoard(board, title, blurb, panelWidth, blurbHeight);
            rowHeight = MathF.Max(rowHeight, height);
            drawn++;
        }

        if (drawn == 0)
        {
            DrawNotice(FontAwesomeIcon.ChartBar,
                snapshot.IsMonth
                    ? "Nothing recorded this month yet. Play a show and it shows up here."
                    : "Nothing recorded yet. Play a show and it shows up here.");
            return;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, rowTop + rowHeight - origin.Y));
    }

    /// One board.
    private float DrawBoard(DjStatBoardDto board, string title, string blurb, float width, float blurbHeight)
    {
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());

        Surfaces.BeginPanel(width);

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextPrimary, title);

        var blurbTop = ImGui.GetCursorScreenPos().Y;

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, blurb);

        if (blurbHeight > 0f)
        {
            ImGui.SetCursorScreenPos(new Vector2(
                ImGui.GetCursorScreenPos().X,
                blurbTop + blurbHeight + ImGui.GetStyle().ItemSpacing.Y));
        }

        Surfaces.Gap(Metrics.Md);

        var inner = Surfaces.ContentWidth;
        var drawList = ImGui.GetWindowDrawList();
        var rowHeight = MathF.Round(Metrics.ControlMd);
        var avatar = MathF.Round(Metrics.ControlXs);
        var shown = Math.Min(board.Entries.Count, 5);

        for (var i = 0; i < shown; i++)
        {
            var entry = board.Entries[i];
            var pos = Chrome.Snap(ImGui.GetCursorScreenPos());

            if (entry.IsRequester)
                drawList.AddRectFilled(
                    new Vector2(pos.X - Metrics.Sm, pos.Y),
                    new Vector2(pos.X + inner + Metrics.Sm, pos.Y + rowHeight),
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.12f)), Metrics.RadiusSoft);

            var rankWidth = MathF.Round(Metrics.Xl);
            using (TypeScale.Caption())
            {
                var label = entry.Rank.ToString(CultureInfo.InvariantCulture);
                var size = ImGui.CalcTextSize(label);
                Chrome.Text(drawList,
                    Chrome.CenterY(pos.X + rankWidth - size.X, pos.Y, rowHeight, size.Y),
                    ImGui.GetColorU32(Semantic.TextDisabled), label);
            }

            var avatarPos = Chrome.Snap(new Vector2(
                pos.X + rankWidth + Metrics.Md, pos.Y + ((rowHeight - avatar) * 0.5f)));

            DrawRowAvatar(drawList, entry, avatarPos, avatar);

            var valueText = Format(board.Key, entry.Value);
            float valueWidth;
            using (TypeScale.Caption())
                valueWidth = ImGui.CalcTextSize(valueText).X;

            var nameLeft = avatarPos.X + avatar + Metrics.Md;
            var nameWidth = MathF.Max(Metrics.Xxl, inner - (nameLeft - pos.X) - valueWidth - Metrics.Lg);

            var nameColor = new Vector4(entry.NameColorR, entry.NameColorG, entry.NameColorB, 1f);
            if (nameColor.X + nameColor.Y + nameColor.Z < 0.05f)
                nameColor = Semantic.TextPrimary;

            ImGui.SetCursorScreenPos(new Vector2(nameLeft, pos.Y + ((rowHeight - ImGui.GetTextLineHeight()) * 0.5f)));
            DjCosmetics.DrawDjName(plugin.Fonts, UiHelpers.TruncateToWidth(entry.DjName, nameWidth),
                nameColor, entry.NameEffect, 0.7f, Metrics.Scale);

            using (TypeScale.Caption())
                Chrome.Text(drawList,
                    Chrome.CenterY(pos.X + inner - valueWidth - Metrics.Xs, pos.Y, rowHeight, ImGui.GetTextLineHeight()),
                    ImGui.GetColorU32(entry.IsRequester ? Semantic.Primary : Semantic.TextSecondary), valueText);

            ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + rowHeight));
        }

        Surfaces.EndPanel();

        return ImGui.GetCursorScreenPos().Y - origin.Y;
    }

    private void DrawRowAvatar(ImDrawListPtr drawList, DjStatEntryDto entry, Vector2 pos, float size)
    {
        var texture = plugin.DjDeckWindow.DjAvatarImage(entry.ProfileId, entry.AvatarBase64);
        var box = new Vector2(size, size);

        if (texture != null)
        {
            drawList.AddImageRounded(texture.Handle, pos, pos + box, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), Metrics.RadiusSharp);
        }
        else
        {
            drawList.AddRectFilled(pos, pos + box, ImGui.GetColorU32(Elevation.Sunken), Metrics.RadiusSharp);
        }

        drawList.AddRect(pos, pos + box,
            ImGui.GetColorU32(Semantic.Alpha(new Vector4(entry.FrameColorR, entry.FrameColorG, entry.FrameColorB, 1f), 0.6f)),
            Metrics.RadiusSharp, ImDrawFlags.None, Metrics.Hairline);
    }

    /// Seconds render as a duration, everything else as a plain count.
    private static string Format(string key, long value) => key switch
    {
        "OnAirSeconds" or "ListenerSeconds" => Duration(value),
        _ => value.ToString("N0", CultureInfo.InvariantCulture),
    };

    /// Shared with the profile page's own all-time strip, so a duration reads the same in both places rather
    /// than each inventing its own rounding.
    public static string FormatDuration(long seconds) => Duration(seconds);

    private static string Duration(long seconds)
    {
        if (seconds < 60)
            return $"{seconds}s";

        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;

        if (hours == 0)
            return $"{minutes}m";

        return hours >= 100 ? $"{hours:N0}h" : $"{hours}h {minutes}m";
    }

    private static string MonthName(string? monthKey)
    {
        if (monthKey != null && DateTime.TryParseExact(monthKey, "yyyy-MM",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("MMMM", CultureInfo.InvariantCulture);
        }

        return "Month";
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
