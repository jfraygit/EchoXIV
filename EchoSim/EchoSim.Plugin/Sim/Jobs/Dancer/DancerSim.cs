using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Dancer;

/// Dancer's gauges, dance position and proc accumulators on top of the generic sim state.
public sealed class DancerState : SimState
{
    public int Esprit { get; set; }

    public int Feathers { get; set; }

    /// Dance steps still owed.
    public int StepsRemaining { get; set; }

    /// Whether the dance in progress is the four-step one.
    public bool DancingTechnical { get; set; }

    /// True once the steps are done and the finish is what comes next.
    public bool FinishReady { get; set; }

    /// Banked probability for the 50% proc rolls.
    public double SymmetryCharge;

    public double FlowCharge;

    public double FeatherCharge;

    public double FanDanceCharge;

    /// Fractional Esprit from the rest of the party, banked until it makes a whole point.
    public double ExternalEsprit { get; set; }

    /// When the party's Esprit contribution was last collected.
    public double ExternalEspritCollectedAt { get; set; }

    public void GainEsprit(int amount)
    {
        var room = DancerData.MaxEsprit - Esprit;
        if (amount > room)
            Count("esprit.overcapped", amount - room);

        Esprit = System.Math.Min(DancerData.MaxEsprit, Esprit + amount);
    }

    public void GainFeather()
    {
        if (Feathers >= DancerData.MaxFeathers)
        {
            Count("feather.overcapped", 1);
            return;
        }

        Feathers++;
    }

