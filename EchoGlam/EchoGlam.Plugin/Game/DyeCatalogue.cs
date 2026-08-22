using System;
using System.Collections.Generic;
using System.Numerics;
using LuminaStain = Lumina.Excel.Sheets.Stain;

namespace EchoGlam.Game;

/// One dye, with the colour the game actually tints with.
public sealed record Dye(byte Id, string Name, Vector4 Colour)
{
    /// No dye.
    public static readonly Dye None = new(0, "No dye", new Vector4(0.30f, 0.30f, 0.34f, 1f));
}

/// Every dye in the game, with its swatch colour read from the Stain sheet.
public sealed class DyeCatalogue
{
    private readonly List<Dye> all = [];
    private readonly List<Dye> palette = [];
    private readonly Dictionary<byte, Dye> byId = [];

    public bool Ready { get; private set; }

    /// Sheet order.
    public IReadOnlyList<Dye> All => all;

    /// The order to draw a grid of swatches in: neutrals first, then by colour family.
    public IReadOnlyList<Dye> Palette => palette;

    public Dye Resolve(byte id) => byId.TryGetValue(id, out var dye) ? dye : Dye.None;

    public void Build()
    {
        if (Ready)
            return;

        all.Add(Dye.None);
        byId[0] = Dye.None;

        try
        {
            foreach (var stain in Plugin.DataManager.GetExcelSheet<LuminaStain>())
            {
                if (stain.RowId == 0)
                    continue;

                var name = stain.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (stain.RowId > byte.MaxValue)
                    continue;

                var dye = new Dye((byte)stain.RowId, name, FromPackedColour(stain.Color));
                all.Add(dye);
                byId[dye.Id] = dye;
            }

            BuildPalette();

            Ready = true;
            Plugin.Log.Information($"[EchoGlam] Dye catalogue built: {all.Count} dyes.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not build the dye catalogue");
        }
    }

    /// Groups the palette so neighbours look like neighbours.
    private void BuildPalette()
    {
        palette.Clear();

        palette.Add(Dye.None);

        var darks = new List<(Dye Dye, float Hue, float Value)>();
        var neutrals = new List<(Dye Dye, float Value)>();
        var coloured = new List<(Dye Dye, int Family, float Value, float Saturation)>();

        foreach (var dye in all)
        {
            if (dye.Id == 0)
                continue;

            var (hue, saturation, value) = ToHsv(dye.Colour);

            if (value < DarkCeiling)
            {
                darks.Add((dye, saturation < NeutralSaturation ? -1f : hue, value));
                continue;
            }

            if (saturation < NeutralSaturation)
            {
                neutrals.Add((dye, value));
                continue;
            }

            coloured.Add((dye, Family(hue), value, saturation));
        }

        darks.Sort((a, b) =>
        {
            var byHue = a.Hue.CompareTo(b.Hue);
            return byHue != 0 ? byHue : a.Value.CompareTo(b.Value);
        });

        foreach (var (dye, _, _) in darks)
            palette.Add(dye);

        neutrals.Sort((a, b) => a.Value.CompareTo(b.Value));
        foreach (var (dye, _) in neutrals)
            palette.Add(dye);

        coloured.Sort((a, b) =>
        {
            var byFamily = a.Family.CompareTo(b.Family);
            if (byFamily != 0)
                return byFamily;

            var byValue = a.Value.CompareTo(b.Value);
            return byValue != 0 ? byValue : a.Saturation.CompareTo(b.Saturation);
        });

        foreach (var (dye, _, _, _) in coloured)
            palette.Add(dye);
    }

    /// Below this a swatch reads as black rather than as its colour, so it is sorted with the darks instead
    /// of into a family.
    private const float DarkCeiling = 0.22f;

    /// Below this a swatch has no usable hue.
    private const float NeutralSaturation = 0.15f;

    /// Which of twelve wedges a hue falls in.
    private static int Family(float hue) => (int)MathF.Floor(((hue + 15f) % 360f) / 30f);

    /// RGB to hue (degrees), saturation and value (both 0-1).
    private static (float Hue, float Saturation, float Value) ToHsv(Vector4 colour)
    {
        float r = colour.X, g = colour.Y, b = colour.Z;

        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;

        if (delta < 0.0001f)
            return (0f, 0f, max);

        float hue;
        if (max == r)
            hue = 60f * (((g - b) / delta) % 6f);
        else if (max == g)
            hue = 60f * (((b - r) / delta) + 2f);
        else
            hue = 60f * (((r - g) / delta) + 4f);

        if (hue < 0f)
            hue += 360f;

        return (hue, max <= 0f ? 0f : delta / max, max);
    }

    /// The sheet stores a dye as one packed integer, 0x00RRGGBB.
    private static Vector4 FromPackedColour(uint packed) => new(
        ((packed >> 16) & 0xFF) / 255f,
        ((packed >> 8) & 0xFF) / 255f,
        (packed & 0xFF) / 255f,
        1f);
}
