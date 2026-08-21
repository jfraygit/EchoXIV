using System;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace EchoNav.Nav;

/// Wraps Occult Crescent's "Occult Return", the action that drops you straight back at base camp.
public sealed unsafe class ReturnAction(IDataManager dataManager, ICondition condition)
{
    /// Name to find in the Action sheet.
    private const string ActionName = "Occult Return";

    /// Three second cast plus the loading screen, expressed as the distance that would take about as long.
    public const float CostInYalms = 200f;

    private uint? actionId;
    private bool searched;

    /// Row id of the action, or null if it isn't in the sheet.
    public uint? ActionId
    {
        get
        {
            if (searched)
                return actionId;

            searched = true;

            try
            {
                var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>(ClientLanguage.English);
                if (sheet != null)
                {
                    foreach (var row in sheet)
                    {
                        if (string.Equals(row.Name.ExtractText(), ActionName, StringComparison.OrdinalIgnoreCase))
                        {
                            actionId = row.RowId;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[EchoNav] Could not look up the Return action");
            }

            return actionId;
        }
    }

    /// Status the game reports for using it now.
    public uint LastStatus { get; private set; }

    public bool IsAvailable
    {
        get
        {
            if (ActionId is not { } id)
                return false;

            if (condition[ConditionFlag.InCombat] || condition[ConditionFlag.BetweenAreas])
                return false;

            try
            {
                var manager = ActionManager.Instance();
                if (manager == null)
                    return false;

                LastStatus = manager->GetActionStatus(ActionType.Action, id);
                return LastStatus == 0;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[EchoNav] Could not check the Return action");
                return false;
            }
        }
    }

    public bool Use()
    {
        if (ActionId is not { } id)
            return false;

        try
        {
            var manager = ActionManager.Instance();
            return manager != null && manager->UseAction(ActionType.Action, id);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Could not use the Return action");
            return false;
        }
    }
}
