using EchoSim.Sim;
using EchoSim.Sim.Engine;
using LuminaClassJobCategory = Lumina.Excel.Sheets.ClassJobCategory;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace EchoSim.Game;

public enum GearSlot
{
    Weapon,

    /// Paladin's shield, and nothing else in the game.
    OffHand,

    Head,
    Body,
    Hands,
    Legs,
    Feet,
    Ears,
    Neck,
    Wrists,
    RingL,
    RingR,
}

/// One equippable item, with the stats the sim cares about already extracted.
public sealed record GearPiece(
    uint ItemId,
    string Name,
    uint IconId,
    int ItemLevel,
    int MateriaSlots,
    int WeaponDamage,
    double WeaponDelay,
    int MainStat,
    int Crit,
    int Determination,
    int DirectHit,
    int SkillSpeed,
    int Tenacity,
    bool IsUnique = false,
    bool AdvancedMeldingPermitted = false,
    int Piety = 0)
{
    public static readonly GearPiece Empty = new(0, "-", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// Total materia an item may hold once overmelding is allowed for.
    public const int OvermeldCap = 5;

    /// The most materia this item can actually carry, which is NOT its slot count.
    public int MaxMelds => AdvancedMeldingPermitted ? OvermeldCap : MateriaSlots;

    /// The highest this item can carry of each substat, base plus melds combined.
    public IReadOnlyDictionary<SubStat, int> Caps { get; init; } = new Dictionary<SubStat, int>();

    public int CapFor(SubStat stat) => Caps.TryGetValue(stat, out var cap) ? cap : int.MaxValue;

    /// Which weapon slot this occupies, or null when it is not a weapon at all.
    public RelicSlot? RelicScale { get; init; }

    /// A weapon whose Item row carries no substats at all - which is how a customisable relic looks, because
    /// its substats are chosen by the player rather than baked into the item.
    public bool IsCustomisableRelic
        => RelicScale is not null
           && Crit == 0 && Determination == 0 && DirectHit == 0 && SkillSpeed == 0 && Tenacity == 0
           && Piety == 0;

    /// How good this piece is, used to auto-pick a set.
    public double Score(bool wantsSpeed = false)
    {
        var score =
            (ItemLevel * 10_000.0)
            + (Crit * 1.00)
            + (Determination * 0.90)
            + (DirectHit * 0.85)
            - (wantsSpeed ? 0.0 : SkillSpeed * 0.50)
            + (WeaponDamage * 50.0);

        if (IsCustomisableRelic && RelicScale is { } scale)
        {
            score += (RelicAllocation.MajorValue(scale) * 1.00)
                     + (RelicAllocation.MajorValue(scale) * 0.90)
                     + (RelicAllocation.MinorValue(scale) * 0.85);
        }

        return score;
    }

    public int StatFor(SubStat stat) => stat switch
    {
        SubStat.Crit => Crit,
        SubStat.Determination => Determination,
        SubStat.DirectHit => DirectHit,
        SubStat.Speed => SkillSpeed,
        SubStat.Piety => Piety,
        _ => Tenacity,
    };

    /// "CRT +123, DET +87" style summary of everything non-zero.
    public string StatSummary()
    {
        var parts = new List<string>();
        if (MainStat > 0) parts.Add($"{GearCatalog.MainStatLabel} +{MainStat}");
        if (Crit > 0) parts.Add($"CRT +{Crit}");
        if (Determination > 0) parts.Add($"DET +{Determination}");
        if (DirectHit > 0) parts.Add($"DH +{DirectHit}");
        if (SkillSpeed > 0) parts.Add($"{GearCatalog.SpeedStatLabel} +{SkillSpeed}");
        if (Tenacity > 0) parts.Add($"TEN +{Tenacity}");
        if (Piety > 0) parts.Add($"PIE +{Piety}");
        return parts.Count == 0 ? "no substats" : string.Join(", ", parts);
    }
}

/// The list of gear a Ninja can equip, built from the game's Item sheet.
public static class GearCatalog
{
    private const uint ParamStr = 1;
    private const uint ParamDex = 2;
    private const uint ParamInt = 4;
    private const uint ParamMind = 5;
    /// Piety.
    private const uint ParamPiety = 6;

    private const uint ParamTenacity = 19;
    private const uint ParamDirectHit = 22;
    private const uint ParamCrit = 27;
    private const uint ParamDetermination = 44;
    private const uint ParamSkillSpeed = 45;
    private const uint ParamSpellSpeed = 46;

    /// Which attribute the active job scales off, asked of the JOB rather than kept as a list here.
    private static MainAttribute activeAttribute = Sim.Jobs.JobRegistry.Default.CreateSim().MainAttribute;

    private static MainAttribute ActiveAttribute => activeAttribute;

    private static uint MainStatParam => ActiveAttribute switch
    {
        MainAttribute.Dexterity => ParamDex,
        MainAttribute.Intelligence => ParamInt,
        MainAttribute.Mind => ParamMind,
        _ => ParamStr,
    };

    /// The main stat's short label, for anywhere the number is shown to a player.
    public static string MainStatLabel => ActiveAttribute switch
    {
        MainAttribute.Dexterity => "DEX",
        MainAttribute.Intelligence => "INT",
        MainAttribute.Mind => "MND",
        _ => "STR",
    };

    /// Which speed substat the active job's gear carries - Spell Speed for the casters, Skill Speed for
    /// everyone else.
    public static uint SpeedParam => ActiveAttribute.UsesSpellSpeed() ? ParamSpellSpeed : ParamSkillSpeed;

    /// The speed substat's short label.
    public static string SpeedStatLabel => ActiveAttribute.UsesSpellSpeed() ? "SPS" : "SKS";

    /// Any substat's short label, named as the ACTIVE JOB names it.
    public static string StatLabel(SubStat stat)
        => stat == SubStat.Speed
            ? (ActiveAttribute.UsesSpellSpeed() ? "SpS" : "SkS")
            : FoodDef.Label(stat);

    /// The main stat's full name, for the stat editor's field label.
    public static string MainStatName => ActiveAttribute switch
    {
        MainAttribute.Dexterity => "Dexterity",
        MainAttribute.Intelligence => "Intelligence",
        MainAttribute.Mind => "Mind",
        _ => "Strength",
    };

    /// The speed substat's full name.
    public static string SpeedStatName => ActiveAttribute.UsesSpellSpeed() ? "Spell Speed" : "Skill Speed";

    /// How far below the highest available item level still counts as "current tier".
    private const int CurrentTierSpread = 40;

    /// The job the catalogue is currently built for.
    public static uint ActiveJob { get; private set; } = Sim.Jobs.JobRegistry.NinjaId;

    /// Rebuilds the catalogue for a different job.
    public static void SetJob(uint classJobId, bool warm = true, bool force = false)
    {
        if (pinned && !force)
            return;

        if (classJobId == ActiveJob || classJobId == 0)
            return;

        ActiveJob = classJobId;
        activeAttribute = Sim.Jobs.JobRegistry.ForOrDefault(classJobId).CreateSim().MainAttribute;
        cache = null;
        byId = [];

        if (warm)
            Warm();
    }

    /// Whether a job can equip items in this category.
    private static bool CanEquip(LuminaClassJobCategory category, uint classJobId) => classJobId switch
    {
        20 => category.MNK,
        22 => category.DRG,
        30 => category.NIN,
        34 => category.SAM,
        39 => category.RPR,
        41 => category.VPR,

        23 => category.BRD,
        31 => category.MCH,
        38 => category.DNC,

        25 => category.BLM,
        27 => category.SMN,
        35 => category.RDM,
        42 => category.PCT,

        24 => category.WHM,
        28 => category.SCH,
        33 => category.AST,
        40 => category.SGE,

        19 => category.PLD,
        21 => category.WAR,
        32 => category.DRK,
        37 => category.GNB,

        _ => !Sim.Jobs.JobRegistry.IsImplemented(classJobId) && category.NIN,
    };

    private static Dictionary<GearSlot, List<GearPiece>>? cache;

    /// The build in flight, if any, and the job it is for.
    private static Task? building;
    private static uint buildingFor;

    /// Starts building the catalogue off the draw thread.
    public static void BuildNow()
    {
        lock (BuildGate)
        {
            var job = ActiveJob;

            try
            {
                building?.Wait(TimeSpan.FromSeconds(60));
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "EchoSim: waiting for a gear catalogue rebuild failed; building again.");
            }

            if (cache is not null && buildingFor == job)
                return;

            buildingFor = job;

            var built = Build();

            if (buildingFor != job)
                return;

            byId = built.ById;
            cache = built.Slots;
        }
    }

    /// Serialises catalogue builds, which share global attribute state while they run.
    private static readonly object BuildGate = new();

    /// Set while one caller owns the catalogue outright.
    private static bool pinned;

    /// Takes exclusive ownership of the catalogue until the returned handle is disposed.
    public static IDisposable Pin()
    {
        pinned = true;
        return new Pinned();
    }

    private sealed class Pinned : IDisposable
    {
        public void Dispose() => pinned = false;
    }

    public static void Warm()
    {
        if (cache is not null)
            return;

        var job = ActiveJob;
        buildingFor = job;

        building = building is { IsCompleted: false }
            ? building.ContinueWith(_ => BuildAndPublish(job))
            : Task.Run(() => BuildAndPublish(job));
    }

    /// Builds the catalogue and publishes it, unless the job moved on while it ran.
    private static void BuildAndPublish(uint job)
    {
        try
        {
            var built = Build();

            if (buildingFor != job)
                return;

            byId = built.ById;
            cache = built.Slots;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "EchoSim: could not build the gear catalogue");
        }
    }

    /// Item id to piece, including pieces resolved outside the catalogue.
    private static Dictionary<uint, GearPiece?> byId = [];

    public static IReadOnlyList<GearPiece> For(GearSlot slot)
    {
        if (cache is null)
        {
            Warm();
            return [];
        }

        return cache.GetValueOrDefault(slot, []);
    }

    /// Looks an item up by id.
    public static GearPiece? ById(uint itemId)
    {
        if (itemId == 0)
            return null;

        if (cache is null)
            Warm();

        if (byId.TryGetValue(itemId, out var cached))
            return cached;

        GearPiece? resolved = null;

        try
        {
            if (Plugin.DataManager.GetExcelSheet<LuminaItem>().TryGetRow(itemId, out var item))
                resolved = Extract(item, (int)item.LevelItem.RowId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Could not read item {itemId}");
        }

        byId[itemId] = resolved;
        return resolved;
    }

    /// Scans every item in the game for this job.
    private static (Dictionary<GearSlot, List<GearPiece>> Slots, Dictionary<uint, GearPiece?> ById) Build()
    {
        var resolved = new Dictionary<uint, GearPiece?>();
        var result = new Dictionary<GearSlot, List<GearPiece>>();
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
            result[slot] = [];

        try
        {
            var candidates = new List<(int ItemLevel, List<GearSlot> Slots, GearPiece Piece)>();

            foreach (var item in Plugin.DataManager.GetExcelSheet<LuminaItem>())
            {
                if (item.LevelEquip != 100)
                    continue;

                if (!item.ClassJobCategory.IsValid || !CanEquip(item.ClassJobCategory.Value, ActiveJob))
                    continue;

                if (!item.EquipSlotCategory.IsValid)
                    continue;

                var slots = SlotsFor(item.EquipSlotCategory.Value);
                if (slots.Count == 0)
                    continue;

                var itemLevel = (int)item.LevelItem.RowId;
                candidates.Add((itemLevel, slots, Extract(item, itemLevel)));
            }

            var maxItemLevel = candidates.Count > 0 ? candidates.Max(c => c.ItemLevel) : 0;
            var cutoff = maxItemLevel - CurrentTierSpread;

            foreach (var (itemLevel, slots, piece) in candidates)
            {
                if (itemLevel < cutoff)
                    continue;

                resolved[piece.ItemId] = piece;
                foreach (var slot in slots)
                    result[slot].Add(piece);
            }

            Plugin.Log.Information(
                $"EchoSim: gear catalogue - top item level {maxItemLevel}, including i{cutoff} and above.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not build the gear catalogue");
        }

        foreach (var (slot, list) in result)
            result[slot] = [.. list.OrderByDescending(p => p.ItemLevel).ThenBy(p => p.Name)];

        Plugin.Log.Information($"EchoSim: gear catalogue built, {result.Sum(kv => kv.Value.Count)} entries.");
        return (result, resolved);
    }

    private static List<GearSlot> SlotsFor(Lumina.Excel.Sheets.EquipSlotCategory category)
    {
        var slots = new List<GearSlot>();

        if (category.MainHand == 1) slots.Add(GearSlot.Weapon);

        if (category.OffHand == 1) slots.Add(GearSlot.OffHand);

        if (category.Head == 1) slots.Add(GearSlot.Head);
        if (category.Body == 1) slots.Add(GearSlot.Body);
        if (category.Gloves == 1) slots.Add(GearSlot.Hands);
        if (category.Legs == 1) slots.Add(GearSlot.Legs);
        if (category.Feet == 1) slots.Add(GearSlot.Feet);
        if (category.Ears == 1) slots.Add(GearSlot.Ears);
        if (category.Neck == 1) slots.Add(GearSlot.Neck);
        if (category.Wrists == 1) slots.Add(GearSlot.Wrists);

        if (category.FingerL == 1 || category.FingerR == 1)
        {
            slots.Add(GearSlot.RingL);
            slots.Add(GearSlot.RingR);
        }

        return slots;
    }

    /// The percentage of an item level's budget this slot gets for a given stat, per thousand.
    private static ushort SlotPercent(Lumina.Excel.Sheets.BaseParam param, Lumina.Excel.Sheets.EquipSlotCategory cat)
    {
        if (cat.MainHand == 1)
            return cat.OffHand == -1 ? param.TwoHandWeaponPercent : param.OneHandWeaponPercent;

        if (cat.OffHand == 1) return param.OffHandPercent;

        if (cat.Head == 1) return param.HeadPercent;
        if (cat.Body == 1) return param.ChestPercent;
        if (cat.Gloves == 1) return param.HandsPercent;
        if (cat.Legs == 1) return param.LegsPercent;
        if (cat.Feet == 1) return param.FeetPercent;
        if (cat.Ears == 1) return param.EarringPercent;
        if (cat.Neck == 1) return param.NecklacePercent;
        if (cat.Wrists == 1) return param.BraceletPercent;
        if (cat.FingerL == 1 || cat.FingerR == 1) return param.RingPercent;

        return 0;
    }

    /// Per-substat caps for an item.
    private static Dictionary<SubStat, int> BuildCaps(LuminaItem item, int itemLevel)
    {
        var caps = new Dictionary<SubStat, int>();

        try
        {
            if (!item.EquipSlotCategory.IsValid)
                return caps;

            var category = item.EquipSlotCategory.Value;
            var paramSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.BaseParam>();
            var levelSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ItemLevel>();

            if (!levelSheet.TryGetRow((uint)itemLevel, out var levelRow))
                return caps;

            foreach (var (paramId, stat) in SubStatParams)
            {
                if (!paramSheet.TryGetRow(paramId, out var param))
                    continue;

                var budget = paramId switch
                {
                    ParamTenacity => levelRow.Tenacity,
                    ParamDirectHit => levelRow.DirectHitRate,
                    ParamCrit => levelRow.CriticalHit,
                    ParamDetermination => levelRow.Determination,
                    ParamSkillSpeed => levelRow.SkillSpeed,
                    ParamSpellSpeed => levelRow.SpellSpeed,
                    ParamPiety => levelRow.Piety,

                    _ => (ushort)0,
                };

                var cap = GearMath.SubstatCap(budget, SlotPercent(param, category));
                if (cap > 0)
                    caps[stat] = cap;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Could not compute stat caps for item {item.RowId}");
        }

        return caps;
    }

    /// The BaseParam rows worth reading off an item, for the job the catalogue is currently built for.
    private static (uint Param, SubStat Stat)[] SubStatParams =>
    [
        (ParamCrit, SubStat.Crit),
        (ParamDetermination, SubStat.Determination),
        (ParamDirectHit, SubStat.DirectHit),
        (SpeedParam, SubStat.Speed),
        (ParamTenacity, SubStat.Tenacity),

        (ParamPiety, SubStat.Piety),
    ];

    /// Which of the three weapon shapes an item is, or null for armour and accessories.
    private static RelicSlot? ScaleFor(Lumina.Excel.Sheets.EquipSlotCategory category)
    {
        if (category.MainHand == 1)
            return category.OffHand == -1 ? RelicSlot.TwoHand : RelicSlot.OneHand;

        return category.OffHand == 1 ? RelicSlot.OffHand : null;
    }

    private static GearPiece Extract(LuminaItem item, int itemLevel)
    {
        var stats = new Dictionary<uint, int>();

        for (var i = 0; i < item.BaseParam.Count; i++)
        {
            var param = item.BaseParam[i].RowId;
            if (param != 0)
                stats[param] = stats.GetValueOrDefault(param) + item.BaseParamValue[i];
        }

        for (var i = 0; i < item.BaseParamSpecial.Count; i++)
        {
            var param = item.BaseParamSpecial[i].RowId;
            if (param != 0)
                stats[param] = stats.GetValueOrDefault(param) + item.BaseParamValueSpecial[i];
        }

        return new GearPiece(
            item.RowId,
            item.Name.ExtractText(),
            item.Icon,
            itemLevel,
            item.MateriaSlotCount,
            item.DamagePhys,
            item.Delayms / 1000.0,
            stats.GetValueOrDefault(MainStatParam),
            stats.GetValueOrDefault(ParamCrit),
            stats.GetValueOrDefault(ParamDetermination),
            stats.GetValueOrDefault(ParamDirectHit),
            stats.GetValueOrDefault(SpeedParam),
            stats.GetValueOrDefault(ParamTenacity),
            item.IsUnique,
            item.IsAdvancedMeldingPermitted,
            stats.GetValueOrDefault(ParamPiety))
        {
            Caps = BuildCaps(item, itemLevel),
            RelicScale = item.EquipSlotCategory.IsValid ? ScaleFor(item.EquipSlotCategory.Value) : null,
        };
    }

    /// Fills every slot with the strongest piece the job can equip, by GearPiece.Score.
    public static Dictionary<string, uint> BestAvailable(bool wantsSpeed = false)
    {
        var selection = new Dictionary<string, uint>();

        foreach (var slot in Enum.GetValues<GearSlot>())
        {
            var options = For(slot);
            if (options.Count == 0)
                continue;

            var ordered = options.OrderByDescending(p => p.Score(wantsSpeed)).ToList();
            var best = ordered[0];

            if (slot == GearSlot.RingR
                && selection.TryGetValue(Label(GearSlot.RingL), out var leftRing)
                && best.ItemId == leftRing
                && best.IsUnique)
            {
                best = ordered.FirstOrDefault(p => p.ItemId != leftRing) ?? best;
            }

            selection[Label(slot)] = best.ItemId;
        }

        return selection;
    }

    /// Totals a set of chosen pieces into a stat line.
    public static StatPreset ToStatPreset(
        IReadOnlyDictionary<string, uint> selection,
        int jobMainStatModifier,
        string name,
        IReadOnlyDictionary<string, List<int>>? melds = null,
        RelicAllocation? relic = null,
        IReadOnlyDictionary<SubStat, int>? inferredRelicStats = null)
    {
        var level = LevelStats.Lv100;

        var preset = new StatPreset
        {
            Name = name,
            MainStat = (int)Math.Floor(level.BaseMainStat * jobMainStatModifier / 100.0),
            Crit = level.BaseSubStat,
            Determination = level.BaseMainStat,
            DirectHit = level.BaseSubStat,
            SkillSpeed = level.BaseSubStat,
            Tenacity = level.BaseSubStat,
            Piety = level.BaseMainStat,
            WeaponDamage = 0,
            WeaponDelay = 2.56,
        };

        var relicTotalSlot = selection
            .Where(kv => ById(kv.Value) is { IsCustomisableRelic: true })
            .OrderBy(kv => ById(kv.Value)!.RelicScale == RelicSlot.OffHand ? 1 : 0)
            .Select(kv => kv.Key)
            .FirstOrDefault();

        foreach (var (slotName, itemId) in selection)
        {
            var piece = ById(itemId);
            if (piece is null)
                continue;

            preset.MainStat += piece.MainStat;

            foreach (var stat in Enum.GetValues<SubStat>())
            {
                var onItem = piece.StatFor(stat);
                var meldTotal = MeldTotal(melds, slotName, stat);
                var cap = piece.Caps.TryGetValue(stat, out var c) ? c : 0;
                var effective = GearMath.EffectiveSubstat(onItem, meldTotal, cap);

                if (piece.IsCustomisableRelic)
                {
                    if (inferredRelicStats is not null && inferredRelicStats.TryGetValue(stat, out var inferred))
                    {
                        if (slotName == relicTotalSlot)
                            effective += inferred;
                    }
                    else if (relic is not null)
                    {
                        effective += relic.ValueFor(stat, piece.RelicScale ?? RelicSlot.TwoHand);
                    }
                }

                switch (stat)
                {
                    case SubStat.Crit: preset.Crit += effective; break;
                    case SubStat.Determination: preset.Determination += effective; break;
                    case SubStat.DirectHit: preset.DirectHit += effective; break;
                    case SubStat.Speed: preset.SkillSpeed += effective; break;
                    case SubStat.Piety: preset.Piety += effective; break;
                    default: preset.Tenacity += effective; break;
                }
            }

            if (piece.WeaponDamage > 0)
            {
                preset.WeaponDamage = piece.WeaponDamage;
                preset.WeaponDelay = piece.WeaponDelay;
            }
        }

        return preset;
    }

    /// Checks the gear model against reality.
    public static void LogCaps(
        IReadOnlyDictionary<string, uint> selection,
        IReadOnlyDictionary<string, List<int>> melds)
    {
        foreach (var (slotName, itemId) in selection)
        {
            var piece = ById(itemId);
            if (piece is null)
                continue;

            var parts = new List<string>();
            foreach (var stat in Enum.GetValues<SubStat>())
            {
                var baseStat = piece.StatFor(stat);
                var meld = MeldTotal(melds, slotName, stat);
                if (baseStat == 0 && meld == 0)
                    continue;

                var cap = piece.Caps.TryGetValue(stat, out var c) ? c : 0;
                var over = cap > 0 && baseStat + meld > cap ? "  <-- OVER CAP" : string.Empty;
                parts.Add($"{StatLabel(stat)} {baseStat}+{meld} cap {cap}{over}");
            }

            Plugin.Log.Information($"EchoSim cap check | {slotName,-10} i{piece.ItemLevel} {piece.Name}: {string.Join("  |  ", parts)}");
        }
    }

    /// The per-stat offset needed to make a catalogue-built set match the character sheet.
    public static Dictionary<string, int> ModelCorrection(
        IReadOnlyDictionary<string, uint> selection,
        IReadOnlyDictionary<string, List<int>> melds,
        StatPreset actual,
        int jobMainStatModifier,
        IReadOnlyDictionary<SubStat, int>? inferredRelicStats = null)
    {
        var modelled = ToStatPreset(selection, jobMainStatModifier, "correction", melds, null, inferredRelicStats);

        var correction = new Dictionary<string, int>
        {
            ["MainStat"] = actual.MainStat - modelled.MainStat,
        };

        foreach (var stat in Enum.GetValues<SubStat>())
        {
            var delta = actual.StatFor(stat) - modelled.StatFor(stat);
            if (delta != 0)
                correction[stat.ToString()] = delta;
        }

        return correction;
    }

    /// Applies a stored correction to a freshly built stat line.
    public static void ApplyCorrection(StatPreset preset, IReadOnlyDictionary<string, int> correction)
    {
        foreach (var (key, delta) in correction)
        {
            if (key == "MainStat")
            {
                preset.MainStat += delta;
                continue;
            }

            if (!SubStats.TryParse(key, out var stat))
                continue;

            switch (stat)
            {
                case SubStat.Crit: preset.Crit += delta; break;
                case SubStat.Determination: preset.Determination += delta; break;
                case SubStat.DirectHit: preset.DirectHit += delta; break;
                case SubStat.Speed: preset.SkillSpeed += delta; break;
                case SubStat.Piety: preset.Piety += delta; break;
                default: preset.Tenacity += delta; break;
            }
        }
    }

    /// Returns:Per-stat (modelled - actual).
    public static Dictionary<SubStat, int> ModelError(
        IReadOnlyDictionary<string, uint> selection,
        IReadOnlyDictionary<string, List<int>> melds,
        StatPreset actual,
        int jobMainStatModifier,
        IReadOnlyDictionary<SubStat, int>? inferredRelicStats = null)
    {
        var modelled = ToStatPreset(selection, jobMainStatModifier, "model check", melds, null, inferredRelicStats);
        var error = new Dictionary<SubStat, int>();

        foreach (var stat in Enum.GetValues<SubStat>())
        {
            var delta = modelled.StatFor(stat) - actual.StatFor(stat);
            if (delta != 0)
                error[stat] = delta;
        }

        return error;
    }

    /// Works out what a relic is contributing, by subtracting everything else from the character's real
    /// totals.
    public static Dictionary<SubStat, int> InferRelicSubstats(
        IReadOnlyDictionary<string, uint> selection,
        IReadOnlyDictionary<string, List<int>> melds,
        StatPreset actual)
    {
        var inferred = new Dictionary<SubStat, int>();
        var level = LevelStats.Lv100;

        foreach (var stat in Enum.GetValues<SubStat>())
        {
            var accounted = GearMath.BaseFor(stat, level);

            foreach (var (slotName, itemId) in selection)
            {
                var piece = ById(itemId);
                if (piece is null || piece.IsCustomisableRelic)
                    continue;

                var melded = piece.StatFor(stat) + MeldTotal(melds, slotName, stat);
                accounted += Math.Min(melded, piece.CapFor(stat));
            }

            var remainder = actual.StatFor(stat) - accounted;

            if (remainder >= 20)
                inferred[stat] = remainder;
        }

        return inferred;
    }

    /// Total value melded into one slot for one stat.
    public static int MeldTotal(IReadOnlyDictionary<string, List<int>>? melds, string slotName, SubStat stat)
    {
        if (melds is null || !melds.TryGetValue(slotName, out var encoded))
            return 0;

        var total = 0;
        foreach (var e in encoded)
        {
            if (MateriaCatalog.Decode(e) is { } option && option.Stat == stat)
                total += option.Value;
        }

        return total;
    }

    public static string Label(GearSlot slot) => slot switch
    {
        GearSlot.Weapon => "Weapon",
        GearSlot.OffHand => "Shield",
        GearSlot.Head => "Head",
        GearSlot.Body => "Body",
        GearSlot.Hands => "Hands",
        GearSlot.Legs => "Legs",
        GearSlot.Feet => "Feet",
        GearSlot.Ears => "Earrings",
        GearSlot.Neck => "Necklace",
        GearSlot.Wrists => "Bracelets",
        GearSlot.RingL => "Ring L",
        _ => "Ring R",
    };
}
