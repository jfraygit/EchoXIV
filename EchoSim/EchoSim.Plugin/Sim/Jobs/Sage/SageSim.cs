using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Sage;

/// Sage's state, which is almost nothing: whether Eukrasia is up, and when the DoT lapses.
public sealed class SageState : SimState
{
    /// When Eukrasian Dosis III's damage-over-time runs out, for netting out an early refresh.
    public double DotExpiresAt { get; set; }

    public override SimState Clone()
    {
        var copy = new SageState { DotExpiresAt = DotExpiresAt };
        CopyInto(copy);
        return copy;
    }
}

/// Sage's rules: what is legal, what it is worth, and what a press costs.
public sealed class SageSim : IJobSim
{
    public string JobName => "Sage";

    public MainAttribute MainAttribute => MainAttribute.Mind;

    public CombatRole Role => CombatRole.Healer;

    /// 115, off the ClassJob sheet's ModifierMind.
    public int MainStatModifier { get; init; } = 115;

    public int HastePercent => 0;

    /// Zero, like every caster - see BlackMageSim for the measurement.
    public int AutoAttackPotency => 0;

    /// The shared magic damage term.
    public double TraitMultiplier => XivMath.MaimAndMend;

    public IReadOnlyDictionary<string, ActionDef> Actions => SageData.Actions;

    public SimState CreateState()
    {
        var state = new SageState();

        foreach (var (name, action) in SageData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var sge = (SageState)state;

        return action.Name switch
        {
            Sge.EukrasiaAction => !sge.HasStatus(Sge.EukrasiaStatus),
            Sge.EukrasianDosis3 => sge.HasStatus(Sge.EukrasiaStatus),

            Sge.Dosis3 => !sge.HasStatus(Sge.EukrasiaStatus),

            Sge.Toxikon2 => false,

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state) => action.Potency;

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var sge = (SageState)state;

        switch (action.Name)
        {
            case Sge.EukrasiaAction:
                sge.ApplyStatus(Sge.EukrasiaStatus, 30.0);
                return;

            case Sge.EukrasianDosis3:
                sge.RemoveStatus(Sge.EukrasiaStatus);

                var overlap = System.Math.Max(0.0, sge.DotExpiresAt - sge.Time);
                var alreadyPaid = overlap
                                  / (SageData.EukrasianDosisDuration / SageData.EukrasianDosisTicks);

                if (overlap > 0)
                    sge.Count("dot.clipped", overlap);

                recordHit(Sge.EukrasianDosisDot,
                    SageData.EukrasianDosisTickPotency * (SageData.EukrasianDosisTicks - alreadyPaid),
                    false, false);

                sge.DotExpiresAt = sge.Time + SageData.EukrasianDosisDuration;
                return;
        }
    }
}
