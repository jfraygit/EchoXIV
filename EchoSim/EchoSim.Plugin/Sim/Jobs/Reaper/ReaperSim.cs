using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Reaper;

/// Reaper's gauges on top of the generic sim state.
public sealed class ReaperState : SimState
{
    /// Soul, capped at 100.
    public int Soul { get; set; }

    /// Shroud, capped at 100.
    public int Shroud { get; set; }

    /// Lemure Shroud: four reapings and the Communio that closes the window.
    public int LemureShroud { get; set; }

    /// Void Shroud, one per reaping, spent two at a time on Lemure's Slice.
    public int VoidShroud { get; set; }

    public void GainSoul(int amount)
    {
        var room = ReaperData.MaxSoul - Soul;
        if (amount > room)
            Count("soul.overcapped", amount - room);

        Soul = Math.Min(ReaperData.MaxSoul, Soul + amount);
    }

    public void GainShroud(int amount)
    {
        var room = ReaperData.MaxShroud - Shroud;
        if (amount > room)
            Count("shroud.overcapped", amount - room);

        Shroud = Math.Min(ReaperData.MaxShroud, Shroud + amount);
    }

    public override SimState Clone()
    {
        var copy = new ReaperState
        {
            Soul = Soul,
            Shroud = Shroud,
            LemureShroud = LemureShroud,
            VoidShroud = VoidShroud,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Reaper's rules: what's legal, what it's worth, and what it does to the gauges.
public sealed class ReaperSim : IJobSim
{
    public string JobName => "Reaper";

    /// Reaper's STR modifier from the ClassJob sheet.
    public MainAttribute MainAttribute => MainAttribute.Strength;

    public CombatRole Role => CombatRole.Melee;

    public int MainStatModifier { get; init; } = 115;

    /// None.
    public int HastePercent => 0;

    public int AutoAttackPotency => 90;

    public IReadOnlyDictionary<string, ActionDef> Actions => ReaperData.Actions;

    public SimState CreateState()
    {
        var state = new ReaperState();

        foreach (var (name, action) in ReaperData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var rpr = (ReaperState)state;

        return action.Name switch
        {
            Rpr.VoidReaping => rpr.HasStatus(Rpr.Enshrouded) && rpr.LemureShroud > 0,
            Rpr.CrossReaping => rpr.HasStatus(Rpr.Enshrouded) && rpr.LemureShroud > 0,
            Rpr.Communio => rpr.HasStatus(Rpr.Enshrouded) && rpr.LemureShroud > 0,
            Rpr.Sacrificium => rpr.HasStatus(Rpr.Enshrouded) && rpr.HasStatus(Rpr.Oblatio),
            Rpr.LemuresSlice => rpr.HasStatus(Rpr.Enshrouded) && rpr.VoidShroud >= ReaperData.LemuresSliceCost,

            Rpr.Perfectio => rpr.HasStatus(Rpr.PerfectioParata),

            Rpr.Gibbet or Rpr.Gallows => rpr.HasStatus(Rpr.SoulReaver) && !rpr.HasStatus(Rpr.Enshrouded),
            Rpr.ExecutionersGibbet or Rpr.ExecutionersGallows =>
                rpr.HasStatus(Rpr.Executioner) && !rpr.HasStatus(Rpr.Enshrouded),

            Rpr.Guillotine => rpr.HasStatus(Rpr.SoulReaver) && !rpr.HasStatus(Rpr.Enshrouded),
            Rpr.ExecutionersGuillotine => rpr.HasStatus(Rpr.Executioner) && !rpr.HasStatus(Rpr.Enshrouded),
            Rpr.GrimReaping => rpr.HasStatus(Rpr.Enshrouded) && rpr.LemureShroud > 0,

            Rpr.UnveiledGibbet => rpr.Soul >= ReaperData.SoulSpenderCost && !rpr.HasStatus(Rpr.Enshrouded)
                                  && !rpr.HasStatus(Rpr.SoulReaver) && !rpr.HasStatus(Rpr.EnhancedGallows),
            Rpr.UnveiledGallows => rpr.Soul >= ReaperData.SoulSpenderCost && !rpr.HasStatus(Rpr.Enshrouded)
                                   && !rpr.HasStatus(Rpr.SoulReaver) && rpr.HasStatus(Rpr.EnhancedGallows),

            Rpr.GrimSwathe => rpr.Soul >= ReaperData.SoulSpenderCost && !rpr.HasStatus(Rpr.Enshrouded)
                              && !rpr.HasStatus(Rpr.SoulReaver),

            Rpr.Gluttony => rpr.Soul >= ReaperData.SoulSpenderCost && !rpr.HasStatus(Rpr.Enshrouded),

            Rpr.EnshroudAction => !rpr.HasStatus(Rpr.Enshrouded)
                                  && (rpr.HasStatus(Rpr.IdealHost) || rpr.Shroud >= ReaperData.EnshroudShroudCost),

            Rpr.PlentifulHarvest => rpr.HasStatus(Rpr.ImmortalSacrifice) && !rpr.HasStatus(Rpr.Enshrouded),

            Rpr.ShadowOfDeath or Rpr.WhorlOfDeath => true,

            _ when action.IsGcd => !rpr.HasStatus(Rpr.Enshrouded)
                                   && (action.ComboFrom is null || rpr.IsComboReady(action)),

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var rpr = (ReaperState)state;

        if (action.Name is Rpr.VoidReaping or Rpr.CrossReaping)
        {
            var enhanced = action.Name == Rpr.VoidReaping
                ? rpr.HasStatus(Rpr.EnhancedVoidReaping)
                : rpr.HasStatus(Rpr.EnhancedCrossReaping);

            return enhanced ? action.Potency : ReaperData.ReapingBasePotency;
        }

        return action.ComboFrom is not null && rpr.IsComboReady(action) ? action.ComboPotency : action.Potency;
    }

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var rpr = (ReaperState)state;

        switch (action.Name)
        {
            case Rpr.Slice:
            case Rpr.WaxingSlice:
            case Rpr.InfernalSlice:
                rpr.GainSoul(10);
                rpr.AdvanceCombo(action);
                return;

            case Rpr.ShadowOfDeath:
            case Rpr.WhorlOfDeath:
                rpr.GainSoul(10);
                rpr.ApplyStatus(Rpr.DeathsDesign, ReaperData.DeathsDesignDuration,
                    damageMulti: ReaperData.DeathsDesignMulti);
                return;

            case Rpr.SoulSlice:
            case Rpr.SoulScythe:
                rpr.GainSoul(50);
                return;

            case Rpr.Gibbet:
            case Rpr.ExecutionersGibbet:
                rpr.GainShroud(10);
                rpr.ConsumeStack(action.Name == Rpr.Gibbet ? Rpr.SoulReaver : Rpr.Executioner);
                rpr.RemoveStatus(Rpr.EnhancedGibbet);
                rpr.ApplyStatus(Rpr.EnhancedGallows, ReaperData.BuffDuration);
                return;

            case Rpr.Gallows:
            case Rpr.ExecutionersGallows:
                rpr.GainShroud(10);
                rpr.ConsumeStack(action.Name == Rpr.Gallows ? Rpr.SoulReaver : Rpr.Executioner);
                rpr.RemoveStatus(Rpr.EnhancedGallows);
                rpr.ApplyStatus(Rpr.EnhancedGibbet, ReaperData.BuffDuration);
                return;

            case Rpr.Guillotine:
            case Rpr.ExecutionersGuillotine:
                rpr.GainShroud(10);
                rpr.ConsumeStack(action.Name == Rpr.Guillotine ? Rpr.SoulReaver : Rpr.Executioner);

                if (rpr.HasStatus(Rpr.EnhancedGallows))
                {
                    rpr.RemoveStatus(Rpr.EnhancedGallows);
                    rpr.ApplyStatus(Rpr.EnhancedGibbet, ReaperData.BuffDuration);
                }
                else
                {
                    rpr.RemoveStatus(Rpr.EnhancedGibbet);
                    rpr.ApplyStatus(Rpr.EnhancedGallows, ReaperData.BuffDuration);
                }

                return;

            case Rpr.GrimReaping:
                rpr.LemureShroud--;
                rpr.VoidShroud++;
                return;

            case Rpr.PlentifulHarvest:
                rpr.RemoveStatus(Rpr.ImmortalSacrifice);
                rpr.ApplyStatus(Rpr.IdealHost, ReaperData.BuffDuration);
                rpr.ApplyStatus(Rpr.PerfectioOcculta, ReaperData.BuffDuration);
                return;

            case Rpr.VoidReaping:
                rpr.LemureShroud--;
                rpr.VoidShroud++;
                rpr.RemoveStatus(Rpr.EnhancedVoidReaping);
                rpr.ApplyStatus(Rpr.EnhancedCrossReaping, ReaperData.BuffDuration);
                return;

            case Rpr.CrossReaping:
                rpr.LemureShroud--;
                rpr.VoidShroud++;
                rpr.RemoveStatus(Rpr.EnhancedCrossReaping);
                rpr.ApplyStatus(Rpr.EnhancedVoidReaping, ReaperData.BuffDuration);
                return;

            case Rpr.LemuresSlice:
            case Rpr.LemuresScythe:
                rpr.VoidShroud -= ReaperData.LemuresSliceCost;
                return;

            case Rpr.Sacrificium:
                rpr.RemoveStatus(Rpr.Oblatio);
                return;

            case Rpr.Communio:
                rpr.LemureShroud--;
                rpr.RemoveStatus(Rpr.Enshrouded);
                rpr.RemoveStatus(Rpr.Oblatio);
                rpr.VoidShroud = 0;
                rpr.LemureShroud = 0;

                if (rpr.HasStatus(Rpr.PerfectioOcculta))
                {
                    rpr.RemoveStatus(Rpr.PerfectioOcculta);
                    rpr.ApplyStatus(Rpr.PerfectioParata, ReaperData.BuffDuration);
                }

                return;

            case Rpr.Perfectio:
                rpr.RemoveStatus(Rpr.PerfectioParata);
                return;

            case Rpr.UnveiledGibbet:
            case Rpr.UnveiledGallows:
            case Rpr.GrimSwathe:
                rpr.Soul -= ReaperData.SoulSpenderCost;
                rpr.ApplyStatus(Rpr.SoulReaver, ReaperData.BuffDuration);
                return;

            case Rpr.Gluttony:
                rpr.Soul -= ReaperData.SoulSpenderCost;
                rpr.ApplyStatus(Rpr.Executioner, ReaperData.BuffDuration, stacks: 2);
                return;

            case Rpr.EnshroudAction:
                if (rpr.HasStatus(Rpr.IdealHost))
                    rpr.RemoveStatus(Rpr.IdealHost);
                else
                    rpr.Shroud -= ReaperData.EnshroudShroudCost;

                rpr.ApplyStatus(Rpr.Enshrouded, ReaperData.EnshroudDuration);
                rpr.ApplyStatus(Rpr.Oblatio, ReaperData.EnshroudDuration);
                rpr.LemureShroud = ReaperData.LemureShroudStacks;
                rpr.VoidShroud = 0;
                return;

            case Rpr.ArcaneCircleAction:
                rpr.ApplyStatus(Rpr.ArcaneCircleBuff, ReaperData.ArcaneCircleDuration,
                    damageMulti: ReaperData.ArcaneCircleMulti);
                rpr.ApplyStatus(Rpr.ImmortalSacrifice, ReaperData.BuffDuration);
                return;
        }
    }
}
