using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using LuminaEquipSlotCategory = Lumina.Excel.Sheets.EquipSlotCategory;

namespace EchoGlam.Game;

/// The slots a glamour can fill.
public enum GlamSlot
{
    MainHand,
    OffHand,
    Head,
    Body,
    Hands,
    Legs,
    Feet,
    Ears,
    Neck,
    Wrists,
    RingR,
    RingL,
}

public static class GlamSlots
{
    /// Every slot, in display order.
    public static readonly GlamSlot[] All =
    [
        GlamSlot.MainHand, GlamSlot.OffHand,
        GlamSlot.Head, GlamSlot.Body, GlamSlot.Hands, GlamSlot.Legs, GlamSlot.Feet,
        GlamSlot.Ears, GlamSlot.Neck, GlamSlot.Wrists, GlamSlot.RingR, GlamSlot.RingL,
    ];

    /// The ten slots the game draws as equipment.
    public static readonly GlamSlot[] Armour =
    [
        GlamSlot.Head, GlamSlot.Body, GlamSlot.Hands, GlamSlot.Legs, GlamSlot.Feet,
        GlamSlot.Ears, GlamSlot.Neck, GlamSlot.Wrists, GlamSlot.RingR, GlamSlot.RingL,
    ];

    public static bool IsWeapon(this GlamSlot slot) => slot is GlamSlot.MainHand or GlamSlot.OffHand;

    public static string Label(this GlamSlot slot) => slot switch
    {
        GlamSlot.MainHand => "Main Hand",
        GlamSlot.OffHand => "Off Hand",
        GlamSlot.Head => "Head",
        GlamSlot.Body => "Body",
        GlamSlot.Hands => "Hands",
        GlamSlot.Legs => "Legs",
        GlamSlot.Feet => "Feet",
        GlamSlot.Ears => "Earrings",
        GlamSlot.Neck => "Necklace",
        GlamSlot.Wrists => "Bracelets",
        GlamSlot.RingR => "Ring (Right)",
        GlamSlot.RingL => "Ring (Left)",
        _ => slot.ToString(),
    };

    /// Where this slot lives in the character's drawn equipment.
    public static DrawDataContainer.EquipmentSlot ToEquipmentSlot(this GlamSlot slot) => slot switch
    {
        GlamSlot.Head => DrawDataContainer.EquipmentSlot.Head,
        GlamSlot.Body => DrawDataContainer.EquipmentSlot.Body,
        GlamSlot.Hands => DrawDataContainer.EquipmentSlot.Hands,
        GlamSlot.Legs => DrawDataContainer.EquipmentSlot.Legs,
        GlamSlot.Feet => DrawDataContainer.EquipmentSlot.Feet,
        GlamSlot.Ears => DrawDataContainer.EquipmentSlot.Ears,
        GlamSlot.Neck => DrawDataContainer.EquipmentSlot.Neck,
        GlamSlot.Wrists => DrawDataContainer.EquipmentSlot.Wrists,
        GlamSlot.RingR => DrawDataContainer.EquipmentSlot.RFinger,
        GlamSlot.RingL => DrawDataContainer.EquipmentSlot.LFinger,
        _ => throw new ArgumentOutOfRangeException(
            nameof(slot), slot, "Weapons are not equipment slots - use ToWeaponSlot."),
    };

    public static DrawDataContainer.WeaponSlot ToWeaponSlot(this GlamSlot slot) => slot switch
    {
        GlamSlot.MainHand => DrawDataContainer.WeaponSlot.MainHand,
        GlamSlot.OffHand => DrawDataContainer.WeaponSlot.OffHand,
        _ => throw new ArgumentOutOfRangeException(
            nameof(slot), slot, "Only the two weapons are weapon slots."),
    };

    /// Which of these slots an item fits, from the game's own description of it.
    public static List<GlamSlot> SlotsFor(LuminaEquipSlotCategory category)
    {
        var slots = new List<GlamSlot>(2);

        if (category.MainHand == 1) slots.Add(GlamSlot.MainHand);
        if (category.OffHand == 1) slots.Add(GlamSlot.OffHand);
        if (category.Head == 1) slots.Add(GlamSlot.Head);
        if (category.Body == 1) slots.Add(GlamSlot.Body);
        if (category.Gloves == 1) slots.Add(GlamSlot.Hands);
        if (category.Legs == 1) slots.Add(GlamSlot.Legs);
        if (category.Feet == 1) slots.Add(GlamSlot.Feet);
        if (category.Ears == 1) slots.Add(GlamSlot.Ears);
        if (category.Neck == 1) slots.Add(GlamSlot.Neck);
        if (category.Wrists == 1) slots.Add(GlamSlot.Wrists);

        if (category.FingerL == 1 || category.FingerR == 1)
        {
            slots.Add(GlamSlot.RingR);
            slots.Add(GlamSlot.RingL);
        }

        return slots;
    }
}
