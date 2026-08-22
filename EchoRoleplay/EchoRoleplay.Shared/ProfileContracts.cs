using System;
using System.Collections.Generic;

namespace EchoRoleplay.Shared;

/// Where a character stands toward roleplay - a profile field, not a status.
public enum RpStatus
{
    /// Said nothing.
    Unspecified = 0,
    InCharacter = 1,
    OutOfCharacter = 2,
    LookingForRp = 3,
    InAScene = 4,
    DoNotDisturb = 5,
}

/// One status a character is showing.
public sealed class RoleplayStatus
{
    /// Which icon, as the game's own icon id.
    public uint IconId { get; set; }

    /// What the player called it.
    public string Label { get; set; } = string.Empty;

    /// The player's own line about it, shown under the label when the icon is hovered.
    public string Detail { get; set; } = string.Empty;

    /// When this clears itself, or null to leave it until it is taken off.
    public DateTime? ExpiresUtc { get; set; }

    public bool HasExpired(DateTime utcNow) => ExpiresUtc is { } expiry && expiry <= utcNow;
}

/// A label and a value the schema did not anticipate.
public sealed class ProfileField
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// A character's roleplay profile.
public sealed class RoleplayProfile
{
    /// Stable identity for this profile within one installation.
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// What the player calls this profile in their own list ("Seraphine", "the disguise").
    public string ProfileName { get; set; } = string.Empty;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;


    /// The name they roleplay under, which is routinely not their character name.
    public string Name { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;

    /// House, clan or family name.
    public string HouseName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Pronouns { get; set; } = string.Empty;

    /// Name colour, as RGB floats.
    public float[]? NameColour { get; set; }

    public RpStatus RpStatus { get; set; } = RpStatus.Unspecified;

    /// This character's theme, as an orchestrion row from the game's own music.
    public uint ThemeSongId { get; set; }



    public string Age { get; set; } = string.Empty;
    public string ApparentAge { get; set; } = string.Empty;

    /// FREE TEXT, NOT THE GAME'S RACE.
    public string Race { get; set; } = string.Empty;

    public string Birthplace { get; set; } = string.Empty;
    public string Residence { get; set; } = string.Empty;
    public string Occupation { get; set; } = string.Empty;
    public string Height { get; set; } = string.Empty;
    public string Build { get; set; } = string.Empty;
    public string Eyes { get; set; } = string.Empty;
    public string Hair { get; set; } = string.Empty;
    public string Marks { get; set; } = string.Empty;
    public string Alignment { get; set; } = string.Empty;
    public string Voice { get; set; } = string.Empty;


    /// Nameday, as written text ("32nd Sun of the Second Astral Moon").
    public string Nameday { get; set; } = string.Empty;

    /// Row id in the GuardianDeity sheet, or 0 for none.
    public uint GuardianDeityId { get; set; }

    /// Row id in the GrandCompany sheet, or 0 for none.
    public uint GrandCompanyId { get; set; }

    public string FreeCompany { get; set; } = string.Empty;


    /// ClassJob row ids the character uses in character.
    public List<uint> Jobs { get; set; } = [];


    public string Appearance { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string Backstory { get; set; } = string.Empty;

    /// What others may have heard.
    public string Rumours { get; set; } = string.Empty;

    /// How to approach me.
    public string Hooks { get; set; } = string.Empty;


    public List<ProfileField> CustomFields { get; set; } = [];

    /// The non-exclusive statuses shown beside RpStatus in the row.
    public List<RoleplayStatus> Statuses { get; set; } = [];

    /// Drops any status whose time is up.
    public bool PruneExpiredStatuses(DateTime utcNow) => Statuses.RemoveAll(s => s.HasExpired(utcNow)) > 0;

    /// Whether there is anything here worth showing somebody.
    public bool HasAnything =>
        !string.IsNullOrWhiteSpace(Name)
        || !string.IsNullOrWhiteSpace(Title)
        || !string.IsNullOrWhiteSpace(Appearance)
        || !string.IsNullOrWhiteSpace(Backstory)
        || RpStatus != RpStatus.Unspecified
        || Statuses.Count > 0;
}

/// Length caps, in one place because both ends need the same numbers.
public static class ProfileLimits
{
    public const int Name = 48;
    public const int Nickname = 32;
    public const int HouseName = 48;
    public const int Title = 64;
    public const int Pronouns = 24;
    public const int ShortField = 64;
    public const int OneLiner = 160;

    /// A status label.
    public const int StatusLabel = 20;

    /// The line under a status label.
    public const int StatusDetail = 100;

    public const int LongForm = 4000;
    public const int CustomFieldLabel = 32;
    public const int CustomFieldValue = 256;
    public const int ProfileName = 48;

    /// How many extra statuses may sit beside the RP status.
    public const int Statuses = 4;

    public const int CustomFields = 12;
}
