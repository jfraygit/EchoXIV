using System.Collections.Generic;
using EchoMix.AudioHost.Audio;
using EchoMix.Shared;

namespace EchoMix.AudioHost.Playlists;

/// Per-deck queue of upcoming songs - runtime-only session state, not persisted (unlike playlists/sound
/// pads), since "what's queued up right now" doesn't need to survive a restart.
public sealed class DeckQueueManager
{
    public List<Track> QueueA { get; } = new();
    public List<Track> QueueB { get; } = new();

    private bool autoplayEnabledA = true;
    private bool autoplayEnabledB = true;

    private List<Track> QueueFor(DeckId id) => id == DeckId.A ? QueueA : QueueB;

    public bool IsAutoplayEnabled(DeckId id) => id == DeckId.A ? autoplayEnabledA : autoplayEnabledB;

    public void SetAutoplayEnabled(DeckId id, bool enabled)
    {
        if (id == DeckId.A)
            autoplayEnabledA = enabled;
        else
            autoplayEnabledB = enabled;
    }

    public void Assign(DeckId id, Track track) => QueueFor(id).Add(track);

    public void RemoveAt(DeckId id, int index)
    {
        var queue = QueueFor(id);
        if (index >= 0 && index < queue.Count)
            queue.RemoveAt(index);
    }

    /// Called every status-tick for both decks.
    public bool Pump(DeckEngine deck, DeckId id)
    {
        if (deck.HasTrack && !deck.HasEnded)
            return false;

        var queue = QueueFor(id);
        if (queue.Count == 0)
        {
            if (deck.HasEnded)
            {
                deck.Unload();
                return true;
            }

            return false;
        }

        var next = queue[0];
        queue.RemoveAt(0);
        deck.LoadTrack(next.FilePath, next.Title, next.Bpm, next.BeatGridOffsetSeconds);
        deck.Trim = next.Gain;
        if (IsAutoplayEnabled(id))
            deck.Play();
        return true;
    }
}
