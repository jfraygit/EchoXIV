using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EchoGlam.Game;

/// What came out of a Glamourer design, in EchoGlam's own vocabulary.
public sealed record GlamourerLook(
    string Name,
    Dictionary<GlamSlot, GlamEntry> Look,
    CustomizeSet? Appearance,
    IReadOnlyList<string> Notes)
{
    /// How many slots hold an actual piece of gear.
    public int Pieces => Look.Count(e => e.Value.ItemId != 0);
}

/// Reads and writes Glamourer's design share strings - the thing its "Copy the current design to your
/// clipboard" button produces.
public static class GlamourerDesign
{
    /// The version Glamourer 1.6 writes, and what this plugin writes back.
    public const byte ShareVersion = 6;

    private const int MaximumEncoded = 512 * 1024;

    /// Glamourer's own slot numbering, needed only to write the "there is nothing here" item ids back out.
    private static readonly Dictionary<GlamSlot, uint> GlamourerSlot = new()
    {
        [GlamSlot.MainHand] = 1,
        [GlamSlot.OffHand] = 2,
        [GlamSlot.Head] = 3,
        [GlamSlot.Body] = 4,
        [GlamSlot.Hands] = 5,
        [GlamSlot.Legs] = 7,
        [GlamSlot.Feet] = 8,
        [GlamSlot.Ears] = 9,
        [GlamSlot.Neck] = 10,
        [GlamSlot.Wrists] = 11,
        [GlamSlot.RingR] = 12,
        [GlamSlot.RingL] = 12,
    };

    /// The name Glamourer's Equipment object uses for each slot this plugin fills.
    private static readonly Dictionary<GlamSlot, string> SlotNames = new()
    {
        [GlamSlot.MainHand] = "MainHand",
        [GlamSlot.OffHand] = "OffHand",
        [GlamSlot.Head] = "Head",
        [GlamSlot.Body] = "Body",
        [GlamSlot.Hands] = "Hands",
        [GlamSlot.Legs] = "Legs",
        [GlamSlot.Feet] = "Feet",
        [GlamSlot.Ears] = "Ears",
        [GlamSlot.Neck] = "Neck",
        [GlamSlot.Wrists] = "Wrists",
        [GlamSlot.RingR] = "RFinger",
        [GlamSlot.RingL] = "LFinger",
    };

    /// Glamourer's appearance fields that are a plain byte at one index.
    private static readonly (string Field, CustomizeIndex Index, bool Flagged)[] Values =
    [
        ("Race", CustomizeIndex.Race, false),
        ("Gender", CustomizeIndex.Sex, false),
        ("BodyType", CustomizeIndex.BodyType, false),
        ("Height", CustomizeIndex.Height, false),
        ("Clan", CustomizeIndex.Tribe, false),
        ("Face", CustomizeIndex.Face, false),
        ("Hairstyle", CustomizeIndex.Hair, false),
        ("SkinColor", CustomizeIndex.SkinColour, false),
        ("EyeColorRight", CustomizeIndex.EyeColourRight, false),
        ("HairColor", CustomizeIndex.HairColour, false),
        ("HighlightsColor", CustomizeIndex.HighlightsColour, false),
        ("TattooColor", CustomizeIndex.FacialFeatureColour, false),
        ("Eyebrows", CustomizeIndex.Eyebrows, false),
        ("EyeColorLeft", CustomizeIndex.EyeColourLeft, false),
        ("EyeShape", CustomizeIndex.EyeShape, true),
        ("Nose", CustomizeIndex.Nose, false),
        ("Jaw", CustomizeIndex.Jaw, false),
        ("Mouth", CustomizeIndex.Mouth, true),
        ("LipColor", CustomizeIndex.LipColour, false),
        ("MuscleMass", CustomizeIndex.RaceFeatureSize, false),
        ("TailShape", CustomizeIndex.RaceFeatureType, false),
        ("BustSize", CustomizeIndex.BustSize, false),
        ("FacePaint", CustomizeIndex.Facepaint, true),
        ("FacePaintColor", CustomizeIndex.FacepaintColour, false),
    ];

