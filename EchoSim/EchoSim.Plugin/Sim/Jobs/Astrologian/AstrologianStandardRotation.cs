using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Astrologian;

/// Astrologian's standard single-target rotation.
public sealed class AstrologianStandardRotation(IReadOnlyList<double>? potionTimes = null) : IRotation
{
    public string Name => "7.55 Rotation";

    private readonly List<double> potionTimes = [.. potionTimes ?? []];

    private int openerIndex;
    private int openerWeaveIndex;
    private int potionIndex;

    private readonly record struct PlannedWeave(int AfterGcd, string Action);

    /// The star goes down before the timer, and one hard-cast Fall Malefic lands as the fight starts.
    public static readonly (string Timing, string Action, string Why)[] PrePullSteps =
    [
        ("-4.0s", Ast.EarthlyStar,
            "Placing it deals nothing, so it costs no global and the explosion twenty seconds later "
            + "lands at +16s - inside the Divination that goes up on the third global. The Balance: "
            + "\"-4s because that has become our bread and butter, but it can be anywhere from -4s "
            + "-> ~ -19s.\""),

        ("-2.1s", Ast.FallMalefic,
            "The only pre-pull cast. Lands as the fight starts, so Combust III goes up on the first "
            + "global cooldown rather than a hard cast being spent getting there. The Balance times "
            + "it \"-2.1s prepull due to how long it takes for Malefic to apply to the boss.\""),
    ];

    /// The opener as a player performs it, weaves included, for the Opener tab.
    public static readonly string[] OpenerSteps =
    [
        Ast.EarthlyStar,
        Ast.FallMalefic,

        Buffs.PotionAction,
        Ast.Combust3, Ast.Lightspeed,
        Ast.FallMalefic,
        Ast.FallMalefic, Ast.DivinationAction,
        Ast.FallMalefic, Ast.LordOfCrowns, Ast.UmbralDraw,
        Ast.FallMalefic, Ast.Oracle,
        Ast.FallMalefic, Ast.StellarDetonation,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.Combust3,
        Ast.FallMalefic,
    ];

    /// The opener's global cooldowns: Combust III, nine Fall Malefics, an EARLY Combust III refresh, and one
    /// more Fall Malefic.
    private static readonly string[] Opener =
    [
        Ast.Combust3,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.FallMalefic,
        Ast.Combust3,
        Ast.FallMalefic,
    ];

    /// The opener's weaves, in order, each after the global it was recorded against.
    private static readonly PlannedWeave[] OpenerWeaves =
    [
        new(0, Ast.Lightspeed),
        new(2, Ast.DivinationAction),
        new(3, Ast.LordOfCrowns),
        new(3, Ast.UmbralDraw),
        new(4, Ast.Oracle),
    ];

    /// Whether to run the area filler.
    private bool areaChain;

    public void Reset(SimState state, PlayerStats stats)
    {
        areaChain = state.UseAreaRotation;

        openerIndex = 0;
        openerWeaveIndex = 0;
        potionIndex = 0;

        var ast = (AstrologianState)state;

        ast.GiantDominanceAt = -AstrologianData.PrePullStarLead + AstrologianData.GiantDominanceDelay;
        ast.StarExpiresAt = -AstrologianData.PrePullStarLead + AstrologianData.EarthlyStarLifetime;
        ast.Cooldown(Ast.EarthlyStar).Use(-AstrologianData.PrePullStarLead);

        ast.LordReady = true;
        ast.NextDrawIsAstral = false;
    }

    public ActionDef? NextAction(SimState state, IJobSim job, PlayerStats stats)
    {
        var ast = (AstrologianState)state;

        if (Weave(ast, job, stats) is { } ability)
            return ability;

        if (ast.Time + 1e-9 < ast.NextGcdAt)
            return null;

        if (openerIndex < Opener.Length)
        {
            var planned = AstrologianData.Get(Opener[openerIndex]);
            openerIndex++;

            if (job.CanUse(planned, ast))
                return planned;

            ast.Count("opener.skipped", 1);
            ast.Count($"opener.skipped:{planned.Name}", 1);
        }

        return NextGcd(ast, job);
    }

