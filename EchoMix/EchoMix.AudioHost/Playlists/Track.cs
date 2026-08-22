namespace EchoMix.AudioHost.Playlists;

public sealed class Track
{
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }

    /// Per-track gain override, applied to the deck's fader whenever this track loads - lets songs that are
    /// mixed quieter/louder than the rest of a playlist play back at a consistent level without the user
    /// riding the fader manually every time.
    public float Gain { get; set; } = 1f;

    /// Beats-per-minute, filled in once by BpmAnalyzer shortly after import (runs off-thread since it
    /// requires a full decode, unlike the cheap duration probe TrackImporter already does - see IpcServer's
    /// UploadTrack handler), or set directly by the DJ via the playlist's "Edit BPM" context-menu entry when
    /// auto-detection is wrong or unavailable.
    public float? Bpm { get; set; }

    /// Timestamp (in seconds, within this track's own file) of the first confidently- detected beat - the
    /// anchor for a simple beatgrid: every beat after it is assumed to fall at BeatGridOffsetSeconds + n *
    /// (60 / Bpm) (see DeckEngine.CurrentBeatPhase01).
    public float? BeatGridOffsetSeconds { get; set; }
}
