using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Summoner;

/// Summoner's standard single-target rotation.
public sealed class SummonerStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// One hard-cast Ruin III before the pull, and no potion.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-1.5s", Smn.Ruin3,
            "The only hard cast in the opening. Lands as the fight starts so the first Solar Bahamut "
            + "goes off on the pull rather than a global cooldown into it."),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Smn.Ruin3,

        Smn.SummonSolarBahamut, Buffs.PotionAction,
        Smn.UmbralImpulse, Smn.SearingLightAction,
        Smn.UmbralImpulse,
        Smn.UmbralImpulse, Smn.EnergyDrain,
        Smn.UmbralImpulse, Smn.EnkindleSolarBahamut, Smn.Necrotize,
        Smn.UmbralImpulse, Smn.Sunflare, Smn.Necrotize,
        Smn.UmbralImpulse, Smn.SearingFlash,

        Smn.SummonTitan,
        Smn.TopazRite, Smn.MountainBuster,
        Smn.TopazRite, Smn.MountainBuster,
        Smn.TopazRite, Smn.MountainBuster,
        Smn.TopazRite, Smn.MountainBuster,

        Smn.SummonGaruda,
        Smn.Slipstream,
    ];

    /// The opener's global cooldowns: the demi and its six filler casts, then Titan.
    private static readonly string[] Opener =
    [
        Smn.SummonSolarBahamut,
        Smn.UmbralImpulse,
        Smn.UmbralImpulse,
        Smn.UmbralImpulse,
        Smn.UmbralImpulse,
        Smn.UmbralImpulse,
        Smn.UmbralImpulse,
        Smn.SummonTitan,
    ];

    /// The opener's weaves, in order, each after the global it was recorded against.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(1, Smn.SearingLightAction),
        new(3, Smn.EnergyDrain),
        new(4, Smn.EnkindleSolarBahamut),
        new(4, Smn.Necrotize),
        new(5, Smn.Sunflare),
        new(5, Smn.Necrotize),
        new(6, Smn.SearingFlash),
    ];

    /// The potion follows the first global rather than leading the opener.
    private const int OpenerPotionAfterGcd = 0;

    /// How close the next Energy Drain has to be before banked Necrotize are spent regardless.
    private const double NecrotizeClearWindow = 6.0;

    /// Whether to run the area twins.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        areaChain = state.UseAreaRotation;

        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var smn = (SummonerState)state;

        if (Weave(smn, job, stats) is { } ability)
            return ability;

        if (smn.Time + 1e-9 < smn.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = SummonerData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, smn))
                return planned;

            smn.Count("opener.skipped", 1);
            smn.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(smn, job);
    }

    /// The global cooldown priority, which is mostly "finish the window you are in".
    private ActionDef NextGcd(SummonerState smn, IJobSim job)
    {
        if (areaChain && First(smn, job, Smn.UmbralFlare, Smn.AstralFlare, Smn.BrandOfPurgatory) is { } areaTrance)
            return areaTrance;

        if (First(smn, job, Smn.UmbralImpulse, Smn.AstralImpulse, Smn.FountainOfFire) is { } trance)
            return trance;

        if (First(smn, job, Smn.CrimsonCyclone, Smn.CrimsonStrike) is { } ifrit)
            return ifrit;

        if (First(smn, job, Smn.Slipstream) is { } garuda)
            return garuda;

        if (areaChain && First(smn, job, Smn.Ruin4) is { } areaRuin4)
            return areaRuin4;

        if (areaChain
            && First(smn, job, Smn.RubyCatastrophe, Smn.TopazCatastrophe, Smn.EmeraldCatastrophe)
                is { } areaGemshine)
        {
            return areaGemshine;
        }

        if (First(smn, job, Smn.RubyRite, Smn.TopazRite, Smn.EmeraldRite) is { } gemshine)
            return gemshine;

        if (First(smn, job, Smn.SummonSolarBahamut, Smn.SummonBahamut, Smn.SummonPhoenix) is { } demi
            && smn.Cooldown(demi.Name).ChargesAt(Math.Max(smn.Time, smn.AnimationLockUntil)) > 0)
        {
            return demi;
        }

        if (First(smn, job, Smn.SummonIfrit, Smn.SummonTitan, Smn.SummonGaruda) is { } egi)
            return egi;

        if (smn.ActiveDemi is null && First(smn, job, Smn.Ruin4) is { } ruin4)
            return ruin4;

        return SummonerData.Get(Smn.Ruin3);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(SummonerState smn, IJobSim job, PlayerStats stats)
    {
        if (!WeavePlanner.CanWeave(smn, stats))
            return null;

        if (openerIndex > OpenerPotionAfterGcd
            && potionIndex < potionTimes.Count && smn.Time + 1e-9 >= potionTimes[potionIndex]
            && smn.Cooldown(Buffs.PotionAction).ChargesAt(smn.Time) > 0)
        {
            potionIndex++;
            return SummonerData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd
                     && Ready(smn, job, planned.Action))
            {
                openerWeaveIndex++;
                return SummonerData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (Ready(smn, job, Smn.SearingLightAction))
            return SummonerData.Get(Smn.SearingLightAction);

        if (First(smn, job, Smn.EnkindleSolarBahamut, Smn.EnkindleBahamut, Smn.EnkindlePhoenix) is { } enkindle
            && Ready(smn, job, enkindle.Name))
        {
            return enkindle;
        }

        if (First(smn, job, Smn.Sunflare, Smn.Deathflare, Smn.Rekindle) is { } flow)
            return flow;

        if (First(smn, job, Smn.SearingFlash) is { } flash)
            return flash;

        if (First(smn, job, Smn.MountainBuster) is { } buster)
            return buster;

        if (!HoldNecrotizeForBurst(smn) && First(smn, job, Smn.Necrotize) is { } necrotize)
            return necrotize;

        if (smn.Aetherflow == 0 && Ready(smn, job, Smn.EnergyDrain))
            return SummonerData.Get(Smn.EnergyDrain);

        return null;
    }

    /// Whether to bank the Aetherflow stacks rather than spend them now.
    private static bool HoldNecrotizeForBurst(SummonerState smn)
    {
        if (smn.Aetherflow <= 0)
            return false;

        if (smn.HasStatus(Smn.SearingLightStatus))
            return false;

        var untilDrain = smn.Cooldown(Smn.EnergyDrain).ReadyAt(smn.Time) - smn.Time;
        if (untilDrain <= NecrotizeClearWindow)
            return false;

        var untilLight = smn.Cooldown(Smn.SearingLightAction).ReadyAt(smn.Time) - smn.Time;

        return untilLight < untilDrain;
    }

    private static ActionDef? First(SummonerState smn, IJobSim job, params string[] names)
    {
        foreach (var name in names)
        {
            var action = SummonerData.Get(name);
            if (job.CanUse(action, smn))
                return action;
        }

        return null;
    }

    /// Whether an ability is off recast and legal, asked at the moment it could actually be pressed.
    private static bool Ready(SummonerState smn, IJobSim job, string name)
    {
        var action = SummonerData.Get(name);
        var at = Math.Max(smn.Time, smn.AnimationLockUntil);

        return (!smn.HasCooldown(name) || smn.Cooldown(name).ChargesAt(at) > 0)
               && job.CanUse(action, smn);
    }
}
