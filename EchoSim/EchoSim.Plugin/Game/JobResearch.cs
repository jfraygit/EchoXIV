using System.Text;
using Lumina.Excel.Sheets;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace EchoSim.Game;

/// Dumps a job's actions out of the game's own data, for building a new job's sim against.
public static class JobResearch
{
    /// Actions the game marks as belonging to a job, at level 100 and below.
    public static string Dump(uint classJobId, string jobName)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {jobName} actions (ClassJob {classJobId}) - dumped from game data");
        text.AppendLine("# columns: id | name | lvl | category | cdGroup/alt | cast | recast | mp | charges | range | potency text");
        text.AppendLine();

        try
        {
            var actions = Plugin.DataManager.GetExcelSheet<LuminaAction>();
            var transients = Plugin.DataManager.GetExcelSheet<ActionTransient>();

            var parent = Plugin.DataManager.GetExcelSheet<ClassJob>().TryGetRow(classJobId, out var job)
                ? job.ClassJobParent.RowId
                : 0;

            text.AppendLine($"# includes base class {parent} where one exists");
            text.AppendLine();

            var rows = actions
                .Where(a => (a.ClassJob.RowId == classJobId || (parent != 0 && a.ClassJob.RowId == parent))
                            && !a.IsPvP
                            && !string.IsNullOrEmpty(a.Name.ExtractText()))

                .OrderBy(a => a.ClassJobLevel)
                .ThenBy(a => a.RowId);

            foreach (var action in rows)
            {
                var potency = transients.TryGetRow(action.RowId, out var transient)
                    ? Summarise(transient.Description.ExtractText())
                    : string.Empty;

                var cost = action.PrimaryCostType == 3 ? $"mp {action.PrimaryCostValue * 100,5}" : "mp     -";

                text.AppendLine(
                    $"{action.RowId,6} | {action.Name.ExtractText(),-28} | {action.ClassJobLevel,3} | " +
                    $"cat {action.ActionCategory.RowId,2} | cd {action.CooldownGroup,3}/{action.AdditionalCooldownGroup,3} | " +
                    $"cast {action.Cast100ms * 0.1f,4:F1}s | recast {action.Recast100ms * 0.1f,5:F1}s | {cost} | " +
                    $"chg {action.MaxCharges,2} | rng {action.Range,3} | {potency}");
            }
        }
        catch (Exception ex)
        {
            text.AppendLine($"# FAILED: {ex}");
        }

        return text.ToString();
    }

    /// Pulls the numbers out of a tooltip and drops the prose.
    private static string Summarise(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        var flattened = description.Replace('\n', ' ').Replace("\r", string.Empty);

        var kept = flattened
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(sentence => sentence.Contains("otency", StringComparison.OrdinalIgnoreCase)
                               || sentence.Contains("Duration", StringComparison.OrdinalIgnoreCase)
                               || sentence.Contains("Combo", StringComparison.OrdinalIgnoreCase))
            .Take(4);

        var joined = string.Join(" | ", kept);
        return joined.Replace("potency of  ", "potency of <?> ", StringComparison.OrdinalIgnoreCase)
                     .Replace("Potency:  ", "Potency: <?> ", StringComparison.OrdinalIgnoreCase);
    }
}
