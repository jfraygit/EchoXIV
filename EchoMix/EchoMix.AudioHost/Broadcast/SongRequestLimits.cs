using System.IO;
using EchoMix.AudioHost.Playlists;

namespace EchoMix.AudioHost.Broadcast;

/// Format/size gate for a listener's uploaded song request - checked once before the listener's own AudioHost
/// starts chunking the file, and again by the receiving host once the full file's back together (never trust
/// the sender alone).
public static class SongRequestLimits
{
    private const long MaxFileSizeBytes = 20L * 1024 * 1024;

    public static bool IsSupported(string filePath) => TrackImporter.IsSupported(filePath);

    public static bool ExceedsMaxSize(string filePath) => new FileInfo(filePath).Length > MaxFileSizeBytes;

    public static bool ExceedsMaxSize(long fileSizeBytes) => fileSizeBytes > MaxFileSizeBytes;
}
