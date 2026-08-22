using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EchoRoleplay.Game;

/// One piece of the game's music a profile can claim as its theme.
public readonly record struct MusicTrack(uint Id, string Name, string Search);

/// Every track a theme song can be: the game's own music, and nothing else.
public sealed class MusicCatalogue
{
    private readonly IDataManager data;

    private MusicTrack[]? all;
    private Dictionary<uint, MusicTrack>? byId;

    public MusicCatalogue(IDataManager data) => this.data = data;

    public IReadOnlyList<MusicTrack> All => all ?? [];

    public bool Built => all is not null;

    /// Builds the list, once, on first use.
    public void EnsureBuilt()
    {
        if (all is not null)
            return;

        var names = new Dictionary<uint, string>();

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Claim(uint bgmId, string rawName, string? kind = null)
        {
            if (bgmId == 0 || string.IsNullOrWhiteSpace(rawName))
                return;

            var name = Title(rawName);

            if (kind is not null)
                name = $"{name} ({kind})";

            if (names.ContainsKey(bgmId) || !taken.Add(name))
                return;

            names[bgmId] = name;
        }

        if (data.GetExcelSheet<ContentFinderCondition>() is { } duties)
        {
            foreach (var duty in duties)
                Claim(duty.TerritoryType.ValueNullable?.BGM.RowId ?? 0, duty.Name.ExtractText());
        }

        if (data.GetExcelSheet<TerritoryType>() is { } territories)
        {
            foreach (var territory in territories)
                Claim(territory.BGM.RowId, territory.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty);
        }

        if (data.GetExcelSheet<Fate>() is { } fates)
        {
            foreach (var fate in fates)
                Claim(fate.Music.RowId, fate.Name.ExtractText(), "FATE");
        }


        var tracks = new List<MusicTrack>(names.Count);

        foreach (var (id, name) in names)
        {
            if (data.GetExcelSheet<BGM>() is not { } bgm
                || !bgm.TryGetRow(id, out var row))
            {
                continue;
            }

            var file = row.File.ExtractText();

            if (file.Length == 0 || file.Contains("BGM_Null", StringComparison.OrdinalIgnoreCase))
                continue;

            tracks.Add(new MusicTrack(id, name, name.ToLowerInvariant()));
        }

        all = [.. tracks.OrderBy(t => t.Search, StringComparer.Ordinal)];
        byId = all.ToDictionary(t => t.Id);
    }

    /// Capitalises the first letter, and nothing else.
    private static string Title(string text)
    {
        var trimmed = text.Trim();

        return trimmed.Length == 0 ? trimmed : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    public MusicTrack? ById(uint id)
    {
        if (id == 0)
            return null;

        EnsureBuilt();

        return byId is not null && byId.TryGetValue(id, out var track) ? track : null;
    }

    /// Whether this row is something the sound system can actually play.
    public bool CanPlay(uint id)
    {
        if (id == 0 || id > ushort.MaxValue)
            return false;

        if (data.GetExcelSheet<BGM>() is not { } bgm || !bgm.TryGetRow(id, out var row))
            return false;

        var file = row.File.ExtractText();

        return file.Length > 0 && !file.Contains("BGM_Null", StringComparison.OrdinalIgnoreCase);
    }

    /// What this track is called.
    public string NameOf(uint id)
    {
        if (ById(id) is { } track)
            return track.Name;

        return CanPlay(id) ? $"Track {id}" : string.Empty;
    }

    /// Tracks whose name contains every word typed.
    public IEnumerable<MusicTrack> Search(string query)
    {
        EnsureBuilt();

        var tracks = All;

        if (string.IsNullOrWhiteSpace(query))
            return tracks;

        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tracks.Where(t => words.All(w => t.Search.Contains(w, StringComparison.Ordinal)));
    }
}
