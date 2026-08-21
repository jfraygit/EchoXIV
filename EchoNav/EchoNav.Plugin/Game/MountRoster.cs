using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace EchoNav.Game;

/// One mount the player actually owns.
public sealed record OwnedMount(uint Id, string Name);

/// The player's mount collection, for the picker in Settings.
public sealed unsafe class MountRoster(IDataManager dataManager)
{
    /// Long enough that the walk is invisible, short enough that a mount unlocked while the game is running
    /// turns up without a reload.
    private const long RefreshIntervalMs = 30000;

    /// How soon to try again while the list is still empty - on login, PlayerState isn't ready for a moment.
    private const long RetryIntervalMs = 2000;

    private long lastRefreshTick;

    /// Alphabetical, which is the only order that makes a list this long findable.
    public IReadOnlyList<OwnedMount> Owned { get; private set; } = [];

    /// The name to show for an id, or null if the player doesn't own it any more - a mount chosen on one
    /// character and missing on another, which is the ordinary case for an alt.
    public string? NameOf(uint id)
    {
        foreach (var mount in Owned)
        {
            if (mount.Id == id)
                return mount.Name;
        }

        return null;
    }

    /// Rebuilds the list if it is due.
    public void Tick()
    {
        var now = Environment.TickCount64;
        var interval = Owned.Count > 0 ? RefreshIntervalMs : RetryIntervalMs;

        if (lastRefreshTick != 0 && now - lastRefreshTick < interval)
            return;

        lastRefreshTick = now;
        Refresh();
    }

    /// Words the game leaves lowercase inside a name - "Kamuy of the Nine Tails", not "Kamuy Of The Nine
    /// Tails".
    private static readonly HashSet<string> LowercaseWords =
        new(StringComparer.OrdinalIgnoreCase) { "of", "the", "a", "an", "and", "in", "on", "at", "to", "for", "de" };

    /// Capitalises a mount's name the way the game's own collection does.
    private static string TitleCase(string name)
    {
        var characters = name.ToCharArray();
        var startOfWord = true;
        var wordStart = 0;

        for (var i = 0; i < characters.Length; i++)
        {
            var c = characters[i];

            if (c is ' ' or '-' or '\'' or '(')
            {
                startOfWord = c != '\'';
                if (startOfWord)
                    wordStart = i + 1;

                continue;
            }

            if (!startOfWord)
                continue;

            startOfWord = false;

            if (wordStart > 0 && IsLowercaseWord(name, wordStart))
                continue;

            characters[i] = char.ToUpperInvariant(c);
        }

        return new string(characters);
    }

    private static bool IsLowercaseWord(string name, int start)
    {
        var end = name.IndexOf(' ', start);
        if (end < 0)
            end = name.Length;

        return LowercaseWords.Contains(name[start..end]);
    }

    private void Refresh()
    {
        try
        {
            var state = PlayerState.Instance();
            if (state == null)
                return;

            var sheet = dataManager.GetExcelSheet<Mount>();
            if (sheet == null)
                return;

            var owned = new List<OwnedMount>();

            foreach (var row in sheet)
            {
                if (row.RowId == 0 || row.Order < 0)
                    continue;

                var name = row.Singular.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!state->IsMountUnlocked(row.RowId))
                    continue;

                owned.Add(new OwnedMount(row.RowId, TitleCase(name)));
            }

            owned.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            Owned = owned;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Could not read the mount collection");
        }
    }
}
