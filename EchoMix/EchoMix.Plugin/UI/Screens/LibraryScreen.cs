using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using EchoMix.Plugin.Ipc;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Your music, in one place.
public sealed class LibraryScreen
{
    private enum Category
    {
        Playlists,
    }

    private static readonly CategoryList.Item[] Categories =
    {
        new(nameof(Category.Playlists), "Playlists", FontAwesomeIcon.ListUl, "Your songs and sets"),
    };

    private readonly Plugin plugin;
    private readonly CategoryList categoryList = new();
    private readonly FileDialogManager uploadDialog = new();
    private Category selected = Category.Playlists;

    private readonly Dictionary<string, float> gainBuffers = new();
    private readonly Dictionary<string, float> bpmBuffers = new();

    public LibraryScreen(Plugin plugin) => this.plugin = plugin;

    private State.EchoMixEditState Edit => plugin.EditState;

    /// The upload picker's dialog, pumped by the shell - this screen is drawn by the shell rather than by its
    /// own window, so nothing else would.
    public void DrawDialogs() => uploadDialog.Draw();

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var listWidth = CategoryList.DefaultWidth;
        var origin = ImGui.GetCursorScreenPos();

        var catHeight = CategoryList.HeightFor(Categories.Length);

        var picked = categoryList.Draw("##libraryCat", origin, new Vector2(listWidth, catHeight),
            Categories, selected.ToString());
        if (Enum.TryParse<Category>(picked, out var next))
            selected = next;

        var queuesTop = origin + new Vector2(0f, catHeight + Metrics.Xxl);
        var queuesHeight = MathF.Max(1f, avail.Y - catHeight - Metrics.Xxl - Metrics.Md);
        DrawDeckQueues(queuesTop, new Vector2(listWidth, queuesHeight));

        ImGui.SetCursorScreenPos(origin + new Vector2(listWidth + Metrics.Xxl, 0f));
        var detailWidth = MathF.Max(1f, avail.X - listWidth - Metrics.Xxl);

        Surfaces.ReserveScrollbar = true;

        ImGui.BeginChild("##libraryDetail", new Vector2(detailWidth, avail.Y), false);
        DrawPlaylistColumn();
        ImGui.EndChild();

