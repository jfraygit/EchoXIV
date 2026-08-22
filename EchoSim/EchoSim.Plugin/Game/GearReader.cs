using EchoSim.Sim;
using EchoSim.Sim.Engine;
using EchoSim.Sim.Jobs;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace EchoSim.Game;

/// Pulls the player's real equipped stats out of the client, so a simulation can be run against what you're
/// actually wearing rather than a typed-in approximation.
public static unsafe class GearReader
{
    private const int ParamStrength = 1;
    private const int ParamDexterity = 2;
    private const int ParamIntelligence = 4;
    private const int ParamMind = 5;
    private const int ParamTenacity = 19;
    private const int ParamDirectHit = 22;
    private const int ParamCriticalHit = 27;
    private const int ParamDetermination = 44;
    private const int ParamSkillSpeed = 45;
    private const int ParamSpellSpeed = 46;

    /// Reads current stats into a preset.
    public static bool TryRead(out StatPreset preset, out string error)
    {
        preset = Sim.Jobs.JobRegistry.ForOrDefault(GearCatalog.ActiveJob).ReferenceGear();
        error = string.Empty;

        var local = Plugin.ObjectTable.LocalPlayer;
        if (local is null)
        {
            error = "Not logged in.";
            return false;
        }

        var currentJob = local.ClassJob.RowId;
        if (currentJob != GearCatalog.ActiveJob)
        {
            var selected = Sim.Jobs.JobRegistry.For(GearCatalog.ActiveJob)?.Name ?? "the selected job";

            var actual = JobList.ById(currentJob)?.Name ?? "another job";

            error = $"Wrong job: you're on {actual}, EchoSim is set to {selected}.";
            return false;
        }

        var playerState = PlayerState.Instance();
        if (playerState is null)
        {
            error = "Player state unavailable.";
            return false;
        }

        var attributes = playerState->Attributes;

        preset = new StatPreset
        {
            Name = $"{local.Name} - live gear",
            MainStat = attributes[MainStatParam(GearCatalog.ActiveJob)],
            Crit = attributes[ParamCriticalHit],
            Determination = attributes[ParamDetermination],
            DirectHit = attributes[ParamDirectHit],
            SkillSpeed = attributes[SpeedParam(GearCatalog.ActiveJob)],
            Tenacity = attributes[ParamTenacity],
            WeaponDamage = 0,
            WeaponDelay = 2.56,
        };

        if (TryReadWeapon(out var weaponDamage, out var delay))
        {
            preset.WeaponDamage = weaponDamage;
            preset.WeaponDelay = delay;
        }
        else
        {
            error = "Read substats, but couldn't read the equipped weapon - check Weapon Damage by hand.";
            preset.WeaponDamage = Sim.Jobs.JobRegistry.ForOrDefault(GearCatalog.ActiveJob).ReferenceGear().WeaponDamage;
        }

        return true;
    }

    /// Which attribute is a job's main stat, asked of the job rather than kept as a list here.
    private static int MainStatParam(uint classJobId) => AttributeOf(classJobId) switch
    {
        MainAttribute.Dexterity => ParamDexterity,
        MainAttribute.Intelligence => ParamIntelligence,
        MainAttribute.Mind => ParamMind,
        _ => ParamStrength,
    };

    /// Which speed substat to read off the character sheet - the same question as the main stat and with the
    /// same failure mode.
    private static int SpeedParam(uint classJobId)
        => AttributeOf(classJobId).UsesSpellSpeed() ? ParamSpellSpeed : ParamSkillSpeed;

    private static MainAttribute AttributeOf(uint classJobId)
        => JobRegistry.ForOrDefault(classJobId).CreateSim().MainAttribute;

    /// Equipment slots in the order the client stores them, paired with the catalogue's slots.
    private static readonly (int Index, GearSlot Slot)[] SlotLayout =
    [
        (0, GearSlot.Weapon), (2, GearSlot.Head), (3, GearSlot.Body), (4, GearSlot.Hands),
        (6, GearSlot.Legs), (7, GearSlot.Feet), (8, GearSlot.Ears), (9, GearSlot.Neck),
        (10, GearSlot.Wrists), (11, GearSlot.RingL), (12, GearSlot.RingR),
    ];

