using EchoSim.Sim;
using LuminaMateria = Lumina.Excel.Sheets.Materia;

namespace EchoSim.Game;

/// One meldable materia: a stat, a grade, and what it's worth.
public readonly record struct MateriaOption(uint MateriaRowId, int Grade, SubStat Stat, int Value, string Name, uint IconId)
{
    /// Compact form for the saved configuration.
    public int Encoded => (int)((MateriaRowId * 100) + (uint)Grade);

    public static (uint RowId, int Grade) Decode(int encoded) => ((uint)(encoded / 100), encoded % 100);
}

/// The materia the sim offers, read from game data.
public static class MateriaCatalog
{
    /// How many grades down from the highest to offer.
    private const int GradesOffered = 2;

    /// BaseParam to substat.
    private static readonly Dictionary<uint, SubStat> ParamToSubStat = new()
    {
        [6] = SubStat.Piety,

        [19] = SubStat.Tenacity,
        [22] = SubStat.DirectHit,
        [27] = SubStat.Crit,
        [44] = SubStat.Determination,
        [45] = SubStat.Speed,
        [46] = SubStat.Speed,
    };

    /// The two speed rows, which are the only ones that depend on the job.
    private static readonly uint[] SpeedParams = [45, 46];

    private static List<MateriaOption>? cache;

    /// The job the cached list was built for, so a job change rebuilds rather than reuses.
    private static uint cachedFor;

    /// Every materia worth melding for the ACTIVE job.
    public static IReadOnlyList<MateriaOption> All()
    {
        if (cache is not null && cachedFor == GearCatalog.ActiveJob)
            return cache;

        Warm();
        return cache is not null && cachedFor == GearCatalog.ActiveJob ? cache : [];
    }

    /// Reads the meld list for the active job in the background.
    public static void Warm()
    {
        var job = GearCatalog.ActiveJob;

        if (buildingFor == job && building is { IsCompleted: false })
            return;

        buildingFor = job;
        building = Task.Run(() => BuildAndPublish(job));
    }

    private static Task? building;
    private static uint buildingFor = uint.MaxValue;

    private static void BuildAndPublish(uint job)
    {
        var built = Build();

        if (buildingFor != job)
            return;

        cache = built;
        cachedFor = job;

        Plugin.Log.Information($"EchoSim: {built.Count} materia options loaded.");
    }

    private static List<MateriaOption> Build()
    {
        var options = new List<MateriaOption>();

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaMateria>();

            foreach (var row in sheet)
            {
                if (!ParamToSubStat.TryGetValue(row.BaseParam.RowId, out var stat))
                    continue;

                if (SpeedParams.Contains(row.BaseParam.RowId) && row.BaseParam.RowId != GearCatalog.SpeedParam)
                    continue;

                var top = -1;
                for (var g = row.Item.Count - 1; g >= 0; g--)
                {
                    if (row.Item[g].RowId != 0 && row.Value[g] > 0)
                    {
                        top = g;
                        break;
                    }
                }

                if (top < 0)
                    continue;

                for (var g = top; g > top - GradesOffered && g >= 0; g--)
                {
                    if (row.Item[g].RowId == 0 || row.Value[g] <= 0)
                        continue;

                    var item = row.Item[g].Value;
                    options.Add(new MateriaOption(
                        row.RowId,
                        g,
                        stat,
                        row.Value[g],
                        item.Name.ExtractText(),
                        item.Icon));
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not read the materia list");
        }

        return [.. options.OrderBy(o => o.Stat).ThenByDescending(o => o.Value)];
    }

    /// One meld option by its encoded id.
    public static MateriaOption? Decode(int encoded)
    {
        var options = All();

        if (index is null || indexFor != options)
        {
            index = options.ToDictionary(o => o.Encoded);
            indexFor = options;
        }

        return index.GetValueOrDefault(encoded);
    }

    /// Rebuilt whenever All() hands back a different list, which is only on a job change.
    private static Dictionary<int, MateriaOption>? index;
    private static IReadOnlyList<MateriaOption>? indexFor;

    /// A relic substat read straight off the equipped weapon, or null if this isn't one.
    public static (SubStat Stat, int Value)? RelicSubstat(ushort materiaId, byte grade)
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaMateria>();
            if (!sheet.TryGetRow(materiaId, out var row))
                return null;

            if (!ParamToSubStat.TryGetValue(row.BaseParam.RowId, out var stat))
                return null;

            if (grade >= row.Value.Count)
                return null;

            var value = row.Value[grade];
            return value > 0 ? (stat, value) : null;
        }
        catch
        {
            return null;
        }
    }

    /// Why a materia id failed to decode, as a sentence for the log.
    public static string WhyUnresolved(ushort materiaId, byte grade)
    {
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaMateria>();

            if (!sheet.TryGetRow(materiaId, out var row))
                return $"No such row in the Materia sheet (it holds {sheet.Count} rows), so this is not a materia id.";

            if (!ParamToSubStat.TryGetValue(row.BaseParam.RowId, out _))
                return $"It maps to BaseParam {row.BaseParam.RowId}, which is not a substat this sim models.";

            if (grade >= row.Value.Count)
                return $"Grade {grade} is past the {row.Value.Count} grades that row defines.";

            return $"Grade {grade} on that row has no item behind it.";
        }
        catch (Exception ex)
        {
            return $"The Materia sheet could not be read ({ex.GetType().Name}).";
        }
    }

    /// Materia id and grade as the client stores them on an equipped item.
    public static MateriaOption? FromEquipped(ushort materiaId, byte grade)
    {
        foreach (var option in All())
        {
            if (option.MateriaRowId == materiaId && option.Grade == grade)
                return option;
        }

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaMateria>();
            if (!sheet.TryGetRow(materiaId, out var row))
                return null;

            if (!ParamToSubStat.TryGetValue(row.BaseParam.RowId, out var stat))
                return null;

            if (grade >= row.Value.Count || row.Item[grade].RowId == 0)
                return null;

            var item = row.Item[grade].Value;
            return new MateriaOption(materiaId, grade, stat, row.Value[grade], item.Name.ExtractText(), item.Icon);
        }
        catch
        {
            return null;
        }
    }
}