        Surfaces.ReserveScrollbar = false;
    }


    private void DrawPlaylistColumn()
    {
        var client = plugin.AudioHostClient;
        var playlists = client.LatestPlaylists;
        var current = playlists.FirstOrDefault(p => p.Name == Edit.SelectedPlaylistName);

        Surfaces.SectionHeader("Playlists");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var names = new List<string>(playlists.Count);
        foreach (var playlist in playlists)
            names.Add(playlist.Name);

        var index = current == null ? -1 : names.IndexOf(current.Name);
        if (Fields.Dropdown("##v2PlaylistPick", "Playlist", ref index, names,
                playlists.Count == 0 ? "None yet - create one below." : "Which set you're working on."))
        {
            Edit.SelectedPlaylistName = index >= 0 && index < names.Count ? names[index] : null;
        }

        if (current != null)
        {
            Fields.Divider();
            Surfaces.BeginRowAligned();

            if (Fields.Button("Add Songs", icon: FontAwesomeIcon.Upload))
                OpenUploadDialog(current.Name);

            ImGui.SameLine(0f, Metrics.Md);
            if (Fields.Button("Delete Playlist", Fields.ButtonStyle.Danger, icon: FontAwesomeIcon.Trash))
            {
                client.Send(MessageType.DeletePlaylist, new PlaylistNameCommand { PlaylistName = current.Name });
                Edit.SelectedPlaylistName = null;
            }

            Surfaces.EndRowAligned();
        }

        Fields.Divider();
        Surfaces.Gap(Metrics.Sm);

        var createWidth = Surfaces.BeginRowAligned();
        var buttonWidth = MathF.Round(Metrics.Xxxl * 2.6f);
        var fieldWidth = MathF.Max(Metrics.Xxxl, createWidth - buttonWidth - Metrics.Md);
        var frameHeight = MathF.Round(ImGui.GetTextLineHeight() + (Metrics.Md * 2f));

        Fields.TextInput("##v2NewPlaylist", ref Edit.NewPlaylistNameBuffer, 64, fieldWidth, "New playlist name");

        ImGui.SameLine(0f, Metrics.Md);
        var canCreate = !string.IsNullOrWhiteSpace(Edit.NewPlaylistNameBuffer);
        if (Fields.Button("Create", Fields.ButtonStyle.Secondary, buttonWidth, canCreate,
                FontAwesomeIcon.Plus, frameHeight) && canCreate)
        {
            var name = Edit.NewPlaylistNameBuffer.Trim();
            client.Send(MessageType.CreatePlaylist, new PlaylistNameCommand { PlaylistName = name });

            Edit.SelectedPlaylistName = name;
            Edit.NewPlaylistNameBuffer = string.Empty;
        }

        Surfaces.EndRowAligned();
        Surfaces.EndPanel();

        if (current == null)
            return;

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader(current.Tracks.Count == 1 ? "1 Song" : $"{current.Tracks.Count} Songs");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        if (current.Tracks.Count == 0)
        {
            Surfaces.RowText("Nothing here yet - use Add Songs above.", Semantic.TextTertiary);
            Surfaces.EndPanel();
            return;
        }

        var broadcast = client.LatestStatus.Broadcast;
        var isCoHost = broadcast.IsLive && !broadcast.IsLead;

        var queues = client.LatestDeckQueues;
        var inA = PathsIn(queues.QueueA);
        var inB = PathsIn(queues.QueueB);

        var rowWidth = Surfaces.BeginRowAligned();
        foreach (var track in current.Tracks)
            DrawTrackRow(client, current.Name, track, rowWidth, isCoHost, inA, inB);
        Surfaces.EndRowAligned();

        Surfaces.EndPanel();
    }

    private void DrawTrackRow(
        AudioHostClient client,
        string playlistName,
        TrackDto track,
        float width,
        bool isCoHost,
        HashSet<string> inA,
        HashSet<string> inB)
    {
        var rowHeight = MathF.Round(Metrics.ControlLg);
        var pos = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        var buttonWidth = MathF.Round(Metrics.Xxxl);
        var buttonHeight = MathF.Round(Metrics.ControlSm);
        var buttonsWidth = isCoHost
            ? MathF.Round(Metrics.Xxxl * 2.6f)
            : (buttonWidth * 2f) + Metrics.Sm;

        var hitWidth = MathF.Max(Metrics.Xxl, width - buttonsWidth - Metrics.Md);
        ImGui.InvisibleButton($"##track{track.FilePath}", new Vector2(hitWidth, rowHeight));
        var rowHovered = ImGui.IsItemHovered();

        DrawTrackMenu(client, playlistName, track);

        if (rowHovered)
            drawList.AddRectFilled(pos, pos + new Vector2(width, rowHeight),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.04f)), Metrics.RadiusSoft);

        var meta = track.Bpm is > 0f
            ? $"{UiHelpers.FormatClock(track.DurationSeconds)}   {track.Bpm:0.#} BPM"
            : UiHelpers.FormatClock(track.DurationSeconds);

        float metaWidth;
        using (TypeScale.Caption())
            metaWidth = ImGui.CalcTextSize(meta).X;

        var titleRoom = MathF.Max(Metrics.Xxl, width - buttonsWidth - metaWidth - (Metrics.Lg * 3f));

        using (TypeScale.Body())
        {
            var shown = UiHelpers.TruncateToWidth(track.Title, titleRoom);
            var size = ImGui.CalcTextSize(shown);
            Chrome.Text(drawList, Chrome.CenterY(pos.X + Metrics.Md, pos.Y, rowHeight, size.Y),
                ImGui.GetColorU32(Semantic.TextPrimary), shown);
        }

        using (TypeScale.Caption())
        {
            var size = ImGui.CalcTextSize(meta);
            Chrome.Text(drawList,
                Chrome.CenterY(pos.X + width - buttonsWidth - Metrics.Lg - size.X, pos.Y, rowHeight, size.Y),
                ImGui.GetColorU32(Semantic.TextTertiary), meta);
        }

        var buttonY = pos.Y + ((rowHeight - buttonHeight) * 0.5f);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + width - buttonsWidth, buttonY));

        if (isCoHost)
        {
            if (Fields.Button("Request", Fields.ButtonStyle.Secondary, buttonsWidth, true,
                    FontAwesomeIcon.Upload, buttonHeight, idSuffix: track.FilePath))
            {
                client.Send(MessageType.RequestSong, new RequestSongCommand
                {
                    SourceFilePath = track.FilePath,
                    RequesterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "Unknown",
                });
            }
        }
        else
        {
            if (Fields.DeckButton($"##assignA{track.FilePath}", "A", Semantic.DeckA, buttonWidth, buttonHeight,
                    inA.Contains(track.FilePath)))
            {
                AssignTrack(client, DeckId.A, track);
            }

            ImGui.SameLine(0f, Metrics.Sm);
            if (Fields.DeckButton($"##assignB{track.FilePath}", "B", Semantic.DeckB, buttonWidth, buttonHeight,
                    inB.Contains(track.FilePath)))
            {
                AssignTrack(client, DeckId.B, track);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + rowHeight));

        if (rowHovered)
            Tip.Hovered(track.Title, "Right-click for gain and BPM.");
    }

    /// A compact deck-assign button.

    /// Right-click a song for its saved gain and its BPM.
    private void DrawTrackMenu(AudioHostClient client, string playlistName, TrackDto track)
    {
        var popupId = $"##trackMenu{track.FilePath}";

        ImGui.OpenPopupOnItemClick(popupId, ImGuiPopupFlags.MouseButtonRight);

        if (!ImGui.IsPopupOpen(popupId))
            return;

        using var detached = Surfaces.Detach();

        var trackWidth = 130f * Metrics.Scale;
        var popupWidth = Fields.MeasureSliderRow("Gain", trackWidth) + (Metrics.Md * 2f);
        ImGui.SetNextWindowSize(new Vector2(popupWidth, 0f));

        using var style = Sty.Popup()
            .Var(ImGuiStyleVar.WindowPadding, new Vector2(Metrics.Md, Metrics.Md))
            .Var(ImGuiStyleVar.ItemSpacing, new Vector2(Metrics.Md, Metrics.Xs));

        if (!ImGui.BeginPopup(popupId, Sty.PopupFlags))
            return;

        if (ImGui.IsWindowAppearing())
        {
            gainBuffers[track.FilePath] = track.Gain;
            bpmBuffers[track.FilePath] = track.Bpm ?? 120f;
        }

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, UiHelpers.TruncateToWidth(track.Title, popupWidth - (Metrics.Lg * 2f)));

        var gain = gainBuffers.TryGetValue(track.FilePath, out var g) ? g : track.Gain;
        if (Fields.Slider($"##gain{track.FilePath}", "Gain", ref gain, 0f, 2f, "{0:F2}x", 1f,
                trackWidth: trackWidth))
            gainBuffers[track.FilePath] = gain;

        var bpm = bpmBuffers.TryGetValue(track.FilePath, out var b) ? b : track.Bpm ?? 120f;
        if (Fields.Slider($"##bpm{track.FilePath}", "BPM", ref bpm, 60f, 200f, "{0:F0}", track.Bpm ?? 120f,
                trackWidth: trackWidth))
            bpmBuffers[track.FilePath] = bpm;

        Surfaces.Gap(Metrics.Sm);
        Surfaces.BeginRowAligned();

        if (Fields.Button("Save", Fields.ButtonStyle.Primary, idSuffix: track.FilePath))
        {
            client.Send(MessageType.SetTrackGain, new SetTrackGainCommand
            {
                PlaylistName = playlistName,
                TrackFilePath = track.FilePath,
                Gain = gain,
            });

            if (bpm > 0f)
            {
                client.Send(MessageType.SetTrackBpm, new SetTrackBpmCommand
                {
                    PlaylistName = playlistName,
                    TrackFilePath = track.FilePath,
                    Bpm = bpm,
                });
            }

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0f, Metrics.Md);
        if (Fields.Button("Remove", Fields.ButtonStyle.Danger, icon: FontAwesomeIcon.Trash,
                idSuffix: track.FilePath))
        {
            client.Send(MessageType.RemoveTrack, new RemoveTrackCommand
            {
                PlaylistName = playlistName,
                TrackFilePath = track.FilePath,
            });

            ImGui.CloseCurrentPopup();
        }

        Surfaces.EndRowAligned();
        ImGui.EndPopup();
    }

    /// What is queued on each deck, listed track by track, down the side column.
    private void DrawDeckQueues(Vector2 origin, Vector2 size)
    {
        var queues = plugin.AudioHostClient.LatestDeckQueues;
        var status = plugin.AudioHostClient.LatestStatus;

        var half = MathF.Round((size.Y - Metrics.Lg) * 0.5f);
        if (half < Metrics.ControlLg)
            return;

        DrawDeckQueue(origin, new Vector2(size.X, half),
            "DECK A", queues.QueueA, status.DeckA, Semantic.DeckA);
        DrawDeckQueue(origin + new Vector2(0f, half + Metrics.Lg), new Vector2(size.X, half),
            "DECK B", queues.QueueB, status.DeckB, Semantic.DeckB);
    }

    private void DrawDeckQueue(
        Vector2 origin, Vector2 size, string label, DeckQueueDto queue, DeckStatus deck, Vector4 accent)
    {
        origin = Chrome.Snap(origin);

        var drawList = ImGui.GetWindowDrawList();
        Elevation.DrawSurface(drawList, origin, origin + size, Elevation.Surface,
            Metrics.RadiusCard, Elevation.ShadowSpec.Low, topEdge: true, Semantic.Alpha(accent, 0.22f));

        var padX = Metrics.Lg;
        var lineHeight = ImGui.GetTextLineHeight();
        var rowHeight = MathF.Round(lineHeight + Metrics.Sm);
        var numberWidth = TypeScale.Measure(TypeScale.Caption, "88").X + Metrics.Md;
        var titleX = origin.X + padX + numberWidth;
        var titleWidth = MathF.Max(1f, size.X - padX - numberWidth - padX);
        var y = origin.Y + Metrics.Lg;

        var hasAnything = deck.HasTrack || queue.Tracks.Count > 0;
        var right = origin.X + size.X - padX;

        if (hasAnything)
        {
            var box = MathF.Round(Metrics.ControlSm);
            var boxMin = Chrome.Snap(new Vector2(right - box, y + ((lineHeight - box) * 0.5f)));

            ImGui.SetCursorScreenPos(boxMin);
            if (ImGui.InvisibleButton($"##clear{label}", new Vector2(box, box)))
                ClearDeck(deck.HasTrack, queue, label == "DECK A" ? DeckId.A : DeckId.B);

            var hovered = ImGui.IsItemHovered();
            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (hovered)
            {
                drawList.AddRectFilled(boxMin, boxMin + new Vector2(box, box),
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.Danger, 0.18f)), Metrics.RadiusSoft);
            }

            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.TimesCircle,
                    Chrome.Snap(boxMin + new Vector2(box * 0.5f, box * 0.5f)),
                    ImGui.GetColorU32(hovered ? Semantic.Danger : Semantic.TextTertiary));

            Tip.Hovered("Clear Deck", "Unloads the track and empties this deck's queue.");
            right -= box + Metrics.Md;
        }

        using (TypeScale.Caption())
        {
            Chrome.Text(drawList, new Vector2(origin.X + padX, y), ImGui.GetColorU32(accent), label);

            if (queue.Tracks.Count > 0)
            {
                var count = $"{queue.Tracks.Count} queued";
                var countWidth = ImGui.CalcTextSize(count).X;
                Chrome.Text(drawList, new Vector2(right - countWidth, y),
                    ImGui.GetColorU32(Semantic.TextTertiary), count);
            }
        }

        y += lineHeight + Metrics.Md;

        if (!hasAnything)
        {
            using (TypeScale.Body())
                Chrome.Text(drawList, new Vector2(origin.X + padX, y),
                    ImGui.GetColorU32(Semantic.TextDisabled), "Empty");

            return;
        }

        if (deck.HasTrack)
        {
            var dotRadius = MathF.Max(2f, 3f * Metrics.Scale);
            drawList.AddCircleFilled(
                Chrome.Snap(new Vector2(origin.X + padX + dotRadius, y + (lineHeight * 0.5f))),
                dotRadius,
                ImGui.GetColorU32(Semantic.Alpha(accent, deck.IsPlaying ? 1f : 0.4f)));

            using (TypeScale.Body())
            {
                var loaded = UiHelpers.TruncateToWidth(
                    string.IsNullOrWhiteSpace(deck.TrackTitle) ? "Loaded" : deck.TrackTitle, titleWidth);
                Chrome.Text(drawList, new Vector2(titleX, y), ImGui.GetColorU32(accent), loaded);
            }

            y += rowHeight;

            if (queue.Tracks.Count > 0)
            {
                drawList.AddLine(
                    Chrome.Snap(new Vector2(origin.X + padX, y + Metrics.Xs)),
                    Chrome.Snap(new Vector2(origin.X + size.X - padX, y + Metrics.Xs)),
                    ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);

                y += Metrics.Md;
            }
        }

        if (queue.Tracks.Count == 0)
            return;

        var bottom = origin.Y + size.Y - Metrics.Lg;

        var room = MathF.Max(0f, bottom - y);
        var fits = Math.Max(0, (int)MathF.Floor(room / rowHeight));
        var overflow = queue.Tracks.Count > fits;
        var shown = overflow ? Math.Max(0, fits - 1) : queue.Tracks.Count;

        for (var i = 0; i < shown; i++)
        {
            using (TypeScale.Caption())
                Chrome.Text(drawList, new Vector2(origin.X + padX, y),
                    ImGui.GetColorU32(Semantic.TextDisabled), (i + 1).ToString());

            using (TypeScale.Body())
            {
                var title = UiHelpers.TruncateToWidth(queue.Tracks[i].Title, titleWidth);
                Chrome.Text(drawList, new Vector2(titleX, y),
                    ImGui.GetColorU32(i == 0 ? Semantic.TextPrimary : Semantic.TextSecondary), title);
            }

            y += rowHeight;
        }

        if (!overflow)
            return;

        using (TypeScale.Caption())
        {
            var remaining = queue.Tracks.Count - shown;
            Chrome.Text(drawList, new Vector2(titleX, y),
                ImGui.GetColorU32(Semantic.TextTertiary), $"+{remaining} more");
        }
    }

    /// Unloads the deck and empties its queue.
    private void ClearDeck(bool hasTrack, DeckQueueDto queue, DeckId id)
    {
        var client = plugin.AudioHostClient;

        for (var i = queue.Tracks.Count - 1; i >= 0; i--)
            client.Send(MessageType.RemoveFromDeckQueue, new RemoveFromDeckQueueCommand { Deck = id, Index = i });

        if (hasTrack)
            client.Send(MessageType.UnloadDeck, new DeckCommand { Deck = id });
    }

    private static HashSet<string> PathsIn(DeckQueueDto queue)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in queue.Tracks)
            paths.Add(track.FilePath);

        return paths;
    }


    private static void AssignTrack(AudioHostClient client, DeckId deck, TrackDto track) =>
        client.Send(MessageType.AssignTrackToDeck, new AssignTrackToDeckCommand
        {
            Deck = deck,
            Title = track.Title,
            FilePath = track.FilePath,
            Gain = track.Gain,
            DurationSeconds = track.DurationSeconds,
            Bpm = track.Bpm,
            BeatGridOffsetSeconds = track.BeatGridOffsetSeconds,
        });

    private void OpenUploadDialog(string playlistName) =>
        uploadDialog.OpenFileDialog(
            "Select audio files to upload",
            "Audio files{.mp3,.wav,.wma,.aac,.m4a,.flac,.ogg}",
            (success, paths) =>
            {
                if (!success)
                    return;

                foreach (var path in paths)
                {
                    plugin.AudioHostClient.Send(MessageType.UploadTrack,
                        new UploadTrackCommand { PlaylistName = playlistName, SourceFilePath = path });
                }
            },
            20,
            null,
            false);


    /// Moves the category selection one place, for the Up/Down hotkeys while the nav rail is collapsed - see
    /// EchoMixShellWindow.StepRailSelection.
    public bool StepCategory(int delta)
    {
        if (CategoryList.StepKey(Categories, selected.ToString(), delta) is not { } key
            || !Enum.TryParse<Category>(key, out var next))
        {
            return false;
        }

        selected = next;
        return true;
    }

}
