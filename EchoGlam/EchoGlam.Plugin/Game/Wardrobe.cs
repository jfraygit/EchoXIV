using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace EchoGlam.Game;

/// One slot's worth of a look: what to wear and how it is dyed.
public readonly record struct GlamEntry(uint ItemId, byte Stain0, byte Stain1);

/// What EchoGlam is actually for: putting a look on your character and keeping it there.
public sealed unsafe class Wardrobe
{
    private readonly ItemCatalogue catalogue;

    /// The look being worn.
    private readonly Dictionary<GlamSlot, GlamEntry> desired = [];

    /// What the slot held before this plugin first touched it, so revert has somewhere to go.
    private readonly Dictionary<GlamSlot, EquipmentModelId> baseline = [];
    private readonly Dictionary<GlamSlot, WeaponModelId> weaponBaseline = [];

    /// The override a slot held immediately before it was hidden, so hiding can be undone.
    private readonly Dictionary<GlamSlot, GlamEntry> beforeHiding = [];

    private CustomizeSet? desiredAppearance;
    private CustomizeSet? appearanceBaseline;

    private long lastRedrawTick;

    /// A redraw is visible - the character blinks out and back - so one is never issued twice in quick
    /// succession.
    private const long RedrawCooldownMs = 500;

    public Wardrobe(ItemCatalogue catalogue) => this.catalogue = catalogue;

    /// Whether anything is currently being overridden.
    public bool Active => desired.Count > 0 || desiredAppearance != null;

    public bool HasAppearanceOverride => desiredAppearance != null;

    /// The appearance override in force, or null when the character is wearing its own face.
    public CustomizeSet? CurrentAppearance => desiredAppearance?.Clone();

    /// The character's OWN appearance, underneath whatever override is in force - what it would go back to if
    /// everything here were dropped.
    public CustomizeSet? BaseAppearance => appearanceBaseline?.Clone();

    public IReadOnlyDictionary<GlamSlot, GlamEntry> Current => desired;

    public GlamEntry? EntryFor(GlamSlot slot) => desired.TryGetValue(slot, out var e) ? e : null;

    /// Overrides a slot.
    public void Set(GlamSlot slot, uint itemId, byte stain0 = 0, byte stain1 = 0)
    {
        if (itemId == 0)
        {
            if (desired.TryGetValue(slot, out var previous) && previous.ItemId != 0)
                beforeHiding[slot] = previous;
        }
        else
        {
            beforeHiding.Remove(slot);
        }

        desired[slot] = new GlamEntry(itemId, stain0, stain1);
        Apply();
    }

    /// Puts back whatever a hidden slot was showing, and says whether it had anything to put back.
    public bool Unhide(GlamSlot slot)
    {
        if (EntryFor(slot) is not { ItemId: 0 })
            return false;

        if (!beforeHiding.TryGetValue(slot, out var previous))
            return false;

        Set(slot, previous.ItemId, previous.Stain0, previous.Stain1);
        return true;
    }

    /// What a hidden slot would show again, or null when there is nothing remembered.
    public GlamEntry? HiddenBefore(GlamSlot slot) =>
        EntryFor(slot) is { ItemId: 0 } && beforeHiding.TryGetValue(slot, out var previous)
            ? previous
            : null;

    public void SetDye(GlamSlot slot, byte stain0, byte stain1)
    {
        if (!desired.TryGetValue(slot, out var entry))
            return;

        desired[slot] = entry with { Stain0 = stain0, Stain1 = stain1 };
        Apply();
    }

    /// Hands one slot back to the game and restores what it had.
    public void Clear(GlamSlot slot)
    {
        beforeHiding.Remove(slot);

        if (!desired.Remove(slot))
            return;

        RestoreSlot(slot);
    }

    public void SetAppearance(CustomizeSet set)
    {
        if (!Appearance.LayoutTrusted)
            return;

        desiredAppearance = set.Clone();
        Apply();
    }

    public void ClearAppearance()
    {
        if (desiredAppearance == null)
            return;

        desiredAppearance = null;

        var character = LocalCharacter();
        if (character != null && appearanceBaseline != null)
        {
            Appearance.Write(character, appearanceBaseline);
            Redraw(character);
        }
    }

    /// Takes everything off and puts the character back as the game has it.
    public void RevertAll()
    {
        foreach (var slot in new List<GlamSlot>(desired.Keys))
            RestoreSlot(slot);

        if (offHandFollow is not null)
        {
            RestoreSlot(GlamSlot.OffHand);
            offHandFollow = null;
        }

        desired.Clear();
        ClearAppearance();
    }

