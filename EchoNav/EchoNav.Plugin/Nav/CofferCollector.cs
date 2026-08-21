using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace EchoNav.Nav;

public enum CofferStage
{
    Idle,
    Approaching,
    Opening,
    Done,
    Failed,
}

/// Picks up coffers that happen to be on the way.
public sealed unsafe class CofferCollector(
    Configuration configuration, ICondition condition, IObjectTable objectTable, MovementDriver driver)
{
    /// How far off the current position a coffer may be and still count as on the way.
    private const float DetourBudget = 25f;

    /// How close to be before interacting.
    private const float InteractReach = 3f;

    private const long ApproachTimeoutMs = 15000;
    private const long OpenTimeoutMs = 8000;
    private const long InteractRetryMs = 600;

    /// How long to leave a coffer alone after failing at it.
    private const long GiveUpForMs = 120000;

    private readonly Dictionary<ulong, long> giveUpUntil = [];

    private ulong targetId;
    private uint targetDataId;
    private Vector3 targetPosition;
    private long stageDeadline;
    private long nextInteractTick;

    public CofferStage Stage { get; private set; } = CofferStage.Idle;
    public string Detail { get; private set; } = string.Empty;
    public int Collected { get; private set; }

    public bool IsRunning => Stage is CofferStage.Approaching or CofferStage.Opening;

    /// Starts on a coffer if one is worth the detour from here, and says whether it did.
    public bool TryStartNow(Vector3 playerPosition)
    {
        if (!configuration.CollectCoffers || IsRunning || !CanAct())
            return false;

        if (Nearest(playerPosition) is not { } coffer)
            return false;

        targetId = coffer.GameObjectId;
        targetDataId = coffer.BaseId;
        targetPosition = coffer.Position;

        Stage = CofferStage.Approaching;
        Detail = "Grabbing a coffer...";
        stageDeadline = Environment.TickCount64 + ApproachTimeoutMs;

        driver.SuppressMount = true;
        driver.Start([coffer.Position], InteractReach * 0.6f, "a coffer");
        return true;
    }

    public void Tick(Vector3 playerPosition)
    {
        if (!IsRunning)
            return;

        if (!configuration.CollectCoffers || condition[ConditionFlag.InCombat])
        {
            Abandon("Left it.");
            return;
        }

        switch (Stage)
        {
            case CofferStage.Approaching:
                TickApproaching(playerPosition);
                break;
            case CofferStage.Opening:
                TickOpening();
                break;
        }
    }

    private void TickApproaching(Vector3 playerPosition)
    {
        if (Find(targetId) == null)
        {
            Finish(collected: false);
            return;
        }

        if (Vector3.Distance(targetPosition, playerPosition) <= InteractReach)
        {
            driver.Stop();
            driver.SuppressMount = false;
            Stage = CofferStage.Opening;
            stageDeadline = Environment.TickCount64 + OpenTimeoutMs;
            nextInteractTick = 0;
            return;
        }

        if (Environment.TickCount64 > stageDeadline || driver.Status == MovementStatus.Blocked)
            Abandon("Couldn't reach it.");
    }

    private void TickOpening()
    {
        if (Find(targetId) is not { } coffer)
        {
            Plugin.Log.Information($"[EchoNav] Opened a coffer, data id {targetDataId}");
            Finish(collected: true);
            return;
        }

        if (Environment.TickCount64 > stageDeadline)
        {
            Abandon("Wouldn't open.");
            return;
        }

        if (condition[ConditionFlag.Mounted])
        {
            Detail = "Getting off to open it...";
            driver.Dismount();
            return;
        }

        if (Environment.TickCount64 < nextInteractTick)
            return;

        nextInteractTick = Environment.TickCount64 + InteractRetryMs;
        Interact(coffer);
    }

    private static void Interact(IGameObject coffer)
    {
        try
        {
            var native = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)coffer.Address;
            if (native != null)
                TargetSystem.Instance()->InteractWithObject(native);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Could not interact with a coffer");
        }
    }

    /// The closest coffer within the detour budget, ignoring any recently given up on.
    private IGameObject? Nearest(Vector3 playerPosition)
    {
        IGameObject? best = null;
        var bestDistance = DetourBudget;
        var now = Environment.TickCount64;

        foreach (var obj in objectTable)
        {
            if (obj.ObjectKind != ObjectKind.Treasure)
                continue;

            if (giveUpUntil.TryGetValue(obj.GameObjectId, out var until) && now < until)
                continue;

            var distance = Vector3.Distance(obj.Position, playerPosition);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = obj;
        }

        return best;
    }

    private IGameObject? Find(ulong id)
    {
        foreach (var obj in objectTable)
        {
            if (obj.GameObjectId == id)
                return obj;
        }

        return null;
    }

    private void Finish(bool collected)
    {
        if (collected)
            Collected++;

        driver.Stop();
        driver.SuppressMount = false;
        Stage = CofferStage.Done;
        Detail = string.Empty;
    }

    private void Abandon(string reason)
    {
        driver.Stop();
        driver.SuppressMount = false;
        giveUpUntil[targetId] = Environment.TickCount64 + GiveUpForMs;
        Stage = CofferStage.Failed;
        Detail = reason;
    }

    private bool CanAct() =>
        !condition[ConditionFlag.InCombat]
        && !condition[ConditionFlag.BetweenAreas]
        && !condition[ConditionFlag.Occupied]
        && !condition[ConditionFlag.Unconscious];
}
