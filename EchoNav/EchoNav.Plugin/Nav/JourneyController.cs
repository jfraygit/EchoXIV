using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using EchoNav.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace EchoNav.Nav;

public enum JourneyStage
{
    Idle,
    Starting,
    Returning,
    WalkingToShard,
    OpeningShard,
    Teleporting,
    WaitingForArrival,
    Buffing,
    Collecting,
    WalkingToTarget,
    Done,
    Failed,
}

/// Runs a whole trip: walk to a shard, teleport, walk the rest.
public sealed unsafe class JourneyController(
    IObjectTable objectTable,
    MovementDriver driver,
    AethernetMenu menu,
    ReturnAction returnAction,
    PhantomBuffer buffer,
    CofferCollector coffers)
{
    /// How long to let a top-up run before giving up and getting on with the trip.
    private const long BuffTimeoutMs = 90000;

    /// Where to carry on once buffing finishes.
    private JourneyStage resumeStage;

    /// Which opportunities to buff this trip has already used.
    private readonly System.Collections.Generic.HashSet<JourneyStage> buffOpportunitiesUsed = [];

    /// True while the trip is deliberately parked at a crystal, so the buffer isn't treated as being
    /// interrupted by the very journey that asked for it.
    public bool IsBuffing => Stage == JourneyStage.Buffing;

    /// True while the trip has paused to pick something up, so whatever is driving that isn't treated as the
    /// journey being interrupted.
    public bool IsCollecting => Stage == JourneyStage.Collecting;

    /// Whether this trip is worth a chime when it ends - see the Start parameter.
    public bool AnnouncesArrival { get; private set; } = true;

    /// Stops at a crystal to top buffs up, if there is anything to top up.
    private bool TopUpBuffs(Vector3 playerPosition, JourneyStage resumeInto)
    {
        if (!buffOpportunitiesUsed.Add(resumeInto))
            return false;

        if (!buffer.TryStartNow(playerPosition))
            return false;

        resumeStage = resumeInto;
        Stage = JourneyStage.Buffing;
        Detail = "Topping up phantom buffs...";
        stageDeadline = Environment.TickCount64 + BuffTimeoutMs;
        return true;
    }

    private void TickBuffing()
    {
        if (buffer.IsRunning && Environment.TickCount64 <= stageDeadline)
            return;

        Stage = resumeStage;
    }

    /// How close to a shard to get before trying to interact.
    private const float ShardReachDistance = 4f;

    /// Interact attempts before concluding the problem is distance rather than timing, and walking closer.
    private const int InteractsBeforeClosingIn = 3;
    private int interactAttempts;

    /// How close the journey is currently trying to get.
    private float reachTarget = ShardReachDistance;

    /// How close to the arrival shard means the teleport landed.
    private const float ArrivedAtShardDistance = 30f;

    /// How close to base camp's aetheryte counts as having Returned.
    private const float ReturnLandingDistance = 150f;

    /// Minimum wait after firing Return before its arrival is believed.
    private const long ReturnMinWaitMs = 4000;
    private long returnFiredTick;

    /// How long to stand still after a teleport before planning the next leg.
    private const long LandingSettleMs = 2000;
    private long landedTick;

    /// When the current trip began, so a cancellation from an earlier one can't end it.
    private long startedTick;

    /// How long "Arrived." stays on screen before the trip is forgotten and the strip goes away.
    private const long DoneLingerMs = 5000;
    private long finishedTick;

    private const long InteractRetryMs = 1200;
    private const long OpenTimeoutMs = 12000;
    private const long TeleportTimeoutMs = 20000;
    private const long ReturnTimeoutMs = 25000;
    private bool returnFired;

    private Vector3 finalDestination;
    private float arriveWithin;
    private KnownAetheryte? departureShard;
    private KnownAetheryte? arrivalShard;

    private long stageDeadline;
    private long nextInteractTick;

    /// How many times to nudge the character the last few yalms onto a shard before concluding it can't be
    /// reached.
    private const int MaxShardApproachAttempts = 3;
    private int shardApproachAttempts;

    public JourneyStage Stage { get; private set; } = JourneyStage.Idle;
    public string Detail { get; private set; } = string.Empty;
    public string TargetLabel { get; private set; } = string.Empty;

    public bool IsRunning => Stage is not (JourneyStage.Idle or JourneyStage.Done or JourneyStage.Failed);

    /// Starts a trip.
    public void Start(
        TravelPlan plan, float arriveWithinYalms, string label,
        Func<Vector3, System.Collections.Generic.IReadOnlyList<Vector3>> replanner,
        bool announceArrival = true)
    {
        Stop();

        AnnouncesArrival = announceArrival;

        startedTick = Environment.TickCount64;
        buffOpportunitiesUsed.Clear();
        TargetLabel = label;
        finalDestination = plan.Route.Count > 0 ? plan.Route[^1] : default;
        arriveWithin = arriveWithinYalms;

        pendingPlan = plan;
        pendingReplanner = replanner;

        departureShard = plan.FromShard;
        arrivalShard = plan.ToShard;
        shardApproachAttempts = 0;
        interactAttempts = 0;
        reachTarget = ShardReachDistance;

        if (plan.UsesTeleport)
            finalDestination = plan.FinalDestination;

        var here = objectTable.LocalPlayer?.Position ?? default;
        if (TopUpBuffs(here, JourneyStage.Starting))
            return;

        BeginFirstLeg();
    }

    private TravelPlan? pendingPlan;
    private Func<Vector3, System.Collections.Generic.IReadOnlyList<Vector3>>? pendingReplanner;

    /// Sets the trip going.
    private void BeginFirstLeg()
    {
        if (pendingPlan is not { } plan || pendingReplanner is not { } replanner)
        {
            Fail("Lost the travel plan.");
            return;
        }

        if (plan.UsesReturn)
        {
            Stage = JourneyStage.Returning;
            Detail = "Returning to base camp...";
            stageDeadline = Environment.TickCount64 + ReturnTimeoutMs;
            returnFired = false;
            return;
        }

        if (!plan.UsesTeleport)
        {
            driver.Start(plan.Route, arriveWithin, TargetLabel, replanner);
            Stage = JourneyStage.WalkingToTarget;
            Detail = "Walking the whole way.";
            return;
        }

        driver.Start(plan.Route, ShardReachDistance, $"{TargetLabel} (via {plan.FromShard!.AethernetName})", replanner);
        Stage = JourneyStage.WalkingToShard;
        Detail = $"Walking to {plan.FromShard.AethernetName}.";
    }

    public void Stop()
    {
        driver.Stop();
        menu.CancelSelection();
        Stage = JourneyStage.Idle;
        Detail = string.Empty;
        departureShard = null;
        arrivalShard = null;
        landedTick = 0;
    }

    public void Tick(Vector3 playerPosition, Func<Vector3, System.Collections.Generic.IReadOnlyList<Vector3>> replanner)
    {
        if (Stage != JourneyStage.Idle && driver.LastCancelledTick > startedTick)
        {
            Stop();
            Stage = JourneyStage.Idle;
            Detail = "Cancelled - you took the controls.";
            return;
        }

        if (Stage == JourneyStage.Done && Environment.TickCount64 - finishedTick >= DoneLingerMs)
        {
            Stop();
            return;
        }

        switch (Stage)
        {
            case JourneyStage.Returning:
                TickReturning(playerPosition, replanner);
                break;
            case JourneyStage.WalkingToShard:
                TickWalkingToShard(playerPosition);
                break;
            case JourneyStage.OpeningShard:
                TickOpeningShard(playerPosition);
                break;
            case JourneyStage.Teleporting:
                TickTeleporting();
                break;
            case JourneyStage.WaitingForArrival:
                TickWaitingForArrival(playerPosition, replanner);
                break;
            case JourneyStage.Buffing:
                TickBuffing();
                break;
            case JourneyStage.Collecting:
                TickCollecting(replanner);
                break;
            case JourneyStage.Starting:
                BeginFirstLeg();
                break;
            case JourneyStage.WalkingToTarget:
                TickWalkingToTarget(playerPosition);
                break;
        }
    }

    /// Fires Occult Return and waits to land at base camp.
    private void TickReturning(Vector3 playerPosition, Func<Vector3, System.Collections.Generic.IReadOnlyList<Vector3>> replanner)
    {
        if (departureShard == null)
        {
            Fail("Lost track of base camp.");
            return;
        }

        var flat = new Vector2(
            departureShard.Position.X - playerPosition.X,
            departureShard.Position.Z - playerPosition.Z).Length();

        if (returnFired
            && Environment.TickCount64 - returnFiredTick >= ReturnMinWaitMs
            && flat <= ReturnLandingDistance)
        {
            if (TopUpBuffs(playerPosition, JourneyStage.Returning))
                return;

            if (arrivalShard == null)
            {
                driver.Start(replanner(finalDestination), arriveWithin, TargetLabel, replanner);
                Stage = JourneyStage.WalkingToTarget;
                Detail = "Walking from base camp.";
                return;
            }

            Stage = JourneyStage.WalkingToShard;
            Detail = $"Heading to the base camp aetheryte for {arrivalShard.AethernetName}.";
            driver.Start([departureShard.Position], ShardReachDistance * 0.5f, departureShard.AethernetName);
            return;
        }

        if (Environment.TickCount64 > stageDeadline)
        {
            Fail("Occult Return didn't land us at base camp.");
            return;
        }

        if (returnFired)
            return;

        returnFired = true;
        returnFiredTick = Environment.TickCount64;
        if (!returnAction.Use())
            Fail($"Occult Return was refused (status {returnAction.LastStatus}).");
    }

    private void TickWalkingToShard(Vector3 playerPosition)
    {
        if (departureShard == null)
        {
            Fail("Lost track of the departure shard.");
            return;
        }

        var flat = new Vector2(
            departureShard.Position.X - playerPosition.X,
            departureShard.Position.Z - playerPosition.Z).Length();

        if (flat <= reachTarget)
        {
            driver.Stop();
            interactAttempts = 0;
            Stage = JourneyStage.OpeningShard;
            Detail = "Opening the aethernet menu...";
            stageDeadline = Environment.TickCount64 + OpenTimeoutMs;
            nextInteractTick = 0;
            return;
        }

        if (driver.IsRunning && GrabCoffer(playerPosition, JourneyStage.WalkingToShard))
            return;

        if (driver.Status == MovementStatus.Blocked)
        {
            Fail($"Couldn't reach {departureShard.AethernetName}: {driver.StatusDetail}");
            return;
        }

        if (driver.IsRunning)
            return;

        if (++shardApproachAttempts > MaxShardApproachAttempts)
        {
            Fail($"Stopped {flat:F0}y short of {departureShard.AethernetName}.");
            return;
        }

        Detail = $"Closing the last {flat:F0}y to the shard...";
        driver.Start([departureShard.Position], ShardReachDistance * 0.5f, departureShard.AethernetName);
    }

    private void TickOpeningShard(Vector3 playerPosition)
    {
        if (menu.IsOpen)
        {
            Stage = JourneyStage.Teleporting;
            Detail = $"Selecting {arrivalShard?.AethernetName}...";
            stageDeadline = Environment.TickCount64 + TeleportTimeoutMs;
            return;
        }

        if (Environment.TickCount64 > stageDeadline)
        {
            Fail("The aethernet menu didn't open.");
            return;
        }

        if (Environment.TickCount64 < nextInteractTick)
            return;

        nextInteractTick = Environment.TickCount64 + InteractRetryMs;

        if (++interactAttempts > InteractsBeforeClosingIn && departureShard != null)
        {
            if (++shardApproachAttempts > MaxShardApproachAttempts)
            {
                Fail("Couldn't get close enough to the aetheryte to use it.");
                return;
            }

            var tighter = MathF.Max(1.5f, ShardReachDistance - shardApproachAttempts);
            reachTarget = tighter;
            Detail = $"Too far to interact - closing to {tighter:F0}y...";
            interactAttempts = 0;
            Stage = JourneyStage.WalkingToShard;
            driver.Start([departureShard.Position], tighter, departureShard.AethernetName);
            return;
        }

        InteractWithNearestShard(playerPosition);
    }

    private void TickTeleporting()
    {
        if (arrivalShard == null)
        {
            Fail("Lost track of the arrival shard.");
            return;
        }

        if (Environment.TickCount64 > stageDeadline)
        {
            Fail($"\"{arrivalShard.AethernetName}\" never appeared in the list. Saw: " +
                 $"[{string.Join(" | ", menu.Destinations)}]");
            return;
        }

        if (menu.IsSelecting)
            return;

        var index = IndexOfDestination(arrivalShard.AethernetName);
        if (index < 0)
        {
            Detail = $"Waiting for the destination list ({menu.Destinations.Count} so far)...";
            return;
        }

        if (!menu.Select(index))
        {
            Fail("Couldn't select the destination.");
            return;
        }

        Stage = JourneyStage.WaitingForArrival;
        Detail = $"Teleporting to {arrivalShard.AethernetName}...";
        stageDeadline = Environment.TickCount64 + TeleportTimeoutMs;
    }

    private void TickWaitingForArrival(Vector3 playerPosition, Func<Vector3, System.Collections.Generic.IReadOnlyList<Vector3>> replanner)
    {
        if (arrivalShard == null)
        {
            Fail("Lost track of the arrival shard.");
            return;
        }

        if (Vector3.Distance(playerPosition, arrivalShard.Position) <= ArrivedAtShardDistance)
        {
            if (landedTick == 0)
            {
                landedTick = Environment.TickCount64;
                Detail = "Landed - letting things settle.";
                return;
            }

            if (Environment.TickCount64 - landedTick < LandingSettleMs)
                return;

            if (TopUpBuffs(playerPosition, JourneyStage.WaitingForArrival))
                return;

            driver.Start(replanner(finalDestination), arriveWithin, TargetLabel, replanner);
            Stage = JourneyStage.WalkingToTarget;
            Detail = "Walking the last leg.";
            return;
        }

        if (Environment.TickCount64 > stageDeadline)
            Fail("The teleport didn't seem to happen.");
    }

    /// How long to allow for one coffer before writing it off and getting on.
    private const long CollectTimeoutMs = 30000;

    /// Steps aside for a coffer, if one is close enough to be on the way.
    private bool GrabCoffer(Vector3 playerPosition, JourneyStage resumeInto)
    {
        if (!coffers.TryStartNow(playerPosition))
            return false;

        resumeStage = resumeInto;
        Stage = JourneyStage.Collecting;
        Detail = "Grabbing a coffer on the way...";
        stageDeadline = Environment.TickCount64 + CollectTimeoutMs;
        return true;
    }

    private void TickCollecting(Func<Vector3, System.Collections.Generic.IReadOnlyList<Vector3>> replanner)
    {
        if (coffers.IsRunning && Environment.TickCount64 <= stageDeadline)
            return;

        Stage = resumeStage;

        if (resumeStage == JourneyStage.WalkingToTarget)
        {
            driver.Start(replanner(finalDestination), arriveWithin, TargetLabel, replanner);
            Detail = "Back on the way.";
        }
        else if (resumeStage == JourneyStage.WalkingToShard && departureShard != null)
        {
            driver.Start([departureShard.Position], reachTarget, departureShard.AethernetName);
            Detail = $"Back on the way to {departureShard.AethernetName}.";
        }
    }

    private void TickWalkingToTarget(Vector3 playerPosition)
    {
        if (driver.IsRunning && GrabCoffer(playerPosition, JourneyStage.WalkingToTarget))
            return;

        if (driver.Status == MovementStatus.Arrived)
        {
            Stage = JourneyStage.Done;
            Detail = "Arrived.";
            finishedTick = Environment.TickCount64;
            return;
        }

        if (driver.Status == MovementStatus.Blocked)
            Fail(driver.StatusDetail);
        else if (!driver.IsRunning)
            Fail("Stopped short.");
    }

    /// Position of the destination in the window's own list, matched by the name learned when the player last
    /// stood at that shard.
    private int IndexOfDestination(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return -1;

        for (var i = 0; i < menu.Destinations.Count; i++)
        {
            if (string.Equals(menu.Destinations[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void InteractWithNearestShard(Vector3 playerPosition)
    {
        try
        {
            foreach (var obj in objectTable)
            {
                if (Vector3.Distance(obj.Position, playerPosition) > ShardReachDistance * 2f)
                    continue;

                if (!AetheryteRegistry.LooksLikeAetheryte(obj))
                    continue;

                var native = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
                if (native == null)
                    continue;

                TargetSystem.Instance()->InteractWithObject(native);
                return;
            }

            Detail = "No shard within reach to interact with.";
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Could not interact with the shard");
        }
    }

    private void Fail(string reason)
    {
        driver.Stop();
        menu.CancelSelection();
        Stage = JourneyStage.Failed;
        Detail = reason;
    }
}
