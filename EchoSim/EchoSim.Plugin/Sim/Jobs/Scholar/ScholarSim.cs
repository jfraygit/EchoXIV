using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Scholar;

/// Scholar's state: Aetherflow stacks, and when the damage-over-time lapses.
public sealed class ScholarState : SimState
{
    public int Aetherflow { get; set; }

    /// When Biolysis runs out, for netting out an early refresh.
    public double DotExpiresAt { get; set; }

    public override SimState Clone()
    {
        var copy = new ScholarState
        {
            Aetherflow = Aetherflow,
            DotExpiresAt = DotExpiresAt,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Scholar's rules: what is legal, what it is worth, and what a press costs.
public sealed class ScholarSim : IJobSim
{
    public string JobName => "Scholar";

    public MainAttribute MainAttribute => MainAttribute.Mind;

    public CombatRole Role => CombatRole.Healer;

    /// 115, off the ClassJob sheet's ModifierMind.
    public int MainStatModifier { get; init; } = 115;

    public int HastePercent => 0;

    /// Zero, like every caster - see BlackMageSim for the measurement.
    public int AutoAttackPotency => 0;

    /// Maim and Mend II, +30%, inherited from Arcanist - so Scholar does carry it, and this used to say it
    /// carried no such trait at all.
    public double TraitMultiplier => XivMath.MaimAndMend;

    public IReadOnlyDictionary<string, ActionDef> Actions => ScholarData.Actions;

    public SimState CreateState()
    {
        var state = new ScholarState();

        foreach (var (name, action) in ScholarData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var sch = (ScholarState)state;

        return action.Name switch
        {
            Sch.EnergyDrain => sch.Aetherflow > 0,
            Sch.BanefulImpaction => sch.HasStatus(Sch.ImpactImminent),

            Sch.AetherflowAction or Sch.Dissipation => sch.Aetherflow == 0,

            Sch.Ruin2 => false,

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state) => action.Potency;

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var sch = (ScholarState)state;

        switch (action.Name)
        {
            case Sch.Biolysis:
                var overlap = System.Math.Max(0.0, sch.DotExpiresAt - sch.Time);
                var alreadyPaid = overlap / (ScholarData.BiolysisDuration / ScholarData.BiolysisTicks);

                if (overlap > 0)
                    sch.Count("dot.clipped", overlap);

                recordHit(
                    Sch.BiolysisDot,
                    ScholarData.BiolysisTickPotency * (ScholarData.BiolysisTicks - alreadyPaid),
                    false,
                    false);

                sch.DotExpiresAt = sch.Time + ScholarData.BiolysisDuration;
                return;

            case Sch.ChainStratagemAction:
                sch.ApplyStatus(Sch.ChainStratagemStatus, ScholarData.ChainStratagemDuration,
                    critBonus: ScholarData.ChainStratagemCritBonus);

                sch.ApplyStatus(Sch.ImpactImminent, ScholarData.ImpactImminentDuration);
                return;

            case Sch.BanefulImpaction:
                sch.RemoveStatus(Sch.ImpactImminent);

                recordHit(Sch.BanefulImpactionDot,
                    ScholarData.BanefulImpactionTickPotency * ScholarData.BanefulImpactionTicks
                        * action.TargetMultiplier(state.Targets),
                    false, false);

                return;

            case Sch.AetherflowAction:
            case Sch.Dissipation:
                sch.Aetherflow = ScholarData.AetherflowStacks;
                return;

            case Sch.EnergyDrain:
                sch.Aetherflow--;
                return;
        }
    }
}
