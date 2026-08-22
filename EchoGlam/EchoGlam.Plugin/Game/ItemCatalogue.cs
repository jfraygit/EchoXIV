using System;
using System.Collections.Generic;
using System.Linq;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;
using LuminaEquipRaceCategory = Lumina.Excel.Sheets.EquipRaceCategory;
using LuminaFittingShopItemSet = Lumina.Excel.Sheets.FittingShopItemSet;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace EchoGlam.Game;

/// How hard an item is to come by, as far as the game's own sheets will say.
[Flags]
public enum ItemAvailability : byte
{
    None = 0,

    /// Listed on the market board.
    Marketable = 1,

    /// Sold on the online store.
    Store = 2,

    /// A seasonal event item.
    Seasonal = 4,

    /// Promotional, collaboration or pre-order gear - the armoire's Exclusive Extras.
    Exclusive = 8,
}

/// One wearable thing, reduced to what a glamour needs.
public sealed record GlamItem(
    uint ItemId,
    string Name,
    ushort IconId,
    ushort ModelId,
    ushort ModelType,
    byte ModelVariant,

    /// The off-hand half of a paired weapon, from the item's own ModelSub.
    ushort SubModelId,
    ushort SubModelType,
    byte SubModelVariant,

    int ItemLevel,
    byte DyeChannels,
    ushort RaceCategory,
    ulong JobMask,

    /// Where this piece can be got, as far as the sheets know.
    ItemAvailability Availability = ItemAvailability.None)
{
    /// Whether this item is equippable by the given ClassJob row id.
    public bool UsableBy(uint jobId) => jobId < 64 && (JobMask & (1UL << (int)jobId)) != 0;

    /// The empty slot.
    public static readonly GlamItem None =
        new(0, "Nothing", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public bool IsNone => ItemId == 0;

    /// Whether this weapon carries its own off hand rather than leaving the slot to a separately equipped
    /// item.
    public bool HasPairedOffHand => SubModelId != 0;

    /// Whether this item can be dyed at all, and how many channels it takes.
    public bool IsDyeable => DyeChannels > 0;

    public bool IsMarketable => (Availability & ItemAvailability.Marketable) != 0;
    public bool IsStore => (Availability & ItemAvailability.Store) != 0;
    public bool IsSeasonal => (Availability & ItemAvailability.Seasonal) != 0;
    public bool IsExclusive => (Availability & ItemAvailability.Exclusive) != 0;
}

/// Every item in the game that can be worn, indexed by the slot it goes in.
public sealed class ItemCatalogue
{
    private readonly Dictionary<GlamSlot, List<GlamItem>> bySlot = [];
    private readonly Dictionary<uint, GlamItem> byId = [];

    /// Model back to item, so what the character is drawn wearing can be turned into a list of named pieces -
    /// see Wardrobe.CaptureWorn.
    private readonly Dictionary<(GlamSlot Slot, ushort Model, ushort Type, byte Variant), GlamItem> byModel = [];

    /// Which slots each item can fill, as a set.
    private readonly HashSet<(GlamSlot Slot, uint ItemId)> slotMembership = [];

    public bool Ready { get; private set; }

    /// How many items were indexed, for the diagnostics readout.
    public int Count { get; private set; }

    public IReadOnlyList<GlamItem> ForSlot(GlamSlot slot) =>
        bySlot.TryGetValue(slot, out var list) ? list : [];

    public GlamItem Resolve(uint itemId) =>
        itemId == 0 ? GlamItem.None : byId.TryGetValue(itemId, out var item) ? item : GlamItem.None;

    /// The item a drawn model corresponds to, or null when nothing in the game matches.
    public GlamItem? ResolveByModel(GlamSlot slot, ushort modelId, ushort modelType, byte variant) =>
        byModel.TryGetValue((slot, modelId, modelType, variant), out var item) ? item : null;

    /// Whether this item can go in this slot.
    public bool FitsSlot(GlamSlot slot, uint itemId) => slotMembership.Contains((slot, itemId));

    /// Builds the index.
    public void Build()
    {
        if (Ready)
            return;

        foreach (var slot in GlamSlots.All)
            bySlot[slot] = [];

        try
        {
            var store = StoreItems();
            var (seasonal, exclusive) = ArmoireItems();

            foreach (var item in Plugin.DataManager.GetExcelSheet<LuminaItem>())
            {
                if (!item.EquipSlotCategory.IsValid)
                    continue;

                var slots = GlamSlots.SlotsFor(item.EquipSlotCategory.Value);
                if (slots.Count == 0)
                    continue;

                var name = item.Name.ExtractText();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var availability = ItemAvailability.None;

                if (item.ItemSearchCategory.RowId != 0)
                    availability |= ItemAvailability.Marketable;

                if (store.Contains(item.RowId))
                    availability |= ItemAvailability.Store;

                if (seasonal.Contains(item.RowId))
                    availability |= ItemAvailability.Seasonal;

                if (exclusive.Contains(item.RowId))
                    availability |= ItemAvailability.Exclusive;

                var glam = Extract(item, name, JobMaskFor(item), availability);

                if (glam.ModelId == 0)
                    continue;

                byId[glam.ItemId] = glam;

                foreach (var slot in slots)
                {
                    bySlot[slot].Add(glam);
                    slotMembership.Add((slot, glam.ItemId));
                }
            }

            foreach (var slot in bySlot.Keys.ToList())
            {
                bySlot[slot] = [.. bySlot[slot].OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)];
            }

            foreach (var (slot, items) in bySlot)
            {
                foreach (var item in items)
                    byModel.TryAdd((slot, item.ModelId, item.ModelType, item.ModelVariant), item);
            }

            Count = byId.Count;
            Ready = true;

            Plugin.Log.Information($"[EchoGlam] Item catalogue built: {Count} wearable items.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not build the item catalogue");
        }
    }

    /// Pulls the model triple out of the packed ModelMain value.
    private readonly Dictionary<uint, ulong> jobMaskCache = [];

    private ulong JobMaskFor(LuminaItem item)
    {
        if (!item.ClassJobCategory.IsValid)
            return 0;

        var id = item.ClassJobCategory.RowId;
        if (jobMaskCache.TryGetValue(id, out var cached))
            return cached;

        var mask = BuildJobMask(item.ClassJobCategory.Value);
        jobMaskCache[id] = mask;
        return mask;
    }

    /// Written out column by column rather than reflected over the sheet.
    private static ulong BuildJobMask(Lumina.Excel.Sheets.ClassJobCategory c)
    {
        ulong mask = 0;

        void Set(bool allowed, int jobId)
        {
            if (allowed)
                mask |= 1UL << jobId;
        }

        Set(c.GLA, 1); Set(c.PGL, 2); Set(c.MRD, 3); Set(c.LNC, 4);
        Set(c.ARC, 5); Set(c.CNJ, 6); Set(c.THM, 7);

        Set(c.CRP, 8); Set(c.BSM, 9); Set(c.ARM, 10); Set(c.GSM, 11);
        Set(c.LTW, 12); Set(c.WVR, 13); Set(c.ALC, 14); Set(c.CUL, 15);

        Set(c.MIN, 16); Set(c.BTN, 17); Set(c.FSH, 18);

        Set(c.PLD, 19); Set(c.MNK, 20); Set(c.WAR, 21); Set(c.DRG, 22);
        Set(c.BRD, 23); Set(c.WHM, 24); Set(c.BLM, 25); Set(c.ACN, 26);
        Set(c.SMN, 27); Set(c.SCH, 28); Set(c.ROG, 29); Set(c.NIN, 30);
        Set(c.MCH, 31); Set(c.DRK, 32); Set(c.AST, 33); Set(c.SAM, 34);
        Set(c.RDM, 35); Set(c.BLU, 36); Set(c.GNB, 37); Set(c.DNC, 38);
        Set(c.RPR, 39); Set(c.SGE, 40); Set(c.VPR, 41); Set(c.PCT, 42);

        return mask;
    }

    /// Every item the online store's fitting room can dress you in.
    private static HashSet<uint> StoreItems()
    {
        var ids = new HashSet<uint>();

        foreach (var set in Plugin.DataManager.GetExcelSheet<LuminaFittingShopItemSet>())
        {
            foreach (var reference in set.Item)
            {
                if (reference.RowId != 0)
                    ids.Add(reference.RowId);
            }
        }

        return ids;
    }

    /// Seasonal and exclusive gear, from the armoire's own filing.
    private static (HashSet<uint> Seasonal, HashSet<uint> Exclusive) ArmoireItems()
    {
        HashSet<uint> seasonalCategories = [2, 5, 6, 7, 8];
        const uint exclusiveCategory = 4;

        var seasonal = new HashSet<uint>();
        var exclusive = new HashSet<uint>();

        foreach (var row in Plugin.DataManager.GetExcelSheet<LuminaCabinet>())
        {
            if (row.Item.RowId == 0)
                continue;

            if (seasonalCategories.Contains(row.Category.RowId))
                seasonal.Add(row.Item.RowId);
            else if (row.Category.RowId == exclusiveCategory)
                exclusive.Add(row.Item.RowId);
        }

        return (seasonal, exclusive);
    }

    private static GlamItem Extract(LuminaItem item, string name, ulong jobMask, ItemAvailability availability)
    {
        var main = item.ModelMain;
        var isWeapon = item.EquipSlotCategory.Value.MainHand == 1 || item.EquipSlotCategory.Value.OffHand == 1;

        var modelId = (ushort)(main & 0xFFFF);
        var modelType = isWeapon ? (ushort)((main >> 16) & 0xFFFF) : (ushort)0;
        var variant = isWeapon ? (byte)((main >> 32) & 0xFF) : (byte)((main >> 16) & 0xFF);

        var sub = isWeapon ? item.ModelSub : 0;

        var subModelId = (ushort)(sub & 0xFFFF);
        var subModelType = (ushort)((sub >> 16) & 0xFFFF);
        var subVariant = (byte)((sub >> 32) & 0xFF);

        return new GlamItem(
            item.RowId,
            name,
            item.Icon,
            modelId,
            modelType,
            variant,
            subModelId,
            subModelType,
            subVariant,
            (int)item.LevelItem.RowId,
            item.DyeCount,
            (ushort)item.EquipRestriction.RowId,
            jobMask,
            availability);
    }

    /// Every race category a character of this race and sex may wear.
    public static HashSet<ushort> AllowedRaceCategories(byte raceId, byte sex)
    {
        var allowed = new HashSet<ushort>();

        try
        {
            foreach (var row in Plugin.DataManager.GetExcelSheet<LuminaEquipRaceCategory>())
            {
                if (row.RowId is 0 or > ushort.MaxValue)
                    continue;

                if (Permits(row, raceId, sex))
                    allowed.Add((ushort)row.RowId);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not read equip race categories");
        }

        return allowed;
    }

    /// Whether one race category admits this race and sex.
    private static bool Permits(LuminaEquipRaceCategory row, byte raceId, byte sex)
    {
        var raceAllowed = raceId switch
        {
            1 => row.Hyur,
            2 => row.Elezen,
            3 => row.Lalafell,
            4 => row.Miqote,
            5 => row.Roegadyn,
            6 => row.AuRa,
            7 => row.Hrothgar,
            8 => row.Viera,

            _ => true,
        };

        if (!raceAllowed)
            return false;

        return sex == 0 ? row.Male : row.Female;
    }
}
