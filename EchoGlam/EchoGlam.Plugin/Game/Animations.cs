using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace EchoGlam.Game;

/// Where an animation came from, which is only ever used to filter the picker.
public enum AnimationSource
{
    Emote,
    Action,
}

/// One animation that can be played when an outfit goes on.
public readonly record struct AnimationEntry(ushort TimelineId, string Name, uint IconId, AnimationSource Source);

/// Every animation a character can be told to play, out of the game's own data.
public sealed class Animations
{
    private readonly List<AnimationEntry> all = [];

    public Animations()
    {
        try
        {
            var seen = new HashSet<ushort>();

            foreach (var emote in Plugin.DataManager.GetExcelSheet<Emote>())
            {
                var name = emote.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var timeline = 0u;

                foreach (var reference in emote.ActionTimeline)
                {
                    if (reference.RowId == 0)
                        continue;

                    timeline = reference.RowId;
                    break;
                }

                if (timeline == 0 || timeline > ushort.MaxValue || !seen.Add((ushort)timeline))
                    continue;

                all.Add(new AnimationEntry((ushort)timeline, name, emote.Icon, AnimationSource.Emote));
            }

            foreach (var action in Plugin.DataManager.GetExcelSheet<LuminaAction>())
            {
                if (!action.IsPlayerAction)
                    continue;

                var name = action.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var timeline = action.AnimationEnd.RowId;

                if (timeline == 0 || timeline > ushort.MaxValue || !seen.Add((ushort)timeline))
                    continue;

                all.Add(new AnimationEntry((ushort)timeline, name, action.Icon, AnimationSource.Action));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not read the animation list.");
        }

        all = [.. all.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];

        Plugin.Log.Information($"[EchoGlam] Animation list built: {all.Count} emotes and actions.");
    }

    public bool Ready => all.Count > 0;

    public IEnumerable<AnimationEntry> Search(string term, AnimationSource? source)
    {
        var pool = source is { } only ? all.Where(a => a.Source == only) : all;

        if (string.IsNullOrWhiteSpace(term))
            return pool;

        return pool.Where(a => a.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public AnimationEntry? Find(ushort timelineId)
    {
        foreach (var entry in all)
        {
            if (entry.TimelineId == timelineId)
                return entry;
        }

        return null;
    }


    /// What the player is doing, if anything.
    private enum Phase
    {
        None,

        /// Waiting for the character's model to come back before playing.
        Deferred,

        /// Played, and waiting for the game to admit it is playing.
        Starting,

        /// Running.
        Playing,
    }

    private Phase phase;
    private ushort timeline;
    private long since;
    private bool sawDrawObject;
    private System.Action? afterwards;

    /// The earliest the waiting callback may run, whatever the animation slots say.
    private long holdUntil;

    /// Whether something is waiting on an animation, so callers do not start a second one or act as though
    /// the change they asked for has already happened.
    public bool Busy => phase != Phase.None;

    /// How long to keep trying to play before giving up.
    private const long GiveUpAfterMs = 4000;

    /// How long to let the rebuilt model settle before playing.
    private const long SettleMs = 120;

    /// How long to wait for a played animation to show up in the character's timeline slot before concluding
    /// it is not going to.
    private const long StartTimeoutMs = 400;

    /// The longest an animation is allowed to hold up an outfit change.
    private const long LongestWaitMs = 4000;

    /// The animation this character last played for an outfit change, and when.
    public ushort LastPlayed { get; private set; }

    public float LastHold { get; private set; }

    public long LastPlayedAt { get; private set; }

    private void Remember(ushort timelineId, float holdSeconds)
    {
        LastPlayed = timelineId;
        LastHold = holdSeconds;
        LastPlayedAt = Environment.TickCount64;
    }

    /// Plays an animation once the character is in a state to play it.
    public void Request(ushort timelineId)
    {
        if (timelineId == 0)
            return;

        Remember(timelineId, 0f);
        Cancel();

        timeline = timelineId;
        phase = Phase.Deferred;
        since = Environment.TickCount64;
        sawDrawObject = false;
    }

    /// Plays an animation now and runs then when it finishes - which is what makes an outfit change look like
    /// the animation caused it rather than interrupted it.
    public void PlayThen(ushort timelineId, float holdSeconds, System.Action then)
    {
        Cancel();

        if (timelineId == 0)
        {
            then();
            return;
        }

        Remember(timelineId, holdSeconds);

        timeline = timelineId;
        afterwards = then;
        phase = Phase.Starting;
        since = Environment.TickCount64;
        holdUntil = since + (long)(holdSeconds * 1000f);

        Play(timelineId);

        if (Build.Diagnostics)
            Plugin.Log.Information($"[EchoGlam] Played {timelineId}; slots now {DescribeSlots()}");
    }

    /// Drops whatever is pending WITHOUT running its callback.
    public void Cancel()
    {
        phase = Phase.None;
        timeline = 0;
        afterwards = null;
        sawDrawObject = false;
    }

    public void Tick()
    {
        if (phase == Phase.None)
            return;

        var waited = Environment.TickCount64 - since;

        switch (phase)
        {
            case Phase.Deferred:
                TickDeferred(waited);
                break;

            case Phase.Starting:
                TickStarting(waited);
                break;

            case Phase.Playing:
                TickPlaying(waited);
                break;
        }
    }

    private unsafe void TickDeferred(long waited)
    {
        if (waited > GiveUpAfterMs)
        {
            Cancel();
            return;
        }

        var character = LocalCharacter();

        if (character == null || character->GameObject.DrawObject == null)
        {
            sawDrawObject = false;
            return;
        }

        if (!sawDrawObject)
        {
            sawDrawObject = true;
            since = Environment.TickCount64;
            return;
        }

        if (waited < SettleMs)
            return;

        var playing = timeline;
        Cancel();
        Play(playing);
    }

    private void TickStarting(long waited)
    {
        if (IsPlaying(timeline))
        {
            if (Build.Diagnostics)
                Plugin.Log.Information($"[EchoGlam] {timeline} started after {waited}ms; slots {DescribeSlots()}");

            phase = Phase.Playing;
            since = Environment.TickCount64;
            return;
        }

        if (waited > StartTimeoutMs)
        {
            if (Build.Diagnostics)
                Plugin.Log.Information($"[EchoGlam] {timeline} never appeared in {waited}ms; slots {DescribeSlots()}");

            phase = Phase.Playing;
        }
    }

    private void TickPlaying(long waited)
    {
        if (Environment.TickCount64 < holdUntil && waited <= LongestWaitMs)
            return;

        if (!IsPlaying(timeline) || waited > LongestWaitMs)
        {
            if (Build.Diagnostics)
                Plugin.Log.Information($"[EchoGlam] {timeline} finished after {waited}ms; slots {DescribeSlots()}");

            Finish();
        }
    }

    /// Runs the waiting callback and goes idle.
    private void Finish()
    {
        var then = afterwards;
        Cancel();

        try
        {
            then?.Invoke();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Outfit change after an animation failed");
        }
    }

    private unsafe void Play(ushort timelineId)
    {
        var character = LocalCharacter();
        if (character == null)
            return;

        try
        {
            character->Timeline.PlayActionTimeline(timelineId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoGlam] Could not play animation {timelineId}");
        }
    }

    /// Whether this timeline is running in ANY of the character's animation slots.
    private static unsafe bool IsPlaying(ushort timelineId) => IsPlayingOn(LocalCharacter(), timelineId);

    /// The same question about anybody.
    public static unsafe bool IsPlayingOn(Character* character, ushort timelineId)
    {
        if (character == null || character->GameObject.DrawObject == null)
            return false;

        try
        {
            var sequencer = &character->Timeline.TimelineSequencer;

            for (uint slot = 0; slot < SlotCount; slot++)
            {
                if (sequencer->GetSlotTimeline(slot) == timelineId)
                    return true;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// How many animation slots to look in.
    private const int SlotCount = 13;

    /// Dumps what the character's animation slots hold, for working out why a wait did not wait.
    internal static unsafe string DescribeSlots()
    {
        var character = LocalCharacter();
        if (character == null || character->GameObject.DrawObject == null)
            return "no draw object";

        try
        {
            var sequencer = &character->Timeline.TimelineSequencer;
            var parts = new List<string>();

            for (uint slot = 0; slot < SlotCount; slot++)
            {
                var id = sequencer->GetSlotTimeline(slot);
                if (id != 0)
                    parts.Add($"{slot}={id}");
            }

            return parts.Count == 0 ? "all empty" : string.Join(" ", parts);
        }
        catch (Exception ex)
        {
            return $"unreadable: {ex.Message}";
        }
    }

    private static unsafe Character* LocalCharacter()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        return player is null ? null : (Character*)player.Address;
    }
}
