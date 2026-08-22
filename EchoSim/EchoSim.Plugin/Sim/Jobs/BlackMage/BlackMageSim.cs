using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.BlackMage;

/// Black Mage's elemental gauge, MP, and the four resources that hang off them.
public sealed class BlackMageState : SimState
{
    public int Mp { get; set; } = BlackMageData.MaxMp;

    /// Astral Fire stacks, 0-3.
    public int AstralFire { get; set; }

    /// Umbral Ice stacks, 0-3.
    public int UmbralIce { get; set; }

    /// Umbral Hearts.
    public int UmbralHearts { get; set; }

    /// Astral Souls, one per Fire IV, six for a Flare Star.
    public int AstralSoul { get; set; }

    public int Polyglot { get; set; }

    /// Whether a Paradox is banked.
    public bool ParadoxReady { get; set; }

    /// When the next server tick pays MP out.
    public double NextMpTickAt { get; set; } = BlackMageData.MpTickInterval;

    /// When the next Polyglot arrives, while an element is up.
    public double NextPolyglotAt { get; set; } = BlackMageData.PolyglotInterval;

    /// When High Thunder's damage-over-time runs out, for netting out a refresh.
    public double DotExpiresAt { get; set; }

    public bool InAstralFire => AstralFire > 0;

    public bool InUmbralIce => UmbralIce > 0;

    /// Pays out every server tick between the last one and time.
    public void AdvanceResources(double time)
    {
        while (NextMpTickAt <= time + 1e-9)
        {
            var perTick = InAstralFire
                ? BlackMageData.AstralFireMpPerTick
                : BlackMageData.UmbralIceMpPerTick[UmbralIce];

            GainMp(perTick);
            NextMpTickAt += BlackMageData.MpTickInterval;
        }

        while (NextPolyglotAt <= time + 1e-9)
        {
            if (InAstralFire || InUmbralIce)
                GainPolyglot();

            NextPolyglotAt += BlackMageData.PolyglotInterval;
        }
    }

    public void GainMp(int amount)
    {
        var room = BlackMageData.MaxMp - Mp;
        if (amount > room)
            Count("mp.overcapped", amount - room);

        Mp = System.Math.Min(BlackMageData.MaxMp, Mp + amount);
    }

    public void GainPolyglot()
    {
        if (Polyglot >= BlackMageData.MaxPolyglot)
        {
            Count("polyglot.overcapped", 1);
            return;
        }

        Polyglot++;
    }

    public void GainAstralSoul()
    {
        if (AstralSoul >= BlackMageData.AstralSoulsForFlareStar)
        {
            Count("astralsoul.overcapped", 1);
            return;
        }

        AstralSoul++;
    }

    /// Enter Astral Fire at the given stack count, dropping any Umbral Ice.
    public void EnterAstralFire(int stacks)
    {
        var fromFullIce = UmbralIce >= BlackMageData.MaxElementStacks
                          && UmbralHearts >= BlackMageData.MaxUmbralHearts;

        UmbralIce = 0;
        AstralFire = System.Math.Min(BlackMageData.MaxElementStacks, stacks);

        if (fromFullIce)
            ParadoxReady = true;
    }

    /// Enter Umbral Ice at the given stack count, dropping any Astral Fire and the souls with it.
    public void EnterUmbralIce(int stacks)
    {
        if (AstralFire > 0 && AstralSoul > 0)
            Count("astralsoul.lost", AstralSoul);

        var fromFullFire = AstralFire >= BlackMageData.MaxElementStacks;

        AstralFire = 0;
        AstralSoul = 0;
        UmbralIce = System.Math.Min(BlackMageData.MaxElementStacks, stacks);

        if (fromFullFire)
            ParadoxReady = true;
    }

