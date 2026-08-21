using System.Numerics;

namespace EchoNav.Game;

/// Which of the game's two parallel systems a target came out of.
public enum NavTargetKind
{
    /// An ordinary FATE, from Dalamud's IFateTable.
    Fate,

    /// A Critical Encounter / Critical Engagement, from DynamicEventContainer.
    CriticalEncounter,
}

/// One thing worth travelling to, normalised out of whichever table produced it, so the list and the Go
/// button never have to care which one that was.
public sealed record NavTarget
{
    public required NavTargetKind Kind { get; init; }

    /// FateId or DynamicEventId.
    public required uint Id { get; init; }

    public required string Name { get; init; }

    /// What to call this in the UI - "FATE", "Critical Encounter", "Forked Tower".
    public required string TypeLabel { get; init; }

    /// The Forked Tower rather than an ordinary Critical Encounter - DynamicEvent.EventType 4, where the
    /// other fifteen North Horn encounters are type 1.
    public bool IsForkedTower { get; init; }

    /// A known encounter location that isn't running right now.
    public bool IsDormant { get; init; }

    /// World-space, ready to route against as it stands.
    public required Vector3 Position { get; init; }

    /// How close counts as "there".
    public float Radius { get; init; }

    /// 0-100.
    public byte Progress { get; init; }

    /// Seconds left, or 0 when the source doesn't know yet.
    public long SecondsRemaining { get; init; }

    /// When this began, as a unix timestamp, or 0 when the source doesn't say.
    public long StartedAtEpoch { get; init; }

    /// Whether SecondsRemaining is counting down to this starting rather than to it ending.
    public bool IsStaging { get; init; }

    /// The source's own state, already turned into something printable - FateState and DynamicEventState
    /// don't share values or meanings, so there's nothing useful to unify here beyond IsEngageable.
    public required string StateLabel { get; init; }

    /// Whether travelling here right now actually accomplishes something.
    public bool IsEngageable { get; init; }

    /// The icon the game's own map uses.
    public uint MapIconId { get; init; }

    /// 0 when unknown - Critical Encounters have no level of their own, since the whole zone is level-synced
    /// anyway.
    public byte Level { get; init; }

    public int Participants { get; init; }
    public int MaxParticipants { get; init; }

    public string Key => $"{Kind}:{Id}";
}
