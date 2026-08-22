using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Samurai;

/// Samurai's gauges on top of the generic sim state.
public sealed class SamuraiState : SimState
{
    /// Kenki, capped at 100.
    public int Kenki { get; set; }

    /// Meditation, capped at three, which is exactly what Shoha costs.
    public int Meditation { get; set; }

    public bool Setsu { get; set; }

    public bool Getsu { get; set; }

    public bool Ka { get; set; }

    public int SenCount => (Setsu ? 1 : 0) + (Getsu ? 1 : 0) + (Ka ? 1 : 0);

    /// When the Higanbana damage-over-time currently on the target runs out.
    public double HiganbanaExpiresAt { get; set; }

    public double FugetsuExpiresAt { get; set; }

    public double FukaExpiresAt { get; set; }

    /// Record a Fugetsu or Fuka application and charge the rotation for any gap before it.
    public void TrackBuff(string name, double expiresAt)
    {
        if (expiresAt > 0 && Time > expiresAt)
            Count($"{name}.lapsed", Time - expiresAt);
    }

    public void GainKenki(int amount)
    {
        var room = SamuraiData.MaxKenki - Kenki;
        if (amount > room)
            Count("kenki.overcapped", amount - room);

        Kenki = Math.Min(SamuraiData.MaxKenki, Kenki + amount);
    }

    /// Meditation from an Iaijutsu or Ogi Namikiri.
    public void GainMeditation()
    {
        if (Meditation >= SamuraiData.MaxMeditation)
        {
            Count("meditation.overcapped", 1);
            return;
        }

        Meditation++;
    }

    /// Open one Sen, and count it if it was already open.
    public void OpenSen(string which)
    {
        var already = which switch
        {
            nameof(Setsu) => Setsu,
            nameof(Getsu) => Getsu,
            _ => Ka,
        };

        if (already)
            Count($"sen.wasted.{which}", 1);

        switch (which)
        {
            case nameof(Setsu): Setsu = true; break;
            case nameof(Getsu): Getsu = true; break;
            default: Ka = true; break;
        }
    }

    public void ClearSen()
    {
        Setsu = false;
        Getsu = false;
        Ka = false;
    }

