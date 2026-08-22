namespace EchoMix.AudioHost.Playlists;

public sealed class SoundPad
{
    public string Label { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public bool Looping { get; set; }
    public float LoopIntervalSeconds { get; set; } = 1f;
    public float Volume { get; set; } = 1f;
}
