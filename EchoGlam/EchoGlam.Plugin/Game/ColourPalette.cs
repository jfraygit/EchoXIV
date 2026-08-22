using System;
using System.Collections.Generic;
using System.Numerics;

namespace EchoGlam.Game;

/// The real character colours, read out of the game's own colour tables.
public static class ColourPalette
{
    /// Total size of human.cmp.
    private const int ExpectedSize = 186_752;

    private const int ColourSize = 4;

    /// One ColorParameters block, and the byte offsets of the palettes inside it.
    private const int ParametersSize = 9_216;

    private const int ParametersOffset = 0;
    private const int InterfaceOffset = ParametersSize;

    private const int EyesOffset = 0;
    private const int HairHighlightsOffset = 1_024;
    private const int LipsDarkOffset = 2_048;
    private const int FacePaintDarkOffset = 2_560;
    private const int FeaturesOffset = 3_072;
    private const int LipsLightOffset = 4_096;
    private const int FacePaintLightOffset = 4_608;

    /// The per-clan tables: thirty-two of them, one for each clan and sex.
    private const int RacesOffset = ParametersSize * 2;
    private const int GenderClanSize = 5_120;

    private const int SkinInterfaceOffset = 1_024 + 2_048;
    private const int HairInterfaceOffset = 1_024 + 2_048 + 1_024;

    /// How many entries of a full palette the game actually offers.
    private const int FullCount = 192;

    /// Lips and face paint are two half-palettes - a dark set at values 0-95 and a light set at 128-223 -
    /// rather than one run.
    private const int TonedCount = 96;

    /// Value that selects the first of the light half.
    private const int LightBase = 128;

    private static byte[]? data;

    public static bool Ready { get; private set; }

    /// Reads and validates the file.
    public static void Build()
    {
        if (Ready)
            return;

        try
        {
            var file = Plugin.DataManager.GetFile("chara/xls/charamake/human.cmp");

            if (file == null)
            {
                Plugin.Log.Warning("[EchoGlam] human.cmp is missing; colour swatches are unavailable.");
                return;
            }

            if (file.Data.Length != ExpectedSize)
            {
                Plugin.Log.Error(
                    $"[EchoGlam] human.cmp is {file.Data.Length} bytes, expected {ExpectedSize}. " +
                    "The colour tables have changed shape; swatches stay off and the editor falls " +
                    "back to palette numbers.");

                return;
            }

            data = file.Data;
            Ready = true;
            Plugin.Log.Information("[EchoGlam] Colour palettes loaded from human.cmp.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not read human.cmp");
        }
    }

    /// The swatches for one colour field, as the value to store and the colour to draw.
    public static IReadOnlyList<(byte Value, Vector4 Colour)> For(CustomizeIndex index, byte tribe, byte sex)
    {
        if (!Ready || data == null)
            return [];

        return index switch
        {
            CustomizeIndex.EyeColourLeft or CustomizeIndex.EyeColourRight =>
                Full(InterfaceOffset + EyesOffset),

            CustomizeIndex.HighlightsColour => Full(InterfaceOffset + HairHighlightsOffset),
            CustomizeIndex.FacialFeatureColour => Full(InterfaceOffset + FeaturesOffset),

            CustomizeIndex.SkinColour => Full(ClanOffset(tribe, sex) + SkinInterfaceOffset),
            CustomizeIndex.HairColour => Full(ClanOffset(tribe, sex) + HairInterfaceOffset),

            CustomizeIndex.LipColour =>
                Toned(ParametersOffset + LipsDarkOffset, ParametersOffset + LipsLightOffset),

            CustomizeIndex.FacepaintColour =>
                Toned(ParametersOffset + FacePaintDarkOffset, ParametersOffset + FacePaintLightOffset),

            _ => [],
        };
    }

    /// Where this clan and sex's tables start.
    private static int ClanOffset(byte tribe, byte sex)
    {
        var clan = Math.Max(0, tribe - 1);
        var index = Math.Clamp((clan * 2) + (sex == 1 ? 1 : 0), 0, 31);
        return RacesOffset + (index * GenderClanSize);
    }

    private static List<(byte, Vector4)> Full(int byteOffset)
    {
        var list = new List<(byte, Vector4)>(FullCount);

        for (var i = 0; i < FullCount; i++)
            list.Add(((byte)i, Read(byteOffset + (i * ColourSize))));

        return list;
    }

    private static List<(byte, Vector4)> Toned(int darkOffset, int lightOffset)
    {
        var list = new List<(byte, Vector4)>(TonedCount * 2);

        for (var i = 0; i < TonedCount; i++)
            list.Add(((byte)i, Read(darkOffset + (i * ColourSize))));

        for (var i = 0; i < TonedCount; i++)
            list.Add(((byte)(LightBase + i), Read(lightOffset + (i * ColourSize))));

        return list;
    }

    /// One colour.
    private static Vector4 Read(int offset)
    {
        if (data == null || offset + 3 >= data.Length)
            return new Vector4(0f, 0f, 0f, 1f);

        return new Vector4(
            data[offset] / 255f,
            data[offset + 1] / 255f,
            data[offset + 2] / 255f,
            1f);
    }
}
