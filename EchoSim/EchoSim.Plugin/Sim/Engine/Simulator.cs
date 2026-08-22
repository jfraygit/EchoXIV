using EchoSim.Sim.Analysis;

namespace EchoSim.Sim.Engine;

/// Callback a job uses to emit an extra damage instance (pet hits, multi-hit abilities).
public delegate void HitRecorder(string source, double potency, bool autoCrit = false, bool autoDirectHit = false);

/// The attribute a job scales its damage off.
public enum MainAttribute
{
    Strength,
    Dexterity,
    Intelligence,
    Mind,
}

public static class MainAttributes
{
    /// Whether a job on this attribute takes Spell Speed rather than Skill Speed.
    public static bool UsesSpellSpeed(this MainAttribute attribute)
        => attribute is MainAttribute.Intelligence or MainAttribute.Mind;
}

/// What a job is for.
public enum CombatRole
{
    Tank,
    Melee,
    PhysicalRanged,
    Caster,
    Healer,
}

public static class CombatRoles
{
    /// The level constants this role's damage formula uses.
    public static LevelStats LevelStats(this CombatRole role)
        => role == CombatRole.Tank ? Sim.LevelStats.Lv100Tank : Sim.LevelStats.Lv100;

    /// Whether Tenacity does anything for this role.
    public static bool UsesTenacity(this CombatRole role) => role == CombatRole.Tank;

    /// Whether Piety does anything for this role.
    public static bool UsesPiety(this CombatRole role) => role == CombatRole.Healer;

    /// Whether this role's gear can carry a substat at all - which is a different question from whether the
    /// stat would help.
    public static bool GearCarries(this CombatRole role, SubStat stat) => stat switch
    {
        SubStat.Tenacity => role == CombatRole.Tank,
        SubStat.Piety => role == CombatRole.Healer,
        SubStat.DirectHit => role is not (CombatRole.Tank or CombatRole.Healer),
        _ => true,
    };

    /// Every substat this role's relic may be allocated, in SubStat order.
    public static IEnumerable<SubStat> RelicSubstats(this CombatRole role)
        => Enum.GetValues<SubStat>().Where(stat => role.GearCarries(stat));

    /// Whether this role has to pay for its damage in MP.
    public static bool SpendsBudgetedMp(this CombatRole role) => role == CombatRole.Healer;
}

/// The job-specific half of a simulation: what actions exist, when they're legal, what they're worth, and
/// what pressing them does to the job gauge.
public interface IJobSim
{
    string JobName { get; }

    /// Main-stat modifier from the ClassJob sheet.
    int MainStatModifier { get; }

    /// Which role this job plays.
    CombatRole Role { get; }

    /// Which attribute this job reads off its gear.
    MainAttribute MainAttribute { get; }

    /// Permanent haste from job traits, as a percentage.
    int HastePercent { get; }

    /// The job's flat damage trait, as a multiplier.
    double TraitMultiplier => 1.0;

    int AutoAttackPotency { get; }

    IReadOnlyDictionary<string, ActionDef> Actions { get; }

    /// Fresh state with all recasts registered and gauge zeroed.
    SimState CreateState();

    /// Whether this action's resource and status requirements are met right now.
    bool CanUse(ActionDef action, SimState state);

    /// Potency after job modifiers - combos, positionals, gauge bonuses, damage buffs on self.
    double EffectivePotency(ActionDef action, SimState state);

    /// Spend resources, apply statuses, and emit any additional hits the action causes.
    void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit);

    /// Whether this cast is guaranteed to land a critical hit.
    bool IsAutoCrit(ActionDef action, SimState state) => false;

    /// Whether this cast is guaranteed to land a direct hit.
    bool IsAutoDirectHit(ActionDef action, SimState state) => false;

    /// Seconds to the next auto-attack swing, which a job may shorten while a buff is up.
    double AutoAttackInterval(SimState state, PlayerStats stats) => stats.AutoAttackInterval;

    /// Haste beyond the permanent job trait that is active right now, as a percentage.
    int TransientHastePercent(SimState state) => 0;

    /// This action's cast time in seconds, before Spell Speed.
    double CastTimeOf(ActionDef action, SimState state) => action.CastTime;
}

