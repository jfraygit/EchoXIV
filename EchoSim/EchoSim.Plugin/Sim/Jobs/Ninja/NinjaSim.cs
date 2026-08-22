using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Ninja;

/// Ninja's gauge on top of the generic sim state.
public sealed class NinjaState : SimState
{
    public int Ninki { get; set; }
    public int Kazematoi { get; set; }

    /// How many of Ten Chi Jin's three ninjutsu have been used, 0-3.
    public int TcjStep { get; set; }

    public void GainNinki(int amount)
    {
        var wasted = System.Math.Max(0, Ninki + amount - NinjaData.MaxNinki);
        if (wasted > 0)
            Count("ninki.overcapped", wasted);

        Ninki = System.Math.Min(NinjaData.MaxNinki, Ninki + amount);
    }

    public void SpendNinki(int amount) => Ninki = System.Math.Max(0, Ninki - amount);

    public void GainKazematoi(int amount)
    {
        var wasted = System.Math.Max(0, Kazematoi + amount - NinjaData.MaxKazematoi);
        if (wasted > 0)
            Count("kazematoi.overcapped", wasted);

        Kazematoi = System.Math.Min(NinjaData.MaxKazematoi, Kazematoi + amount);
    }

    public override SimState Clone()
    {
        var copy = new NinjaState { Ninki = Ninki, Kazematoi = Kazematoi, TcjStep = TcjStep };
        CopyInto(copy);
        return copy;
    }
}

/// Ninja's rules, rebuilt from the sources rather than carried over from the version this replaced.
public sealed class NinjaSim : IJobSim
{
    public string JobName => "Ninja";

    /// Ninja's DEX modifier from the ClassJob sheet.
    public MainAttribute MainAttribute => MainAttribute.Dexterity;

    public CombatRole Role => CombatRole.Melee;

    public int MainStatModifier { get; init; } = 110;

    /// Increased Attack Speed: a permanent 15% trait, confirmed on The Balance's skills overview.
    public int HastePercent => 15;

    public int AutoAttackPotency => 90;

    public IReadOnlyDictionary<string, ActionDef> Actions => NinjaData.Actions;

    /// Ninki granted per action.
    private static readonly Dictionary<string, int> NinkiGain = new()
    {
        [Nin.SpinningEdge] = 5,
        [Nin.GustSlash] = 5,
        [Nin.AeolianEdge] = 15,
        [Nin.ArmorCrush] = 15,
        [Nin.PhantomKamaitachi] = 10,
        [Nin.FleetingRaiju] = 5,

        [Nin.DeathBlossom] = 5,
        [Nin.HakkeMujinsatsu] = 5,
    };

