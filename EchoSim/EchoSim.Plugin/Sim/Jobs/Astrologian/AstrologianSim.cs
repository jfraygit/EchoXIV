using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Astrologian;

/// Astrologian's state: which half of the draw cycle is next, whether Lord of Crowns is stocked, and when the
/// star ripens.
public sealed class AstrologianState : SimState
{
    /// When Combust III runs out, for netting out an early refresh.
    public double DotExpiresAt { get; set; }

    /// True when the next draw is the astral one - the half that stocks Lord of Crowns.
    public bool NextDrawIsAstral { get; set; } = true;

    /// Whether Lord of Crowns is stocked in the Minor Arcana slot.
    public bool LordReady { get; set; }

    /// When the placed star becomes worth its full 310, or MaxValue when no star is out.
    public double GiantDominanceAt { get; set; } = double.MaxValue;

    /// When the placed star goes off by itself, or MaxValue when no star is out.
    public double StarExpiresAt { get; set; } = double.MaxValue;

    public override SimState Clone()
    {
        var copy = new AstrologianState
        {
            DotExpiresAt = DotExpiresAt,
            NextDrawIsAstral = NextDrawIsAstral,
            LordReady = LordReady,
            GiantDominanceAt = GiantDominanceAt,
            StarExpiresAt = StarExpiresAt,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Astrologian's rules: what is legal, what it is worth, and what a press costs.
public sealed class AstrologianSim : IJobSim
{
    public string JobName => "Astrologian";

    public MainAttribute MainAttribute => MainAttribute.Mind;

    public CombatRole Role => CombatRole.Healer;

    /// 115, off the ClassJob sheet's ModifierMind.
    public int MainStatModifier { get; init; } = 115;

    public int HastePercent => 0;

    /// Zero, like every caster - see BlackMageSim for the measurement.
    public int AutoAttackPotency => 0;

    /// The shared magic damage term.
    public double TraitMultiplier => XivMath.MaimAndMend;

    public IReadOnlyDictionary<string, ActionDef> Actions => AstrologianData.Actions;

    public SimState CreateState()
    {
        var state = new AstrologianState();

        foreach (var (name, action) in AstrologianData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var ast = (AstrologianState)state;

        return action.Name switch
        {
            Ast.Oracle => ast.HasStatus(Ast.Divining),
            Ast.LordOfCrowns => ast.LordReady,

            Ast.AstralDraw => ast.NextDrawIsAstral,
            Ast.UmbralDraw => !ast.NextDrawIsAstral,

            Ast.EarthlyStar => ast.GiantDominanceAt == double.MaxValue,
            Ast.StellarDetonation => ast.Time + 1e-9 >= ast.GiantDominanceAt,

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state) => action.Potency;

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var ast = (AstrologianState)state;

        switch (action.Name)
        {
            case Ast.Combust3:
                var overlap = System.Math.Max(0.0, ast.DotExpiresAt - ast.Time);
                var alreadyPaid = overlap / (AstrologianData.CombustDuration / AstrologianData.CombustTicks);

                if (overlap > 0)
                    ast.Count("dot.clipped", overlap);

                recordHit(Ast.CombustDot,
                    AstrologianData.CombustTickPotency * (AstrologianData.CombustTicks - alreadyPaid),
                    false, false);

                ast.DotExpiresAt = ast.Time + AstrologianData.CombustDuration;
                return;

            case Ast.DivinationAction:
                ast.ApplyStatus(Ast.DivinationStatus, AstrologianData.DivinationDuration,
                    damageMulti: AstrologianData.DivinationBonus);

                ast.ApplyStatus(Ast.Divining, AstrologianData.DiviningDuration);
                return;

            case Ast.Oracle:
                ast.RemoveStatus(Ast.Divining);
                return;

            case Ast.AstralDraw:
                ast.LordReady = true;
                ast.NextDrawIsAstral = false;
                return;

            case Ast.UmbralDraw:
                ast.NextDrawIsAstral = true;
                return;

            case Ast.LordOfCrowns:
                ast.LordReady = false;
                return;

            case Ast.EarthlyStar:
                ast.GiantDominanceAt = ast.Time + AstrologianData.GiantDominanceDelay;
                ast.StarExpiresAt = ast.Time + AstrologianData.EarthlyStarLifetime;
                return;

            case Ast.StellarDetonation:
                ast.GiantDominanceAt = double.MaxValue;
                ast.StarExpiresAt = double.MaxValue;
                return;

            case Ast.Lightspeed:
                return;
        }
    }
}