    public override SimState Clone()
    {
        var copy = new SamuraiState
        {
            Kenki = Kenki,
            Meditation = Meditation,
            Setsu = Setsu,
            Getsu = Getsu,
            Ka = Ka,
            HiganbanaExpiresAt = HiganbanaExpiresAt,
            FugetsuExpiresAt = FugetsuExpiresAt,
            FukaExpiresAt = FukaExpiresAt,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Samurai's rules: what's legal, what it's worth, and what it does to the gauges.
public sealed class SamuraiSim : IJobSim
{
    public string JobName => "Samurai";

    /// Samurai's STR modifier from the ClassJob sheet.
    public MainAttribute MainAttribute => MainAttribute.Strength;

    public CombatRole Role => CombatRole.Melee;

    public int MainStatModifier { get; init; } = 112;

    /// Fuka's 13%, modelled as permanent.
    public int HastePercent => 13;

    public int AutoAttackPotency => 90;

    public IReadOnlyDictionary<string, ActionDef> Actions => SamuraiData.Actions;

    public SimState CreateState()
    {
        var state = new SamuraiState();

        foreach (var (name, action) in SamuraiData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var sam = (SamuraiState)state;

        return action.Name switch
        {
            Sam.Higanbana => sam.SenCount >= 1,
            Sam.MidareSetsugekka => sam.SenCount == 3 && !sam.HasStatus(Sam.Tendo),
            Sam.TendoSetsugekka => sam.SenCount == 3 && sam.HasStatus(Sam.Tendo),

            Sam.KaeshiSetsugekka => sam.HasStatus(Sam.TsubameReady),
            Sam.TendoKaeshiSetsugekka => sam.HasStatus(Sam.TendoKaeshiReady),

            Sam.TenkaGoken => sam.SenCount == 2 && !sam.HasStatus(Sam.Tendo),
            Sam.TendoGoken => sam.SenCount == 2 && sam.HasStatus(Sam.Tendo),
            Sam.KaeshiGoken => sam.HasStatus(Sam.TsubameReady),
            Sam.TendoKaeshiGoken => sam.HasStatus(Sam.TendoKaeshiReady),

            Sam.OgiNamikiri => sam.HasStatus(Sam.OgiReady),
            Sam.KaeshiNamikiri => sam.HasStatus(Sam.KaeshiNamikiriReady),

            Sam.Shinten => sam.Kenki >= SamuraiData.ShintenCost,
            Sam.Gyoten => sam.Kenki >= SamuraiData.GyotenCost,
            Sam.Senei => sam.Kenki >= SamuraiData.SeneiCost,

            Sam.Kyuten => sam.Kenki >= SamuraiData.ShintenCost,
            Sam.Guren => sam.Kenki >= SamuraiData.SeneiCost,
            Sam.Zanshin => sam.HasStatus(Sam.ZanshinReady) && sam.Kenki >= SamuraiData.ZanshinCost,

            Sam.Shoha => sam.Meditation >= SamuraiData.MaxMeditation,

            _ when action.ComboFrom is not null => sam.IsComboReady(action) || sam.HasStatus(Sam.Meikyo),

            _ => true,
        };
    }

    /// A weaponskill pressed under Meikyo Shisui lands at its full combo potency despite having no combo
    /// behind it.
    public double EffectivePotency(ActionDef action, SimState state)
    {
        var sam = (SamuraiState)state;

        if (action.ComboFrom is null)
            return action.Potency;

        return sam.IsComboReady(action) || sam.HasStatus(Sam.Meikyo)
            ? action.ComboPotency
            : action.Potency;
    }

    /// The Setsugekka family and both Namikiri are guaranteed critical hits by their own tooltips.
    public bool IsAutoCrit(ActionDef action, SimState state) => action.Name is
        Sam.MidareSetsugekka or Sam.KaeshiSetsugekka or
        Sam.TendoSetsugekka or Sam.TendoKaeshiSetsugekka or
        Sam.OgiNamikiri or Sam.KaeshiNamikiri;

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var sam = (SamuraiState)state;

        if (action.IsGcd && action.ComboFrom is not null && !sam.IsComboReady(action) && sam.HasStatus(Sam.Meikyo))
        {
            sam.ConsumeStack(Sam.Meikyo);

            sam.Count($"meikyo.spent.{action.Name}", 1);
        }

        switch (action.Name)
        {
            case Sam.Gyofu:
                sam.GainKenki(5);
                sam.AdvanceCombo(action);
                return;

            case Sam.Jinpu:
                sam.GainKenki(5);
                sam.TrackBuff("fugetsu", sam.FugetsuExpiresAt);
                sam.FugetsuExpiresAt = sam.Time + SamuraiData.FugetsuDuration;
                sam.ApplyStatus(Sam.Fugetsu, SamuraiData.FugetsuDuration, damageMulti: SamuraiData.FugetsuMulti);
                sam.AdvanceCombo(action);
                return;

            case Sam.Shifu:
                sam.GainKenki(5);
                sam.TrackBuff("fuka", sam.FukaExpiresAt);
                sam.FukaExpiresAt = sam.Time + SamuraiData.FukaDuration;
                sam.ApplyStatus(Sam.Fuka, SamuraiData.FukaDuration);
                sam.AdvanceCombo(action);
                return;

            case Sam.Gekko:
                sam.GainKenki(10);
                sam.OpenSen(nameof(SamuraiState.Getsu));
                if (sam.HasStatus(Sam.Meikyo))
                {
                    sam.TrackBuff("fugetsu", sam.FugetsuExpiresAt);
                    sam.FugetsuExpiresAt = sam.Time + SamuraiData.FugetsuDuration;
                    sam.ApplyStatus(Sam.Fugetsu, SamuraiData.FugetsuDuration, damageMulti: SamuraiData.FugetsuMulti);
                }

                sam.AdvanceCombo(action);
                return;

            case Sam.Kasha:
                sam.GainKenki(10);
                sam.OpenSen(nameof(SamuraiState.Ka));
                if (sam.HasStatus(Sam.Meikyo))
                {
                    sam.TrackBuff("fuka", sam.FukaExpiresAt);
                    sam.FukaExpiresAt = sam.Time + SamuraiData.FukaDuration;
                    sam.ApplyStatus(Sam.Fuka, SamuraiData.FukaDuration);
                }

                sam.AdvanceCombo(action);
                return;

            case Sam.Yukikaze:
                sam.GainKenki(15);
                sam.OpenSen(nameof(SamuraiState.Setsu));
                sam.AdvanceCombo(action);
                return;

            case Sam.Fuko:
                sam.GainKenki(10);
                sam.AdvanceCombo(action);
                return;

            case Sam.Mangetsu:
                sam.GainKenki(5);
                sam.Getsu = true;
                sam.TrackBuff("fugetsu", sam.FugetsuExpiresAt);
                sam.FugetsuExpiresAt = sam.Time + SamuraiData.FugetsuDuration;
                sam.ApplyStatus(Sam.Fugetsu, SamuraiData.FugetsuDuration, damageMulti: SamuraiData.FugetsuMulti);
                sam.AdvanceCombo(action);
                return;

            case Sam.Oka:
                sam.GainKenki(5);
                sam.Ka = true;
                sam.TrackBuff("fuka", sam.FukaExpiresAt);
                sam.FukaExpiresAt = sam.Time + SamuraiData.FukaDuration;
                sam.ApplyStatus(Sam.Fuka, SamuraiData.FukaDuration);
                sam.AdvanceCombo(action);
                return;

            case Sam.TenkaGoken:
                sam.ClearSen();
                sam.GainMeditation();
                sam.ApplyStatus(Sam.TsubameReady, SamuraiData.TsubameDuration);
                sam.BreakCombo();
                return;

            case Sam.TendoGoken:
                sam.ClearSen();
                sam.GainMeditation();
                sam.ApplyStatus(Sam.TendoKaeshiReady, SamuraiData.TsubameDuration);
                sam.RemoveStatus(Sam.Tendo);
                sam.BreakCombo();
                return;

            case Sam.KaeshiGoken:
                sam.RemoveStatus(Sam.TsubameReady);
                sam.BreakCombo();
                return;

            case Sam.TendoKaeshiGoken:
                sam.RemoveStatus(Sam.TendoKaeshiReady);
                sam.BreakCombo();
                return;

            case Sam.Higanbana:
                sam.ClearSen();
                sam.GainMeditation();
                sam.BreakCombo();

                var remaining = Math.Max(0, sam.HiganbanaExpiresAt - sam.Time);
                var alreadyPaid = (int)Math.Round(remaining / (SamuraiData.HiganbanaDotDuration / SamuraiData.HiganbanaTicks));
                var ticks = Math.Max(0, SamuraiData.HiganbanaTicks - alreadyPaid);

                recordHit(Sam.HiganbanaDot, SamuraiData.HiganbanaTickPotency * ticks);
                sam.HiganbanaExpiresAt = sam.Time + SamuraiData.HiganbanaDotDuration;
                return;

            case Sam.MidareSetsugekka:
                sam.ClearSen();
                sam.GainMeditation();
                sam.ApplyStatus(Sam.TsubameReady, SamuraiData.TsubameDuration);
                sam.BreakCombo();
                return;

            case Sam.TendoSetsugekka:
                sam.ClearSen();
                sam.GainMeditation();
                sam.ApplyStatus(Sam.TendoKaeshiReady, SamuraiData.TsubameDuration);
                sam.RemoveStatus(Sam.Tendo);
                sam.BreakCombo();
                return;

            case Sam.KaeshiSetsugekka:
                sam.RemoveStatus(Sam.TsubameReady);
                sam.BreakCombo();
                return;

            case Sam.TendoKaeshiSetsugekka:
                sam.RemoveStatus(Sam.TendoKaeshiReady);
                sam.BreakCombo();
                return;

            case Sam.OgiNamikiri:
                sam.RemoveStatus(Sam.OgiReady);
                sam.GainMeditation();
                sam.ApplyStatus(Sam.KaeshiNamikiriReady, SamuraiData.TsubameDuration);
                sam.BreakCombo();
                return;

            case Sam.KaeshiNamikiri:
                sam.RemoveStatus(Sam.KaeshiNamikiriReady);
                sam.BreakCombo();
                return;

            case Sam.Shinten:
                sam.Kenki -= SamuraiData.ShintenCost;
                return;

            case Sam.Gyoten:
                sam.Kenki -= SamuraiData.GyotenCost;
                return;

            case Sam.Senei:
                sam.Kenki -= SamuraiData.SeneiCost;
                return;

            case Sam.Zanshin:
                sam.Kenki -= SamuraiData.ZanshinCost;
                sam.RemoveStatus(Sam.ZanshinReady);
                return;

            case Sam.Shoha:
                sam.Meditation = 0;
                return;

            case Sam.MeikyoAction:
                sam.ApplyStatus(Sam.Meikyo, SamuraiData.MeikyoDuration, stacks: SamuraiData.MeikyoStacks);
                sam.ApplyStatus(Sam.Tendo, SamuraiData.TendoDuration);
                return;

            case Sam.Ikishoten:
                sam.GainKenki(50);
                sam.ApplyStatus(Sam.OgiReady, SamuraiData.IkishotenBuffDuration);
                sam.ApplyStatus(Sam.ZanshinReady, SamuraiData.IkishotenBuffDuration);
                return;
        }
    }
}
