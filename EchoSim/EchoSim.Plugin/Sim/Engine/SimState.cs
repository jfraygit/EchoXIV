namespace EchoSim.Sim.Engine;

/// Recast tracking for one action (or one group of actions that share a recast).
public sealed class CooldownState
{
    public int MaxCharges { get; }
    public double Recast { get; }
    public int Charges { get; private set; }

    /// When the next charge finishes recharging.
    private double nextChargeAt;

    /// The moment the pool last filled to max, for measuring wasted recharge time.
    private double fullSince;

    /// Total seconds this pool has sat at max charges.
    public double CappedSeconds { get; private set; }

    public CooldownState(double recast, int maxCharges)
    {
        Recast = recast;
        MaxCharges = maxCharges;
        Charges = maxCharges;
        nextChargeAt = 0;
        fullSince = 0;
    }

    private CooldownState(CooldownState other)
    {
        Recast = other.Recast;
        MaxCharges = other.MaxCharges;
        Charges = other.Charges;
        nextChargeAt = other.nextChargeAt;
        fullSince = other.fullSince;
        CappedSeconds = other.CappedSeconds;
    }

    public CooldownState Clone() => new(this);

    private void Refresh(double time)
    {
        while (Charges < MaxCharges && time >= nextChargeAt - 1e-9)
        {
            if (Charges + 1 == MaxCharges)
                fullSince = nextChargeAt;

            Charges++;
            nextChargeAt += Recast;
        }
    }

    /// Earliest time a charge is available.
    public double ReadyAt(double time)
    {
        Refresh(time);
        return Charges > 0 ? time : nextChargeAt;
    }

    /// Charges available at time, after catching up on recharges.
    public int ChargesAt(double time)
    {
        Refresh(time);
        return Charges;
    }

    public void Use(double time)
    {
        Refresh(time);
        if (Charges == MaxCharges)
        {
            CappedSeconds += System.Math.Max(0, time - fullSince);
            nextChargeAt = time + Recast;
        }

        Charges--;
    }

    /// Takes seconds off the time remaining on the recharging charge.
    public void Reduce(double seconds, double time)
    {
        Refresh(time);
        if (Charges >= MaxCharges)
            return;

        nextChargeAt -= seconds;
        Refresh(time);

        if (Charges >= MaxCharges && nextChargeAt < time)
            nextChargeAt = time;
    }

    /// Force the recast to be fully available, for effects that reset a cooldown.
    public void Reset()
    {
        Charges = MaxCharges;
        nextChargeAt = 0;
    }
}

public sealed class StatusState
{
    public double ExpiresAt;
    public int Stacks;

    /// Outgoing damage multiplier this status contributes while active.
    public double DamageMulti = 1.0;

    /// Added critical hit rate, 0..1.
    public double CritBonus;

    /// Added direct hit rate, 0..1.
    public double DirectHitBonus;

    public StatusState Clone() => new()
    {
        ExpiresAt = ExpiresAt,
        Stacks = Stacks,
        DamageMulti = DamageMulti,
        CritBonus = CritBonus,
        DirectHitBonus = DirectHitBonus,
    };
}

/// Everything that changes over the course of a run: the clock, recasts, statuses, and combo position.
public class SimState
{
    public double Time { get; set; }

    /// When the global cooldown next comes up.
    public double NextGcdAt { get; set; }

    /// When the character is free to press anything at all.
    public double AnimationLockUntil { get; set; }

    /// How long the weaponskill currently occupying the global cooldown runs for.
    public double CurrentGcdLength { get; set; }

    /// Seconds to hold the next off-GCD back by, set by a rotation and cleared as soon as it is used.
    public double WeaveDelay { get; set; }

    /// How many enemies the rotation is being played against.
    public int Targets { get; set; } = 1;

    /// The target count from which positionals stop being credited.
    public int PositionalsLostFrom { get; set; } = ActionDef.DefaultPositionalsLostFrom;

    /// Whether the rotation should run its area chain rather than its single-target one.
    public bool UseAreaRotation { get; set; }

    /// Name of the last weaponskill used, for combo continuation.
    public string? LastComboAction { get; set; }

    public double ComboExpiresAt { get; set; }

    private readonly Dictionary<string, CooldownState> cooldowns = [];
    private readonly Dictionary<string, StatusState> statuses = [];

    /// Free-form tallies the job records as it goes - resource overcap, expired procs, and anything else the
    /// rotation linter wants to assert on.
    public Dictionary<string, double> Counters { get; } = [];

    public void Count(string key, double amount = 1)
        => Counters[key] = Counters.GetValueOrDefault(key) + amount;

    /// Every registered recast pool, for diagnostics.
    public IEnumerable<KeyValuePair<string, CooldownState>> AllCooldowns() => cooldowns;

