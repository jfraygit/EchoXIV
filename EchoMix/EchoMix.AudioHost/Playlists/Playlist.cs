using System.Collections.Generic;

namespace EchoMix.AudioHost.Playlists;

public sealed class Playlist
{
    public string Name { get; set; } = string.Empty;
    public List<Track> Tracks { get; set; } = new();
}