/// Decides what to press next.
public interface IRotation
{
    string Name { get; }

    /// Called once before a run, to reset internal counters and seed any pre-pull state.
    void Reset(SimState state, PlayerStats stats);

    ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats);
}

/// Drives a rotation against a target dummy and records everything that happens.
public sealed class Simulator
{
    /// Every damaging action locks you out for this long.
    public const double StandardAnimationLock = 0.6;

    /// The pause after a cast bar finishes before anything else can be pressed - the caster tax.
    public const double CasterTax = 0.1;

    /// Guards against a priority list that can never act, which would otherwise spin forever.
    private const int MaxIterations = 200_000;

    /// How many times stepping past a downtime window may cascade before it gives up.
    private const int MaximumDowntimeWindows = 64;

    public SimResult Run(
        IJobSim job,
        IRotation rotation,
        PlayerStats stats,
        double duration,
        IReadOnlyList<ScheduledBuff>? partyBuffs = null,
        int targets = 1,
        int? positionalsLostFrom = null,
        bool useAreaRotation = false,
        IReadOnlyList<DowntimeWindow>? downtime = null)
    {
        var state = job.CreateState();
        state.Targets = Math.Max(1, targets);
        state.UseAreaRotation = useAreaRotation;

        if (positionalsLostFrom is { } lostFrom)
            state.PositionalsLostFrom = lostFrom;

        rotation.Reset(state, stats);

        var result = new SimResult { JobName = job.JobName, Duration = duration, Stats = stats };
        RunLoop(job, rotation, stats, state, result, autoAttacksFrom: 0, endTime: duration, recordTimeline: true, partyBuffs: partyBuffs, downtime: downtime);
        result.FinalState = state;
        return result;
    }

    /// The better of a job's two rotations at this target count, measured rather than predicted.
    public SimResult RunBest(
        IJobSim job,
        Func<IRotation> rotation,
        PlayerStats stats,
        double duration,
        IReadOnlyList<ScheduledBuff>? partyBuffs = null,
        int targets = 1,
        int? positionalsLostFrom = null)
    {
        var single = Run(job, rotation(), stats, duration, partyBuffs, targets, positionalsLostFrom);

        if (targets <= 1)
            return single;

        var area = Run(job, rotation(), stats, duration, partyBuffs, targets, positionalsLostFrom,
            useAreaRotation: true);

        return area.Dps > single.Dps ? area : single;
    }

    /// Plays rotation forward from an existing state for a short window and returns the damage it produced.
    public static double Rollout(IJobSim job, IRotation rotation, PlayerStats stats, SimState state, double horizon)
    {
        var end = state.Time + horizon;
        var result = new SimResult { JobName = job.JobName, Duration = horizon, Stats = stats };
        RunLoop(job, rotation, stats, state, result, autoAttacksFrom: state.Time, endTime: end, recordTimeline: false);
        return result.TotalDamage;
    }