    /// Item ids of everything currently equipped, keyed the same way the gear picker stores its selection so
    /// the two are interchangeable.
    public static Dictionary<SubStat, int> ReadRelicSubstats()
    {
        var result = new Dictionary<SubStat, int>();

        try
        {
            var inventory = InventoryManager.Instance();
            var equipped = inventory is null ? null : inventory->GetInventoryContainer(InventoryType.EquippedItems);
            if (equipped is null || equipped->Size == 0)
                return result;

            for (var index = 0; index < 2 && index < equipped->Size; index++)
            {
                var item = equipped->GetInventorySlot(index);
                if (item is null || item->ItemId == 0)
                    continue;

                if (GearCatalog.ById(item->ItemId) is not { IsCustomisableRelic: true })
                    continue;

                var count = item->GetMateriaCount();
                for (byte i = 0; i < count; i++)
                {
                    var materiaId = item->GetMateriaId(i);
                    if (materiaId == 0)
                        continue;

                    if (MateriaCatalog.FromEquipped(materiaId, item->GetMateriaGrade(i)) is not null)
                        continue;

                    if (MateriaCatalog.RelicSubstat(materiaId, item->GetMateriaGrade(i)) is { } substat)
                        result[substat.Stat] = result.GetValueOrDefault(substat.Stat) + substat.Value;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not read the relic's substats");
        }

        return result;
    }

    public static Dictionary<string, List<int>> ReadEquippedMelds()
    {
        var melds = new Dictionary<string, List<int>>();

        try
        {
            var inventory = InventoryManager.Instance();
            var equipped = inventory is null ? null : inventory->GetInventoryContainer(InventoryType.EquippedItems);
            if (equipped is null)
                return melds;

            foreach (var (index, slot) in SlotLayout)
            {
                if (index >= equipped->Size)
                    continue;

                var item = equipped->GetInventorySlot(index);
                if (item is null || item->ItemId == 0)
                    continue;

                var list = new List<int>();
                var count = item->GetMateriaCount();
                var unresolved = 0;

                var isRelic = GearCatalog.ById(item->ItemId) is { IsCustomisableRelic: true };

                for (byte i = 0; i < count; i++)
                {
                    var materiaId = item->GetMateriaId(i);
                    if (materiaId == 0)
                        continue;

                    var grade = item->GetMateriaGrade(i);
                    if (MateriaCatalog.FromEquipped(materiaId, grade) is { } option)
                    {
                        list.Add(option.Encoded);
                    }
                    else if (isRelic)
                    {
                        var substat = MateriaCatalog.RelicSubstat(materiaId, grade);
                        Plugin.Log.Information(
                            $"EchoSim: {GearCatalog.Label(slot)} relic substat slot {i} - " +
                            (substat is { } s
                                ? $"{s.Stat} +{s.Value}."
                                : $"id {materiaId}, grade {grade} could not be read. {MateriaCatalog.WhyUnresolved(materiaId, grade)}"));
                    }
                    else
                    {
                        unresolved++;
                        Plugin.Log.Warning(
                            $"EchoSim: {GearCatalog.Label(slot)} carries materia id {materiaId} grade {grade}, " +
                            $"which didn't resolve. {MateriaCatalog.WhyUnresolved(materiaId, grade)}");
                    }
                }

                Plugin.Log.Information(
                    $"EchoSim: {GearCatalog.Label(slot)} item {item->ItemId} - {count} materia slot(s) reported, " +
                    $"{list.Count} resolved, {unresolved} unresolved.");

                if (list.Count > 0)
                    melds[GearCatalog.Label(slot)] = list;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not read equipped materia");
        }

        return melds;
    }

    /// Containers worth searching for a copy of an item the player owns.
    private static readonly InventoryType[] SearchContainers =
    [
        InventoryType.EquippedItems,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody,
        InventoryType.ArmoryHands, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar, InventoryType.ArmoryNeck, InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
        InventoryType.Inventory1, InventoryType.Inventory2,
        InventoryType.Inventory3, InventoryType.Inventory4,
    ];

    /// The materia on the player's own copy of an item, wherever it happens to be.
    public static List<int>? FindMeldsForItem(uint itemId)
    {
        if (itemId == 0)
            return null;

        try
        {
            var inventory = InventoryManager.Instance();
            if (inventory is null)
                return null;

            foreach (var containerType in SearchContainers)
            {
                var container = inventory->GetInventoryContainer(containerType);
                if (container is null || !container->IsLoaded)
                    continue;

                for (var i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item is null || item->ItemId != itemId)
                        continue;

                    var melds = new List<int>();
                    var count = item->GetMateriaCount();
                    for (byte m = 0; m < count; m++)
                    {
                        var materiaId = item->GetMateriaId(m);
                        if (materiaId == 0)
                            continue;

                        if (MateriaCatalog.FromEquipped(materiaId, item->GetMateriaGrade(m)) is { } option)
                            melds.Add(option.Encoded);
                    }

                    return melds;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Could not search for item {itemId}");
        }

        return null;
    }

    public static Dictionary<string, uint> ReadEquippedSelection()
    {
        var selection = new Dictionary<string, uint>();

        try
        {
            var inventory = InventoryManager.Instance();
            var equipped = inventory is null ? null : inventory->GetInventoryContainer(InventoryType.EquippedItems);
            if (equipped is null)
                return selection;

            foreach (var (index, slot) in SlotLayout)
            {
                if (index >= equipped->Size)
                    continue;

                var item = equipped->GetInventorySlot(index);
                if (item is null || item->ItemId == 0)
                    continue;

                selection[GearCatalog.Label(slot)] = item->ItemId;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not read equipped gear");
        }

        return selection;
    }

    /// Physical damage and delay of the equipped main hand, from the Item sheet.
    private static bool TryReadWeapon(out int weaponDamage, out double delaySeconds)
    {
        weaponDamage = 0;
        delaySeconds = 2.56;

        var inventory = InventoryManager.Instance();
        if (inventory is null)
            return false;

        var equipped = inventory->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped is null || equipped->Size == 0)
            return false;

        var mainHand = equipped->GetInventorySlot(0);
        if (mainHand is null || mainHand->ItemId == 0)
            return false;

        var sheet = Plugin.DataManager.GetExcelSheet<Item>();
        if (!sheet.TryGetRow(mainHand->ItemId, out var item))
            return false;

        weaponDamage = item.DamagePhys;
        delaySeconds = item.Delayms / 1000.0;
        return weaponDamage > 0;
    }
}
