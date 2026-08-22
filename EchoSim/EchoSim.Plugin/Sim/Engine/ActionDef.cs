namespace EchoSim.Sim.Engine;

/// Status names that aren't job-specific.
public static class Buffs
{
    /// A Gemdraught.
    public const string Potion = "Potion";

    /// The action name used for the potion on the timeline.
    public const string PotionAction = "Gemdraught";
}

public enum ActionKind
{
    /// A weaponskill or spell.
    Gcd,

    /// An ability.
    OffGcd,

    /// A Ninja ninjutsu.
    Ninjutsu,
}

/// Static definition of a single action.
public sealed class ActionDef
{
    public required string Name { get; init; }
    public required ActionKind Kind { get; init; }

    /// Potency of the action itself, uncomboed.
    public int Potency { get; init; }

    /// Potency when used as the correct continuation of ComboFrom.
    public int ComboPotency { get; init; }

    /// Name of the action that must precede this one for ComboPotency to apply.
    public string? ComboFrom { get; init; }

    /// True for the first action of a combo.
    public bool IsComboStarter { get; init; }

    /// True for weaponskills that sit outside the combo system entirely - they neither continue a combo nor
    /// break one.
    public bool PreservesCombo { get; init; }

    /// Recast in seconds for off-GCD abilities.
    public double Cooldown { get; init; }

    public int MaxCharges { get; init; } = 1;

    /// How long the action locks out every other action.
    public double AnimationLock { get; init; } = 0.6;

    /// Number of mudra presses required.
    public int MudraCost { get; init; }

    /// Tooltip recast for a GCD whose base is not the standard 2.5s, BEFORE Skill Speed and haste.
    public double BaseRecast { get; init; }

    /// A recast the game does not scale at all, in seconds.
    public double FixedRecast { get; init; }

    /// Actions that share a recast timer with this one, by name (e.g.
    public string[] SharesCooldownWith { get; init; } = [];

    /// The names a LOG may record this action under, when the button on the hotbar is not what actually
    /// fires.
    public string[] LogAliases { get; init; } = [];

    /// Whether a cast logged under this name is this action, allowing for transformations.
    public bool MatchesLoggedName(string logged)
        => logged == Name || Array.IndexOf(LogAliases, logged) >= 0;

    /// How many enemies this action can hit.
    public int MaxTargets { get; init; } = 1;

    /// The value MaxTargets takes for an action that hits everything in its area.
    public const int AllNearby = 8;

    /// The share of potency lost on every enemy after the first, as a fraction - 0.30 for an action that
    /// deals "30% less for all remaining enemies", 0 for one that hits everything equally.
    public double Falloff { get; init; }

    /// What this action's potency is worth against targets enemies, as a multiple of its single-target
    /// potency.
    public double TargetMultiplier(int targets)
    {
        var hit = Math.Clamp(targets, 1, Math.Max(MaxTargets, 1));
        return 1 + ((hit - 1) * (1 - Falloff));
    }

    /// How much potency this action loses when its positional is not hit.
    public int PositionalBonus { get; init; }

    /// The target count from which positionals are assumed to be missed entirely.
    public const int DefaultPositionalsLostFrom = 3;

    /// A potency with the positional assumption applied for the current target count.
    public double WithoutMissedPositional(double potency, int targets, int lostFrom)
        => targets >= lostFrom ? potency - PositionalBonus : potency;

    /// Tooltip cast time in seconds, BEFORE Spell Speed.
    public double CastTime { get; init; }

    public bool IsGcd => Kind is ActionKind.Gcd or ActionKind.Ninjutsu;

    public override string ToString() => Name;
}