    public override SimState Clone()
    {
        var copy = new BlackMageState
        {
            Mp = Mp,
            AstralFire = AstralFire,
            UmbralIce = UmbralIce,
            UmbralHearts = UmbralHearts,
            AstralSoul = AstralSoul,
            Polyglot = Polyglot,
            ParadoxReady = ParadoxReady,
            NextMpTickAt = NextMpTickAt,
            NextPolyglotAt = NextPolyglotAt,
            DotExpiresAt = DotExpiresAt,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Black Mage's rules: what is legal, what it is worth, and what it does to the elemental gauge.
public sealed class BlackMageSim : IJobSim
{
    public string JobName => "Black Mage";

    public MainAttribute MainAttribute => MainAttribute.Intelligence;

    public CombatRole Role => CombatRole.Caster;

    public int MainStatModifier { get; init; } = 115;

    /// None.
    public int HastePercent => 0;

    /// The magic damage term, which is NOT a job trait despite living in the trait slot.
    public double TraitMultiplier => XivMath.MaimAndMend;

    /// Zero, which is measured rather than an omission.
    public int AutoAttackPotency => 0;

    public IReadOnlyDictionary<string, ActionDef> Actions => BlackMageData.Actions;

    public SimState CreateState()
    {
        var state = new BlackMageState();

        foreach (var (name, action) in BlackMageData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    /// Ley Lines cuts cast and recast alike by 15%, which is exactly what transient haste is.
    public int TransientHastePercent(SimState state)
        => state.HasStatus(Blm.LeyLinesStatus) ? BlackMageData.LeyLinesHastePercent : 0;

    /// What the cast bar actually runs for.
    public double CastTimeOf(ActionDef action, SimState state)
    {
        var blm = (BlackMageState)state;

        if (action.CastTime <= 0)
            return 0;

        if (blm.HasStatus(Blm.SwiftcastStatus) || blm.HasStatus(Blm.TriplecastStatus))
            return 0;

        return action.Name == Blm.Fire3 && blm.HasStatus(Blm.Firestarter) ? 0 : action.CastTime;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var blm = (BlackMageState)state;
        blm.AdvanceResources(blm.Time);

        return action.Name switch
        {
            Blm.FlareStar => blm.AstralSoul >= BlackMageData.AstralSoulsForFlareStar,
            Blm.Xenoglossy or Blm.Foul => blm.Polyglot > 0,
            Blm.HighThunder or Blm.HighThunder2 => blm.HasStatus(Blm.Thunderhead),
            Blm.Paradox => blm.ParadoxReady && blm.Mp >= MpCost(action, blm),
            Blm.Despair => blm.InAstralFire && blm.Mp >= BlackMageData.DespairMinimumMp,

            Blm.Flare => blm.InAstralFire && blm.Mp >= BlackMageData.FlareMinimumMp,

            Blm.Freeze => blm.InUmbralIce && blm.Mp >= MpCost(action, blm),

            Blm.Blizzard4 => blm.InUmbralIce && blm.Mp >= MpCost(action, blm),

            Blm.Transpose => blm.InAstralFire || blm.InUmbralIce,

            _ => blm.Mp >= MpCost(action, blm),
        };
    }

    /// What a spell costs right now.
    public static int MpCost(ActionDef action, BlackMageState blm)
    {
        if (action.Name == Blm.Despair)
            return blm.Mp;

        if (action.Name == Blm.Flare)
        {
            return blm.UmbralHearts > 0
                ? (int)System.Math.Round(blm.Mp * BlackMageData.FlareCostWithHearts)
                : blm.Mp;
        }

        if (action.Name == Blm.Fire3 && blm.HasStatus(Blm.Firestarter))
            return 0;

        var baseCost = BaseMpCost.GetValueOrDefault(action.Name);
        if (baseCost == 0)
            return 0;

        return BlackMageData.AspectOf(action.Name) switch
        {
            Aspect.Fire when blm.UmbralIce >= BlackMageData.MaxElementStacks => 0,
            Aspect.Fire when blm.InUmbralIce => baseCost / 2,
            Aspect.Fire when blm.InAstralFire => blm.UmbralHearts > 0 ? baseCost : baseCost * 2,
            Aspect.Ice when blm.InUmbralIce => 0,

            _ when action.Name == Blm.Paradox => blm.InUmbralIce ? 0 : baseCost,

            _ => baseCost,
        };
    }

    private static readonly IReadOnlyDictionary<string, int> BaseMpCost = new Dictionary<string, int>
    {
        [Blm.Fire3] = 2000,
        [Blm.Fire4] = 800,
        [Blm.Blizzard3] = 800,
        [Blm.Blizzard4] = 800,
        [Blm.Paradox] = 1600,
        [Blm.Freeze] = 1000,
    };

    /// Potency after the elemental gauge, which for a Fire spell under Astral Fire III is most of it.
    public double EffectivePotency(ActionDef action, SimState state)
    {
        var blm = (BlackMageState)state;
        var potency = (double)action.Potency;

        var aspected = BlackMageData.AspectOf(action.Name) switch
        {
            Aspect.Fire when blm.InAstralFire => potency * BlackMageData.AstralFireOnFire[blm.AstralFire],
            Aspect.Fire when blm.InUmbralIce => potency * BlackMageData.UmbralIceOnFire,
            Aspect.Ice => potency * BlackMageData.AnyElementOnIce,
            _ => potency,
        };

        return blm.InAstralFire || blm.InUmbralIce ? aspected * BlackMageData.Enochian : aspected;
    }

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var blm = (BlackMageState)state;
        blm.AdvanceResources(blm.Time);

        blm.Mp -= MpCost(action, blm);
        if (blm.Mp < 0)
            blm.Mp = 0;

        if (action.IsGcd && action.CastTime > 0)
        {
            if (blm.HasStatus(Blm.SwiftcastStatus))
                blm.RemoveStatus(Blm.SwiftcastStatus);
            else if (blm.HasStatus(Blm.TriplecastStatus))
                blm.ConsumeStack(Blm.TriplecastStatus);
        }

        switch (action.Name)
        {
            case Blm.Fire3:
                blm.RemoveStatus(Blm.Firestarter);
                GrantThunderheadOnEntry(blm, wasInFire: blm.InAstralFire);
                blm.EnterAstralFire(BlackMageData.MaxElementStacks);
                return;

            case Blm.Fire4:
                if (blm.UmbralHearts > 0)
                    blm.UmbralHearts--;

                blm.GainAstralSoul();
                return;

            case Blm.Despair:
                blm.Mp = 0;
                return;

            case Blm.FlareStar:
                blm.AstralSoul = 0;
                return;

            case Blm.Flare:
                blm.UmbralHearts = 0;

                for (var i = 0; i < BlackMageData.FlareAstralSouls; i++)
                    blm.GainAstralSoul();

                blm.EnterAstralFire(BlackMageData.MaxElementStacks);
                return;

            case Blm.Blizzard3:
                GrantThunderheadOnEntry(blm, wasInFire: blm.InAstralFire);
                blm.EnterUmbralIce(BlackMageData.MaxElementStacks);
                return;

            case Blm.Blizzard4:
                blm.UmbralHearts = BlackMageData.MaxUmbralHearts;
                return;

            case Blm.Freeze:
                blm.UmbralHearts = BlackMageData.MaxUmbralHearts;
                return;

            case Blm.Paradox:
                blm.ParadoxReady = false;
                if (blm.InAstralFire)
                    blm.ApplyStatus(Blm.Firestarter, BlackMageData.FirestarterDuration);

                return;

            case Blm.Xenoglossy:
            case Blm.Foul:
                blm.Polyglot--;
                return;

            case Blm.HighThunder:
            case Blm.HighThunder2:
                blm.RemoveStatus(Blm.Thunderhead);
                ApplyDot(blm, recordHit, action);
                return;

            case Blm.Transpose:
                if (blm.InAstralFire)
                    blm.EnterUmbralIce(1);
                else if (blm.InUmbralIce)
                    blm.EnterAstralFire(1);

                blm.ApplyStatus(Blm.Thunderhead, BlackMageData.ThunderheadDuration);
                return;

            case Blm.Manafont:
                blm.Mp = BlackMageData.MaxMp;
                blm.EnterAstralFire(BlackMageData.MaxElementStacks);
                blm.UmbralHearts = BlackMageData.MaxUmbralHearts;
                blm.ParadoxReady = true;
                blm.ApplyStatus(Blm.Thunderhead, BlackMageData.ThunderheadDuration);
                return;

            case Blm.Amplifier:
                blm.GainPolyglot();
                return;

            case Blm.LeyLinesAction:
                blm.ApplyStatus(Blm.LeyLinesStatus, BlackMageData.LeyLinesDuration);
                return;

            case Blm.TriplecastAction:
                blm.ApplyStatus(Blm.TriplecastStatus, BlackMageData.TriplecastDuration,
                    stacks: BlackMageData.TriplecastCharges);
                return;

            case Blm.SwiftcastAction:
                blm.ApplyStatus(Blm.SwiftcastStatus, BlackMageData.SwiftcastDuration);
                return;
        }
    }

    /// Thunderhead arrives on crossing between the two elements, which is what keeps High Thunder available
    /// roughly once every thirty seconds without anything tracking a recast for it.
    private static void GrantThunderheadOnEntry(BlackMageState blm, bool wasInFire)
        => blm.ApplyStatus(Blm.Thunderhead, BlackMageData.ThunderheadDuration);

    /// High Thunder's damage-over-time, paid as one lump at application.
    private static void ApplyDot(BlackMageState blm, HitRecorder recordHit, ActionDef action)
    {
        var remaining = System.Math.Max(0, blm.DotExpiresAt - blm.Time);
        var fresh = BlackMageData.HighThunderDotDuration - remaining;

        blm.DotExpiresAt = blm.Time + BlackMageData.HighThunderDotDuration;

        if (remaining > 0)
            blm.Count("dot.clipped", remaining);

        if (fresh <= 0)
            return;

        var ticks = fresh / BlackMageData.DotTickInterval;
        var source = action.Name == Blm.HighThunder2 ? Blm.HighThunder2Ticks : Blm.HighThunderTicks;

        recordHit(
            source,
            ticks * BlackMageData.HighThunderDotPotency * action.TargetMultiplier(blm.Targets));
    }
}
