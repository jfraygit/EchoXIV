using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Ninja;

/// Ninja's standard single-target rotation.
public sealed class NinjaStandardRotation(
    bool includeOpener = true,
    IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation (rebuilt)";

    /// A ceiling on abilities per GCD, on top of what WeavePlanner allows.
    private const int MaxWeavesPerGcd = 2;

    /// How far before Kunai's Bane the window's Suiton has to be cast.
    private const double SuitonLead = 18.0;

    /// The pre-pull, which for Ninja is three mudra presses and nothing else.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-6.5s", "Ten, Chi, Jin",
            "The three mudra presses, held rather than cast. They deal no damage and do not engage "
            + "the boss, so they are the only part of the opener that happens before the pull. "
            + "Suiton itself is the pull - it is an attack, and casting it starts the fight."),
    ];

    /// The opener as a player performs it, for the Opener tab.
    public static IReadOnlyList<string> OpenerSteps =>
    [
        Nin.Suiton,
        Nin.KassatsuAction,
        Buffs.PotionAction,
        Nin.SpinningEdge,
        Nin.GustSlash,
        Nin.DokumoriAction,
        Nin.BunshinAction,
        Nin.PhantomKamaitachi,
        Nin.ArmorCrush,
        Nin.KunaisBaneAction,
        Nin.DreamWithinADream,
        Nin.HyoshoRanryu,
        Nin.Raiton,
        Nin.TenChiJinAction,
        Nin.TcjFuma,
        Nin.TcjRaiton,
        Nin.TcjSuiton,
        Nin.MeisuiAction,
        Nin.FleetingRaiju,
        Nin.ZeshoMeppo,
        Nin.TenriJindo,
        Nin.FleetingRaiju,
        Nin.Bhavacakra,
    ];

    /// The opener's GCDs, in order.
    private static readonly string[] Opener =
    [
        Nin.Suiton,
        Nin.SpinningEdge,
        Nin.GustSlash,

        Nin.PhantomKamaitachi,

        Nin.ArmorCrush,

        Nin.Raiton,

        Nin.FleetingRaiju,
        Nin.FleetingRaiju,
        Nin.Raiton,
        Nin.FleetingRaiju,
    ];

    /// An opener ability and the opener global cooldown it follows, counted from zero.
    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The opener's burst cooldowns, in the order both guides print them.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(0, Nin.KassatsuAction),
        new(2, Nin.DokumoriAction),
        new(2, Nin.BunshinAction),
        new(4, Nin.KunaisBaneAction),
        new(4, Nin.DreamWithinADream),
        new(5, Nin.TenChiJinAction),
    ];


    private int openerIndex = includeOpener ? 0 : int.MaxValue;
    private int openerWeaveIndex;
    private int potionIndex;

    /// Abilities woven since the last weaponskill, against the two-per-GCD ceiling.
    private int weavesSinceGcd;

    /// The GCD boundary the weave counter was last reset against.
    private double weaveWindowMark = double.NegativeInfinity;

    /// Whether the filler combo should be the area one, decided once from the target count.
    private bool areaCombo;

    public void Reset(SimState state, PlayerStats stats)
    {
        openerIndex = includeOpener ? 0 : int.MaxValue;
        openerWeaveIndex = includeOpener ? 0 : int.MaxValue;
        potionIndex = 0;
        weavesSinceGcd = 0;
        weaveWindowMark = double.NegativeInfinity;

        areaCombo = state.UseAreaRotation;

        state.AnimationLockUntil = -1.5;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var nin = (NinjaState)state;

        if (nin.NextGcdAt > weaveWindowMark + 1e-9)
        {
            weaveWindowMark = nin.NextGcdAt;
            weavesSinceGcd = 0;
        }

        if (nin.HasStatus(Nin.TenChiJin))
        {
            return nin.TcjStep switch
            {
                0 => NinjaData.Get(Nin.TcjFuma),
                1 => NinjaData.Get(Nin.TcjRaiton),
                _ => NinjaData.Get(Nin.TcjSuiton),
            };
        }

        if (NextWeave(nin, job, stats) is { } ability)
        {
            weavesSinceGcd++;
            if (ability.Name == Buffs.PotionAction)
                potionIndex++;

            return ability;
        }

        if (nin.Time + 1e-9 < nin.NextGcdAt)
            return null;

        if (nin.HasStatus(Nin.Kassatsu) && nin.HasStatus(Nin.KunaisBane))
            return KassatsuNinjutsu(nin, job);

        if (openerIndex < Opener.Length)
        {
            var planned = NinjaData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, nin))
                return planned;

            nin.Count("opener.skipped", 1);
            nin.Count($"opener.skipped:{planned.Name}@{openerIndex}", 1);
        }

        return NextGcd(nin, job);
    }


    /// Which of a paired Ninki spender to weave, by name, at the current target count.
    private static string Spender(NinjaState s, IJobSim job, string single, string area)
        => TargetChoice.Best(job, s, NinjaData.Get(single), NinjaData.Get(area)).Name;

    private ActionDef? NextWeave(NinjaState s, IJobSim job, PlayerStats stats)
    {
        if (weavesSinceGcd >= MaxWeavesPerGcd)
            return null;

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd
                     && s.Cooldown(planned.Action).ChargesAt(System.Math.Max(s.Time, s.AnimationLockUntil)) > 0
                     && job.CanUse(NinjaData.Get(planned.Action), s))
            {
                openerWeaveIndex++;
                return NinjaData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return openerIndex > 0 && PotionDue(s) ? NinjaData.Get(Buffs.PotionAction) : null;
            }
        }

        var inBurst = s.HasStatus(Nin.KunaisBane);
        var kunaiIn = ReadyIn(s, Nin.KunaisBaneAction);
        var kunaiSoon = kunaiIn <= 4.0;

        var candidates = new (string Action, bool Want)[]
        {
            (Buffs.PotionAction, PotionDue(s)),

            (Nin.DokumoriAction, true),

            (Nin.KassatsuAction, !s.HasStatus(Nin.Kassatsu) && kunaiIn <= 6.0),

            (Nin.KunaisBaneAction, s.HasStatus(Nin.ShadowWalker)),

            (Nin.BunshinAction, s.Ninki >= NinjaData.SpenderCost),

            (Nin.TenChiJinAction, inBurst),
            (Nin.DreamWithinADream, inBurst || !kunaiSoon),

            (Nin.MeisuiAction, s.HasStatus(Nin.ShadowWalker) && inBurst),

            (Nin.TenriJindo, s.HasStatus(Nin.TenriJindoReady)),

            (Spender(s, job, Nin.ZeshoMeppo, Nin.DeathfrogMedium),
                s.HasStatus(Nin.Higi) && (inBurst || s.StatusRemaining(Nin.Higi) < 6)),

            (Spender(s, job, Nin.Bhavacakra, Nin.HellfrogMedium),
                inBurst || s.Ninki >= 90 || kunaiIn > 12),
        };

        foreach (var (name, want) in candidates)
        {
            if (!want)
                continue;

            var action = NinjaData.Get(name);
            if (job.CanUse(action, s) && WeaveAllowed(s, action, stats))
                return action;
        }

        return null;
    }

    /// Whether this ability is worth weaving now, allowing for a bounded clip.
    private static bool WeaveAllowed(SimState s, ActionDef action, PlayerStats stats)
    {
        var earliest = System.Math.Max(s.Time, s.AnimationLockUntil);

        if (s.HasCooldown(action.Name) && s.Cooldown(action.Name).ReadyAt(earliest) > earliest + 1e-9)
            return false;

        return WeavePlanner.CanWeave(s, stats, action.AnimationLock);
    }

    /// True once the clock has reached the next planned potion time.
    private bool PotionDue(SimState s)
        => potionTimes is not null
           && potionIndex < potionTimes.Count
           && s.Time >= potionTimes[potionIndex] - 0.5;


    /// What Kassatsu is spent on: Hyosho Ranryu, or Goka Mekkyaku once there is enough to hit.
    private static ActionDef KassatsuNinjutsu(NinjaState s, IJobSim job)
        => TargetChoice.Best(job, s, NinjaData.Get(Nin.HyoshoRanryu), NinjaData.Get(Nin.GokaMekkyaku));

    private ActionDef NextGcd(NinjaState s, IJobSim job)
    {
        var inBurst = s.HasStatus(Nin.KunaisBane);
        var kunaiIn = ReadyIn(s, Nin.KunaisBaneAction);
        var mudraCharges = s.Cooldown(Nin.MudraCharges).ChargesAt(System.Math.Max(s.Time, s.AnimationLockUntil));

        if (s.HasStatus(Nin.Kassatsu) && (inBurst || s.StatusRemaining(Nin.Kassatsu) < 4))
            return KassatsuNinjutsu(s, job);

        if (s.HasStatus(Nin.PhantomReady))
            return NinjaData.Get(Nin.PhantomKamaitachi);

        var bankForBurst = !inBurst && kunaiIn <= 12.0;

        if (s.StatusStacks(Nin.RaijuReady) > 0
            && (inBurst
                || s.StatusStacks(Nin.RaijuReady) >= NinjaData.MaxRaijuStacks
                || s.StatusRemaining(Nin.RaijuReady) < 8
                || !bankForBurst))
        {
            return NinjaData.Get(Nin.FleetingRaiju);
        }

        if (!s.HasStatus(Nin.ShadowWalker) && kunaiIn <= SuitonLead && mudraCharges > 0)
            return NinjaData.Get(Nin.Suiton);

        if (mudraCharges >= NinjaData.MudraMaxCharges || (mudraCharges > 0 && (inBurst || !bankForBurst)))
            return TargetChoice.Best(job, s, NinjaData.Get(Nin.Raiton), NinjaData.Get(Nin.Katon));

        var comboAlive = s.ComboExpiresAt > s.Time;

        if (areaCombo)
        {
            return comboAlive && s.LastComboAction == Nin.DeathBlossom
                ? NinjaData.Get(Nin.HakkeMujinsatsu)
                : NinjaData.Get(Nin.DeathBlossom);
        }

        if (comboAlive && s.LastComboAction == Nin.GustSlash)
        {
            return s.Kazematoi == 0
                ? NinjaData.Get(Nin.ArmorCrush)
                : NinjaData.Get(Nin.AeolianEdge);
        }

        if (comboAlive && s.LastComboAction == Nin.SpinningEdge)
            return NinjaData.Get(Nin.GustSlash);

        return NinjaData.Get(Nin.SpinningEdge);
    }

    private static double ReadyIn(SimState s, string action)
        => s.HasCooldown(action) ? System.Math.Max(0, s.Cooldown(action).ReadyAt(s.Time) - s.Time) : 0;
}
