using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using EchoMix.Plugin.UI.Controls;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI;

public sealed class PlaylistPanel
{
    private readonly Plugin plugin;
    private readonly FileDialogManager fileDialogManager = new();

    private string? selectedPlaylistName;
    private string newPlaylistName = string.Empty;

    public PlaylistPanel(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        fileDialogManager.Draw();

        var scale = Math.Clamp(plugin.Configuration.UiScale, 0.75f, 1.5f);

        var client = plugin.AudioHostClient;
        var playlists = client.LatestPlaylists;
        var selected = playlists.FirstOrDefault(p => p.Name == selectedPlaylistName);

        ImGui.SetNextItemWidth(240 * scale);
        ImGui.InputTextWithHint("##new-playlist", "New playlist name", ref newPlaylistName, 64);
        ImGui.SameLine();
        if (PanelButton.Draw("##createPlaylist", plugin.Fonts.Icon, FontAwesomeIcon.Plus, null, new Vector2(32, 24) * scale, Theme.NeutralAccent)
            && !string.IsNullOrWhiteSpace(newPlaylistName))
        {
            var name = newPlaylistName.Trim();
            client.Send(MessageType.CreatePlaylist, new PlaylistNameCommand { PlaylistName = name });
            selectedPlaylistName = name;
            newPlaylistName = string.Empty;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        const float padding = 10f;
        var sidebarWidth = 220f * scale;
        var columnHeight = MathF.Max(200f * scale, ImGui.GetContentRegionAvail().Y);
        var tracksWidth = MathF.Max(200f * scale, ImGui.GetContentRegionAvail().X - sidebarWidth - ImGui.GetStyle().ItemSpacing.X);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding) * scale);
        ImGui.BeginChild("##playlist-sidebar", new Vector2(sidebarWidth, columnHeight), false, ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(scale);
        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextColored(Theme.NeutralAccent, "PLAYLISTS");
        ImGui.Separator();
        ImGui.Spacing();

        if (playlists.Count == 0)
            ImGui.TextDisabled("No playlists yet - create one above.");

        foreach (var playlist in playlists)
        {
            if (ImGui.Selectable(playlist.Name, playlist.Name == selectedPlaylistName))
                selectedPlaylistName = playlist.Name;
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();

        ImGui.SameLine();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding) * scale);
        ImGui.BeginChild("##playlist-tracks", new Vector2(tracksWidth, columnHeight), false, ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(scale);
        DrawTrackList(selected, scale, tracksWidth);
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    /// Right-aligns an element against this column's own known width - same technique (and same reasoning:
    /// ImGui.GetContentRegionAvail() shifts once a scrollbar actually renders) DjDeckWindow's bottom-section
    /// queue/playlist columns already use.
    private static float RightAlignedX(float columnWidth, float elementsWidth, float scale) =>
        columnWidth - (10f * scale) - ImGui.GetStyle().ScrollbarSize - elementsWidth;

    private void DrawTrackList(PlaylistDto? selected, float scale, float columnWidth)
    {
        var client = plugin.AudioHostClient;

        if (selected == null)
        {
            ImGui.TextDisabled("Select a playlist to see its songs, or create one above.");
            return;
        }

        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextColored(Theme.NeutralAccent, selected.Name);

        var headerButtonSize = new Vector2(32, 26) * scale;
        var headerButtonsWidth = (headerButtonSize.X * 2f) + (8f * scale);
        ImGui.SameLine(RightAlignedX(columnWidth, headerButtonsWidth, scale));
        if (PanelButton.Draw("##uploadTrack", plugin.Fonts.Icon, FontAwesomeIcon.Upload, null, headerButtonSize, Theme.NeutralAccent))
            OpenUploadDialog(selected.Name);
        ImGui.SameLine();
        if (PanelButton.Draw("##deletePlaylist", plugin.Fonts.Icon, FontAwesomeIcon.Trash, null, headerButtonSize, Theme.NeutralAccent))
        {
            client.Send(MessageType.DeletePlaylist, new PlaylistNameCommand { PlaylistName = selected.Name });
            selectedPlaylistName = null;
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (selected.Tracks.Count == 0)
        {
            ImGui.TextDisabled("No songs yet - use Upload above to add some.");
            return;
        }

        foreach (var track in selected.Tracks)
        {
            ImGui.PushID(track.FilePath);
            ImGui.AlignTextToFramePadding();
            ImGui.Text(track.Title);
            ImGui.SameLine();
            ImGui.TextDisabled($"({FormatTime(track.DurationSeconds)})");

            var removeSize = new Vector2(30, 24) * scale;
            ImGui.SameLine(RightAlignedX(columnWidth, removeSize.X, scale));
            if (PanelButton.Draw("##removeTrack", plugin.Fonts.Icon, FontAwesomeIcon.Times, null, removeSize, Theme.NeutralAccent))
                client.Send(MessageType.RemoveTrack, new RemoveTrackCommand { PlaylistName = selected.Name, TrackFilePath = track.FilePath });

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PopID();
        }
    }

    private static string FormatTime(double seconds)
    {
        var t = System.TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    private void OpenUploadDialog(string playlistName)
    {
        fileDialogManager.OpenFileDialog(
            "Select audio files to upload",
            "Audio files{.mp3,.wav,.wma,.aac,.m4a,.flac,.ogg}",
            (success, paths) =>
            {
                if (!success)
                    return;

                foreach (var path in paths)
                {
                    plugin.AudioHostClient.Send(
                        MessageType.UploadTrack, new UploadTrackCommand { PlaylistName = playlistName, SourceFilePath = path });
                }
            },
            20,
            null,
            false);
    }
}