    private static void RunLoop(
        IJobSim job,
        IRotation rotation,
        PlayerStats stats,
        SimState state,
        SimResult result,
        double autoAttacksFrom,
        double endTime,
        bool recordTimeline,
        IReadOnlyList<ScheduledBuff>? partyBuffs = null,
        IReadOnlyList<DowntimeWindow>? downtime = null)
    {
        double PastDowntime(double at)
        {
            if (downtime is null)
                return at;

            for (var pass = 0; pass < MaximumDowntimeWindows; pass++)
            {
                var moved = false;

                foreach (var window in downtime)
                {
                    if (at >= window.Start && at < window.End)
                    {
                        at = window.End;
                        moved = true;
                    }
                }

                if (!moved)
                    break;
            }

            return at;
        }

        var nextAutoAt = stats.AutoAttackPotency > 0 ? autoAttacksFrom : double.PositiveInfinity;
        var iterations = 0;
        var nextBuff = 0;

        void ApplyDueBuffs(double upTo)
        {
            while (partyBuffs is not null && nextBuff < partyBuffs.Count && partyBuffs[nextBuff].Time <= upTo)
            {
                var scheduled = partyBuffs[nextBuff++];
                var saved = state.Time;

                state.Time = scheduled.Time;
                state.ApplyStatus(
                    scheduled.Buff.Name,
                    scheduled.Buff.Duration,
                    damageMulti: scheduled.Buff.DamageMulti,
                    critBonus: scheduled.Buff.CritBonus,
                    directHitBonus: scheduled.Buff.DirectHitBonus);

                state.Time = saved;
            }
        }

        while (state.Time < endTime && iterations++ < MaxIterations)
        {
            ApplyDueBuffs(state.Time);
            var action = rotation.NextAction(state, job, stats);

            if (action is null)
            {
                var skipTo = System.Math.Max(state.NextGcdAt, state.AnimationLockUntil);
                state.Time = skipTo > state.Time ? skipTo : state.Time + 0.01;
                continue;
            }

            var execTime = PastDowntime(EarliestUsable(action, state));

            if (!action.IsGcd && state.WeaveDelay > 0)
            {
                var latest = state.NextGcdAt - action.AnimationLock;
                if (latest > execTime)
                    execTime = System.Math.Min(execTime + state.WeaveDelay, latest);
            }

            state.WeaveDelay = 0;

            if (execTime >= endTime)
                break;

            while (nextAutoAt <= execTime && nextAutoAt < endTime)
            {
                state.Time = nextAutoAt;

                if (PastDowntime(nextAutoAt) == nextAutoAt)
                    RecordHit(result, state, stats, "Auto-attack", stats.AutoAttackPotency, isAutoAttack: true);

                nextAutoAt += job.AutoAttackInterval(state, stats);
            }

            var gcdDelay = action.IsGcd ? System.Math.Max(0, execTime - state.NextGcdAt) : 0;

            state.Time = execTime;

            ApplyDueBuffs(execTime);

            if (!job.CanUse(action, state))
            {
                result.Warnings.Add($"{execTime:F2}s: {action.Name} requested but not usable - skipped.");
                state.Time += 0.01;
                continue;
            }

            var haste = job.TransientHastePercent(state);
            var castTime = CastLength(action, job, state, stats, haste);
            ConsumeResources(action, state, stats, result, haste, castTime);

            if (castTime > 0)
            {
                state.Time = execTime + castTime;
                ApplyDueBuffs(state.Time);
            }

            var potency = action.WithoutMissedPositional(
                              job.EffectivePotency(action, state), state.Targets, state.PositionalsLostFrom)
                          * action.TargetMultiplier(state.Targets);
            var damage = potency > 0
                ? RecordHit(result, state, stats, action.Name, potency,
                    autoCrit: job.IsAutoCrit(action, state),
                    autoDirectHit: job.IsAutoDirectHit(action, state))
                : 0;

            if (recordTimeline)
                result.Timeline.Add(new TimelineEntry(execTime, action.Name, action.Kind, damage, gcdDelay));

            if (action.Name == Buffs.PotionAction)
                state.ApplyStatus(Buffs.Potion, PlayerStats.PotionDuration);

            job.OnExecuted(action, state, (source, p, autoCrit, autoDh) =>
                RecordHit(result, state, stats, source, p, autoCrit: autoCrit, autoDirectHit: autoDh));
        }

        while (nextAutoAt < endTime)
        {
            state.Time = nextAutoAt;
            RecordHit(result, state, stats, "Auto-attack", stats.AutoAttackPotency, isAutoAttack: true);
            nextAutoAt += job.AutoAttackInterval(state, stats);
        }
    }