    /// The global cooldown priority, which is three lines long.
    private ActionDef NextGcd(AstrologianState ast, IJobSim job)
    {
        if (ast.DotExpiresAt <= ast.Time + AstrologianData.CombustRefreshLead
            && First(ast, job, Ast.Combust3) is { } combust)
            return combust;

        if (Ready(ast, job, Ast.Macrocosmos))
            return AstrologianData.Get(Ast.Macrocosmos);

        return AstrologianData.Get(areaChain ? Ast.Gravity2 : Ast.FallMalefic);
    }

    /// Abilities worth weaving, highest first.
    private ActionDef? Weave(AstrologianState ast, IJobSim job, PlayerStats stats)
    {
        if (openerIndex == 0
            && potionIndex < potionTimes.Count && ast.Time + 1e-9 >= potionTimes[potionIndex]
            && ast.Cooldown(Buffs.PotionAction).ChargesAt(ast.Time) > 0)
        {
            potionIndex++;
            return AstrologianData.Get(Buffs.PotionAction);
        }

        if (!WeavePlanner.CanWeave(ast, stats))
            return null;

        if (potionIndex < potionTimes.Count && ast.Time + 1e-9 >= potionTimes[potionIndex]
            && ast.Cooldown(Buffs.PotionAction).ChargesAt(ast.Time) > 0)
        {
            potionIndex++;
            return AstrologianData.Get(Buffs.PotionAction);
        }

        if (openerWeaveIndex < OpenerWeaves.Length)
        {
            var planned = OpenerWeaves[openerWeaveIndex];

            if (openerIndex > planned.AfterGcd + 1)
            {
                openerWeaveIndex++;
            }
            else if (openerIndex > planned.AfterGcd && Ready(ast, job, planned.Action))
            {
                openerWeaveIndex++;
                return AstrologianData.Get(planned.Action);
            }
            else if (openerIndex <= planned.AfterGcd)
            {
                return null;
            }
        }

        if (Ready(ast, job, Ast.DivinationAction))
            return AstrologianData.Get(Ast.DivinationAction);

        if (Ready(ast, job, Ast.Oracle))
            return AstrologianData.Get(Ast.Oracle);

        if (Ready(ast, job, Ast.StellarDetonation)
            && (ast.Time + AstrologianData.StellarDetonationLead >= ast.StarExpiresAt
                || ast.Cooldown(Ast.EarthlyStar).ChargesAt(ast.Time) > 0))
            return AstrologianData.Get(Ast.StellarDetonation);

        if (Ready(ast, job, Ast.EarthlyStar))
            return AstrologianData.Get(Ast.EarthlyStar);

        if (Ready(ast, job, Ast.AstralDraw))
            return AstrologianData.Get(Ast.AstralDraw);

        if (Ready(ast, job, Ast.UmbralDraw))
            return AstrologianData.Get(Ast.UmbralDraw);

        if (Ready(ast, job, Ast.LordOfCrowns) && !HoldLordForDivination(ast))
            return AstrologianData.Get(Ast.LordOfCrowns);

        return null;
    }

    /// Whether Lord of Crowns is worth banking for the next Divination.
    private static bool HoldLordForDivination(AstrologianState ast)
    {
        if (!ast.LordReady)
            return false;

        if (ast.HasStatus(Ast.DivinationStatus))
            return false;

        var untilDivination = ast.Cooldown(Ast.DivinationAction).ReadyAt(ast.Time) - ast.Time;

        var nextDraw = ast.Cooldown(Ast.AstralDraw).ReadyAt(ast.Time);
        var untilAstral = (ast.NextDrawIsAstral ? nextDraw : nextDraw + AstrologianData.DrawRecast) - ast.Time;

        return untilDivination < untilAstral;
    }

    private static ActionDef? First(AstrologianState ast, IJobSim job, params string[] names)
    {
        foreach (var name in names)
        {
            var action = AstrologianData.Get(name);
            if (job.CanUse(action, ast))
                return action;
        }

        return null;
    }

    /// Whether an ability is off recast and legal, asked at the moment it could actually be pressed.
    private static bool Ready(AstrologianState ast, IJobSim job, string name)
    {
        var action = AstrologianData.Get(name);
        var at = Math.Max(ast.Time, ast.AnimationLockUntil);

        return (!ast.HasCooldown(name) || ast.Cooldown(name).ChargesAt(at) > 0)
               && job.CanUse(action, ast);
    }
}
