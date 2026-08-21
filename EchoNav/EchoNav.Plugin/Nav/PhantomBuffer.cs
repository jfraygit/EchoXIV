using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using EchoNav.Game;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace EchoNav.Nav;

public enum PhantomBuffStage
{
    Idle,
    Approaching,
    Dismounting,
    SwitchingJob,
    UsingAbility,
    Verifying,
    Restoring,
    Done,
    Failed,
}

/// Keeps the four knowledge-crystal buffs up.
public sealed unsafe class PhantomBuffer(
    Configuration configuration, ICondition condition, IObjectTable objectTable, MovementDriver driver, MountController mount)
{
    /// Refresh anything with less than this left.
    private const float RefreshBelowSeconds = 5 * 60;

    /// The knowledge crystal itself, by data id.
    private const uint KnowledgeCrystalDataId = 2007457;

    /// How close to the crystal the abilities actually want.
    private const float CrystalReach = 4f;

    /// How far away a crystal is still worth walking to.
    private const float ApproachRange = 40f;

    private const long ApproachTimeoutMs = 20000;
    private const long DismountTimeoutMs = 6000;

    private const long JobChangeTimeoutMs = 8000;
    private const long AbilityTimeoutMs = 8000;
    private const long VerifyTimeoutMs = 6000;
    private const long RetryIntervalMs = 700;

    /// How long to wait after finishing, or failing, before considering it again.
    private const long CooldownMs = 60000;

    private readonly List<PhantomBuff> queue = [];
    private int step;
    private byte restoreJob;
    private long stageDeadline;
    private long nextAttemptTick;
    private long idleUntilTick;

    /// The game's own reason for refusing the last ability.
    public uint LastActionStatus { get; private set; }

    /// Set when Inquiring Mind is doing the work, so verification expects every qualifying buff rather than
    /// one.
    private bool viaInquiringMind;

    public PhantomBuffStage Stage { get; private set; } = PhantomBuffStage.Idle;
    public string Detail { get; private set; } = string.Empty;

    public bool IsRunning => Stage is PhantomBuffStage.Approaching or PhantomBuffStage.Dismounting or PhantomBuffStage.SwitchingJob or PhantomBuffStage.UsingAbility
        or PhantomBuffStage.Verifying or PhantomBuffStage.Restoring;

    /// busy: Whether anything else has the character - a trip in progress, or the driver steering.
    public void Tick(Vector3 playerPosition, bool busy)
    {
        if (!configuration.PhantomBuffAtCrystals)
        {
            if (IsRunning)
                Abort("Turned off mid-run.");

            return;
        }

        if (IsRunning)
        {
            Advance(playerPosition, busy);
            return;
        }

    }

    /// Starts a run right now if there is one to do, and says whether it did.
    public bool TryStartNow(Vector3 playerPosition)
    {
        if (!configuration.PhantomBuffAtCrystals || IsRunning)
            return false;

        if (Environment.TickCount64 < idleUntilTick)
            return false;

        return Consider(playerPosition, allowApproach: true);
    }

    /// Decides whether there is anything worth doing here, and lines up the work.
    private bool Consider(Vector3 playerPosition, bool allowApproach = false)
    {
        if (!CanAct())
            return false;

        var crystal = NearestCrystal(playerPosition);
        if (crystal is not { } target)
            return false;

        var distance = Vector3.Distance(target, playerPosition);
        if (distance > CrystalReach && !(allowApproach && distance <= ApproachRange))
            return false;

        var state = PhantomReader.Read();
        if (!state.Available)
            return false;

        var missing = Missing(state);
        if (missing.Count == 0)
            return false;

        restoreJob = state.CurrentJob;

        queue.Clear();
        step = 0;

        viaInquiringMind = state.LevelOf(PhantomJobs.Freelancer) >= PhantomJobs.InquiringMindLevel;
        if (viaInquiringMind)
        {
            queue.Add(new PhantomBuff(
                "everything", PhantomJobs.Freelancer, "Freelancer", PhantomJobs.InquiringMindLevel,
                PhantomJobs.InquiringMind, "Inquiring Mind", 0));
        }
        else
        {
            queue.AddRange(missing);
        }

        Detail = viaInquiringMind
            ? "Topping up buffs with Inquiring Mind..."
            : $"Topping up {missing.Count} buff{(missing.Count == 1 ? string.Empty : "s")}...";

        if (distance > CrystalReach)
        {
            crystalTarget = target;
            Stage = PhantomBuffStage.Approaching;
            Detail = "Stepping over to the crystal...";
            stageDeadline = Environment.TickCount64 + ApproachTimeoutMs;
            driver.SuppressMount = true;
            driver.Start([target], CrystalReach * 0.5f, "the knowledge crystal");
            return true;
        }

        BeginBuffing();
        return true;
    }

    private Vector3 crystalTarget;

    /// Walks the last few yalms.
    private void TickApproaching(Vector3 playerPosition)
    {
        if (Vector3.Distance(crystalTarget, playerPosition) <= CrystalReach)
        {
            driver.Stop();
            driver.SuppressMount = false;
            BeginBuffing();
            return;
        }

        if (Environment.TickCount64 > stageDeadline || driver.Status == MovementStatus.Blocked)
            Abort("Couldn't get to the crystal.");
    }

    /// In reach, so either get off the mount or start.
    private void BeginBuffing()
    {
        if (mount.IsMounted)
        {
            Stage = PhantomBuffStage.Dismounting;
            Detail = "Dismounting...";
            stageDeadline = Environment.TickCount64 + DismountTimeoutMs;
            nextAttemptTick = 0;
            return;
        }

        Stage = PhantomBuffStage.SwitchingJob;
        Detail = viaInquiringMind
            ? "Topping up buffs with Inquiring Mind..."
            : "Topping up buffs...";
        stageDeadline = Environment.TickCount64 + JobChangeTimeoutMs;
        nextAttemptTick = 0;
    }

    private void TickDismounting()
    {
        if (!mount.IsMounted)
        {
            BeginBuffing();
            return;
        }

        if (Environment.TickCount64 > stageDeadline)
        {
            Abort("Couldn't dismount.");
            return;
        }

        mount.TryDismount();
    }

    /// The buffs that are absent or nearly gone, and that this character can actually produce.
    private List<PhantomBuff> Missing(PhantomSnapshot state)
    {
        var statuses = Statuses();
        var missing = new List<PhantomBuff>();

        foreach (var buff in PhantomJobs.Buffs)
        {
            if (state.LevelOf(buff.JobId) < buff.JobLevel)
                continue;

            if (!statuses.TryGetValue(buff.StatusId, out var remaining) || remaining < RefreshBelowSeconds)
                missing.Add(buff);
        }

        return missing;
    }

    private void Advance(Vector3 playerPosition, bool busy)
    {
        if (busy)
        {
            Abort("Something else needed the character.");
            return;
        }

        if (Stage != PhantomBuffStage.Approaching
            && NearestCrystal(playerPosition) is var near
            && (near is null || Vector3.Distance(near.Value, playerPosition) > CrystalReach))
        {
            Abort("Moved away from the crystal.");
            return;
        }

        if (condition[ConditionFlag.InCombat])
        {
            Abort("Combat started.");
            return;
        }

        switch (Stage)
        {
            case PhantomBuffStage.Approaching:
                TickApproaching(playerPosition);
                break;
            case PhantomBuffStage.Dismounting:
                TickDismounting();
                break;
            case PhantomBuffStage.SwitchingJob:
                TickSwitching();
                break;
            case PhantomBuffStage.UsingAbility:
                TickUsing();
                break;
            case PhantomBuffStage.Verifying:
                TickVerifying();
                break;
            case PhantomBuffStage.Restoring:
                TickRestoring();
                break;
        }
    }

    private void TickSwitching()
    {
        var wanted = queue[step].JobId;
        var state = PhantomReader.Read();

        if (state.Available && state.CurrentJob == wanted)
        {
            Stage = PhantomBuffStage.UsingAbility;
            Detail = $"Using {queue[step].ActionName}...";
            stageDeadline = Environment.TickCount64 + AbilityTimeoutMs;
            nextAttemptTick = 0;
            return;
        }

        if (Environment.TickCount64 > stageDeadline)
        {
            Abort($"Couldn't switch to Phantom {PhantomJobs.NameOf(wanted)}.");
            return;
        }

        if (Environment.TickCount64 < nextAttemptTick)
            return;

        nextAttemptTick = Environment.TickCount64 + RetryIntervalMs;
        PhantomReader.ChangeJob(wanted);
    }

    private void TickUsing()
    {
        if (Environment.TickCount64 > stageDeadline)
        {
            Abort($"{queue[step].ActionName} was never usable (status {LastActionStatus}).");
            return;
        }

        if (Environment.TickCount64 < nextAttemptTick)
            return;

        nextAttemptTick = Environment.TickCount64 + RetryIntervalMs;

        var manager = ActionManager.Instance();
        if (manager == null)
            return;

        LastActionStatus = manager->GetActionStatus(ActionType.Action, queue[step].ActionId);
        if (LastActionStatus != 0)
            return;

        if (!manager->UseAction(ActionType.Action, queue[step].ActionId))
            return;

        Stage = PhantomBuffStage.Verifying;
        Detail = $"Waiting for {queue[step].Name}...";
        stageDeadline = Environment.TickCount64 + VerifyTimeoutMs;
    }

    /// Waits for the status to actually appear.
    private void TickVerifying()
    {
        var statuses = Statuses();

        var landed = viaInquiringMind
            ? PhantomJobs.Buffs.Any(b => Fresh(statuses, b.StatusId))
            : Fresh(statuses, queue[step].StatusId);

        if (landed)
        {
            if (++step < queue.Count)
            {
                Stage = PhantomBuffStage.SwitchingJob;
                Detail = $"Switching to Phantom {PhantomJobs.NameOf(queue[step].JobId)}...";
                stageDeadline = Environment.TickCount64 + JobChangeTimeoutMs;
                nextAttemptTick = 0;
                return;
            }

            BeginRestore("Buffed.");
            return;
        }

        if (Environment.TickCount64 > stageDeadline)
            Abort($"{queue[step].Name} never appeared - was that a knowledge crystal?");
    }

    private void TickRestoring()
    {
        var state = PhantomReader.Read();
        if (state.Available && state.CurrentJob == restoreJob)
        {
            Stage = PhantomBuffStage.Done;
            idleUntilTick = Environment.TickCount64 + CooldownMs;
            return;
        }

        if (Environment.TickCount64 > stageDeadline)
        {
            Stage = PhantomBuffStage.Failed;
            Detail = $"Buffed, but couldn't switch back to Phantom {PhantomJobs.NameOf(restoreJob)}.";
            idleUntilTick = Environment.TickCount64 + CooldownMs;
            return;
        }

        if (Environment.TickCount64 < nextAttemptTick)
            return;

        nextAttemptTick = Environment.TickCount64 + RetryIntervalMs;
        PhantomReader.ChangeJob(restoreJob);
    }

    private void BeginRestore(string detail)
    {
        Detail = detail;
        Stage = PhantomBuffStage.Restoring;
        stageDeadline = Environment.TickCount64 + JobChangeTimeoutMs;
        nextAttemptTick = 0;
    }

    /// Stops, and still puts the job back.
    private void Abort(string reason)
    {
        Detail = reason;

        if (Stage == PhantomBuffStage.Approaching)
        {
            driver.Stop();
            driver.SuppressMount = false;
        }

        var state = PhantomReader.Read();
        if (state.Available && state.CurrentJob != restoreJob && !condition[ConditionFlag.InCombat])
        {
            BeginRestore(reason);
            return;
        }

        Stage = PhantomBuffStage.Failed;
        idleUntilTick = Environment.TickCount64 + CooldownMs;
    }

    /// A status that has just been applied, rather than the tail end of an old one.
    private static bool Fresh(IReadOnlyDictionary<uint, float> statuses, uint statusId) =>
        statuses.TryGetValue(statusId, out var remaining) && remaining > RefreshBelowSeconds;

    private Dictionary<uint, float> Statuses()
    {
        var result = new Dictionary<uint, float>();

        var player = objectTable.LocalPlayer;
        if (player == null)
            return result;

        foreach (var status in player.StatusList)
        {
            if (status is { StatusId: > 0 })
                result[status.StatusId] = status.RemainingTime;
        }

        return result;
    }

    /// The closest knowledge crystal in the object table, or null if none are loaded.
    private Vector3? NearestCrystal(Vector3 playerPosition)
    {
        Vector3? best = null;
        var bestDistance = float.MaxValue;

        foreach (var obj in objectTable)
        {
            if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj)
                continue;

            if (obj.BaseId != KnowledgeCrystalDataId)
                continue;

            var distance = Vector3.Distance(obj.Position, playerPosition);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = obj.Position;
        }

        return best;
    }

    private bool CanAct() =>
        !condition[ConditionFlag.InCombat]
        && !condition[ConditionFlag.Casting]
        && !condition[ConditionFlag.BetweenAreas]
        && !condition[ConditionFlag.Occupied]
        && !condition[ConditionFlag.Unconscious];
}
