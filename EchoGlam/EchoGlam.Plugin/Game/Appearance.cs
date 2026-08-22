using System;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace EchoGlam.Game;

/// A whole character appearance, as the game stores it: twenty-six bytes.
public sealed class CustomizeSet
{
    public const int Length = 26;

    public byte[] Data { get; }

    public CustomizeSet() => Data = new byte[Length];

    public CustomizeSet(byte[] data)
    {
        Data = new byte[Length];
        Array.Copy(data, Data, Math.Min(data.Length, Length));
    }

    public CustomizeSet Clone() => new(Data);

    public bool SameAs(CustomizeSet other) => Data.AsSpan().SequenceEqual(other.Data);

    public byte this[CustomizeIndex index]
    {
        get => Data[(int)index];
        set => Data[(int)index] = value;
    }

    public string ToBase64() => Convert.ToBase64String(Data);

    public static CustomizeSet? FromBase64(string text)
    {
        try
        {
            var bytes = Convert.FromBase64String(text);
            return bytes.Length >= Length ? new CustomizeSet(bytes) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// Where each part of the appearance sits in those twenty-six bytes.
public enum CustomizeIndex
{
    Race = 0,
    Sex = 1,
    BodyType = 2,
    Height = 3,
    Tribe = 4,
    Face = 5,
    Hair = 6,
    HasHighlights = 7,
    SkinColour = 8,
    EyeColourRight = 9,
    HairColour = 10,
    HighlightsColour = 11,
    FacialFeatures = 12,
    FacialFeatureColour = 13,
    Eyebrows = 14,
    EyeColourLeft = 15,

    /// Eye shape in the low seven bits; bit 7 is the small-iris toggle.
    EyeShape = 16,

    Nose = 17,
    Jaw = 18,
    Mouth = 19,
    LipColour = 20,
    RaceFeatureSize = 21,
    RaceFeatureType = 22,
    BustSize = 23,
    Facepaint = 24,
    FacepaintColour = 25,
}

public static unsafe class Appearance
{
    /// The top bit, where several of these bytes keep a toggle alongside their value.
    public const byte FlagBit = 0x80;

    /// Whether this byte keeps a toggle in its top bit.
    public static bool HasFlagBit(CustomizeIndex index) =>
        index is CustomizeIndex.HasHighlights or CustomizeIndex.EyeShape;


    /// Reads the appearance the character is currently drawn with.
    public static CustomizeSet Read(Character* character)
    {
        var set = new CustomizeSet();
        var source = character->DrawData.CustomizeData;

        for (var i = 0; i < CustomizeSet.Length; i++)
            set.Data[i] = source.Data[i];

        return set;
    }

    /// Writes an appearance into the character's own copy.
    public static void Write(Character* character, CustomizeSet set)
    {
        for (var i = 0; i < CustomizeSet.Length; i++)
            character->DrawData.CustomizeData.Data[i] = set.Data[i];
    }

    /// Checks the index map above against the names the game's own structs give.
    public static void VerifyLayout(Character* character)
    {
        if (verified)
            return;

        verified = true;

        var data = character->DrawData.CustomizeData;
        var anchors = new (CustomizeIndex Index, byte Named, string Label)[]
        {
            (CustomizeIndex.Race, data.Race, "Race"),
            (CustomizeIndex.Sex, data.Sex, "Sex"),
            (CustomizeIndex.BodyType, data.BodyType, "BodyType"),
            (CustomizeIndex.Height, data.Height, "Height"),
            (CustomizeIndex.Tribe, data.Tribe, "Tribe"),
            (CustomizeIndex.Face, data.Face, "Face"),
            (CustomizeIndex.SkinColour, data.SkinColor, "SkinColor"),
            (CustomizeIndex.HairColour, data.HairColor, "HairColor"),
            (CustomizeIndex.Eyebrows, data.Eyebrows, "Eyebrows"),
            (CustomizeIndex.EyeShape, data.EyeShape, "EyeShape"),
            (CustomizeIndex.BustSize, data.BustSize, "BustSize"),
        };

        var bad = anchors.Where(a => (data.Data[(int)a.Index] & 0x7F) != (a.Named & 0x7F)).ToList();

        if (bad.Count == 0)
        {
            Plugin.Log.Information(
                $"[EchoGlam] Customise layout verified against {anchors.Length} named fields.");
            return;
        }

        LayoutTrusted = false;

        foreach (var (index, named, label) in bad)
        {
            Plugin.Log.Error(
                $"[EchoGlam] Customise layout MISMATCH: {label} is {named} but index {(int)index} " +
                $"({index}) holds {data.Data[(int)index]}. The appearance editor is disabled - the " +
                "index map in CustomizeIndex no longer matches the game.");
        }
    }

    private static bool verified;

    /// False once VerifyLayout has caught the map disagreeing with the game.
    public static bool LayoutTrusted { get; private set; } = true;
}