    public SimState CreateState()
    {
        var state = new NinjaState();

        state.RegisterCooldown(Nin.MudraCharges, NinjaData.MudraRecast, NinjaData.MudraMaxCharges);

        foreach (var (name, action) in NinjaData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var nin = (NinjaState)state;

        var inTcj = nin.HasStatus(Nin.TenChiJin);
        var isTcjAction = action.Name is Nin.TcjFuma or Nin.TcjRaiton or Nin.TcjSuiton;
        if (inTcj != isTcjAction)
            return false;

        return action.Name switch
        {
            Nin.PhantomKamaitachi => nin.HasStatus(Nin.PhantomReady),
            Nin.FleetingRaiju => nin.HasStatus(Nin.RaijuReady),
            Nin.HyoshoRanryu => nin.HasStatus(Nin.Kassatsu),

            Nin.TcjFuma => nin.TcjStep == 0,
            Nin.TcjRaiton => nin.TcjStep == 1,
            Nin.TcjSuiton => nin.TcjStep == 2,

            Nin.Bhavacakra => nin.Ninki >= NinjaData.SpenderCost,
            Nin.ZeshoMeppo => nin.Ninki >= NinjaData.SpenderCost && nin.HasStatus(Nin.Higi),
            Nin.BunshinAction => nin.Ninki >= NinjaData.SpenderCost,

            Nin.HellfrogMedium => nin.Ninki >= NinjaData.SpenderCost,
            Nin.DeathfrogMedium => nin.Ninki >= NinjaData.SpenderCost && nin.HasStatus(Nin.Higi),

            Nin.GokaMekkyaku => nin.HasStatus(Nin.Kassatsu),

            Nin.KunaisBaneAction or Nin.MeisuiAction => nin.HasStatus(Nin.ShadowWalker),

            Nin.TenriJindo => nin.HasStatus(Nin.TenriJindoReady),

            Nin.TenChiJinAction => !nin.HasStatus(Nin.Kassatsu),

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state)
    {
        var nin = (NinjaState)state;
        double potency = nin.IsComboReady(action) ? action.ComboPotency : action.Potency;

        switch (action.Name)
        {
            case Nin.AeolianEdge when nin.Kazematoi > 0:
                potency += NinjaData.KazematoiBonus;
                break;

            case Nin.Bhavacakra when nin.HasStatus(Nin.Meisui):
                potency = 550;
                break;
            case Nin.ZeshoMeppo when nin.HasStatus(Nin.Meisui):
                potency = 850;
                break;

            case Nin.PhantomKamaitachi:
                return PetPotency(potency, nin);
        }

        if (action.Kind == ActionKind.Ninjutsu && nin.HasStatus(Nin.Kassatsu))
            potency *= NinjaData.KassatsuMulti;

        return potency;
    }

    /// A pet hit's potency: the listed value at the pet's 92%, with Kunai's Bane divided back out because the
    /// engine is about to multiply it in and the shadow does not get it.
    private static double PetPotency(double listed, NinjaState nin)
    {
        var potency = listed * NinjaData.PetPotencyMultiplier;

        if (nin.HasStatus(Nin.KunaisBane))
            potency /= NinjaData.KunaisBaneMulti;

        return potency;
    }

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var nin = (NinjaState)state;

        if (action.Kind == ActionKind.Gcd
            && action.Name != Nin.PhantomKamaitachi
            && nin.HasStatus(Nin.Bunshin))
        {
            nin.ConsumeStack(Nin.Bunshin);

            var shadow = action.MaxTargets == ActionDef.AllNearby
                ? NinjaData.BunshinAreaShadowPotency * action.TargetMultiplier(nin.Targets)
                : NinjaData.BunshinShadowPotency;

            recordHit(Nin.Bunshin, PetPotency(shadow, nin), false, false);
            nin.GainNinki(5);
        }

        if (NinkiGain.TryGetValue(action.Name, out var ninki))
            nin.GainNinki(ninki);

        if (action.Kind == ActionKind.Gcd && !action.PreservesCombo)
        {
            if (action.IsComboStarter || nin.IsComboReady(action))
                nin.AdvanceCombo(action);
            else
                nin.BreakCombo();
        }

        if (action.Name is Nin.TcjFuma or Nin.TcjRaiton or Nin.TcjSuiton)
        {
            nin.TcjStep++;
            if (nin.TcjStep >= 3)
            {
                nin.RemoveStatus(Nin.TenChiJin);
                nin.TcjStep = 0;
            }
        }

        switch (action.Name)
        {
            case Nin.ArmorCrush:
                nin.GainKazematoi(2);
                break;

            case Nin.AeolianEdge when nin.Kazematoi > 0:
                nin.Kazematoi--;
                break;

            case Nin.FleetingRaiju:
                nin.ConsumeStack(Nin.RaijuReady);
                break;

            case Nin.Raiton:
            case Nin.TcjRaiton:
                nin.AddStack(Nin.RaijuReady, 30, NinjaData.MaxRaijuStacks);
                break;

            case Nin.Suiton:
            case Nin.TcjSuiton:
                nin.ApplyStatus(Nin.ShadowWalker, 20);
                break;

            case Nin.PhantomKamaitachi:
                nin.RemoveStatus(Nin.PhantomReady);
                break;

            case Nin.HyoshoRanryu:
            case Nin.GokaMekkyaku:
                nin.RemoveStatus(Nin.Kassatsu);
                break;

            case Nin.KassatsuAction:
                nin.ApplyStatus(Nin.Kassatsu, 15);
                break;

            case Nin.DokumoriAction:
                nin.ApplyStatus(Nin.Dokumori, 20, damageMulti: NinjaData.DokumoriMulti);
                nin.ApplyStatus(Nin.Higi, 30);
                nin.GainNinki(40);
                break;

            case Nin.KunaisBaneAction:
                nin.ApplyStatus(Nin.KunaisBane, 15, damageMulti: NinjaData.KunaisBaneMulti);
                nin.RemoveStatus(Nin.ShadowWalker);
                break;

            case Nin.MeisuiAction:
                nin.RemoveStatus(Nin.ShadowWalker);
                nin.ApplyStatus(Nin.Meisui, 30);
                nin.GainNinki(50);
                break;

            case Nin.BunshinAction:
                nin.SpendNinki(NinjaData.SpenderCost);
                nin.ApplyStatus(Nin.Bunshin, 30, stacks: 5);
                nin.ApplyStatus(Nin.PhantomReady, 45);
                break;

            case Nin.Bhavacakra:
            case Nin.HellfrogMedium:
                nin.SpendNinki(NinjaData.SpenderCost);
                break;

            case Nin.ZeshoMeppo:
            case Nin.DeathfrogMedium:
                nin.SpendNinki(NinjaData.SpenderCost);
                nin.RemoveStatus(Nin.Higi);
                break;

            case Nin.TenChiJinAction:
                nin.ApplyStatus(Nin.TenChiJin, 6);
                nin.ApplyStatus(Nin.TenriJindoReady, 30);
                nin.TcjStep = 0;
                break;

            case Nin.TenriJindo:
                nin.RemoveStatus(Nin.TenriJindoReady);
                break;


            case Nin.DreamWithinADream:
                recordHit(Nin.DreamWithinADream, action.Potency, false, false);
                recordHit(Nin.DreamWithinADream, action.Potency, false, false);
                break;
        }
    }
}