    /// Replaces the whole look in one go, which is what wearing somebody else's glamour is.
    public void Wear(IReadOnlyDictionary<GlamSlot, GlamEntry> look)
    {
        foreach (var slot in new List<GlamSlot>(desired.Keys))
        {
            if (!look.ContainsKey(slot))
                RestoreSlot(slot);
        }

        desired.Clear();

        foreach (var (slot, entry) in look)
            desired[slot] = entry;

        Apply();
    }

    /// What the character is drawn wearing right now, as a look.
    public Dictionary<GlamSlot, GlamEntry> CaptureWorn()
    {
        var look = new Dictionary<GlamSlot, GlamEntry>();

        var character = LocalCharacter();
        if (character == null)
            return look;

        var draw = &character->DrawData;

        foreach (var slot in GlamSlots.Armour)
        {
            var model = draw->EquipmentModelIds[(int)slot.ToEquipmentSlot()];
            if (model.Id == 0)
                continue;

            var equipped = EquippedMatching(slot, model.Id, 0, model.Variant);
            var item = equipped ?? catalogue.ResolveByModel(slot, model.Id, 0, model.Variant);

            if (Build.Diagnostics)
            {
                Plugin.Log.Information(
                    $"[EchoGlam] Capture {slot}: model {model.Id}/{model.Variant} -> "
                    + $"equipped={(equipped is { } e ? $"{e.Name} ({e.ItemId})" : "none")}, "
                    + $"used={(item is { } u ? u.Name : "nothing")}");
            }

            if (item is { } found)
                look[slot] = new GlamEntry(found.ItemId, model.Stain0, model.Stain1);
        }

        foreach (var slot in new[] { GlamSlot.MainHand, GlamSlot.OffHand })
        {
            var model = draw->Weapon(slot.ToWeaponSlot()).ModelId;
            if (model.Id == 0)
                continue;

            var item = EquippedMatching(slot, model.Id, model.Type, (byte)model.Variant)
                       ?? catalogue.ResolveByModel(slot, model.Id, model.Type, (byte)model.Variant);

            if (item is { } found)
                look[slot] = new GlamEntry(found.ItemId, model.Stain0, 0);
        }

        return look;
    }

    /// The equipped piece that is the one being drawn in this slot, or null.
    private GlamItem? EquippedMatching(GlamSlot slot, ushort modelId, ushort modelType, byte variant)
    {
        foreach (var equipped in Plugin.GameInventory.GetInventoryItems(
                     Dalamud.Game.Inventory.GameInventoryType.EquippedItems))
        {
            if (equipped.ItemId == 0)
                continue;

            var itemId = (equipped.GlamourId != 0 ? equipped.GlamourId : equipped.ItemId) % 1_000_000;

            var item = catalogue.Resolve(itemId);

            if (item.IsNone || !catalogue.FitsSlot(slot, item.ItemId))
                continue;

            if (item.ModelId == modelId && item.ModelType == modelType && item.ModelVariant == variant)
                return item;
        }

        return null;
    }

    /// Called every framework update.
    public void Tick()
    {
        var character = LocalCharacter();
        if (character == null)
            return;

        Appearance.VerifyLayout(character);
        DropUnusableWeapons();
        FollowMainHandWithOffHand();
        CaptureBaselines(character);

        if (!Active)
            return;

        ApplyTo(character);
    }

    /// The ClassJob row id the player is currently on, or zero when there is no player.
    private static uint CurrentJob() => Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;

    /// Whether this job can equip this weapon.
    public static bool JobCanUse(GlamItem item, uint job) => job == 0 || item.IsNone || item.UsableBy(job);

    /// Gives back any weapon slot the current job cannot equip.
    private void DropUnusableWeapons()
    {
        if (!catalogue.Ready)
            return;

        var job = CurrentJob();
        if (job == 0)
            return;

        foreach (var slot in new[] { GlamSlot.MainHand, GlamSlot.OffHand })
        {
            if (!desired.TryGetValue(slot, out var entry))
                continue;

            if (JobCanUse(catalogue.Resolve(entry.ItemId), job))
                continue;

            desired.Remove(slot);
            weaponBaseline.Remove(slot);
        }
    }

    /// What the off hand should be drawn as because of the main hand, or null when the off hand is nobody's
    /// business but its own.
    private WeaponModelId? OffHandFollow()
    {
        if (desired.ContainsKey(GlamSlot.OffHand))
            return null;

        if (!desired.TryGetValue(GlamSlot.MainHand, out var main))
            return null;

        var item = catalogue.Resolve(main.ItemId);

        if (item.IsNone)
            return new WeaponModelId();

        if (!item.HasPairedOffHand)
            return null;

        return new WeaponModelId
        {
            Id = item.SubModelId,
            Type = item.SubModelType,
            Variant = item.SubModelVariant,

            Stain0 = main.Stain0,
        };
    }

    /// What the off hand is currently being driven to on the main hand's behalf, or null while it is not.
    private WeaponModelId? offHandFollow;

