using System.Collections.Generic;
using EchoMix.Shared;

namespace EchoMix.Plugin.Audio;

/// Every tunable threshold for AutoJoinTracker in one documented place, since these are exactly the numbers
/// expected to need real-world beta adjustment - none of them are load-bearing enough elsewhere to justify
/// being scattered through the tracker's own logic.
public static class AutoJoinTuning
{
    /// Throttles the actual scan/decision work below raw frame rate - cheap regardless, but there's no reason
    /// to re-evaluate every candidate 60+ times a second.
    public const float ScanIntervalSeconds = 0.5f;

    /// How often the live public-shows list itself gets refreshed (RequestPublicShows is a real relay round
    /// trip carrying every public show's data, images included) - kept well below the scan cadence above,
    /// since the scan just re-uses whatever list is already cached.
    public const float ListPollIntervalSeconds = 15f;

    /// A candidate's ComputeVolume score must clear this before counting as "in range" at all - filters out
    /// the exact boundary yalm where jitter or simply running past the edge would otherwise flicker a
    /// candidate in and out every scan.
    public const float JoinThreshold = 0.05f;

    /// A candidate must stay above JoinThreshold continuously for this long before actually joining - covers
    /// running past a venue's edge without stopping, so a fly-by doesn't trigger a join immediately followed
    /// by a leave.
    public const float JoinDwellSeconds = 2.5f;

    /// Once auto-joined, the show must read fully out of range continuously for this long before
    /// disconnecting - deliberately longer than JoinDwellSeconds, since a disconnect is an audible cutoff
    /// (more disruptive than a delayed join), and this needs to absorb real blips: a loading-screen doorway
    /// transition right at the boundary, or the object table not yet repopulated after a nearby zone load.
    public const float LeaveGraceSeconds = 6f;

    /// A competing show's score has to beat the currently auto-joined show's by this fraction, not just edge
    /// it out, before it's even considered switch-worthy - the core anti-thrash guard for two overlapping
    /// shows.
    public const float SwitchMarginRatio = 0.20f;

    /// Same reasoning as JoinDwellSeconds, held slightly longer since a switch is a disconnect AND a
    /// reconnect - twice the disruption of a fresh join.
    public const float SwitchDwellSeconds = 5f;

    /// After a listener manually disconnects from a show they're still in range of, re-auto-joining THAT
    /// specific room is suppressed for this long - otherwise a deliberate "I want quiet" click would just get
    /// undone on the very next scan, without the listener needing to remember to flip the whole feature off.
    public const float ManualLeaveSuppressSeconds = 90f;
}

public enum AutoJoinDecisionKind
{
    None,
    Join,
    Leave,
    SwitchTo,
}

/// What AutoJoinTracker.Tick wants done this frame - the tracker only ever decides, it never sends anything
/// itself (see AutoJoinTracker's own doc comment for why).
public readonly record struct AutoJoinDecision(AutoJoinDecisionKind Kind, string? RoomCode)
{
    public static readonly AutoJoinDecision None = new(AutoJoinDecisionKind.None, null);
    public static AutoJoinDecision Join(string roomCode) => new(AutoJoinDecisionKind.Join, roomCode);
    public static readonly AutoJoinDecision Leave = new(AutoJoinDecisionKind.Leave, null);
    public static AutoJoinDecision SwitchTo(string roomCode) => new(AutoJoinDecisionKind.SwitchTo, roomCode);
}

/// Decides whether a listener should be auto-joined into a nearby live, public, Proximity-mode show - see
/// Configuration.ListenerAutoJoinNearbyShows for the feature's own scope (Proximity only, one show at a time,
/// overrides manual joining while on).
public sealed class AutoJoinTracker
{
    private readonly ProximityTracker proximityTracker;

    private readonly Dictionary<string, float> manualLeaveCooldowns = new();

    private float scanAccumulator;

    private string? autoJoinedRoomCode;
    private float leaveGraceElapsedSeconds;

    private string? pendingJoinRoomCode;
    private float pendingJoinElapsedSeconds;

    private string? pendingSwitchRoomCode;
    private float pendingSwitchElapsedSeconds;

    /// Set the instant a SwitchTo decision fires (the disconnect half) - the next Tick() call that sees
    /// IsListening finally read false emits the matching Join() for this target.
    private string? pendingSwitchTarget;

