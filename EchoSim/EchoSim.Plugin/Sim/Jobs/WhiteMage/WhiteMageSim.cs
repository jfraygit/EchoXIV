using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.WhiteMage;

/// White Mage's gauge: three lilies on a timer, and a Blood Lily fed by spending them.
public sealed class WhiteMageState : SimState
{
    public int Lilies { get; set; }

    /// Afflatus Rapture or Solace presses banked toward the bloom, 0 to 3.
    public int BloodLily { get; set; }

    /// When the next lily arrives.
    public double NextLilyAt { get; set; } = WhiteMageData.LilyInterval;

    /// When Dia's damage-over-time runs out, for netting out an early refresh.
    public double DotExpiresAt { get; set; }

    public bool BloodLilyReady => BloodLily >= WhiteMageData.LiliesPerBloom;

    /// Pays out every lily due between the last one and time.
    public void AdvanceResources(double time)
    {
        while (NextLilyAt <= time + 1e-9)
        {
            if (Lilies >= WhiteMageData.MaxLilies)
                Count("lily.overcapped", 1);
            else
                Lilies++;

            NextLilyAt += WhiteMageData.LilyInterval;
        }
    }

    public override SimState Clone()
    {
        var copy = new WhiteMageState
        {
            Lilies = Lilies,
            BloodLily = BloodLily,
            NextLilyAt = NextLilyAt,
            DotExpiresAt = DotExpiresAt,
        };

        CopyInto(copy);
        return copy;
    }
}

/// White Mage's rules: what is legal, what it is worth, and what a press costs.
public sealed class WhiteMageSim : IJobSim
{
    public string JobName => "White Mage";

    public MainAttribute MainAttribute => MainAttribute.Mind;

    public CombatRole Role => CombatRole.Healer;

    /// 115, off the ClassJob sheet's ModifierMind - the same as the four casters.
    public int MainStatModifier { get; init; } = 115;

    public int HastePercent => 0;

    /// Zero, like every caster - see BlackMageSim for the measurement.
    public int AutoAttackPotency => 0;

    /// Maim and Mend II, +30%, which White Mage does carry - and this comment used to say it did not.
    public double TraitMultiplier => XivMath.MaimAndMend;

    public IReadOnlyDictionary<string, ActionDef> Actions => WhiteMageData.Actions;

    public SimState CreateState()
    {
        var state = new WhiteMageState();

        foreach (var (name, action) in WhiteMageData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var whm = (WhiteMageState)state;
        whm.AdvanceResources(whm.Time);

        return action.Name switch
        {
            Whm.Glare4 => whm.HasStatus(Whm.SacredSight),
            Whm.AfflatusMisery => whm.BloodLilyReady,
            Whm.AfflatusRapture => whm.Lilies > 0 && !whm.BloodLilyReady,
            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state) => action.Potency;

    /// Presence of Mind, and the only reason this job overrides it.
    public int TransientHastePercent(SimState state)
        => state.HasStatus(Whm.PresenceOfMindStatus) ? WhiteMageData.PresenceOfMindHaste : 0;

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var whm = (WhiteMageState)state;
        whm.AdvanceResources(whm.Time);

        switch (action.Name)
        {
            case Whm.Dia:
                var overlap = System.Math.Max(0.0, whm.DotExpiresAt - whm.Time);
                var alreadyPaid = overlap / (WhiteMageData.DiaDuration / WhiteMageData.DiaTicks);

                if (overlap > 0)
                    whm.Count("dot.clipped", overlap);

                recordHit(
                    Whm.DiaDot,
                    WhiteMageData.DiaTickPotency * (WhiteMageData.DiaTicks - alreadyPaid),
                    false,
                    false);

                whm.DotExpiresAt = whm.Time + WhiteMageData.DiaDuration;
                return;

            case Whm.Glare4:
                whm.ConsumeStack(Whm.SacredSight);
                return;

            case Whm.AfflatusRapture:
                whm.Lilies--;
                whm.BloodLily++;
                return;

            case Whm.AfflatusMisery:
                whm.BloodLily = 0;
                return;

            case Whm.PresenceOfMindAction:
                whm.ApplyStatus(Whm.PresenceOfMindStatus, WhiteMageData.PresenceOfMindDuration);
                whm.ApplyStatus(Whm.SacredSight, WhiteMageData.SacredSightDuration,
                    stacks: WhiteMageData.SacredSightStacks);
                return;
        }
    }
}
