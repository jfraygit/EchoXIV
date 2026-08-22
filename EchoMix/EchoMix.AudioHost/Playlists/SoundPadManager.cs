using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace EchoMix.AudioHost.Playlists;

/// Persists the 8 sound-effect pad slots (label + assigned file) to disk, mirroring PlaylistManager's
/// manifest-plus-library-folder approach, so pads survive across sessions.
public sealed class SoundPadManager
{
    public const int PadCount = 8;

    private readonly string manifestPath;

    public string LibraryDir { get; }
    public List<SoundPad> Pads { get; private set; } = new();

    public SoundPadManager(string rootDir)
    {
        Directory.CreateDirectory(rootDir);
        LibraryDir = Path.Combine(rootDir, "SoundPads");
        Directory.CreateDirectory(LibraryDir);
        manifestPath = Path.Combine(rootDir, "soundpads.json");
        Load();
    }

    public void Load()
    {
        if (File.Exists(manifestPath))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                Pads = JsonConvert.DeserializeObject<List<SoundPad>>(json) ?? new List<SoundPad>();
            }
            catch
            {
                Pads = new List<SoundPad>();
            }
        }

        while (Pads.Count < PadCount)
            Pads.Add(new SoundPad { Label = $"PAD {Pads.Count + 1}" });
    }

    public void Save()
    {
        var json = JsonConvert.SerializeObject(Pads, Formatting.Indented);
        File.WriteAllText(manifestPath, json);
    }

    public void Upload(int index, string sourceFilePath)
    {
        if (index < 0 || index >= Pads.Count || !TrackImporter.IsSupported(sourceFilePath))
            return;

        var track = TrackImporter.Import(sourceFilePath, LibraryDir);
        var pad = Pads[index];
        pad.FilePath = track.FilePath;

        if (string.IsNullOrWhiteSpace(pad.Label) || pad.Label.StartsWith("PAD "))
            pad.Label = track.Title;

        Save();
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= Pads.Count)
            return;

        var pad = Pads[index];
        if (!string.IsNullOrEmpty(pad.FilePath) && File.Exists(pad.FilePath))
        {
            try { File.Delete(pad.FilePath); } catch { }
        }

        pad.FilePath = null;
        pad.Label = $"PAD {index + 1}";
        pad.Looping = false;
        pad.LoopIntervalSeconds = 1f;
        pad.Volume = 1f;
        Save();
    }

    public void SetLabel(int index, string label)
    {
        if (index < 0 || index >= Pads.Count)
            return;

        Pads[index].Label = label;
        Save();
    }

    public void SetLooping(int index, bool looping)
    {
        if (index < 0 || index >= Pads.Count)
            return;

        Pads[index].Looping = looping;
        Save();
    }

    public void SetLoopInterval(int index, float intervalSeconds)
    {
        if (index < 0 || index >= Pads.Count)
            return;

        Pads[index].LoopIntervalSeconds = System.Math.Max(0.1f, intervalSeconds);
        Save();
    }

    public void SetVolume(int index, float volume)
    {
        if (index < 0 || index >= Pads.Count)
            return;

        Pads[index].Volume = System.Math.Clamp(volume, 0f, 2f);
        Save();
    }
}