    public string? AutoJoinedRoomCode => autoJoinedRoomCode;
    public float AutoJoinedScore { get; private set; }
    public string? BestCandidateRoomCode { get; private set; }
    public float BestCandidateScore { get; private set; }
    public int TrackedCandidateCount { get; private set; }
    public string? PendingJoinRoomCode => pendingJoinRoomCode;
    public float JoinDwellRemainingSeconds =>
        pendingJoinRoomCode != null ? System.MathF.Max(0f, AutoJoinTuning.JoinDwellSeconds - pendingJoinElapsedSeconds) : 0f;
    public string? PendingSwitchRoomCode => pendingSwitchRoomCode;
    public float SwitchDwellRemainingSeconds =>
        pendingSwitchRoomCode != null ? System.MathF.Max(0f, AutoJoinTuning.SwitchDwellSeconds - pendingSwitchElapsedSeconds) : 0f;

    /// How many otherwise-eligible live public Proximity shows are currently excluded from
    /// TrackedCandidateCount by a manual-leave cooldown - without surfacing this, "0 tracked" while standing
    /// right next to a real show reads as a bug rather than the deliberate suppression it actually is (see
    /// NotifyManualLeave).
    public int SuppressedCandidateCount { get; private set; }

    public AutoJoinTracker(ProximityTracker proximityTracker)
    {
        this.proximityTracker = proximityTracker;
    }

    /// Called once per Framework.Update tick regardless of the feature's own toggle (so manual-leave
    /// cooldowns keep decaying in real time even while it's off) - the actual scan/decision work inside is
    /// throttled to AutoJoinTuning.ScanIntervalSeconds.
    public AutoJoinDecision Tick(Configuration config, BroadcastStatusMessage broadcast, PublicShowsSnapshotMessage? publicShows, float deltaSeconds)
    {
        DecayManualLeaveCooldowns(deltaSeconds);

        if (!config.ListenerAutoJoinNearbyShows)
        {
            Reset();
            return AutoJoinDecision.None;
        }

        if (pendingSwitchTarget != null)
        {
            if (broadcast.IsListening)
                return AutoJoinDecision.None;

            var target = pendingSwitchTarget;
            pendingSwitchTarget = null;
            autoJoinedRoomCode = target;
            return AutoJoinDecision.Join(target);
        }

        if (autoJoinedRoomCode == null && broadcast.IsListening && broadcast.RoomCode != null && broadcast.IsProximityAudio)
            autoJoinedRoomCode = broadcast.RoomCode;

        if (autoJoinedRoomCode != null && (!broadcast.IsListening || broadcast.RoomCode != autoJoinedRoomCode))
            Reset();

        scanAccumulator += deltaSeconds;
        if (scanAccumulator < AutoJoinTuning.ScanIntervalSeconds)
            return AutoJoinDecision.None;
        scanAccumulator -= AutoJoinTuning.ScanIntervalSeconds;

        ScoreCandidates(publicShows);

        return autoJoinedRoomCode == null ? EvaluateJoin() : EvaluateSwitchOrLeave(broadcast);
    }

    /// Called by Plugin.OnFrameworkUpdate when it detects a listener manually disconnected from a room this
    /// tracker had auto-joined them into (rather than the engine's own doing) - starts a cooldown so a
    /// deliberate "I want quiet" click isn't immediately undone on the very next scan.
    public void NotifyManualLeave(string roomCode)
    {
        manualLeaveCooldowns[roomCode] = AutoJoinTuning.ManualLeaveSuppressSeconds;
        if (autoJoinedRoomCode == roomCode)
            Reset();
    }

    private void ScoreCandidates(PublicShowsSnapshotMessage? publicShows)
    {
        BestCandidateRoomCode = null;
        BestCandidateScore = 0f;
        var trackedCount = 0;
        var suppressedCount = 0;

        if (publicShows != null)
        {
            foreach (var show in publicShows.Shows)
            {
                if (!show.IsVenueShow || string.IsNullOrEmpty(show.HostCharacterName) || show.ProximityRange <= 0f || show.HasPassword)
                    continue;
                if (manualLeaveCooldowns.ContainsKey(show.RoomCode))
                {
                    suppressedCount++;
                    continue;
                }

                trackedCount++;
                var score = proximityTracker.ComputeVolume(show.HostCharacterName, show.ProximityRange);
                if (score > BestCandidateScore)
                {
                    BestCandidateScore = score;
                    BestCandidateRoomCode = show.RoomCode;
                }
            }
        }

        TrackedCandidateCount = trackedCount;
        SuppressedCandidateCount = suppressedCount;
    }

