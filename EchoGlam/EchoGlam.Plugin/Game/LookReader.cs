using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using EchoGlam.Shared;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace EchoGlam.Game;

/// Reading a look off a character, and writing one onto it.
public static unsafe class LookReader
{
    /// Reads what a character is currently drawn wearing.
    public static SharedLook Read(Character* character, string characterKey, bool includeAppearance)
    {
        var draw = &character->DrawData;
        var look = new SharedLook { Character = characterKey };

        foreach (var slot in GlamSlots.Armour)
        {
            var model = draw->EquipmentModelIds[(int)slot.ToEquipmentSlot()];

            look.Slots.Add(new LookSlotDto
            {
                Slot = (int)slot,
                ModelId = model.Id,
                Variant = model.Variant,
                Stain0 = model.Stain0,
                Stain1 = model.Stain1,
            });
        }

        foreach (var slot in Weapons)
        {
            var model = draw->Weapon(slot.ToWeaponSlot()).ModelId;

            look.Weapons.Add(new LookWeaponDto
            {
                Slot = (int)slot,
                ModelId = model.Id,
                Type = model.Type,
                Variant = model.Variant,
                Stain0 = model.Stain0,
            });
        }

        if (includeAppearance && Appearance.LayoutTrusted)
            look.Customise = Appearance.Read(character).ToBase64();

        look.Stamp = StampOf(look);
        return look;
    }

    /// Writes a look onto a character.
    public static bool Apply(Character* character, SharedLook look, bool withAppearance)
    {
        var draw = &character->DrawData;

        foreach (var entry in look.Slots)
        {
            if (!IsArmourSlot(entry.Slot))
                continue;

            var target = ((GlamSlot)entry.Slot).ToEquipmentSlot();
            var current = draw->EquipmentModelIds[(int)target];

            var wanted = new EquipmentModelId
            {
                Id = entry.ModelId,
                Variant = entry.Variant,
                Stain0 = entry.Stain0,
                Stain1 = entry.Stain1,
            };

            if (current.Id == wanted.Id && current.Variant == wanted.Variant
                && current.Stain0 == wanted.Stain0 && current.Stain1 == wanted.Stain1)
            {
                continue;
            }

            draw->LoadEquipment(target, &wanted, true);
        }

        foreach (var entry in look.Weapons)
        {
            if (!IsWeaponSlot(entry.Slot))
                continue;

            var target = ((GlamSlot)entry.Slot).ToWeaponSlot();
            var current = draw->Weapon(target).ModelId;

            var wanted = new WeaponModelId
            {
                Id = entry.ModelId,
                Type = entry.Type,
                Variant = entry.Variant,
                Stain0 = entry.Stain0,
            };

            if (current.Id == wanted.Id && current.Type == wanted.Type
                && current.Variant == wanted.Variant && current.Stain0 == wanted.Stain0)
            {
                continue;
            }

            draw->LoadWeapon(target, wanted, 0, 0, 0, 0, false);
        }

        if (!withAppearance || look.Customise.Length == 0 || !Appearance.LayoutTrusted)
            return false;

        if (CustomizeSet.FromBase64(look.Customise) is not { } wantedAppearance)
            return false;

        if (Appearance.Read(character).SameAs(wantedAppearance))
            return false;

        Appearance.Write(character, wantedAppearance);
        return true;
    }

    /// Rebuilds a character's model.
    public static void Redraw(Character* character)
    {
        character->GameObject.DisableDraw();
        character->GameObject.EnableDraw();
    }

    /// A short hash of everything a look carries.
    public static string StampOf(SharedLook look)
    {
        var text = new StringBuilder();

        foreach (var slot in look.Slots)
            text.Append(slot.Slot).Append(':').Append(slot.ModelId).Append('.')
                .Append(slot.Variant).Append('.').Append(slot.Stain0).Append('.')
                .Append(slot.Stain1).Append('|');

        foreach (var weapon in look.Weapons)
            text.Append(weapon.Slot).Append(':').Append(weapon.ModelId).Append('.')
                .Append(weapon.Type).Append('.').Append(weapon.Variant).Append('.')
                .Append(weapon.Stain0).Append('|');

        text.Append(look.Customise);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static readonly GlamSlot[] Weapons = [GlamSlot.MainHand, GlamSlot.OffHand];

    /// Whether an int off the wire names an armour slot.
    private static bool IsArmourSlot(int slot) =>
        slot is >= (int)GlamSlot.Head and <= (int)GlamSlot.RingL;

    private static bool IsWeaponSlot(int slot) =>
        slot is (int)GlamSlot.MainHand or (int)GlamSlot.OffHand;
}