    /// A deep copy, so the optimizer can try an action down a branch without disturbing the real run.
    public virtual SimState Clone()
    {
        var copy = new SimState();
        CopyInto(copy);
        return copy;
    }

    protected void CopyInto(SimState copy)
    {
        copy.Time = Time;

        copy.Targets = Targets;
        copy.PositionalsLostFrom = PositionalsLostFrom;
        copy.UseAreaRotation = UseAreaRotation;

        copy.NextGcdAt = NextGcdAt;
        copy.AnimationLockUntil = AnimationLockUntil;
        copy.CurrentGcdLength = CurrentGcdLength;
        copy.WeaveDelay = WeaveDelay;
        copy.LastComboAction = LastComboAction;
        copy.ComboExpiresAt = ComboExpiresAt;

        foreach (var (key, cd) in cooldowns)
            copy.cooldowns[key] = cd.Clone();

        foreach (var (key, status) in statuses)
            copy.statuses[key] = status.Clone();

        foreach (var (key, value) in Counters)
            copy.Counters[key] = value;
    }

    /// Registers a recast timer.
    public void RegisterCooldown(string key, double recast, int maxCharges = 1)
        => cooldowns[key] = new CooldownState(recast, maxCharges);

    public CooldownState Cooldown(string key) => cooldowns[key];

    public bool HasCooldown(string key) => cooldowns.ContainsKey(key);


    public void ApplyStatus(
        string name,
        double duration,
        int stacks = 1,
        double damageMulti = 1.0,
        double critBonus = 0,
        double directHitBonus = 0)
    {
        statuses[name] = new StatusState
        {
            ExpiresAt = Time + duration,
            Stacks = stacks,
            DamageMulti = damageMulti,
            CritBonus = critBonus,
            DirectHitBonus = directHitBonus,
        };
    }

    /// Adds a stack to a stacking status, refreshing its duration, or applies it fresh at one stack.
    public void AddStack(string name, double duration, int maxStacks, double damageMulti = 1.0)
    {
        if (statuses.TryGetValue(name, out var s) && s.ExpiresAt > Time + 1e-9 && s.Stacks > 0)
        {
            s.Stacks = System.Math.Min(maxStacks, s.Stacks + 1);
            s.ExpiresAt = Time + duration;
            return;
        }

        ApplyStatus(name, duration, 1, damageMulti);
    }

    public bool HasStatus(string name)
        => statuses.TryGetValue(name, out var s) && s.ExpiresAt > Time + 1e-9 && s.Stacks > 0;

    public int StatusStacks(string name)
        => HasStatus(name) ? statuses[name].Stacks : 0;

    public double StatusRemaining(string name)
        => HasStatus(name) ? statuses[name].ExpiresAt - Time : 0;

    /// Spends one stack, removing the status entirely when the last one goes.
    public void ConsumeStack(string name)
    {
        if (!statuses.TryGetValue(name, out var s) || !HasStatus(name))
            return;

        s.Stacks--;
        if (s.Stacks <= 0)
            statuses.Remove(name);
    }

    public void RemoveStatus(string name) => statuses.Remove(name);

    /// Product of every active damage-affecting status.
    public double DamageMultiplier()
    {
        var multi = 1.0;
        foreach (var (_, s) in statuses)
        {
            if (s.ExpiresAt > Time + 1e-9 && s.Stacks > 0)
                multi *= s.DamageMulti;
        }

        return multi;
    }

    /// Added critical hit rate from every active status.
    public double CritBonus()
    {
        var bonus = 0.0;
        foreach (var (_, s) in statuses)
        {
            if (s.ExpiresAt > Time + 1e-9 && s.Stacks > 0)
                bonus += s.CritBonus;
        }

        return bonus;
    }

    /// Added direct hit rate from every active status.
    public double DirectHitBonus()
    {
        var bonus = 0.0;
        foreach (var (_, s) in statuses)
        {
            if (s.ExpiresAt > Time + 1e-9 && s.Stacks > 0)
                bonus += s.DirectHitBonus;
        }

        return bonus;
    }

    /// Names of every status currently up, for timeline display.
    public IEnumerable<string> ActiveStatuses()
        => statuses.Where(kv => kv.Value.ExpiresAt > Time + 1e-9 && kv.Value.Stacks > 0).Select(kv => kv.Key);


    /// True if action would land as a combo continuation right now.
    public bool IsComboReady(ActionDef action)
        => action.ComboFrom != null && LastComboAction == action.ComboFrom && ComboExpiresAt > Time + 1e-9;

    public void AdvanceCombo(ActionDef action)
    {
        LastComboAction = action.Name;
        ComboExpiresAt = Time + 30.0;
    }

    public void BreakCombo()
    {
        LastComboAction = null;
        ComboExpiresAt = 0;
    }
}
