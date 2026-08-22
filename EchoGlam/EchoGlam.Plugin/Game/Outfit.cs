using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace EchoGlam.Game;

/// One slot of a saved outfit.
public sealed class OutfitSlot
{
    public GlamSlot Slot { get; set; }
    public uint ItemId { get; set; }
    public byte Stain0 { get; set; }
    public byte Stain1 { get; set; }
}

/// A saved look.
public sealed class Outfit
{
    public string Name { get; set; } = string.Empty;

    public List<OutfitSlot> Slots { get; set; } = [];

    /// The appearance, base64'd, or null when the outfit is equipment only.
    public string? Appearance { get; set; }

    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;

    /// An ActionTimeline row to play when this outfit goes on, or 0 for none.
    public ushort AnimationId { get; set; }

    /// What that animation is called, so the row can say so without a sheet lookup and without going blank if
    /// a patch renumbers something.
    public string? AnimationName { get; set; }

    /// Whether the animation runs BEFORE the clothes change rather than after it.
    public bool? AnimateFirst { get; set; }

    /// Whether the clothes wait for the animation.
    public bool ChangeAfterAnimation => AnimateFirst ?? true;

    /// The least time the animation is given before the clothes change, in seconds.
    public float? AnimationHoldSeconds { get; set; }

    /// Long enough for the recognisable part of most casts and emotes.
    public const float DefaultHoldSeconds = 1.5f;

    public float HoldSeconds => AnimationHoldSeconds ?? DefaultHoldSeconds;

    public bool HasAppearance => !string.IsNullOrEmpty(Appearance);

    public bool HasAnimation => AnimationId != 0;

    public Dictionary<GlamSlot, GlamEntry> ToLook() =>
        Slots.ToDictionary(s => s.Slot, s => new GlamEntry(s.ItemId, s.Stain0, s.Stain1));

    public CustomizeSet? ToAppearance() =>
        string.IsNullOrEmpty(Appearance) ? null : CustomizeSet.FromBase64(Appearance);
}

/// Saved outfits, kept in the plugin's own configuration directory.
public sealed class OutfitStore
{
    private readonly string path;
    private List<Outfit> outfits = [];

    public OutfitStore(string configDirectory)
    {
        path = Path.Combine(configDirectory, "outfits.json");
        Load();
    }

    public IReadOnlyList<Outfit> All => outfits;

    public int Count => outfits.Count;

    public bool Exists(string name) =>
        outfits.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

    /// Saves the current look under a name, replacing any outfit already using it.
    public Outfit Save(string name, IReadOnlyDictionary<GlamSlot, GlamEntry> look, CustomizeSet? appearance)
    {
        var outfit = new Outfit
        {
            Name = name.Trim(),
            Slots = [.. look.Select(kv => new OutfitSlot
            {
                Slot = kv.Key,
                ItemId = kv.Value.ItemId,
                Stain0 = kv.Value.Stain0,
                Stain1 = kv.Value.Stain1,
            })],
            Appearance = appearance?.ToBase64(),
        };

        var previous = outfits.FirstOrDefault(o => string.Equals(o.Name, outfit.Name, StringComparison.OrdinalIgnoreCase));

        if (previous is not null)
        {
            outfit.AnimationId = previous.AnimationId;
            outfit.AnimationName = previous.AnimationName;
            outfit.AnimateFirst = previous.AnimateFirst;
        }

        outfits.RemoveAll(o => string.Equals(o.Name, outfit.Name, StringComparison.OrdinalIgnoreCase));
        outfits.Add(outfit);
        Sort();
        Persist();

        return outfit;
    }

    /// Sets the animation played when an outfit goes on.
    public void SetAnimation(string name, ushort animationId, string? animationName)
    {
        var outfit = outfits.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
        if (outfit is null)
            return;

        outfit.AnimationId = animationId;
        outfit.AnimationName = animationId == 0 ? null : animationName;

        if (animationId == 0)
            outfit.AnimateFirst = null;

        Persist();
    }

    public void SetAnimateFirst(string name, bool first)
    {
        var outfit = outfits.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
        if (outfit is null || outfit.AnimateFirst == first)
            return;

        outfit.AnimateFirst = first;
        Persist();
    }

    public void SetAnimationHold(string name, float seconds)
    {
        var outfit = outfits.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
        if (outfit is null)
            return;

        outfit.AnimationHoldSeconds = seconds;
        Persist();
    }

    public void Delete(string name)
    {
        if (outfits.RemoveAll(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
            Persist();
    }

    private void Sort() =>
        outfits = [.. outfits.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)];

    private void Load()
    {
        try
        {
            if (!File.Exists(path))
                return;

            outfits = JsonConvert.DeserializeObject<List<Outfit>>(File.ReadAllText(path)) ?? [];
            Sort();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoGlam] Could not read {path}. Outfits will not be saved this session.");
            readFailed = true;
        }
    }

    /// Set when the file could not be read, which disables writing for the session.
    private bool readFailed;

    public bool ReadOnly => readFailed;

    private void Persist()
    {
        if (readFailed)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(outfits, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoGlam] Could not write {path}");
        }
    }
}
