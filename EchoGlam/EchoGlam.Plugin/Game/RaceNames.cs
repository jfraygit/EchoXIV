using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace EchoGlam.Game;

/// Race and clan names, for the gallery's "who is wearing this" line and its race filter.
public static class RaceNames
{
    private static readonly Dictionary<(byte Race, byte Sex), string> RaceCache = [];
    private static readonly Dictionary<(byte Tribe, byte Sex), string> TribeCache = [];

    /// Every race, in sheet order, for the filter row.
    private static (byte Id, string Name)[]? all;

    public static string Race(byte race, byte sex)
    {
        if (race == 0)
            return "Any race";

        if (RaceCache.TryGetValue((race, sex), out var cached))
            return cached;

        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Race>();
        var row = sheet?.GetRowOrDefault(race);

        var name = row is { } r
            ? (sex == 1 ? r.Feminine.ExtractText() : r.Masculine.ExtractText())
            : $"Race {race}";

        RaceCache[(race, sex)] = name;
        return name;
    }

    public static string Tribe(byte tribe, byte sex)
    {
        if (tribe == 0)
            return string.Empty;

        if (TribeCache.TryGetValue((tribe, sex), out var cached))
            return cached;

        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Tribe>();
        var row = sheet?.GetRowOrDefault(tribe);

        var name = row is { } r
            ? (sex == 1 ? r.Feminine.ExtractText() : r.Masculine.ExtractText())
            : $"Clan {tribe}";

        TribeCache[(tribe, sex)] = name;
        return name;
    }

    /// Every race with a name, for the filter picker.
    public static (byte Id, string Name)[] All
    {
        get
        {
            if (all is not null)
                return all;

            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Race>();
            if (sheet is null)
                return all = [];

            List<(byte, string)> built = [];

            foreach (var row in sheet)
            {
                if (row.RowId is 0 or > byte.MaxValue)
                    continue;

                var name = row.Masculine.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                built.Add(((byte)row.RowId, name));
            }

            return all = [.. built];
        }
    }

    /// "Au Ra Xaela" - or just the race when the clan adds nothing, which is what the sheets give for a race
    /// whose two clans share a name.
    public static string Describe(byte race, byte tribe, byte sex)
    {
        var raceName = Race(race, sex);
        var tribeName = Tribe(tribe, sex);

        return string.IsNullOrEmpty(tribeName) || tribeName == raceName
            ? raceName
            : $"{raceName} {tribeName}";
    }
}
