using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;

namespace EchoGlam.Game;

/// Puts an outfit on when the conditions for it are met, and puts it back afterwards.
public sealed class OutfitAutomation
{
    private readonly Plugin plugin;

    /// The outfit this last put on, or null when it is not driving.
    private string? claimed;

    /// What the look was immediately after this applied it.
    private Dictionary<GlamSlot, GlamEntry> claimedLook = [];

    /// Set when a manual change was noticed.
    private bool surrendered;

    /// What was on immediately BEFORE automation first took over, and what it goes back to when nothing
    /// matches and no fallback outfit is named.
    private Dictionary<GlamSlot, GlamEntry>? beforeLook;
    private CustomizeSet? beforeAppearance;

    private bool combatRaw;
    private bool combatGated;
    private DateTime combatChangedUtc = DateTime.UtcNow;

    /// How long combat has to hold before a combat rule fires.
    private static readonly TimeSpan EnterCombatDelay = TimeSpan.FromSeconds(0.5);

    /// How long combat has to be over before a combat rule stops firing.
    private static readonly TimeSpan LeaveCombatDelay = TimeSpan.FromSeconds(6);

    public OutfitAutomation(Plugin plugin) => this.plugin = plugin;

    /// The outfit currently being worn because a rule said so, for the UI to mark.
    public string? Claimed => claimed;

    /// Whether a manual change has parked the rule that is otherwise matching.
    public bool Surrendered => surrendered;

    /// The rule that matched on the last evaluation, or null for none.
    public OutfitRule? ActiveRule { get; private set; }

    /// Called every frame from the plugin's framework update.
    public void Tick()
    {
        var rules = plugin.Rules;

        if (!rules.Enabled)
        {
            ActiveRule = null;

            if (claimed is not null)
                Restore();

            return;
        }

        if (!Plugin.ClientState.IsLoggedIn
            || Plugin.ObjectTable.LocalPlayer is null
            || Plugin.Condition[ConditionFlag.BetweenAreas]
            || Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            return;
        }

        var now = DateTime.UtcNow;
        UpdateCombat(Plugin.Condition[ConditionFlag.InCombat], now);

        var target = Resolve();

        if (target != claimed)
        {
            surrendered = false;

            plugin.Animations.Cancel();

            if (target is null)
                Restore();
            else
                Apply(target);

            return;
        }

        if (claimed is null || surrendered)
            return;

        if (plugin.Animations.Busy)
            return;

        if (!SameLook(plugin.Wardrobe.Current, claimedLook))
            surrendered = true;
    }

    /// The first enabled rule that matches, or null for the fallback.
    private string? Resolve()
    {
        var territory = Plugin.ClientState.TerritoryType;

        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty]
                     || Plugin.Condition[ConditionFlag.BoundByDuty56]
                     || Plugin.Condition[ConditionFlag.BoundByDuty95];

        foreach (var rule in plugin.Rules.Rules)
        {
            if (!rule.Enabled || string.IsNullOrEmpty(rule.Outfit))
                continue;

            var matched = rule.Trigger switch
            {
                OutfitTrigger.Zone => plugin.Zones.Matches(rule.TerritoryId, territory)
                                      && (rule.HouseId == 0 || plugin.Housing.Current?.Id == rule.HouseId),
                OutfitTrigger.Duty => inDuty,
                OutfitTrigger.Combat => combatGated,
                _ => false,
            };

            if (!matched)
                continue;

            if (Find(rule.Outfit) is null)
                continue;

            ActiveRule = rule;
            return rule.Outfit;
        }

        ActiveRule = null;
        return null;
    }

    private void Apply(string name)
    {
        if (Find(name) is not { } outfit)
            return;

        if (beforeLook is null)
        {
            beforeLook = new Dictionary<GlamSlot, GlamEntry>(plugin.Wardrobe.Current);
            beforeAppearance = plugin.Wardrobe.CurrentAppearance;
        }

        claimed = name;

        if (outfit.HasAnimation && outfit.ChangeAfterAnimation)
        {
            plugin.Animations.PlayThen(outfit.AnimationId, outfit.HoldSeconds, () => PutOn(outfit));
            return;
        }

        PutOn(outfit);

        plugin.Animations.Request(outfit.AnimationId);
    }

    private void PutOn(Outfit outfit)
    {
        plugin.Wardrobe.Wear(outfit.ToLook());

        if (outfit.ToAppearance() is { } set)
            plugin.Wardrobe.SetAppearance(set);

        claimedLook = new Dictionary<GlamSlot, GlamEntry>(plugin.Wardrobe.Current);
    }

    /// Back to normal, which is one of three things in this order: the fallback outfit if one is named,
    /// otherwise the look that was on before automation first touched anything, otherwise your own gear
    /// because there was no glamour to go back to.
    private void Restore()
    {
        var departing = claimed is null ? null : Find(claimed);

        claimed = null;
        claimedLook = [];
        surrendered = false;

        plugin.Animations.Cancel();

        if (departing is { HasAnimation: true } leaving)
        {
            if (leaving.ChangeAfterAnimation)
            {
                plugin.Animations.PlayThen(leaving.AnimationId, leaving.HoldSeconds, PutBack);
                return;
            }

            PutBack();
            plugin.Animations.Request(leaving.AnimationId);
            return;
        }

        PutBack();
    }

    /// The actual change back, which is either immediate or waits on an animation.
    private void PutBack()
    {
        var original = plugin.Rules.Original;

        if (!string.IsNullOrWhiteSpace(original) && Find(original) is { } outfit)
        {
            plugin.Wardrobe.Wear(outfit.ToLook());

            if (outfit.ToAppearance() is { } set)
                plugin.Wardrobe.SetAppearance(set);
        }
        else if (beforeLook is { Count: > 0 } previous)
        {
            plugin.Wardrobe.Wear(previous);

            if (beforeAppearance is { } set)
                plugin.Wardrobe.SetAppearance(set);
        }
        else
        {
            plugin.Wardrobe.RevertAll();
        }

        beforeLook = null;
        beforeAppearance = null;
    }

    private Outfit? Find(string name) =>
        plugin.Outfits.All.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

    private void UpdateCombat(bool raw, DateTime now)
    {
        if (raw != combatRaw)
        {
            combatRaw = raw;
            combatChangedUtc = now;
        }

        var held = now - combatChangedUtc;

        if (raw && !combatGated && held >= EnterCombatDelay)
            combatGated = true;
        else if (!raw && combatGated && held >= LeaveCombatDelay)
            combatGated = false;
    }

    private static bool SameLook(
        IReadOnlyDictionary<GlamSlot, GlamEntry> a, IReadOnlyDictionary<GlamSlot, GlamEntry> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var (slot, entry) in a)
        {
            if (!b.TryGetValue(slot, out var other) || !other.Equals(entry))
                return false;
        }

        return true;
    }
}