    /// Earliest moment an action can execute, accounting for the GCD, animation lock, its own recast, and -
    /// for ninjutsu - the mudra presses that have to happen first.
    public static double EarliestUsable(ActionDef action, SimState state)
    {
        var time = state.Time;
        var earliest = System.Math.Max(time, state.AnimationLockUntil);

        if (action.IsGcd)
            earliest = System.Math.Max(earliest, state.NextGcdAt);

        var leadIn = action.MudraCost * 0.5;
        if (leadIn > 0)
            earliest = System.Math.Max(earliest, state.AnimationLockUntil + leadIn);

        foreach (var key in CooldownKeys(action))
        {
            if (state.HasCooldown(key))
                earliest = System.Math.Max(earliest, state.Cooldown(key).ReadyAt(time) + leadIn);
        }

        return earliest;
    }

    /// Every recast pool this action draws from: its own timer plus any it shares.
    private static IEnumerable<string> CooldownKeys(ActionDef action)
    {
        yield return action.Name;
        foreach (var shared in action.SharesCooldownWith)
            yield return shared;
    }

    /// How long this GCD occupies the global cooldown, in the order the three cases have to be checked: a
    /// recast the game freezes, then a non-standard tooltip base that scales, then the player's ordinary GCD.
    public static double GcdLength(ActionDef action, PlayerStats stats, int extraHastePercent = 0)
        => action.FixedRecast > 0 ? action.FixedRecast
            : extraHastePercent > 0 || action.BaseRecast > 0
                ? stats.RecastFor(action.BaseRecast > 0 ? action.BaseRecast : StandardRecast, extraHastePercent)
                : stats.Gcd;

    /// The tooltip recast every ordinary weaponskill carries, before Skill Speed and haste.
    public const double StandardRecast = 2.5;

    /// How long this action's cast bar actually runs, after Spell Speed and any transient haste.
    public static double CastLength(
        ActionDef action,
        IJobSim job,
        SimState state,
        PlayerStats stats,
        int extraHastePercent)
    {
        var tooltip = job.CastTimeOf(action, state);

        if (tooltip <= 0)
            return 0;

        return action.FixedRecast > 0 ? tooltip : stats.RecastFor(tooltip, extraHastePercent);
    }

    private static void ConsumeResources(
        ActionDef action,
        SimState state,
        PlayerStats stats,
        SimResult result,
        int extraHastePercent,
        double castTime = 0)
    {
        foreach (var key in CooldownKeys(action))
        {
            if (state.HasCooldown(key))
                state.Cooldown(key).Use(state.Time);
        }

        state.AnimationLockUntil = castTime > 0
            ? state.Time + castTime + CasterTax
            : state.Time + action.AnimationLock;

        if (action.IsGcd)
        {
            var length = GcdLength(action, stats, extraHastePercent);
            state.NextGcdAt = state.Time + length;
            state.CurrentGcdLength = length;
            result.GcdTimeConsumed += length;
        }
    }

    private static double RecordHit(
        SimResult result,
        SimState state,
        PlayerStats stats,
        string source,
        double potency,
        bool isAutoAttack = false,
        bool autoCrit = false,
        bool autoDirectHit = false)
    {
        var buffMulti = state.DamageMultiplier();

        var damage = stats.ExpectedDamage(
            potency,
            buffMulti,
            bonusCritChance: state.CritBonus(),
            bonusDirectHitChance: state.DirectHitBonus(),
            isAutoAttack: isAutoAttack,
            autoCrit: autoCrit,
            autoDirectHit: autoDirectHit,
            potted: state.HasStatus(Buffs.Potion));

        result.Damage.Add(new DamageEvent(
            state.Time, source, potency, buffMulti, damage, isAutoAttack, autoCrit, autoDirectHit));

        return damage;
    }
}
