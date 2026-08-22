using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Newtonsoft.Json;

namespace EchoGlam.Game;

/// One place gear can be, and what was last seen in it.
public sealed class OwnedSourceRecord
{
    /// What to call this in the coverage tooltip.
    public string Label { get; set; } = string.Empty;

    public DateTime SeenUtc { get; set; }

    public List<uint> ItemIds { get; set; } = [];
}

/// Everything one character can get at, per source.
public sealed class OwnedRecord
{
    public Dictionary<string, OwnedSourceRecord> Sources { get; set; } = [];
}

/// What gear this character actually has, across every container the game will show a plugin.
public sealed unsafe class OwnedItems
{
    /// How often the always-available containers are re-read.
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);

    /// HQ items carry this offset in some containers and not others.
    private const uint HighQualityOffset = 1_000_000;

    private static readonly GameInventoryType[] AlwaysReadable =
    [
        GameInventoryType.Inventory1, GameInventoryType.Inventory2,
        GameInventoryType.Inventory3, GameInventoryType.Inventory4,
        GameInventoryType.ArmoryMainHand, GameInventoryType.ArmoryOffHand,
        GameInventoryType.ArmoryHead, GameInventoryType.ArmoryBody, GameInventoryType.ArmoryHands,
        GameInventoryType.ArmoryWaist, GameInventoryType.ArmoryLegs, GameInventoryType.ArmoryFeets,
        GameInventoryType.ArmoryEar, GameInventoryType.ArmoryNeck, GameInventoryType.ArmoryWrist,
        GameInventoryType.ArmoryRings,
        GameInventoryType.EquippedItems,
    ];

    private static readonly GameInventoryType[] Saddlebag =
    [
        GameInventoryType.SaddleBag1, GameInventoryType.SaddleBag2,
        GameInventoryType.PremiumSaddleBag1, GameInventoryType.PremiumSaddleBag2,
    ];

    private static readonly GameInventoryType[] RetainerPages =
    [
        GameInventoryType.RetainerPage1, GameInventoryType.RetainerPage2,
        GameInventoryType.RetainerPage3, GameInventoryType.RetainerPage4,
        GameInventoryType.RetainerPage5, GameInventoryType.RetainerPage6,
        GameInventoryType.RetainerPage7, GameInventoryType.RetainerEquippedItems,
    ];

    public const string CarriedKey = "carried";
    public const string SaddlebagKey = "saddlebag";
    public const string ArmoireKey = "armoire";
    public const string DresserKey = "dresser";

    private readonly string path;

    /// Keyed by "Name@World" rather than by content id, because this Dalamud's IClientState exposes no
    /// content id - the local player hangs off the object table instead.
    private readonly Dictionary<string, OwnedRecord> byCharacter = [];

    /// The flattened set for the character currently logged in, rebuilt whenever a source changes.
    private HashSet<uint> flattened = [];

    private string currentCharacter = string.Empty;
    private DateTime lastScan = DateTime.MinValue;
    private bool dirty;

    public OwnedItems(string configDirectory)
    {
        path = Path.Combine(configDirectory, "owned.json");
        Load();
    }

    /// How many distinct items this character is known to have.
    public int Count => flattened.Count;

    /// Whether the catalogue's copy of this item is one the player can get at.
    public bool Contains(uint itemId) => flattened.Contains(Normalise(itemId));

    /// The sources on record for the current character, newest reading first.
    public IReadOnlyList<OwnedSourceRecord> Sources =>
        Current()?.Sources.Values.OrderByDescending(s => s.SeenUtc).ToList() ?? [];

    /// Whether anything at all has been recorded.
    public bool Ready => flattened.Count > 0;

    /// Reads whatever the game will currently show.
    public void Tick()
    {
        var player = CharacterKey();
        if (player.Length == 0)
            return;

        if (player != currentCharacter)
        {
            currentCharacter = player;
            Rebuild();
        }

        var now = DateTime.UtcNow;
        if (now - lastScan < ScanInterval)
            return;

        lastScan = now;

        var changed = ScanCarried();
        changed |= ScanSaddlebag();
        changed |= ScanArmoire();
        changed |= ScanDresser();
        changed |= ScanRetainer();

        if (!changed)
        {
            FlushIfDirty();
            return;
        }

        Rebuild();
        dirty = true;
        FlushIfDirty();
    }

    /// Inventory, every armoury chest and what is on your back.
    private bool ScanCarried() => Record(CarriedKey, "Inventory, armoury and equipped", Read(AlwaysReadable), replaceWhenEmpty: true);

    /// Only populated once the saddlebag has been opened this session, and an unopened one is
    /// indistinguishable from an empty one - so this never clears what it already knows.
    private bool ScanSaddlebag() => Record(SaddlebagKey, "Chocobo saddlebag", Read(Saddlebag), replaceWhenEmpty: false);

    /// The armoire, which is not an inventory at all - the game holds a bitmask over the Cabinet sheet, so
    /// this walks that sheet and asks about each row.
    private bool ScanArmoire()
    {
        var ui = UIState.Instance();
        if (ui == null || !ui->Cabinet.IsCabinetLoaded())
            return false;

        var ids = new HashSet<uint>();

        foreach (var row in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Cabinet>())
        {
            var item = row.Item.RowId;
            if (item != 0 && ui->Cabinet.IsItemInCabinet(row.RowId))
                ids.Add(item);
        }

        return Record(ArmoireKey, "Armoire", ids, replaceWhenEmpty: true);
    }

    /// The glamour dresser.
    private bool ScanDresser()
    {
        var mirage = MirageManager.Instance();
        if (mirage == null || !mirage->PrismBoxLoaded)
            return false;

        var ids = new HashSet<uint>();

        foreach (var id in mirage->PrismBoxItemIds)
        {
            if (id != 0)
                ids.Add(Normalise(id));
        }

        return Record(DresserKey, "Glamour dresser", ids, replaceWhenEmpty: true);
    }

    /// Whichever retainer is open, recorded under its own id.
    private bool ScanRetainer()
    {
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
            return false;

        var retainer = manager->GetActiveRetainer();
        if (retainer == null || retainer->RetainerId == 0)
            return false;

        var name = retainer->NameString;
        var label = string.IsNullOrWhiteSpace(name) ? "Retainer" : $"Retainer: {name}";

        return Record($"retainer:{retainer->RetainerId}", label, Read(RetainerPages), replaceWhenEmpty: false);
    }

    private static HashSet<uint> Read(GameInventoryType[] containers)
    {
        var ids = new HashSet<uint>();

        foreach (var container in containers)
        {
            var items = Plugin.GameInventory.GetInventoryItems(container);

            foreach (var item in items)
            {
                if (item.ItemId != 0)
                    ids.Add(Normalise(item.ItemId));
            }
        }

        return ids;
    }

    /// Writes one source, and says whether anything actually changed.
    private bool Record(string key, string label, HashSet<uint> ids, bool replaceWhenEmpty)
    {
        if (ids.Count == 0 && !replaceWhenEmpty)
            return false;

        var record = Current();
        if (record == null)
            return false;

        if (record.Sources.TryGetValue(key, out var existing)
            && existing.ItemIds.Count == ids.Count
            && ids.SetEquals(existing.ItemIds))
        {
            existing.SeenUtc = DateTime.UtcNow;
            existing.Label = label;
            dirty = true;
            return false;
        }

        record.Sources[key] = new OwnedSourceRecord
        {
            Label = label,
            SeenUtc = DateTime.UtcNow,
            ItemIds = [.. ids],
        };

        return true;
    }

    /// "Name@World", or empty when nobody is logged in.
    private static string CharacterKey()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return string.Empty;

        var name = player.Name.TextValue;
        var world = player.HomeWorld.Value.Name.ExtractText();

        return name.Length == 0 ? string.Empty : $"{name}@{world}";
    }

    private OwnedRecord? Current()
    {
        if (currentCharacter.Length == 0)
            return null;

        if (!byCharacter.TryGetValue(currentCharacter, out var record))
        {
            record = new OwnedRecord();
            byCharacter[currentCharacter] = record;
        }

        return record;
    }

    private void Rebuild()
    {
        var record = Current();
        var set = new HashSet<uint>();

        if (record != null)
        {
            foreach (var source in record.Sources.Values)
                set.UnionWith(source.ItemIds);
        }

        flattened = set;
    }

    /// Forgets everything known about this character, so the next scans start clean.
    public void Forget()
    {
        if (currentCharacter.Length == 0)
            return;

        byCharacter.Remove(currentCharacter);
        flattened = [];
        dirty = true;
        FlushIfDirty();
    }

    private void FlushIfDirty()
    {
        if (!dirty)
            return;

        dirty = false;

        try
        {
            var json = JsonConvert.SerializeObject(byCharacter, Formatting.Indented);

            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, path, true);
            File.Delete(temp);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not save the owned-item list");
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path))
                return;

            var loaded = JsonConvert.DeserializeObject<Dictionary<string, OwnedRecord>>(File.ReadAllText(path));
            if (loaded == null)
                return;

            foreach (var (key, value) in loaded)
                byCharacter[key] = value;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not read the owned-item list; starting empty");
        }
    }

    private static uint Normalise(uint itemId) => itemId > HighQualityOffset ? itemId - HighQualityOffset : itemId;
}
