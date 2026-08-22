using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;

namespace EchoRoleplay.Game;

/// Where an icon's picture actually is inside its texture, and how wide it is against how tall.
public readonly record struct IconCrop(Vector2 Uv0, Vector2 Uv1, float Aspect, float Fill = 1f);

/// One icon a player can put on a status.
public readonly record struct IconEntry(uint IconId, string Name, string Search);

/// Every icon a status can use, taken from the game's own art.
public sealed class IconCatalogue
{
    private readonly IDataManager data;
    private readonly ITextureProvider textures;

    private IconEntry[]? all;

    public IconCatalogue(IDataManager data, ITextureProvider textures)
    {
        this.data = data;
        this.textures = textures;
    }

    /// Every icon, or empty until the picker has been opened once.
    public IReadOnlyList<IconEntry> All => all ?? [];

    public bool Built => all is not null;

    /// How many distinct icons the sheets named, before checking any of them exist.
    public int RowsRead { get; private set; }

    /// How many of those the game does not actually ship art for.
    public int Missing { get; private set; }

    /// Builds the set, once.
    public void EnsureBuilt()
    {
        if (all is not null)
            return;

        var found = new List<IconEntry>();

        var seen = new HashSet<uint>();

        Collect(found, seen, EmoteIcons());
        Collect(found, seen, StatusIcons());

        RowsRead = found.Count;

        found.RemoveAll(e => !textures.TryGetIconPath(new GameIconLookup(e.IconId), out _));

        Missing = RowsRead - found.Count;

        all = [.. found.OrderBy(e => e.Search, StringComparer.Ordinal)];
    }

    /// Emotes first, deliberately.
    private IEnumerable<(uint Icon, string Name)> EmoteIcons()
    {
        var sheet = data.GetExcelSheet<Emote>();
        if (sheet is null)
            yield break;

        foreach (var row in sheet)
        {
            var name = row.Name.ExtractText();
            if (row.Icon == 0 || string.IsNullOrWhiteSpace(name))
                continue;

            yield return (row.Icon, name);
        }
    }

    private IEnumerable<(uint Icon, string Name)> StatusIcons()
    {
        var sheet = data.GetExcelSheet<Status>();
        if (sheet is null)
            yield break;

        foreach (var row in sheet)
        {
            var name = row.Name.ExtractText();
            if (row.Icon == 0 || string.IsNullOrWhiteSpace(name))
                continue;

            yield return (row.Icon, name);
        }
    }

    private static void Collect(
        List<IconEntry> into, HashSet<uint> seen, IEnumerable<(uint Icon, string Name)> source)
    {
        foreach (var (icon, name) in source)
        {
            if (!seen.Add(icon))
                continue;

            into.Add(new IconEntry(icon, name, name.ToLowerInvariant()));
        }
    }

    /// Icons whose name contains every word typed, or everything when nothing is typed.
    public IEnumerable<IconEntry> Search(string query)
    {
        var icons = All;

        if (string.IsNullOrWhiteSpace(query))
            return icons;

        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return icons.Where(e => words.All(w => e.Search.Contains(w, StringComparison.Ordinal)));
    }

    /// The texture for an icon id, or null while it is still loading or if there is none.
    public IDalamudTextureWrap? Texture(uint iconId)
    {
        if (iconId == 0)
            return null;

        return textures.TryGetFromGameIcon(new GameIconLookup(iconId), out var shared)
            ? shared.GetWrapOrDefault()
            : null;
    }

    /// What to show for a status whose icon will not load - a missing id, or one from a future patch.
    public const uint FallbackIconId = 60071;

    /// Alpha below which a pixel is not part of the picture.
    private const byte AlphaFloor = 8;

    /// How far a pixel's colour has to sit from the canvas corner before it counts as art, summed across the
    /// three channels.
    private const int BackgroundTolerance = 24;

    /// How much of an ordinary icon's measured ink is the soft fringe around its art, rather than the art
    /// itself.
    private const float FringeRatio = 0.915f;

    private readonly Dictionary<uint, IconCrop> crops = [];

    /// Draws one icon filling a square box, cropped to its own art.
    public void Draw(ImDrawListPtr drawList, uint iconId, Vector2 min, float box, uint tint)
    {
        var texture = Texture(iconId);
        if (texture is null)
            return;

        var crop = Crop(iconId);

        var size = crop.Aspect >= 1f
            ? new Vector2(box, box / crop.Aspect)
            : new Vector2(box * crop.Aspect, box);

        size *= crop.Fill;

        var offset = (new Vector2(box, box) - size) / 2f;

        drawList.AddImage(texture.Handle, min + offset, min + offset + size, crop.Uv0, crop.Uv1, tint);
    }

    /// Where this icon's art sits inside its texture.
    public IconCrop Crop(uint iconId)
    {
        if (crops.TryGetValue(iconId, out var cached))
            return cached;

        var measured = Measure(iconId);
        crops[iconId] = measured;
        return measured;
    }

    private static readonly IconCrop WholeTexture = new(Vector2.Zero, Vector2.One, 1f);

    private IconCrop Measure(uint iconId)
    {
        try
        {
            if (!textures.TryGetIconPath(new GameIconLookup(iconId), out var path))
                return WholeTexture;

            var file = data.GetFile<TexFile>(path);
            if (file is null)
                return WholeTexture;

            int width = file.Header.Width;
            int height = file.Header.Height;

            var pixels = file.ImageData;

            if (width <= 0 || height <= 0 || pixels.Length < width * height * 4)
                return WholeTexture;

            var box = InkBox(pixels, width, height);

            if (box.MaxX < 0)
                return WholeTexture;

            var fill = 1f;

            if (box.MinX == 0 && box.MinY == 0 && box.MaxX == width - 1 && box.MaxY == height - 1)
            {
                var byColour = ColourBox(pixels, width, height);

                if (byColour.MaxX >= 0 &&
                    (byColour.MaxX - byColour.MinX + 1 < width || byColour.MaxY - byColour.MinY + 1 < height))
                {
                    box = byColour;
                    fill = FringeRatio;
                }
            }

            var inkWidth = box.MaxX + 1 - box.MinX;
            var inkHeight = box.MaxY + 1 - box.MinY;

            return new IconCrop(
                new Vector2(box.MinX / (float)width, box.MinY / (float)height),
                new Vector2((box.MaxX + 1) / (float)width, (box.MaxY + 1) / (float)height),
                inkWidth / (float)inkHeight,
                fill);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[EchoRoleplay] Could not measure icon {iconId}");
            return WholeTexture;
        }
    }

    /// The rectangle holding every pixel the eye can see, by alpha.
    private static (int MinX, int MinY, int MaxX, int MaxY) InkBox(byte[] pixels, int width, int height)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[(((y * width) + x) * 4) + 3] <= AlphaFloor)
                    continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        return (minX, minY, maxX, maxY);
    }

    /// The same rectangle, measured against the colour in the canvas corner rather than against alpha.
    private static (int MinX, int MinY, int MaxX, int MaxY) ColourBox(byte[] pixels, int width, int height)
    {
        int backB = pixels[0], backG = pixels[1], backR = pixels[2];
        int minX = width, minY = height, maxX = -1, maxY = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;

                if (pixels[offset + 3] <= AlphaFloor)
                    continue;

                var distance = Math.Abs(pixels[offset] - backB)
                               + Math.Abs(pixels[offset + 1] - backG)
                               + Math.Abs(pixels[offset + 2] - backR);

                if (distance <= BackgroundTolerance)
                    continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        return (minX, minY, maxX, maxY);
    }
}