    /// Glamourer's appearance fields that are a single bit sharing a byte with a value.
    private static readonly (string Field, CustomizeIndex Index)[] Flags =
    [
        ("Highlights", CustomizeIndex.HasHighlights),
        ("SmallIris", CustomizeIndex.EyeShape),
        ("Lipstick", CustomizeIndex.Mouth),
        ("FacePaintReversed", CustomizeIndex.Facepaint),
    ];

    /// The eight bits of the facial-feature byte, in order.
    private static readonly string[] FacialFeatures =
    [
        "FacialFeature1", "FacialFeature2", "FacialFeature3", "FacialFeature4",
        "FacialFeature5", "FacialFeature6", "FacialFeature7", "LegacyTattoo",
    ];

    /// Turns a share string into the JSON document inside it.
    public static JObject? Decode(string text, out string error)
    {
        error = string.Empty;

        var trimmed = new string((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());

        if (trimmed.Length == 0)
        {
            error = "There was nothing on the clipboard.";
            return null;
        }

        if (trimmed.Length > MaximumEncoded)
        {
            error = "That is far too long to be a Glamourer design.";
            return null;
        }

        var padded = trimmed.PadRight(trimmed.Length + ((4 - (trimmed.Length % 4)) % 4), '=');

        byte[] raw;

        try
        {
            raw = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            error = "That does not look like a Glamourer design. Copy it again with Glamourer's "
                + "clipboard button.";
            return null;
        }

        var offset =
            raw.Length > 2 && raw[0] == 0x1F && raw[1] == 0x8B ? 0 :
            raw.Length > 3 && raw[1] == 0x1F && raw[2] == 0x8B ? 1 :
            -1;

        if (offset < 0)
        {
            error = "That is not a Glamourer design. It may be a Penumbra or Customize+ string.";
            return null;
        }

        byte[] plain;

        try
        {
            using var input = new MemoryStream(raw, offset, raw.Length - offset);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            gzip.CopyTo(output);
            plain = output.ToArray();
        }
        catch (Exception)
        {
            error = "That design is damaged and could not be unpacked. Copy it again.";
            return null;
        }

        var start = plain.Length > 0 && plain[0] != (byte)'{' ? 1 : 0;

        try
        {
            var json = Encoding.UTF8.GetString(plain, start, plain.Length - start);
            return JObject.Parse(json);
        }
        catch (Exception)
        {
            error = "That design unpacked into something that is not a design.";
            return null;
        }
    }

    /// Reads a share string into a wearable look.
    public static GlamourerLook? Read(
        string text,
        ItemCatalogue catalogue,
        CustomizeSet? current,
        out string error)
    {
        var design = Decode(text, out error);

        if (design is null)
            return null;

        var notes = new List<string>();
        var look = new Dictionary<GlamSlot, GlamEntry>();
        var equipment = design["Equipment"] as JObject;

        foreach (var slot in GlamSlots.All)
        {
            var piece = equipment?[SlotNames[slot]] as JObject;
            var itemId = 0u;
            var stain0 = (byte)0;
            var stain1 = (byte)0;

            if (piece is not null && piece["Apply"]?.Value<bool>() != false)
            {
                var raw = piece["ItemId"]?.Value<ulong>() ?? 0UL;

                if (raw is > 0 and <= uint.MaxValue && catalogue.FitsSlot(slot, (uint)raw))
                {
                    itemId = (uint)raw;

                    var stained = piece["ApplyStain"]?.Value<bool>() != false;

                    stain0 = stained ? (byte)(piece["Stain"]?.Value<uint>() ?? 0) : (byte)0;
                    stain1 = stained ? (byte)(piece["Stain2"]?.Value<uint>() ?? 0) : (byte)0;
                }
            }

            if (itemId == 0 && slot.IsWeapon())
                continue;

            look[slot] = new GlamEntry(itemId, stain0, stain1);
        }

        ApplyVisibility(equipment, look);

        var appearance = ReadAppearance(design["Customize"] as JObject, current, notes);

        NoteWhatWasDropped(design, notes);

        var name = design["Name"]?.Value<string>()?.Trim();

        return new GlamourerLook(
            string.IsNullOrEmpty(name) ? "Glamourer Design" : name,
            look,
            appearance,
            notes);
    }

    /// Rebuilds the twenty-six bytes from Glamourer's named fields.
    private static CustomizeSet? ReadAppearance(JObject? customize, CustomizeSet? current, List<string> notes)
    {
        if (customize is null)
            return null;

        var setsSomething =
            Values.Any(v => Applied(customize, v.Field))
            || Flags.Any(f => Applied(customize, f.Field))
            || FacialFeatures.Any(f => Applied(customize, f));

        if (!setsSomething)
            return null;

        if ((customize["ModelId"]?.Value<uint>() ?? 0) != 0)
            notes.Add("This design turns you into another creature. Only its gear will come across.");

        var set = current?.Clone() ?? new CustomizeSet();

        if (current is null)
            notes.Add("Parts of the appearance it does not set were left at their defaults.");

        foreach (var (field, index, flagged) in Values)
        {
            if (customize[field] is not JObject entry || !Applied(customize, field))
                continue;

            var value = (byte)(entry["Value"]?.Value<uint>() ?? 0);

            set[index] = flagged
                ? (byte)((set[index] & Appearance.FlagBit) | (value & 0x7F))
                : value;
        }

        foreach (var (field, index) in Flags)
        {
            if (customize[field] is not JObject entry || !Applied(customize, field))
                continue;

            var on = (entry["Value"]?.Value<uint>() ?? 0) != 0;

            set[index] = (byte)((set[index] & 0x7F) | (on ? Appearance.FlagBit : 0));
        }

        if (FacialFeatures.Any(f => Applied(customize, f)))
        {
            byte features = 0;

            for (var bit = 0; bit < FacialFeatures.Length; bit++)
            {
                if (customize[FacialFeatures[bit]] is JObject entry
                    && (entry["Value"]?.Value<uint>() ?? 0) != 0)
                    features |= (byte)(1 << bit);
            }

            set[CustomizeIndex.FacialFeatures] = features;
        }

        if (!Applied(customize, "Race") && Applied(customize, "Clan"))
        {
            var tribe = set[CustomizeIndex.Tribe];

            if (tribe > 0)
                set[CustomizeIndex.Race] = (byte)((tribe + 1) / 2);
        }

        return set;
    }

    /// Carries out the design's hide switches, which this plugin CAN do.
    private static void ApplyVisibility(JObject? equipment, Dictionary<GlamSlot, GlamEntry> look)
    {
        if (equipment is null)
            return;

        if (Hidden(equipment["Hat"], "Show"))
            look[GlamSlot.Head] = new GlamEntry(0, 0, 0);

        if (Hidden(equipment["Weapon"], "Show"))
        {
            look[GlamSlot.MainHand] = new GlamEntry(0, 0, 0);
            look[GlamSlot.OffHand] = new GlamEntry(0, 0, 0);
        }
    }

    /// Whether one of Glamourer's visibility switches is both turned off and being applied.
    private static bool Hidden(JToken? token, string field) =>
        token is JObject entry
        && entry[field]?.Value<bool>() == false
        && entry["Apply"]?.Value<bool>() != false;

    /// Whether the design says to apply one of its fields.
    private static bool Applied(JObject customize, string field) =>
        customize[field] is not JObject entry || entry["Apply"]?.Value<bool>() != false;

    /// Adds a note for each thing the design carries that an outfit cannot.
    private static void NoteWhatWasDropped(JObject design, List<string> notes)
    {
        if (design["Bonus"]?["Glasses"] is JObject glasses
            && glasses["BonusId"]?.Value<ulong>() is > 0 and <= uint.MaxValue
            && glasses["Apply"]?.Value<bool>() != false)
            notes.Add("Its facewear was left out. EchoGlam has no facewear slot.");

        if (design["Parameters"] is JObject parameters
            && parameters.Properties().Any(p => p.Value["Apply"]?.Value<bool>() == true))
            notes.Add("Its advanced colour settings were left out.");

        if (design["Materials"] is JObject materials && materials.HasValues)
            notes.Add("Its material edits were left out.");

        if (design["Mods"] is JArray mods && mods.Count > 0)
            notes.Add("Its Penumbra mod settings were left out.");

        if (design["Links"]?["Before"] is JArray before && before.Count > 0
            || design["Links"]?["After"] is JArray after && after.Count > 0)
            notes.Add("Designs it links to were left out.");

        if (design["Equipment"] is JObject equipment)
        {
            if (equipment["Visor"] is JObject visor
                && visor["IsToggled"]?.Value<bool>() == true
                && visor["Apply"]?.Value<bool>() != false)
                notes.Add("Its visor setting was left out.");
        }
    }

    /// Writes an outfit back out as a design string Glamourer will accept.
    public static string Write(string name, IReadOnlyDictionary<GlamSlot, GlamEntry> look, CustomizeSet? appearance)
    {
        var equipment = new JObject();

        foreach (var slot in GlamSlots.All)
        {
            var has = look.TryGetValue(slot, out var entry);

            var wearing = has && entry.ItemId != 0;

            var hidingWeapon = slot.IsWeapon() && has && !wearing;

            equipment[SlotNames[slot]] = new JObject
            {
                ["ItemId"] = wearing ? entry.ItemId : uint.MaxValue - 128 - GlamourerSlot[slot],
                ["Crest"] = false,
                ["Apply"] = has && !hidingWeapon,
                ["ApplyStain"] = wearing,
                ["ApplyCrest"] = false,
                ["Stain"] = wearing ? entry.Stain0 : 0,
                ["Stain2"] = wearing ? entry.Stain1 : 0,
            };
        }

        var weaponsHidden = GlamSlots.All
            .Any(s => s.IsWeapon() && look.TryGetValue(s, out var e) && e.ItemId == 0);

        foreach (var field in new[] { "Hat", "VieraEars" })
            equipment[field] = new JObject { ["Show"] = true, ["Apply"] = false };

        equipment["Weapon"] = new JObject { ["Show"] = !weaponsHidden, ["Apply"] = weaponsHidden };

        equipment["Visor"] = new JObject { ["IsToggled"] = false, ["Apply"] = false };

        var design = new JObject
        {
            ["FileVersion"] = 2,
            ["Identifier"] = Guid.NewGuid().ToString(),
            ["CreationDate"] = DateTime.UtcNow.ToString("o"),
            ["LastEdit"] = DateTime.UtcNow.ToString("o"),
            ["Name"] = name,
            ["Description"] = "Exported from EchoGlam.",
            ["Color"] = string.Empty,
            ["QuickDesign"] = true,
            ["Tags"] = new JArray(),
            ["WriteProtected"] = false,
            ["Equipment"] = equipment,
            ["Customize"] = WriteAppearance(appearance),
            ["Parameters"] = new JObject(),
            ["Materials"] = new JObject(),
            ["Mods"] = new JArray(),
            ["Links"] = new JObject { ["Before"] = new JArray(), ["After"] = new JArray() },
        };

        return Encode(design);
    }

    /// Turns the twenty-six bytes back into Glamourer's named fields.
    private static JObject WriteAppearance(CustomizeSet? set)
    {
        var customize = new JObject { ["ModelId"] = 0 };
        var apply = set is not null;

        foreach (var (field, index, flagged) in Values)
        {
            var value = set is null ? 0 : flagged ? set[index] & 0x7F : set[index];

            customize[field] = new JObject { ["Value"] = value, ["Apply"] = apply };
        }

        foreach (var (field, index) in Flags)
        {
            var on = set is not null && (set[index] & Appearance.FlagBit) != 0;

            customize[field] = new JObject { ["Value"] = on ? Appearance.FlagBit : 0, ["Apply"] = apply };
        }


        for (var bit = 0; bit < FacialFeatures.Length; bit++)
        {
            var on = set is not null && (set[CustomizeIndex.FacialFeatures] & (1 << bit)) != 0;

            customize[FacialFeatures[bit]] = new JObject
            {
                ["Value"] = on ? 1 << bit : 0,
                ["Apply"] = apply,
            };
        }

        customize["Wetness"] = new JObject { ["Value"] = false, ["Apply"] = false };

        return customize;
    }

    /// Packs a design back into a share string, laid out the way Glamourer's own button lays it out - version
    /// byte first, then the gzip stream.
    public static string Encode(JObject design)
    {
        var json = Encoding.UTF8.GetBytes(design.ToString(Formatting.None));

        using var output = new MemoryStream();

        output.WriteByte(ShareVersion);

        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            gzip.Write(json, 0, json.Length);

        return Convert.ToBase64String(output.ToArray());
    }
}
