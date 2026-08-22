using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using EchoSim.Sim.Analysis;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace EchoSim.Game;

/// Watches what the player actually casts, so a fight can be scored while it is still happening.
public sealed unsafe class CombatTracker : IDisposable
{
    private delegate void ActionEffectDelegate(
        uint casterEntityId,
        nint caster,
        nint targetPos,
        FFXIVClientStructs.FFXIV.Client.Game.Character.ActionEffectHandler.Header* header,
        nint effects,
        nint targets);

    private readonly Hook<ActionEffectDelegate>? effectHook;
    private readonly List<TimelineCast> casts = [];
    private readonly object gate = new();

    /// Globals already reported to the log, so the diagnostic is one line each rather than one a second.
    private readonly HashSet<string> seenGlobals = [];

    private DateTime? combatStartedAt;
    private DateTime lastTick;
    private double activeSeconds;
    private bool targetableNow;

    public CombatTracker()
    {
        try
        {
            effectHook = Plugin.GameInterop.HookFromAddress<ActionEffectDelegate>(
                (nint)FFXIVClientStructs.FFXIV.Client.Game.Character.ActionEffectHandler.MemberFunctionPointers.Receive,
                OnActionEffect);

            effectHook.Enable();
            Available = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "EchoSim: could not watch action use, so the live score is unavailable");
            Available = false;
        }
    }

    /// Whether the tracker managed to attach.
    public bool Available { get; }

    public bool InCombat => combatStartedAt is not null;

    /// Seconds since combat began, or zero when out of combat.
    public double Elapsed => elapsedSeconds;

    private double elapsedSeconds;

    /// Whether this fight was already under way when the plugin started watching.
    public bool Partial => partial;

    private bool partial;

    /// Whether the player has ever been seen out of combat, which is what makes a combat START
    /// distinguishable from the plugin arriving late to one.
    private bool observedOutOfCombat;

    /// Seconds there was something to hit, which is what the score is measured against.
    public double ActiveSeconds => activeSeconds;

    /// Whether anything is currently hittable, so the readout can say why it has paused.
    public bool TargetAvailable => targetableNow;

    /// WHERE the downtime was, not just how much of it there was.
    public IReadOnlyList<DowntimeWindow> Downtime
    {
        get
        {
            lock (gate)
                return [.. downtime];
        }
    }

    private readonly List<DowntimeWindow> downtime = [];

    /// Where the gap currently open began, or null when there is something to hit.
    public double? DowntimeOpenedAt => downtimeStartedAt;

    /// Start of the gap currently open, or null when something is hittable.
    private double? downtimeStartedAt;

    /// The shortest gap worth recording.
    private const double MinimumDowntimeSeconds = 1.0;

    /// A snapshot of the fight so far, safe to read from the draw thread.
    public IReadOnlyList<TimelineCast> Casts
    {
        get
        {
            lock (gate)
                return [.. casts];
        }
    }

    /// Starts and stops the fight from the game's own combat flag.
    public void Update()
    {
        var now = DateTime.UtcNow;
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];

        if (inCombat && combatStartedAt is null)
        {
            lock (gate)
                casts.Clear();

            partial = !observedOutOfCombat;

            combatStartedAt = now;
            activeSeconds = 0;
            elapsedSeconds = 0;
            lastTick = now;

            lock (gate)
                downtime.Clear();

            downtimeStartedAt = null;

            bossEntityId = 0;
            bossMaxHp = 0;
            bossDied = false;
            markerPresent = false;
            phaseMarker = false;

            Plugin.Log.Information(partial
                ? "EchoSim: combat started, already under way - this pull cannot be scored."
                : "EchoSim: combat started.");
        }
        else if (!inCombat && combatStartedAt is not null)
        {
            combatStartedAt = null;

            int recorded;
            lock (gate)
                recorded = casts.Count;

            Plugin.Log.Information(
                $"EchoSim: combat ended after {elapsedSeconds:F0}s " +
                $"({activeSeconds:F0}s active, {recorded} casts).");
        }

        if (!inCombat)
            observedOutOfCombat = true;

        targetableNow = ScanEnemies();
        ScanPhaseMarker();

        if (combatStartedAt is { } startedAt)
        {
            var step = Math.Clamp((now - lastTick).TotalSeconds, 0, 1.0);

            elapsedSeconds += step;

            if (targetableNow)
                activeSeconds += step;

            TrackDowntime(targetableNow, (now - startedAt).TotalSeconds);
        }

        lastTick = now;
    }

    /// Opens a gap when the last thing worth hitting goes away and closes it when something returns.
    private void TrackDowntime(bool targetable, double at)
    {
        if (!targetable)
        {
            downtimeStartedAt ??= at;
            return;
        }

        if (downtimeStartedAt is not { } from)
            return;

        downtimeStartedAt = null;

        if (at - from < MinimumDowntimeSeconds)
            return;

        lock (gate)
            downtime.Add(new DowntimeWindow(from, at));
    }

    /// Every status the game calls "Down for the Count", dumped from the Status sheet.
    private static readonly HashSet<uint> PhaseMarkers =
    [
        625, 774, 783, 896, 1762, 1785, 1950, 1953, 1963,
        2408, 2910, 2961, 3165, 3501, 3730, 3908, 3983, 4132, 4350,
    ];

    private bool markerPresent;

    private bool phaseMarker;

    /// The biggest enemy seen this pull, which is the boss.
    private uint bossEntityId;

    private uint bossMaxHp;

    private bool bossDied;

    /// Whether a phase has ended since this was last asked.
    public bool ConsumePhaseBoundary()
    {
        if (!bossDied && !phaseMarker)
            return false;

        bossDied = false;
        phaseMarker = false;
        return true;
    }

    /// Ends the current fight where it stands and starts a new one, returning what was closed off.
    public (IReadOnlyList<TimelineCast> Casts, double Active, double Elapsed,
        IReadOnlyList<DowntimeWindow> Downtime) Split()
    {
        var now = DateTime.UtcNow;
        var elapsed = elapsedSeconds;

        var endedAt = combatStartedAt is { } startedAt ? (now - startedAt).TotalSeconds : elapsed;
        TrackDowntime(true, endedAt);

        TimelineCast[] taken;
        DowntimeWindow[] gaps;
        lock (gate)
        {
            taken = [.. casts];
            casts.Clear();

            gaps = [.. downtime];
            downtime.Clear();
        }

        downtimeStartedAt = null;

        var active = activeSeconds;

        activeSeconds = 0;
        elapsedSeconds = 0;

        partial = false;

        if (combatStartedAt is not null)
            combatStartedAt = now;

        bossEntityId = 0;
        bossMaxHp = 0;
        bossDied = false;

        return (taken, active, elapsed, gaps);
    }

    /// Watches the player's own statuses for the intermission lockout that marks a phase boundary.
    private void ScanPhaseMarker()
    {
        try
        {
            var present = false;

            if (Plugin.ObjectTable.LocalPlayer is { } player)
            {
                foreach (var status in player.StatusList)
                {
                    if (status is null || !PhaseMarkers.Contains(status.StatusId))
                        continue;

                    present = true;

                    if (!markerPresent)
                    {
                        phaseMarker = true;
                        Plugin.Log.Information(
                            $"EchoSim: phase boundary - \"Down for the Count\" ({status.StatusId}) applied");
                    }

                    break;
                }
            }

            markerPresent = present;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "EchoSim: could not read the player's statuses");
        }
    }

    /// Whether there is anything that can currently be hit, and which enemy is the boss.
    private bool ScanEnemies()
    {
        var targetable = false;

        try
        {
            foreach (var obj in Plugin.ObjectTable)
            {
                if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
                    continue;

                if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleChara chara)
                    continue;

                if (obj.IsTargetable && chara.CurrentHp > 0)
                    targetable = true;

                if (combatStartedAt is not null && chara.CurrentHp > 0 && chara.MaxHp > bossMaxHp)
                {
                    var replacing = bossEntityId != 0;

                    bossEntityId = obj.EntityId;
                    bossMaxHp = chara.MaxHp;

                    Plugin.Log.Information(
                        $"EchoSim: {(replacing ? "boss now" : "boss")} \"{obj.Name.TextValue}\" " +
                        $"({chara.MaxHp:N0} HP)");
                }
                else if (obj.EntityId == bossEntityId && bossEntityId != 0 && chara.CurrentHp == 0 && !bossDied)
                {
                    bossDied = true;
                    Plugin.Log.Information($"EchoSim: boss \"{obj.Name.TextValue}\" died");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "EchoSim: could not read the object table for downtime");
        }

        return targetable;
    }

    /// One action RESOLVING, which is the only moment a cast is a fact.
    private void OnActionEffect(
        uint casterEntityId,
        nint caster,
        nint targetPos,
        FFXIVClientStructs.FFXIV.Client.Game.Character.ActionEffectHandler.Header* header,
        nint effects,
        nint targets)
    {
        effectHook!.Original(casterEntityId, caster, targetPos, header, effects, targets);

        try
        {
            if (header is null || combatStartedAt is not { } start)
                return;

            if (casterEntityId != Plugin.ObjectTable.LocalPlayer?.EntityId)
                return;

            if (header->ActionType != PlayerActionType)
                return;

            Record(header->ActionId, (DateTime.UtcNow - start).TotalSeconds);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "EchoSim: failed to record an action effect");
        }
    }

    /// ActionType for a player action, as opposed to an item or a mount.
    private const byte PlayerActionType = 1;

    /// Auto-attack and its ranged twin, which resolve constantly and are not pressed.
    private static readonly HashSet<uint> AutoAttacks = [7, 8];

    private void Record(uint actionId, double at)
    {
        if (AutoAttacks.Contains(actionId))
            return;

        var name = GameData.ActionName(actionId);
        if (string.IsNullOrEmpty(name))
            return;

        var isGcd = GameData.IsGcdAction(actionId);

        if (seenGlobals.Add(name))
        {
            Plugin.Log.Information(
                $"EchoSim: recorded \"{name}\" ({(isGcd ? "global" : "off-global")}, id {actionId})");
        }

        lock (gate)
            casts.Add(new TimelineCast(at, name, isGcd, 0));
    }

    public void Dispose()
    {
        effectHook?.Disable();
        effectHook?.Dispose();
    }
}