    public override SimState Clone()
    {
        var copy = new DancerState
        {
            Esprit = Esprit,
            Feathers = Feathers,
            StepsRemaining = StepsRemaining,
            DancingTechnical = DancingTechnical,
            FinishReady = FinishReady,
            SymmetryCharge = SymmetryCharge,
            FlowCharge = FlowCharge,
            FeatherCharge = FeatherCharge,
            FanDanceCharge = FanDanceCharge,
            ExternalEsprit = ExternalEsprit,
            ExternalEspritCollectedAt = ExternalEspritCollectedAt,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Dancer's rules: what's legal, what it's worth, and what it does to the gauges.
public sealed class DancerSim : IJobSim
{
    public string JobName => "Dancer";

    public MainAttribute MainAttribute => MainAttribute.Dexterity;

    public CombatRole Role => CombatRole.PhysicalRanged;

    public int MainStatModifier { get; init; } = 115;

    public int HastePercent => 0;

    /// 90, the MELEE value - and this is the one number on this job most likely to be set wrong.
    public int AutoAttackPotency => 90;

    /// Increased Action Damage II.
    public double TraitMultiplier => 1.20;

    public IReadOnlyDictionary<string, ActionDef> Actions => DancerData.Actions;

    public SimState CreateState()
    {
        var state = new DancerState();

        foreach (var (name, action) in DancerData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var dnc = (DancerState)state;

        if (action.IsGcd)
        {
            if (dnc.StepsRemaining > 0)
                return action.Name == Dnc.DanceStep;

            if (dnc.FinishReady)
                return action.Name == (dnc.DancingTechnical ? Dnc.TechnicalFinish : Dnc.StandardFinish);
        }

        return action.Name switch
        {
            Dnc.DanceStep => false,
            Dnc.StandardFinish or Dnc.TechnicalFinish => false,

            Dnc.Emboite or Dnc.Entrechat or Dnc.Jete or Dnc.Pirouette => false,

            Dnc.ReverseCascade or Dnc.RisingWindmill => dnc.HasStatus(Dnc.SilkenSymmetry) || dnc.HasStatus(Dnc.FlourishingSymmetry),
            Dnc.Fountainfall or Dnc.Bloodshower => dnc.HasStatus(Dnc.SilkenFlow) || dnc.HasStatus(Dnc.FlourishingFlow),

            Dnc.SaberDance => dnc.Esprit >= DancerData.SaberDanceCost,
            Dnc.DanceOfTheDawn => dnc.Esprit >= DancerData.SaberDanceCost && dnc.HasStatus(Dnc.DanceOfTheDawnReady),
            Dnc.StarfallDance => dnc.HasStatus(Dnc.FlourishingStarfall),
            Dnc.Tillana => dnc.HasStatus(Dnc.FlourishingFinish),
            Dnc.LastDance => dnc.HasStatus(Dnc.LastDanceReady),
            Dnc.FinishingMove => dnc.HasStatus(Dnc.FinishingMoveReady),

            Dnc.FanDance or Dnc.FanDanceII => dnc.Feathers > 0,
            Dnc.FanDanceIII => dnc.HasStatus(Dnc.ThreefoldFanDance),
            Dnc.FanDanceIV => dnc.HasStatus(Dnc.FourfoldFanDance),

            _ when action.ComboFrom is not null => dnc.IsComboReady(action),

            _ => true,
        };
    }

    /// Starfall Dance is a guaranteed critical DIRECT hit, and this job had no auto-crit at all.
    public bool IsAutoCrit(ActionDef action, SimState state)
        => action.Name == Dnc.StarfallDance;

    public bool IsAutoDirectHit(ActionDef action, SimState state) => IsAutoCrit(action, state);

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var dnc = (DancerState)state;
        return action.ComboFrom is not null && dnc.IsComboReady(action) ? action.ComboPotency : action.Potency;
    }

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var dnc = (DancerState)state;

        CollectExternalEsprit(dnc);

        switch (action.Name)
        {
            case Dnc.Cascade:
            case Dnc.Windmill:
                dnc.GainEsprit(DancerData.EspritPerCombo);
                Roll(dnc, ref dnc.SymmetryCharge, Dnc.SilkenSymmetry);
                dnc.AdvanceCombo(action);
                return;

            case Dnc.Fountain:
            case Dnc.Bladeshower:
                dnc.GainEsprit(DancerData.EspritPerCombo);
                Roll(dnc, ref dnc.FlowCharge, Dnc.SilkenFlow);
                dnc.BreakCombo();
                return;

            case Dnc.ReverseCascade:
            case Dnc.RisingWindmill:
                ConsumeProc(dnc, Dnc.FlourishingSymmetry, Dnc.SilkenSymmetry);
                dnc.GainEsprit(DancerData.EspritPerProc);
                RollFeather(dnc);
                return;

            case Dnc.Fountainfall:
            case Dnc.Bloodshower:
                ConsumeProc(dnc, Dnc.FlourishingFlow, Dnc.SilkenFlow);
                dnc.GainEsprit(DancerData.EspritPerProc);
                RollFeather(dnc);
                return;

            case Dnc.SaberDance:
                dnc.Esprit -= DancerData.SaberDanceCost;
                return;

            case Dnc.DanceOfTheDawn:
                dnc.Esprit -= DancerData.SaberDanceCost;
                dnc.RemoveStatus(Dnc.DanceOfTheDawnReady);
                return;

            case Dnc.StarfallDance:
                dnc.RemoveStatus(Dnc.FlourishingStarfall);
                return;

            case Dnc.Tillana:
                dnc.RemoveStatus(Dnc.FlourishingFinish);
                dnc.GainEsprit(DancerData.TillanaEsprit);
                return;

            case Dnc.LastDance:
                dnc.RemoveStatus(Dnc.LastDanceReady);
                return;

            case Dnc.StandardStep:
                dnc.StepsRemaining = DancerData.StandardSteps;
                dnc.DancingTechnical = false;
                return;

            case Dnc.TechnicalStep:
                dnc.StepsRemaining = DancerData.TechnicalSteps;
                dnc.DancingTechnical = true;
                return;

            case Dnc.DanceStep:
                dnc.StepsRemaining--;
                if (dnc.StepsRemaining <= 0)
                    dnc.FinishReady = true;

                return;

            case Dnc.StandardFinish:
                dnc.FinishReady = false;
                ApplyStandardFinish(dnc);
                return;

            case Dnc.TechnicalFinish:
                dnc.FinishReady = false;
                dnc.ApplyStatus(Dnc.TechnicalFinishBuff, DancerData.TechnicalFinishDuration,
                    damageMulti: DancerData.TechnicalFinishMulti);
                dnc.ApplyStatus(Dnc.FlourishingFinish, DancerData.ReadyDuration);
                dnc.ApplyStatus(Dnc.DanceOfTheDawnReady, DancerData.ReadyDuration);
                return;

            case Dnc.FinishingMove:
                dnc.RemoveStatus(Dnc.FinishingMoveReady);
                ApplyStandardFinish(dnc);
                return;

            case Dnc.FanDance:
            case Dnc.FanDanceII:
                dnc.Feathers--;
                Roll(dnc, ref dnc.FanDanceCharge, Dnc.ThreefoldFanDance);
                return;

            case Dnc.FanDanceIII:
                dnc.RemoveStatus(Dnc.ThreefoldFanDance);
                return;

            case Dnc.FanDanceIV:
                dnc.RemoveStatus(Dnc.FourfoldFanDance);
                return;

            case Dnc.Flourish:
                dnc.ApplyStatus(Dnc.FlourishingSymmetry, DancerData.ProcDuration);
                dnc.ApplyStatus(Dnc.FlourishingFlow, DancerData.ProcDuration);
                dnc.ApplyStatus(Dnc.ThreefoldFanDance, DancerData.ProcDuration);
                dnc.ApplyStatus(Dnc.FourfoldFanDance, DancerData.ProcDuration);
                dnc.ApplyStatus(Dnc.FinishingMoveReady, DancerData.ProcDuration);
                return;

            case Dnc.DevilmentAction:
                dnc.ApplyStatus(Dnc.Devilment, DancerData.DevilmentDuration,
                    critBonus: DancerData.DevilmentCritBonus,
                    directHitBonus: DancerData.DevilmentDirectHitBonus);
                dnc.ApplyStatus(Dnc.FlourishingStarfall, DancerData.DevilmentDuration);
                return;
        }
    }

    /// Both routes into Standard Finish grant the same buff and the same follow-up.
    private static void ApplyStandardFinish(DancerState dnc)
    {
        dnc.ApplyStatus(Dnc.StandardFinishBuff, DancerData.StandardFinishDuration,
            damageMulti: DancerData.StandardFinishMulti);
        dnc.ApplyStatus(Dnc.LastDanceReady, DancerData.ReadyDuration);
    }

    /// Collects the Esprit the rest of the party has generated since this was last checked.
    private static void CollectExternalEsprit(DancerState dnc)
    {
        var elapsed = dnc.Time - dnc.ExternalEspritCollectedAt;
        dnc.ExternalEspritCollectedAt = dnc.Time;

        if (elapsed <= 0 || !dnc.HasStatus(Dnc.StandardFinishBuff) && !dnc.HasStatus(Dnc.TechnicalFinishBuff))
            return;

        dnc.ExternalEsprit += elapsed * DancerData.ExternalEspritPerSecond;

        var whole = (int)dnc.ExternalEsprit;
        if (whole <= 0)
            return;

        dnc.ExternalEsprit -= whole;
        dnc.GainEsprit(whole);
    }

    /// Banks a 50% proc chance and grants the status once a whole one has accumulated.
    private static void Roll(DancerState dnc, ref double charge, string status)
    {
        charge += DancerData.ProcChance;
        if (charge < 1.0)
            return;

        charge -= 1.0;
        dnc.ApplyStatus(status, DancerData.ProcDuration);
    }

    private static void RollFeather(DancerState dnc)
    {
        dnc.FeatherCharge += DancerData.ProcChance;
        if (dnc.FeatherCharge < 1.0)
            return;

        dnc.FeatherCharge -= 1.0;
        dnc.GainFeather();
    }

    /// Spends the Flourish version of a proc before the Silken one.
    private static void ConsumeProc(DancerState dnc, string flourishing, string silken)
    {
        if (dnc.HasStatus(flourishing))
            dnc.RemoveStatus(flourishing);
        else
            dnc.RemoveStatus(silken);
    }

}
