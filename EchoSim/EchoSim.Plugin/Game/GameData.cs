using Dalamud.Game;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using EchoSim.Sim;
using EchoSim.Sim.Analysis;
using EchoSim.Sim.Engine;
using EchoSim.Sim.Jobs;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaItem = Lumina.Excel.Sheets.Item;
using LuminaItemFood = Lumina.Excel.Sheets.ItemFood;

namespace EchoSim.Game;

/// Everything read out of the game's own Excel sheets.
public static class GameData
{
    /// BaseParam row ids, matching the ones the gear reader uses.
    private static readonly Dictionary<uint, SubStat> ParamToSubStat = new()
    {
        [19] = SubStat.Tenacity,
        [22] = SubStat.DirectHit,
        [27] = SubStat.Crit,
        [44] = SubStat.Determination,
        [45] = SubStat.Speed,
    };

    /// ItemUICategory row for "Meal".
    private const uint MealCategory = 46;

    private static List<FoodDef>? foodCache;

    /// Loads the food list off the draw thread.
    public static void WarmFoods()
    {
        if (foodCache is not null || foodsLoading)
            return;

        foodsLoading = true;
        Task.Run(() =>
        {
            try
            {
                LoadFoods();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "EchoSim: could not load food data");
            }
            finally
            {
                foodsLoading = false;
            }
        });
    }

    private static volatile bool foodsLoading;

    /// Every level-100-era food that boosts a stat the sim cares about, newest first.
    public static IReadOnlyList<FoodDef> Foods()
    {
        if (foodCache is not null)
            return foodCache;

        WarmFoods();
        return [FoodDef.None];
    }

    private static void LoadFoods()
    {
        var found = new List<(int ItemLevel, FoodDef Food)>();

        try
        {
            var items = Plugin.DataManager.GetExcelSheet<LuminaItem>();
            var foods = Plugin.DataManager.GetExcelSheet<LuminaItemFood>();

            foreach (var item in items)
            {
                var itemLevel = (int)item.LevelItem.RowId;
                if (itemLevel < 640)
                    continue;

                if (item.ItemUICategory.RowId != MealCategory || !item.ItemAction.IsValid)
                    continue;

                var itemAction = item.ItemAction.Value;
                if (itemAction.Data.Count < 2 || !foods.TryGetRow(itemAction.Data[1], out var foodRow))
                    continue;

                var stats = new List<FoodParam>();
                foreach (var p in foodRow.Params)
                {
                    if (!ParamToSubStat.TryGetValue(p.BaseParam.RowId, out var stat))
                        continue;

                    if (!p.IsRelative || p.ValueHQ <= 0)
                        continue;

                    stats.Add(new FoodParam(stat, p.ValueHQ, p.MaxHQ));
                }

                if (stats.Count == 0)
                    continue;

                found.Add((itemLevel, new FoodDef
                {
                    Name = item.Name.ExtractText(),
                    ItemId = item.RowId,
                    IconId = item.Icon,
                    Params = stats,
                }));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not read the food list from game data");
        }

        foodCache =
        [
            FoodDef.None,
            .. found.OrderByDescending(x => x.ItemLevel).ThenBy(x => x.Food.Name).Select(x => x.Food),
        ];

        Plugin.Log.Information($"EchoSim: loaded {foodCache.Count - 1} foods from game data.");
    }

    public static FoodDef FoodById(uint itemId)
        => Foods().FirstOrDefault(f => f.ItemId == itemId) ?? FoodDef.None;


    private static Dictionary<string, uint>? actionIconCache;

    /// Icon id for an action, looked up by name against the Action sheet.
    public static uint ActionIcon(string simActionName)
    {
        if (actionIconCache is null)
        {
            WarmActionIcons();
            return 0;
        }

        var name = Normalise(simActionName);
        return actionIconCache.GetValueOrDefault(name);
    }

    /// Builds the action-icon map off the draw thread.
    public static void WarmActionIcons()
    {
        if (actionIconCache is not null || actionIconsLoading)
            return;

        actionIconsLoading = true;
        Task.Run(() =>
        {
            try
            {
                actionIconCache = BuildActionIcons();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "EchoSim: could not build the action icon map");
            }
            finally
            {
                actionIconsLoading = false;
            }
        });
    }

    private static volatile bool actionIconsLoading;

    /// Names the sim invents that don't exist as actions, mapped to something that does.
    private static string Normalise(string simActionName)
    {
        var name = simActionName;

        var suffix = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (suffix > 0 && name.EndsWith(')'))
            name = name[..suffix];

        return name switch
        {
            "Gemdraught" => "Gemdraught",
            "Auto-attack" => "Attack",

            "Dance Step" => "Emboite",

            _ => name,
        };
    }

    /// How strong a candidate an Action row is for owning its name's icon.
    private static int Rank(LuminaAction action)
    {
        var score = 0;

        if (action.ClassJobLevel > 0)
            score += 4;

        if (action.ClassJob.RowId != uint.MaxValue)
            score += 2;

        if (action.IsPlayerAction)
            score += 2;

        if (!action.IsPvP)
            score += 1;

        return score;
    }

    private static Dictionary<string, uint> BuildActionIcons()
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var action in Plugin.DataManager.GetExcelSheet<LuminaAction>(ClientLanguage.English))
            {
                var name = action.Name.ExtractText();
                if (string.IsNullOrEmpty(name) || action.Icon == 0)
                    continue;

                var score = Rank(action);

                if (scores.TryGetValue(name, out var best) && best >= score)
                    continue;

                scores[name] = score;
                map[name] = action.Icon;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not read action icons from game data");
        }

        try
        {
            var gemdraught = Plugin.DataManager.GetExcelSheet<LuminaItem>()
                .Where(i => i.Name.ExtractText().Contains("Gemdraught", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.LevelItem.RowId)
                .FirstOrDefault();

            if (gemdraught.Icon != 0)
                map["Gemdraught"] = gemdraught.Icon;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not resolve the Gemdraught icon");
        }

        return map;
    }

    /// Logs any sim action name that didn't resolve to an icon.
    public static void LogMissingIcons(IEnumerable<string> simActionNames)
    {
        var missing = simActionNames.Distinct().Where(n => ActionIcon(n) == 0).ToList();
        if (missing.Count > 0)
            Plugin.Log.Warning($"EchoSim: no icon for {string.Join(", ", missing)}");
    }

    /// The in-game name of an action by its id, in English whatever the client is running.
    public static string ActionName(uint actionId)
    {
        if (actionId == 0)
            return string.Empty;

        try
        {
            return Plugin.DataManager.GetExcelSheet<LuminaAction>(ClientLanguage.English)
                       .TryGetRow(actionId, out var action)
                ? action.Name.ExtractText()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// The cooldown group every action sharing the global cooldown belongs to.
    private const byte GlobalCooldownGroup = 58;

    /// Shortest recast, in 100ms units, that counts as consuming a GCD.
    private const ushort MinGcdRecast = 10;

    /// Ninja's mudras, which sit in the global cooldown group but do not cost a global.
    private static readonly HashSet<uint> Mudras = [2259, 2261, 2263, 18805, 18806, 18807];

    /// Whether an action consumes a global cooldown.
    public static bool IsGcdAction(uint actionId)
    {
        if (actionId == 0)
            return false;

        if (Mudras.Contains(actionId))
            return false;

        try
        {
            if (!Plugin.DataManager.GetExcelSheet<LuminaAction>().TryGetRow(actionId, out var action))
                return false;

            var takesGlobal = action.CooldownGroup == GlobalCooldownGroup
                              || action.AdditionalCooldownGroup == GlobalCooldownGroup;

            return takesGlobal && action.Recast100ms >= MinGcdRecast;
        }
        catch
        {
            return false;
        }
    }

    /// Checks every simulated job's main-stat modifier against the game's own ClassJob sheet.
    public static void VerifyJobModifiers()
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();

            foreach (var jobId in JobRegistry.Implemented)
            {
                var definition = JobRegistry.For(jobId)!;
                if (!sheet.TryGetRow(jobId, out var row))
                    continue;

                var sim = definition.CreateSim();
                var actual = sim.MainAttribute switch
                {
                    MainAttribute.Strength => row.ModifierStrength,
                    MainAttribute.Dexterity => row.ModifierDexterity,
                    MainAttribute.Intelligence => row.ModifierIntelligence,
                    MainAttribute.Mind => row.ModifierMind,
                    _ => row.ModifierStrength,
                };

                var modelled = sim.MainStatModifier;

                if (actual == modelled)
                    continue;

                var perPoint = Sim.LevelStats.Lv100.BaseMainStat / 1000.0;

                Plugin.Log.Warning(
                    $"EchoSim: {definition.Name}'s main-stat modifier is {modelled} in the sim but " +
                    $"{actual} in game data. Damage for this job is off by roughly " +
                    $"{Math.Abs(actual - modelled) * perPoint:F1} weapon-damage points.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not verify job main-stat modifiers against game data");
        }
    }

    /// Checks that every cooldown the log analysis audits is a name the game's Action sheet knows.
    public static void VerifyAnalysisActions()
    {
        try
        {
            actionIconCache ??= BuildActionIcons();

            var unresolved = new List<string>();

            foreach (var jobId in JobRegistry.Implemented)
            {
                var definition = JobRegistry.For(jobId)!;

                foreach (var action in RotationAnalysis.AuditedCooldowns(definition.CreateSim()))
                {
                    if (ActionIcon(action.Name) == 0)
                        unresolved.Add($"{definition.Name}: {action.Name}");

                    unresolved.AddRange(action.LogAliases
                        .Where(alias => ActionIcon(alias) == 0)
                        .Select(alias => $"{definition.Name}: {action.Name} -> {alias}"));
                }
            }

            if (unresolved.Count > 0)
            {
                Plugin.Log.Warning(
                    "EchoSim: the log analysis audits these cooldowns but the game's Action sheet has no " +
                    $"row by that name, so a parse can never match them - {string.Join(", ", unresolved)}");
            }

            WarnOnUndeclaredTransformations();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not verify the analysed cooldown names against game data");
        }
    }

    /// Audited cooldowns that transform into something else, and haven't said so.
    private static void WarnOnUndeclaredTransformations()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<LuminaAction>();

        var transformed = new Dictionary<byte, List<LuminaAction>>();

        foreach (var row in sheet)
        {
            if (row.ClassJob.RowId != 0 || row.CooldownGroup == 0 || row.IsPlayerAction || row.IsPvP)
                continue;

            if (string.IsNullOrEmpty(row.Name.ExtractText()))
                continue;

            if (!transformed.TryGetValue(row.CooldownGroup, out var list))
                transformed[row.CooldownGroup] = list = [];

            list.Add(row);
        }

        var undeclared = new List<string>();

        foreach (var jobId in JobRegistry.Implemented)
        {
            var definition = JobRegistry.For(jobId)!;
            var modelled = definition.CreateSim().Actions;

            foreach (var action in RotationAnalysis.AuditedCooldowns(definition.CreateSim()))
            {
                if (action.LogAliases.Length > 0)
                    continue;

                var own = sheet.FirstOrDefault(r =>
                    r.ClassJob.RowId == jobId && r.Name.ExtractText() == action.Name);

                if (own.RowId == 0 || !transformed.TryGetValue(own.CooldownGroup, out var siblings))
                    continue;

                var names = siblings
                    .Where(s => s.ClassJobCategory.RowId == own.ClassJobCategory.RowId
                                && s.AdditionalCooldownGroup == own.AdditionalCooldownGroup
                                && s.Recast100ms == own.Recast100ms
                                && s.MaxCharges == own.MaxCharges
                                && s.Name.ExtractText() != action.Name)
                    .Select(s => s.Name.ExtractText())
                    .Distinct()
                    .ToList();

                if (names.Count > 0 && !names.Any(modelled.ContainsKey))
                    undeclared.Add($"{definition.Name}: {action.Name} logs as {string.Join("/", names)}");
            }
        }

        if (undeclared.Count > 0)
        {
            Plugin.Log.Warning(
                "EchoSim: these audited cooldowns transform into another action, so a parse never records " +
                "them under the modelled name and the analysis will report them as unused. They need " +
                $"ActionDef.LogAliases - {string.Join("; ", undeclared)}");
        }
    }

    /// Loads a game icon as a texture, or null if it isn't ready or doesn't exist.
    public static IDalamudTextureWrap? Icon(uint iconId)
    {
        if (iconId == 0)
            return null;

        try
        {
            return Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
