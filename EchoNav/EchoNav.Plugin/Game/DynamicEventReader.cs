using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace EchoNav.Game;

/// Every field of one DynamicEvent slot, straight out of client memory with no filtering or interpretation
/// applied.
public sealed record DynamicEventRaw
{
    public required int Slot { get; init; }
    public required ushort DynamicEventId { get; init; }
    public required string State { get; init; }
    public required byte EventType { get; init; }
    public required string Name { get; init; }
    public required Vector3 MarkerPosition { get; init; }
    public required uint MarkerIconId { get; init; }
    public required float MarkerRadius { get; init; }
    public required ushort MarkerTerritoryTypeId { get; init; }
    public required uint MarkerMapId { get; init; }
    public required uint SecondsLeft { get; init; }

    /// The staging clock, straight off the struct.
    public required int StartTimestamp { get; init; }

    public required uint SecondsRegistrationTime { get; init; }
    public required uint SecondsWarmupTime { get; init; }
    public required uint SecondsDuration { get; init; }

    /// What the reader made of the above: seconds until this starts, or 0.
    public required long StagingSecondsLeft { get; init; }

    public required byte Progress { get; init; }
    public required byte Participants { get; init; }
    public required byte MaxParticipants { get; init; }
}

public sealed record DynamicEventReadResult
{
    /// False means DynamicEventContainer.GetInstance() returned null - either this is not content that uses
    /// the system, or (the case actually worth catching) North Horn routes through something this build of
    /// FFXIVClientStructs doesn't know about yet.
    public required bool ContainerFound { get; init; }

    public ushort CurrentEventId { get; init; }
    public sbyte CurrentEventIndex { get; init; }
    public required IReadOnlyList<NavTarget> Targets { get; init; }
    public required IReadOnlyList<DynamicEventRaw> RawSlots { get; init; }

    public static DynamicEventReadResult NotFound { get; } = new()
    {
        ContainerFound = false,
        Targets = [],
        RawSlots = [],
    };
}

/// Reads Critical Encounters / Critical Engagements out of the client's DynamicEvent system.
public sealed unsafe class DynamicEventReader
{
    /// DynamicEvent.EventType, as observed in North Horn: all fifteen ordinary Critical Encounters report 1,
    /// and The Forked Tower reports 4.
    private const byte ForkedTowerEventType = 4;

    /// How far past the declared staging window a countdown may run before it is treated as meaning something
    /// other than a start time, and dropped.
    private const long PlausibleSlackSeconds = 120;

    /// Bound to use when the event doesn't say how long its staging window is.
    private const long FallbackStagingWindowSeconds = 900;

    /// Where the two staging-window fields sit in the struct.
    private static readonly int RegistrationOffset = OffsetOf("SecondsRegistrationTime");
    private static readonly int WarmupOffset = OffsetOf("SecondsWarmupTime");

    private static int OffsetOf(string field)
    {
        var info = typeof(DynamicEvent).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        var offset = info?.GetCustomAttribute<FieldOffsetAttribute>()?.Value ?? -1;

        if (offset < 0)
            Plugin.Log.Warning($"[EchoNav] DynamicEvent.{field} not found - staging countdowns fall back to a default window");

        return offset;
    }

    private static uint ReadUInt(ref DynamicEvent ev, int offset) =>
        offset < 0 ? 0u : *(uint*)((byte*)Unsafe.AsPointer(ref ev) + offset);

    /// Seconds until an encounter starts, or 0 if it isn't staging.
    private static long StagingSecondsLeft(ref DynamicEvent ev)
    {
        if (ev.State is not (DynamicEventState.Register or DynamicEventState.Warmup))
            return 0;

        if (ev.StartTimestamp <= 0)
            return 0;

        var remaining = ev.StartTimestamp - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (remaining <= 0)
            return 0;

        var window = (long)ReadUInt(ref ev, RegistrationOffset) + ReadUInt(ref ev, WarmupOffset);
        if (window <= 0)
            window = FallbackStagingWindowSeconds;

        return remaining <= window + PlausibleSlackSeconds ? remaining : 0;
    }

    private static string LabelForEventType(byte eventType) => eventType switch
    {
        1 => "Critical Encounter",
        ForkedTowerEventType => "Forked Tower",
        _ => "Encounter",
    };

    /// currentTerritory: Used only to discard events belonging to somewhere else.
    public DynamicEventReadResult Read(uint currentTerritory)
    {
        var container = DynamicEventContainer.GetInstance();
        if (container == null)
            return DynamicEventReadResult.NotFound;

        var targets = new List<NavTarget>();
        var raw = new List<DynamicEventRaw>();

        var events = container->Events;
        for (var i = 0; i < events.Length; i++)
        {
            ref var ev = ref events[i];

            var marker = ev.MapMarker;
            var name = ev.Name.ToString();
            var staging = StagingSecondsLeft(ref ev);

            raw.Add(new DynamicEventRaw
            {
                StartTimestamp = ev.StartTimestamp,
                SecondsRegistrationTime = ReadUInt(ref ev, RegistrationOffset),
                SecondsWarmupTime = ReadUInt(ref ev, WarmupOffset),
                SecondsDuration = ev.SecondsDuration,
                StagingSecondsLeft = staging,
                Slot = i,
                DynamicEventId = ev.DynamicEventId,
                State = ev.State.ToString(),
                EventType = ev.EventType,
                Name = name,
                MarkerPosition = marker.Position,
                MarkerIconId = marker.IconId,
                MarkerRadius = marker.Radius,
                MarkerTerritoryTypeId = marker.TerritoryTypeId,
                MarkerMapId = marker.MapId,
                SecondsLeft = ev.SecondsLeft,
                Progress = ev.Progress,
                Participants = ev.Participants,
                MaxParticipants = ev.MaxParticipants,
            });

            if (string.IsNullOrEmpty(name) || marker.Position == Vector3.Zero)
                continue;
            if (marker.TerritoryTypeId != 0 && marker.TerritoryTypeId != currentTerritory)
                continue;

            var dormant = ev.State == DynamicEventState.Inactive;

            targets.Add(new NavTarget
            {
                Kind = NavTargetKind.CriticalEncounter,
                Id = ev.DynamicEventId,
                Name = name,
                TypeLabel = LabelForEventType(ev.EventType),
                IsForkedTower = ev.EventType == ForkedTowerEventType,
                IsDormant = dormant,
                Position = marker.Position,
                Radius = marker.Radius > 1f ? marker.Radius : 15f,
                Progress = ev.Progress,
                SecondsRemaining = staging > 0 ? staging : ev.SecondsLeft,
                IsStaging = staging > 0,
                StateLabel = ev.State.ToString(),
                IsEngageable = !dormant,
                MapIconId = marker.IconId,
                Participants = ev.Participants,
                MaxParticipants = ev.MaxParticipants,
            });
        }

        return new DynamicEventReadResult
        {
            ContainerFound = true,
            CurrentEventId = container->CurrentEventId,
            CurrentEventIndex = container->CurrentEventIndex,
            Targets = targets,
            RawSlots = raw,
        };
    }
}