    private AutoJoinDecision EvaluateJoin()
    {
        if (BestCandidateRoomCode == null || BestCandidateScore < AutoJoinTuning.JoinThreshold)
        {
            pendingJoinRoomCode = null;
            pendingJoinElapsedSeconds = 0f;
            return AutoJoinDecision.None;
        }

        if (pendingJoinRoomCode != BestCandidateRoomCode)
        {
            pendingJoinRoomCode = BestCandidateRoomCode;
            pendingJoinElapsedSeconds = 0f;
        }

        pendingJoinElapsedSeconds += AutoJoinTuning.ScanIntervalSeconds;
        if (pendingJoinElapsedSeconds < AutoJoinTuning.JoinDwellSeconds)
            return AutoJoinDecision.None;

        var target = pendingJoinRoomCode;
        pendingJoinRoomCode = null;
        pendingJoinElapsedSeconds = 0f;
        autoJoinedRoomCode = target;
        return AutoJoinDecision.Join(target!);
    }

    private AutoJoinDecision EvaluateSwitchOrLeave(BroadcastStatusMessage broadcast)
    {
        var joinedScore = broadcast.IsProximityAudio
            ? proximityTracker.ComputeVolume(broadcast.HostCharacterName, broadcast.ProximityRange)
            : 0f;
        AutoJoinedScore = joinedScore;

        if (joinedScore < AutoJoinTuning.JoinThreshold)
        {
            pendingSwitchRoomCode = null;
            pendingSwitchElapsedSeconds = 0f;

            leaveGraceElapsedSeconds += AutoJoinTuning.ScanIntervalSeconds;
            if (leaveGraceElapsedSeconds < AutoJoinTuning.LeaveGraceSeconds)
                return AutoJoinDecision.None;

            Reset();
            return AutoJoinDecision.Leave;
        }

        leaveGraceElapsedSeconds = 0f;

        var switchTarget = BestCandidateRoomCode != null
            && BestCandidateRoomCode != autoJoinedRoomCode
            && BestCandidateScore >= joinedScore * (1f + AutoJoinTuning.SwitchMarginRatio)
                ? BestCandidateRoomCode
                : null;

        if (switchTarget == null)
        {
            pendingSwitchRoomCode = null;
            pendingSwitchElapsedSeconds = 0f;
            return AutoJoinDecision.None;
        }

        if (pendingSwitchRoomCode != switchTarget)
        {
            pendingSwitchRoomCode = switchTarget;
            pendingSwitchElapsedSeconds = 0f;
        }

        pendingSwitchElapsedSeconds += AutoJoinTuning.ScanIntervalSeconds;
        if (pendingSwitchElapsedSeconds < AutoJoinTuning.SwitchDwellSeconds)
            return AutoJoinDecision.None;

        pendingSwitchRoomCode = null;
        pendingSwitchElapsedSeconds = 0f;
        pendingSwitchTarget = switchTarget;
        autoJoinedRoomCode = null;
        return AutoJoinDecision.SwitchTo(switchTarget);
    }

    private void DecayManualLeaveCooldowns(float deltaSeconds)
    {
        if (manualLeaveCooldowns.Count == 0)
            return;

        List<string>? expired = null;
        foreach (var pair in manualLeaveCooldowns)
        {
            var remaining = pair.Value - deltaSeconds;
            if (remaining <= 0f)
                (expired ??= new List<string>()).Add(pair.Key);
            else
                manualLeaveCooldowns[pair.Key] = remaining;
        }

        if (expired != null)
        {
            foreach (var key in expired)
                manualLeaveCooldowns.Remove(key);
        }
    }

    private void Reset()
    {
        autoJoinedRoomCode = null;
        AutoJoinedScore = 0f;
        leaveGraceElapsedSeconds = 0f;
        pendingJoinRoomCode = null;
        pendingJoinElapsedSeconds = 0f;
        pendingSwitchRoomCode = null;
        pendingSwitchElapsedSeconds = 0f;
        pendingSwitchTarget = null;
    }
}
