using System;
using System.IO;
using System.Linq;
using EchoMix.AudioHost.Audio;

namespace EchoMix.AudioHost.Playlists;

/// Copies a picked file into the plugin's local library folder.
public static class TrackImporter
{
    private static readonly string[] SupportedExtensions = { ".mp3", ".wav", ".wma", ".aac", ".m4a", ".flac", ".ogg" };

    private const long MaxFileSizeBytes = 100L * 1024 * 1024;

    public static bool IsSupported(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());

    public static bool ExceedsMaxSize(string filePath) =>
        new FileInfo(filePath).Length > MaxFileSizeBytes;

    public static Track Import(string sourceFilePath, string libraryDir)
    {
        if (!IsSupported(sourceFilePath))
            throw new NotSupportedException($"Unsupported audio format: {Path.GetExtension(sourceFilePath)}");
        if (ExceedsMaxSize(sourceFilePath))
            throw new NotSupportedException($"File is too large (over {MaxFileSizeBytes / (1024 * 1024)} MB): {Path.GetFileName(sourceFilePath)}");

        var fileName = Path.GetFileName(sourceFilePath);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var destPath = Path.Combine(libraryDir, fileName);

        var suffix = 1;
        while (File.Exists(destPath))
            destPath = Path.Combine(libraryDir, $"{baseName} ({suffix++}){ext}");

        File.Copy(sourceFilePath, destPath);

        double durationSeconds;
        using (var probe = AudioReaderFactory.Open(destPath))
            durationSeconds = probe.TotalTime.TotalSeconds;

        return new Track
        {
            Title = Path.GetFileNameWithoutExtension(destPath),
            FilePath = destPath,
            DurationSeconds = durationSeconds,
        };
    }
}
