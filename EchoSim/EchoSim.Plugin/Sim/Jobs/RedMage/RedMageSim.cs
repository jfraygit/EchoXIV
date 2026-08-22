using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.RedMage;

/// Red Mage's two-colour mana gauge and the banked proc fractions.
public sealed class RedMageState : SimState
{
    public int BlackMana { get; set; }

    public int WhiteMana { get; set; }

    /// Fractional Verfire and Verstone procs, banked and spent whole.
    public double VerfireBank { get; set; }

    public double VerstoneBank { get; set; }

    /// How far through the melee combo the player is, for choosing the finisher.
    public bool ComboFinisherReady { get; set; }

    public bool ScorchReady { get; set; }

    public bool ResolutionReady { get; set; }

    public void GainBlack(int amount) => BlackMana = Add(BlackMana, amount, "black");

    public void GainWhite(int amount) => WhiteMana = Add(WhiteMana, amount, "white");

    private int Add(int current, int amount, string colour)
    {
        var room = RedMageData.MaxMana - current;
        if (amount > room)
            Count($"mana.{colour}.overcapped", amount - room);

        return System.Math.Min(RedMageData.MaxMana, current + amount);
    }

    /// Banks a fraction of a proc, granting the status once a whole one has accumulated.
    public void BankProc(string status, double chance = RedMageData.ProcChance)
    {
        var bank = (status == Rdm.VerfireReady ? VerfireBank : VerstoneBank) + chance;

        if (bank >= 1.0)
        {
            bank -= 1.0;

            if (HasStatus(status))
                Count($"proc.overwritten:{status}", 1);

            ApplyStatus(status, RedMageData.ProcDuration);
        }

        if (status == Rdm.VerfireReady)
            VerfireBank = bank;
        else
            VerstoneBank = bank;
    }

