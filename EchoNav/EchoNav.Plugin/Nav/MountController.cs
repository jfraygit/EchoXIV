using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace EchoNav.Nav;

/// Gets the character onto a mount before a long ride, and back on after anything that dismounts them - a
/// teleport being the obvious one.
public sealed unsafe class MountController(ICondition condition, Configuration configuration)
{
    /// Mount Roulette, from the general actions.
    private const uint MountRouletteActionId = 9;

    /// Mounting has a cast, and moving cancels it, so the driver has to stand still and wait rather than set
    /// off immediately.
    public const long MountTimeoutMs = 4000;

    /// Gap between attempts.
    private const long RetryIntervalMs = 700;

    private long lastAttemptTick;

    /// Status returned by the last usability check.
    public uint LastStatus { get; private set; }

    /// RidingPillion counts too - riding on someone else's mount is still travelling at mount speed, and
    /// trying to summon another from there would just fail.
    public bool IsMounted => condition[ConditionFlag.Mounted] || condition[ConditionFlag.RidingPillion];

    /// Whether mounting is worth attempting at all right now.
    public bool CanTryMount =>
        !IsMounted
        && !condition[ConditionFlag.InCombat]
        && !condition[ConditionFlag.Casting]
        && !condition[ConditionFlag.BetweenAreas]
        && !condition[ConditionFlag.Mounting]
        && !condition[ConditionFlag.Unconscious];

    /// Fires the mount action if it is usable and no attempt was just made.
    public bool TryMount()
    {
        var now = Environment.TickCount64;
        if (now - lastAttemptTick < RetryIntervalMs)
            return false;

        lastAttemptTick = now;

        try
        {
            var manager = ActionManager.Instance();
            if (manager == null)
                return false;

            var chosen = configuration.MountId;
            if (chosen != 0)
            {
                LastStatus = manager->GetActionStatus(ActionType.Mount, chosen);
                if (LastStatus == 0)
                    return manager->UseAction(ActionType.Mount, chosen);
            }

            LastStatus = manager->GetActionStatus(ActionType.GeneralAction, MountRouletteActionId);
            if (LastStatus != 0)
                return false;

            return manager->UseAction(ActionType.GeneralAction, MountRouletteActionId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Mount attempt failed");
            return false;
        }
    }

    /// Dismount, from the same general actions as the roulette above.
    private const uint DismountActionId = 23;

    public bool TryDismount()
    {
        if (!IsMounted)
            return true;

        var now = Environment.TickCount64;
        if (now - lastAttemptTick < RetryIntervalMs)
            return false;

        lastAttemptTick = now;

        try
        {
            var manager = ActionManager.Instance();
            if (manager == null)
                return false;

            LastStatus = manager->GetActionStatus(ActionType.GeneralAction, DismountActionId);
            return LastStatus == 0 && manager->UseAction(ActionType.GeneralAction, DismountActionId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Dismount attempt failed");
            return false;
        }
    }

    public void Reset() => lastAttemptTick = 0;
}
