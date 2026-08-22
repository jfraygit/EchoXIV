namespace EchoGlam.Shared;

/// One armour slot of a shared look, as the game draws it.
public sealed class LookSlotDto
{
    /// An EchoGlam.Game.GlamSlot value, as an int - this assembly cannot reference the plugin.
    public int Slot { get; set; }

    public ushort ModelId { get; set; }
    public byte Variant { get; set; }
    public byte Stain0 { get; set; }
    public byte Stain1 { get; set; }
}

/// A weapon, which the game loads through a different call with a differently shaped id.
public sealed class LookWeaponDto
{
    public int Slot { get; set; }

    public ushort ModelId { get; set; }
    public ushort Type { get; set; }
    public ushort Variant { get; set; }
    public byte Stain0 { get; set; }
}

/// What one installation is currently wearing, for its friends to draw.
public sealed class SharedLook
{
    /// Whose look this is - the public installation id, never the owner key.
    public string OwnerId { get; set; } = string.Empty;

    /// Which of their characters is wearing it, as "Name@World".
    public string Character { get; set; } = string.Empty;

    public List<LookSlotDto> Slots { get; set; } = [];

    public List<LookWeaponDto> Weapons { get; set; } = [];

    /// The 26-byte customise block, base64.
    public string Customise { get; set; } = string.Empty;

    /// Changes when anything above changes, and not otherwise.
    public string Stamp { get; set; } = string.Empty;

    /// The ActionTimeline row the wearer played as they changed into this, or zero.
    public ushort AnimationId { get; set; }

    /// How long the wearer's own client held the outfit change back for.
    public float AnimationHold { get; set; }

    /// How long ago this look was published, in milliseconds.
    public int AgeMs { get; set; }
}

/// Every friend's current look, as one answer.
public sealed class LookPage
{
    public List<SharedLook> Looks { get; set; } = [];
}
