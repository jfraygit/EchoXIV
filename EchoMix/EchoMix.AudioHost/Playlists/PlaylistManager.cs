using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using Newtonsoft.Json;

namespace EchoMix.AudioHost.Playlists;

public sealed class PlaylistManager
{
    private readonly string manifestPath;

    public string LibraryDir { get; }
    public List<Playlist> Playlists { get; private set; } = new();

    public PlaylistManager(string rootDir)
    {
        Directory.CreateDirectory(rootDir);
        LibraryDir = Path.Combine(rootDir, "Library");
        Directory.CreateDirectory(LibraryDir);
        manifestPath = Path.Combine(rootDir, "playlists.json");
        Load();
    }

    public void Load()
    {
        if (!File.Exists(manifestPath))
            return;

        try
        {
            var json = File.ReadAllText(manifestPath);
            Playlists = JsonConvert.DeserializeObject<List<Playlist>>(json) ?? new List<Playlist>();
        }
        catch
        {
            Playlists = new List<Playlist>();
        }

        BackfillMissingDurations();
    }

    /// Tracks added to playlists.json before duration tracking existed deserialize with DurationSeconds at
    /// its default (0), which is what showed up as "(00:00)" in the UI - probe those once here so an existing
    /// library self-heals instead of needing every track re-uploaded to pick up a length.
    private void BackfillMissingDurations()
    {
        var changed = false;
        foreach (var playlist in Playlists)
        {
            foreach (var track in playlist.Tracks)
            {
                if (track.DurationSeconds > 0 || !File.Exists(track.FilePath))
                    continue;

                try
                {
                    using var probe = new AudioFileReader(track.FilePath);
                    track.DurationSeconds = probe.TotalTime.TotalSeconds;
                    changed = true;
                }
                catch
                {
                }
            }
        }

        if (changed)
            Save();
    }

    public void Save()
    {
        var json = JsonConvert.SerializeObject(Playlists, Formatting.Indented);
        File.WriteAllText(manifestPath, json);
    }

    public Playlist CreatePlaylist(string name)
    {
        var playlist = new Playlist { Name = name };
        Playlists.Add(playlist);
        Save();
        return playlist;
    }

    public void DeletePlaylist(Playlist playlist)
    {
        Playlists.Remove(playlist);
        Save();

        foreach (var track in playlist.Tracks)
            DeleteTrackFile(track);
    }

    public void RenamePlaylist(Playlist playlist, string newName)
    {
        playlist.Name = newName;
        Save();
    }

    public void AddTrack(Playlist playlist, Track track)
    {
        playlist.Tracks.Add(track);
        Save();
    }

    public void RemoveTrack(Playlist playlist, Track track)
    {
        playlist.Tracks.Remove(track);
        Save();
        DeleteTrackFile(track);
    }

    /// Every track's file is its own private copy under LibraryDir (see TrackImporter.Import - there's no
    /// "add an existing library track to another playlist" path, only upload, which always makes a fresh
    /// copy), so it's always safe to delete here rather than just orphaning it once nothing in playlists.json
    /// references it anymore - otherwise re-uploading the same file later collides with the leftover orphan
    /// and gets renamed "Name (1).ext" for no reason visible to the DJ.
    private static void DeleteTrackFile(Track track)
    {
        try
        {
            if (File.Exists(track.FilePath))
                File.Delete(track.FilePath);
        }
        catch
        {
        }
    }
}
