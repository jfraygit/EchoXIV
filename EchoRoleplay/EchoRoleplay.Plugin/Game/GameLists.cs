using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EchoRoleplay.Game;

/// One row of a game sheet, reduced to the two things a picker needs.
public readonly record struct GameChoice(uint Id, string Name);

/// A class or job, which needs both of its names.
public readonly record struct JobChoice(uint Id, string Abbreviation, string Name);

/// The game's own lists, for the profile fields that have a right answer.
public sealed class GameLists
{
    private readonly IDataManager data;

    private GameChoice[]? deities;
    private GameChoice[]? grandCompanies;
    private JobChoice[]? jobs;

    public GameLists(IDataManager data) => this.data = data;

    /// The twelve, in the sheet's own order - which is the order the character creator uses and therefore the
    /// order a player expects to find them in.
    public IReadOnlyList<GameChoice> Deities => deities ??= Build<GuardianDeity>(row => row.Name.ExtractText());

    public IReadOnlyList<GameChoice> GrandCompanies =>
        grandCompanies ??= Build<GrandCompany>(row => row.Name.ExtractText());

    /// Every class and job, base classes included.
    public IReadOnlyList<JobChoice> Jobs
    {
        get
        {
            if (jobs is not null)
                return jobs;

            var sheet = data.GetExcelSheet<ClassJob>();

            jobs = sheet is null
                ? []
                : [.. sheet
                    .Where(row => row.RowId != 0 && row.Abbreviation.ExtractText().Length > 0)
                    .Select(row => new JobChoice(
                        row.RowId, row.Abbreviation.ExtractText(), Capitalise(row.Name.ExtractText())))];

            return jobs;
        }
    }

    /// The name for a stored id, or empty if this client has no such row.
    public static string NameOf(IReadOnlyList<GameChoice> choices, uint id)
    {
        foreach (var choice in choices)
        {
            if (choice.Id == id)
                return choice.Name;
        }

        return string.Empty;
    }

    private GameChoice[] Build<T>(System.Func<T, string> name) where T : struct, Lumina.Excel.IExcelRow<T>
    {
        var sheet = data.GetExcelSheet<T>();
        if (sheet is null)
            return [];

        return [.. sheet
            .Select(row => new GameChoice(row.RowId, name(row)))
            .Where(choice => choice.Name.Length > 0)];
    }

    /// The class sheet stores names lowercase ("white mage"), which reads as a typo beside every other field
    /// on the sheet.
    private static string Capitalise(string text)
    {
        if (text.Length == 0)
            return text;

        var characters = text.ToCharArray();
        var startOfWord = true;

        for (var i = 0; i < characters.Length; i++)
        {
            if (startOfWord)
                characters[i] = char.ToUpperInvariant(characters[i]);

            startOfWord = characters[i] == ' ';
        }

        return new string(characters);
    }
}