    public override SimState Clone()
    {
        var copy = new RedMageState
        {
            BlackMana = BlackMana,
            WhiteMana = WhiteMana,
            VerfireBank = VerfireBank,
            VerstoneBank = VerstoneBank,
            ComboFinisherReady = ComboFinisherReady,
            ScorchReady = ScorchReady,
            ResolutionReady = ResolutionReady,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Red Mage's rules: what is legal, what it is worth, and what it does to the two mana bars.
public sealed class RedMageSim : IJobSim
{
    public string JobName => "Red Mage";

    public MainAttribute MainAttribute => MainAttribute.Intelligence;

    public CombatRole Role => CombatRole.Caster;

    public int MainStatModifier { get; init; } = 115;

    public int HastePercent => 0;

    /// Zero, and measured: the reference parse logs 174 auto-attack swings for 174 total damage.
    public int AutoAttackPotency => 0;

    /// The magic damage term, shared with the other casters.
    public double TraitMultiplier => XivMath.MaimAndMend;

    public IReadOnlyDictionary<string, ActionDef> Actions => RedMageData.Actions;

    public SimState CreateState()
    {
        var state = new RedMageState();

        foreach (var (name, action) in RedMageData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    /// What the cast bar actually runs for - which for this job is either 2.0s or nothing.
    public double CastTimeOf(ActionDef action, SimState state)
    {
        if (action.CastTime <= 0)
            return 0;

        var rdm = (RedMageState)state;

        return rdm.HasStatus(Rdm.Dualcast)
               || rdm.HasStatus(Rdm.SwiftcastStatus)
               || (rdm.HasStatus(Rdm.AccelerationStatus) && IsVerspell(action.Name))
            ? 0
            : action.CastTime;
    }

    /// What Acceleration makes instant, which is a shorter list than "the Verspells": Verthunder III, Veraero
    /// III and Impact, all three of them 5.0s casts.
    private static bool IsVerspell(string name)
        => name is Rdm.Verthunder3 or Rdm.Veraero3 or Rdm.Impact;

    /// The proc chance a long Verspell carries, which Acceleration raises to a certainty.
    private static double ProcChanceFor(bool accelerated)
        => accelerated ? RedMageData.AcceleratedProcChance : RedMageData.ProcChance;

    public bool CanUse(ActionDef action, SimState state)
    {
        var rdm = (RedMageState)state;

        return action.Name switch
        {
            Rdm.Verfire => rdm.HasStatus(Rdm.VerfireReady),
            Rdm.Verstone => rdm.HasStatus(Rdm.VerstoneReady),
            Rdm.GrandImpact => rdm.HasStatus(Rdm.GrandImpactReady),

            Rdm.EnchantedMoulinet => rdm.HasStatus(Rdm.MagickedSwordplay)
                                    || (rdm.BlackMana >= RedMageData.MeleeComboCost
                                        && rdm.WhiteMana >= RedMageData.MeleeComboCost),

            Rdm.EnchantedMoulinetDeux or Rdm.EnchantedMoulinetTrois =>
                rdm.IsComboReady(action)
                && (rdm.HasStatus(Rdm.MagickedSwordplay) || HasContinuationMana(rdm)),

            Rdm.EnchantedRiposte => rdm.HasStatus(Rdm.MagickedSwordplay)
                                    || (rdm.BlackMana >= RedMageData.MeleeComboCost
                                        && rdm.WhiteMana >= RedMageData.MeleeComboCost),

            Rdm.EnchantedZwerchhau => rdm.IsComboReady(action)
                                      && (rdm.HasStatus(Rdm.MagickedSwordplay) || HasContinuationMana(rdm)),

            Rdm.EnchantedRedoublement => rdm.IsComboReady(action)
                                         && (rdm.HasStatus(Rdm.MagickedSwordplay) || HasContinuationMana(rdm)),

            Rdm.Verflare or Rdm.Verholy => rdm.ComboFinisherReady,
            Rdm.Scorch => rdm.ScorchReady,
            Rdm.Resolution => rdm.ResolutionReady,

            Rdm.ViceOfThorns => rdm.HasStatus(Rdm.ThornedFlourish),
            Rdm.Prefulgence => rdm.HasStatus(Rdm.PrefulgenceReady),

            _ => true,
        };
    }

    private static bool HasContinuationMana(RedMageState rdm)
        => rdm.BlackMana >= RedMageData.ContinuationCost && rdm.WhiteMana >= RedMageData.ContinuationCost;

    /// Embolden raises the Red Mage's own magic damage by 5% on top of what it gives the party.
    public double EffectivePotency(ActionDef action, SimState state)
    {
        var rdm = (RedMageState)state;
        var potency = action.ComboFrom is not null && rdm.IsComboReady(action)
            ? action.ComboPotency
            : action.Potency;

        if (action.Name == Rdm.Impact && rdm.HasStatus(Rdm.AccelerationStatus))
            return RedMageData.ImpactAccelerationPotency;

        return potency;
    }

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var rdm = (RedMageState)state;

        var accelerated = false;

        if (action.CastTime > 0)
        {
            if (CastTimeOf(action, rdm) <= 0)
            {
                if (rdm.HasStatus(Rdm.Dualcast))
                    rdm.RemoveStatus(Rdm.Dualcast);
                else if (rdm.HasStatus(Rdm.SwiftcastStatus))
                    rdm.RemoveStatus(Rdm.SwiftcastStatus);
                else
                {
                    accelerated = true;
                    rdm.ConsumeStack(Rdm.AccelerationStatus);
                }
            }
            else
            {
                if (rdm.HasStatus(Rdm.Dualcast))
                    rdm.Count("dualcast.overwritten", 1);

                rdm.ApplyStatus(Rdm.Dualcast, RedMageData.DualcastDuration);

                if (IsVerspell(action.Name))
                    rdm.Count("verspell.hardcast", 1);
            }
        }

        switch (action.Name)
        {
            case Rdm.Jolt3:
                rdm.GainBlack(RedMageData.ManaPerJolt);
                rdm.GainWhite(RedMageData.ManaPerJolt);
                return;

            case Rdm.Verthunder3:
                rdm.GainBlack(RedMageData.ManaPerVerspell);
                rdm.BankProc(Rdm.VerfireReady, ProcChanceFor(accelerated));
                return;

            case Rdm.Veraero3:
                rdm.GainWhite(RedMageData.ManaPerVerspell);
                rdm.BankProc(Rdm.VerstoneReady, ProcChanceFor(accelerated));
                return;

            case Rdm.Verthunder2:
                rdm.GainBlack(RedMageData.ManaPerAreaVerspell);
                return;

            case Rdm.Veraero2:
                rdm.GainWhite(RedMageData.ManaPerAreaVerspell);
                return;

            case Rdm.Impact:
                rdm.GainBlack(RedMageData.ManaPerGrandImpact);
                rdm.GainWhite(RedMageData.ManaPerGrandImpact);
                return;

            case Rdm.Verfire:
                rdm.GainBlack(RedMageData.ManaPerVerspell);
                rdm.RemoveStatus(Rdm.VerfireReady);
                return;

            case Rdm.Verstone:
                rdm.GainWhite(RedMageData.ManaPerVerspell);
                rdm.RemoveStatus(Rdm.VerstoneReady);
                return;

            case Rdm.GrandImpact:
                rdm.GainBlack(RedMageData.ManaPerGrandImpact);
                rdm.GainWhite(RedMageData.ManaPerGrandImpact);
                rdm.RemoveStatus(Rdm.GrandImpactReady);
                return;

            case Rdm.EnchantedMoulinet:
                SpendMelee(rdm, RedMageData.RiposteCost);
                rdm.AdvanceCombo(action);
                return;

            case Rdm.EnchantedMoulinetDeux:
                SpendMelee(rdm, RedMageData.ContinuationCost);
                rdm.AdvanceCombo(action);
                return;

            case Rdm.EnchantedMoulinetTrois:
                SpendMelee(rdm, RedMageData.ContinuationCost);
                rdm.BreakCombo();
                rdm.ComboFinisherReady = true;
                return;

            case Rdm.EnchantedRiposte:
                SpendMelee(rdm, RedMageData.RiposteCost);
                rdm.AdvanceCombo(action);
                return;

            case Rdm.EnchantedZwerchhau:
                SpendMelee(rdm, RedMageData.ContinuationCost);
                rdm.AdvanceCombo(action);
                return;

            case Rdm.EnchantedRedoublement:
                SpendMelee(rdm, RedMageData.ContinuationCost);
                rdm.BreakCombo();
                rdm.ComboFinisherReady = true;
                return;

            case Rdm.Verflare:
                rdm.GainBlack(RedMageData.ManaPerFinisher);
                rdm.BankProc(Rdm.VerfireReady, RedMageData.FinisherProcChance);
                rdm.ComboFinisherReady = false;
                rdm.ScorchReady = true;
                return;

            case Rdm.Verholy:
                rdm.GainWhite(RedMageData.ManaPerFinisher);
                rdm.BankProc(Rdm.VerstoneReady, RedMageData.FinisherProcChance);
                rdm.ComboFinisherReady = false;
                rdm.ScorchReady = true;
                return;

            case Rdm.Scorch:
                rdm.GainBlack(RedMageData.ManaPerScorch);
                rdm.GainWhite(RedMageData.ManaPerScorch);
                rdm.ScorchReady = false;
                rdm.ResolutionReady = true;
                return;

            case Rdm.Resolution:
                rdm.GainBlack(RedMageData.ManaPerScorch);
                rdm.GainWhite(RedMageData.ManaPerScorch);
                rdm.ResolutionReady = false;
                return;

            case Rdm.AccelerationAction:
                rdm.ApplyStatus(Rdm.AccelerationStatus, RedMageData.AccelerationDuration);
                rdm.ApplyStatus(Rdm.GrandImpactReady, RedMageData.GrandImpactDuration);
                return;

            case Rdm.ManaficationAction:
                rdm.ApplyStatus(Rdm.MagickedSwordplay, 60.0, stacks: RedMageData.MagickedSwordplayStacks);
                rdm.ApplyStatus(Rdm.PrefulgenceReady, RedMageData.PrefulgenceDuration);
                return;

            case Rdm.EmboldenAction:
                rdm.ApplyStatus(Rdm.EmboldenStatus, RedMageData.EmboldenDuration,
                    damageMulti: RedMageData.EmboldenSelfBonus);
                rdm.ApplyStatus(Rdm.ThornedFlourish, RedMageData.ThornedFlourishDuration);
                return;

            case Rdm.ViceOfThorns:
                rdm.RemoveStatus(Rdm.ThornedFlourish);
                return;

            case Rdm.Prefulgence:
                rdm.RemoveStatus(Rdm.PrefulgenceReady);
                return;

            case Rdm.SwiftcastAction:
                rdm.ApplyStatus(Rdm.SwiftcastStatus, 10.0);
                return;
        }
    }

    /// Pays for one enchanted melee step - out of Manafication's stacks if any are left, otherwise out of
    /// both colours.
    private static void SpendMelee(RedMageState rdm, int cost)
    {
        if (rdm.HasStatus(Rdm.MagickedSwordplay))
        {
            rdm.ConsumeStack(Rdm.MagickedSwordplay);
            return;
        }

        rdm.BlackMana = System.Math.Max(0, rdm.BlackMana - cost);
        rdm.WhiteMana = System.Math.Max(0, rdm.WhiteMana - cost);
    }
}
