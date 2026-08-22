using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace EchoGlam.Game;

/// What kind of control a customisation menu wants.
public enum CharaMakeKind
{
    /// A short list of named variations - nose, jaw, mouth.
    Options = 0,

    /// A grid of pictures - faces, hairstyles, face paint.
    Icons = 1,

    /// A palette.
    Colour = 2,

    /// A run of on/off marks packed into one byte - the facial features.
    Toggles = 4,

    /// A continuous value.
    Slider = 5,
}

/// One choice within a menu: the value that goes in the byte, and the picture for it.
public sealed record CharaMakeOption(byte Value, uint IconId);

/// One row of the character creator, for one race, clan and sex.
public sealed record CharaMakeMenu(
    CustomizeIndex Index,
    CharaMakeKind Kind,
    IReadOnlyList<CharaMakeOption> Options,
    int Count,
    byte Initial);

/// The game's own character-creation menus, read per race, clan and sex.
public static class CharaMake
{
    private static readonly Dictionary<(byte Race, byte Tribe, byte Sex), IReadOnlyList<CharaMakeMenu>> Cache = [];

    /// The menus for one character, or an empty list if the game has none - which happens for a race and clan
    /// combination that does not exist.
    public static IReadOnlyList<CharaMakeMenu> For(byte race, byte tribe, byte sex)
    {
        var key = (race, tribe, sex);

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var menus = Read(race, tribe, sex);
        Cache[key] = menus;
        return menus;
    }

    /// Finds this character's row and turns its menus into something drawable.
    private static IReadOnlyList<CharaMakeMenu> Read(byte race, byte tribe, byte sex)
    {
        var menus = new List<CharaMakeMenu>();

        try
        {
            var customise = Plugin.DataManager.GetExcelSheet<CharaMakeCustomize>();

            foreach (var row in Plugin.DataManager.GetExcelSheet<CharaMakeType>())
            {
                if (row.Race.RowId != race || row.Tribe.RowId != tribe || row.Gender != sex)
                    continue;

                foreach (var menu in row.CharaMakeStruct)
                {
                    var count = menu.SubMenuNum;

                    if (count <= 0 || menu.Customize >= CustomizeSet.Length)
                        continue;

                    var kind = (CharaMakeKind)menu.SubMenuType;
                    var index = (CustomizeIndex)menu.Customize;
                    var options = new List<CharaMakeOption>();
                    var param = menu.SubMenuParam;
                    var graphic = menu.SubMenuGraphic;

                    if (kind == CharaMakeKind.Options)
                    {
                        for (var i = 0; i < count; i++)
                            options.Add(new CharaMakeOption(i < graphic.Count ? (byte)graphic[i] : (byte)i, 0));
                    }
                    else if (kind == CharaMakeKind.Icons)
                    {
                        var rows = new List<uint>();

                        for (var i = 0; i < count && i < param.Count; i++)
                        {
                            var p = (uint)param[i];
                            if (p == 0)
                                continue;

                            if (p >= 100000)
                            {
                                options.Add(new CharaMakeOption(i < graphic.Count ? (byte)graphic[i] : (byte)i, p));
                            }
                            else if (customise.TryGetRow(p, out var entry))
                            {
                                rows.Add(p);
                                options.Add(new CharaMakeOption((byte)entry.FeatureID, entry.Icon));
                            }
                        }

                        AddUnlockables(customise, rows, options);
                    }

                    menus.Add(new CharaMakeMenu(index, kind, options, count, (byte)menu.InitVal));
                }

                break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not read the character-creation menus");
        }

        return menus;
    }

    /// Adds the styles you have to unlock in game to a menu that only listed the ones you can pick at
    /// character creation.
    private static void AddUnlockables(
        Lumina.Excel.ExcelSheet<CharaMakeCustomize> customise,
        IReadOnlyList<uint> rows,
        List<CharaMakeOption> options)
    {
        if (rows.Count == 0)
            return;

        var seen = options.Select(o => o.Value).ToHashSet();

        for (var id = rows.Min(); ; id++)
        {
            if (!customise.TryGetRow(id, out var entry) || entry.FeatureID == 0)
                break;

            if (entry.Icon == 0 || !seen.Add((byte)entry.FeatureID))
                continue;

            if (entry.HintItem.ValueNullable is not { } unlock || unlock.Name.ExtractText().Length == 0)
                continue;

            options.Add(new CharaMakeOption((byte)entry.FeatureID, entry.Icon));
        }
    }

    /// The menu for one field, or null when this race has none for it.
    public static CharaMakeMenu? Menu(IReadOnlyList<CharaMakeMenu> menus, CustomizeIndex index) =>
        menus.FirstOrDefault(m => m.Index == index);

    /// How many marks the facial-feature byte actually uses for this character.
    public static int FacialFeatureCount(IReadOnlyList<CharaMakeMenu> menus)
    {
        var total = menus
            .Where(m => m.Index == CustomizeIndex.FacialFeatures && m.Kind == CharaMakeKind.Toggles)
            .Sum(m => m.Count);

        return total is > 0 and <= 8 ? total : 7;
    }
}
