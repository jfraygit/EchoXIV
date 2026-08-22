using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace EchoGlam.Game;

/// What makes a rule fire.
public enum OutfitTrigger
{
    /// One named place - an open-world map or a particular duty, picked by name.
    Zone = 0,

    /// Any instanced duty at all.
    Duty = 1,

    /// In combat, anywhere.
    Combat = 2,
}

/// One condition under which an outfit puts itself on.
public sealed class OutfitRule
{
    public string Outfit { get; set; } = string.Empty;

    public OutfitTrigger Trigger { get; set; }

    /// Which map, for OutfitTrigger.Zone.
    public uint TerritoryId { get; set; }

    /// One particular house, or 0 for anywhere on the map named above.
    public ulong HouseId { get; set; }

    /// The address as it read when the rule was made, so the rule can describe itself without being inside
    /// the house.
    public string? HouseAddress { get; set; }

    /// Off keeps the rule and its place in the order without letting it fire.
    public bool Enabled { get; set; } = true;
}

/// The automation settings: the ordered rules, and where to go back to.
public sealed class OutfitRuleBook
{
    /// The master switch.
    public bool Enabled { get; set; }

    /// ORDER IS PRIORITY, first match wins.
    public List<OutfitRule> Rules { get; set; } = [];

    /// The outfit to fall back to when nothing matches, or null for your real gear.
    public string? Original { get; set; }
}

/// The rule book, in its own file beside the outfits.
public sealed class OutfitRuleStore
{
    private readonly string path;
    private OutfitRuleBook book = new();
    private bool readFailed;

    public OutfitRuleStore(string configDirectory)
    {
        path = Path.Combine(configDirectory, "outfit-rules.json");
        Load();
    }

    public bool ReadOnly => readFailed;

    public bool Enabled
    {
        get => book.Enabled;
        set
        {
            if (book.Enabled == value)
                return;

            book.Enabled = value;
            Persist();
        }
    }

    public string? Original
    {
        get => book.Original;
        set
        {
            book.Original = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Persist();
        }
    }

    public IReadOnlyList<OutfitRule> Rules => book.Rules;

    /// A rule's place in the priority order, which is also what makes its row's ImGui ids unique.
    public int IndexOf(OutfitRule rule) => book.Rules.IndexOf(rule);

    public IEnumerable<OutfitRule> For(string outfit) =>
        book.Rules.Where(r => string.Equals(r.Outfit, outfit, StringComparison.OrdinalIgnoreCase));

    public int CountFor(string outfit) => For(outfit).Count();

    public void Add(OutfitRule rule)
    {
        book.Rules.Add(rule);
        Persist();
    }

    public void Remove(OutfitRule rule)
    {
        if (book.Rules.Remove(rule))
            Persist();
    }

    public void Toggle(OutfitRule rule, bool enabled)
    {
        if (rule.Enabled == enabled)
            return;

        rule.Enabled = enabled;
        Persist();
    }

    /// Writes the file after a rule's own fields were changed in place.
    public void Save() => Persist();

    /// Moves a rule one place up or down the priority order.
    public void Move(OutfitRule rule, int delta)
    {
        var from = book.Rules.IndexOf(rule);
        if (from < 0)
            return;

        var to = Math.Clamp(from + delta, 0, book.Rules.Count - 1);
        if (to == from)
            return;

        book.Rules.RemoveAt(from);
        book.Rules.Insert(to, rule);
        Persist();
    }

    /// Drops every rule belonging to an outfit, and clears it as the fallback.
    public void Forget(string outfit)
    {
        var removed = book.Rules.RemoveAll(r => string.Equals(r.Outfit, outfit, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(book.Original, outfit, StringComparison.OrdinalIgnoreCase))
        {
            book.Original = null;
            removed++;
        }

        if (removed > 0)
            Persist();
    }

    /// Renames every reference to an outfit.
    public void Rename(string from, string to)
    {
        var touched = false;

        foreach (var rule in book.Rules.Where(r => string.Equals(r.Outfit, from, StringComparison.OrdinalIgnoreCase)))
        {
            rule.Outfit = to;
            touched = true;
        }

        if (string.Equals(book.Original, from, StringComparison.OrdinalIgnoreCase))
        {
            book.Original = to;
            touched = true;
        }

        if (touched)
            Persist();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path))
                return;

            book = JsonConvert.DeserializeObject<OutfitRuleBook>(File.ReadAllText(path)) ?? new OutfitRuleBook();
            book.Rules ??= [];
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoGlam] Could not read {path}. Outfit rules will not be saved this session.");
            readFailed = true;
        }
    }

    private void Persist()
    {
        if (readFailed)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(book, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoGlam] Could not write {path}");
        }
    }
}