    private void FollowMainHandWithOffHand()
    {
        var follow = OffHandFollow();

        if (offHandFollow is not null && follow is null)
            RestoreSlot(GlamSlot.OffHand);

        offHandFollow = follow;
    }

    private void Apply()
    {
        DropUnusableWeapons();

        FollowMainHandWithOffHand();

        var character = LocalCharacter();
        if (character != null)
            ApplyTo(character);
    }

    /// Keeps the "what the game thinks you are wearing" record current for every slot the wardrobe is not
    /// covering.
    private void CaptureBaselines(Character* character)
    {
        var draw = &character->DrawData;

        foreach (var slot in GlamSlots.Armour)
        {
            if (desired.ContainsKey(slot))
                continue;

            baseline[slot] = draw->EquipmentModelIds[(int)slot.ToEquipmentSlot()];
        }

        foreach (var slot in new[] { GlamSlot.MainHand, GlamSlot.OffHand })
        {
            if (desired.ContainsKey(slot))
                continue;

            if (slot == GlamSlot.OffHand && offHandFollow is not null)
                continue;

            weaponBaseline[slot] = draw->Weapon(slot.ToWeaponSlot()).ModelId;
        }

        if (desiredAppearance == null)
            appearanceBaseline = Appearance.Read(character);
    }

    private void ApplyTo(Character* character)
    {
        var draw = &character->DrawData;

        foreach (var (slot, entry) in desired)
        {
            var item = catalogue.Resolve(entry.ItemId);

            if (slot.IsWeapon())
                ApplyWeapon(draw, slot, item, entry);
            else
                ApplyArmour(draw, slot, item, entry);
        }

        if (offHandFollow is { } off)
            ApplyWeaponModel(draw, GlamSlot.OffHand, off);

        ApplyAppearance(character);
    }

    private void ApplyArmour(DrawDataContainer* draw, GlamSlot slot, GlamItem item, GlamEntry entry)
    {
        var target = slot.ToEquipmentSlot();
        var current = draw->EquipmentModelIds[(int)target];

        var wanted = new EquipmentModelId
        {
            Id = item.ModelId,
            Variant = item.ModelVariant,
            Stain0 = entry.Stain0,
            Stain1 = entry.Stain1,
        };

        if (current.Id == wanted.Id && current.Variant == wanted.Variant
            && current.Stain0 == wanted.Stain0 && current.Stain1 == wanted.Stain1)
            return;

        draw->LoadEquipment(target, &wanted, true);
    }

    private static void ApplyWeapon(DrawDataContainer* draw, GlamSlot slot, GlamItem item, GlamEntry entry)
        => ApplyWeaponModel(draw, slot, new WeaponModelId
        {
            Id = item.ModelId,
            Type = item.ModelType,
            Variant = item.ModelVariant,
            Stain0 = entry.Stain0,
        });

    /// Writes a weapon model straight to a hand.
    private static void ApplyWeaponModel(DrawDataContainer* draw, GlamSlot slot, WeaponModelId wanted)
    {
        var target = slot.ToWeaponSlot();
        var current = draw->Weapon(target).ModelId;

        if (current.Id == wanted.Id && current.Type == wanted.Type
            && current.Variant == wanted.Variant && current.Stain0 == wanted.Stain0)
            return;

        draw->LoadWeapon(target, wanted, 0, 0, 0, 0, false);
    }

    private void ApplyAppearance(Character* character)
    {
        if (desiredAppearance == null || !Appearance.LayoutTrusted)
            return;

        var drawn = Appearance.Read(character);
        if (drawn.SameAs(desiredAppearance))
            return;

        var now = Environment.TickCount64;
        if (now - lastRedrawTick < RedrawCooldownMs)
            return;

        lastRedrawTick = now;

        Appearance.Write(character, desiredAppearance);
        Redraw(character);
    }

    private void RestoreSlot(GlamSlot slot)
    {
        var character = LocalCharacter();
        if (character == null)
            return;

        var draw = &character->DrawData;

        if (slot.IsWeapon())
        {
            if (weaponBaseline.TryGetValue(slot, out var weapon))
                draw->LoadWeapon(slot.ToWeaponSlot(), weapon, 0, 0, 0, 0, false);

            return;
        }

        if (!baseline.TryGetValue(slot, out var model))
            return;

        draw->LoadEquipment(slot.ToEquipmentSlot(), &model, true);
    }

    /// Rebuilds the character's model.
    private static void Redraw(Character* character)
    {
        character->GameObject.DisableDraw();
        character->GameObject.EnableDraw();
    }

    /// The local player, or null if there isn't one - which is most of a loading screen, and the reason every
    /// caller above checks.
    private static Character* LocalCharacter()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        return player == null ? null : (Character*)player.Address;
    }
}
