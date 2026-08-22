using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Summoner;

/// Summoner's two cycles and the resources hanging off them.
public sealed class SummonerState : SimState
{
    /// Where the demi cycle is: Solar Bahamut, Bahamut, Solar Bahamut, Phoenix, repeat.
    public int DemiIndex { get; set; }

    /// Where the egi cycle is: Ifrit, Titan, Garuda.
    public int EgiIndex { get; set; }

    /// The demi currently summoned, or null outside a trance.
    public string? ActiveDemi { get; set; }

    /// Trance filler casts still owed.
    public int TranceCastsLeft { get; set; }

    /// Whether this trance's enkindle and Astral Flow are still banked.
    public bool EnkindleReady { get; set; }

    public bool AstralFlowReady { get; set; }

    /// The egi currently attuned, or null when none is.
    public string? ActiveEgi { get; set; }

    /// Gemshine casts still owed by the attuned egi.
    public int GemshineLeft { get; set; }

    public int Aetherflow { get; set; }

    /// Ifrit's Favor spends two global cooldowns rather than one.
    public int IfritFavorCasts { get; set; }

    /// Egis summoned since the last demi, which caps the egi cycle at one full pass.
    public int EgisThisCycle { get; set; }

    public override SimState Clone()
    {
        var copy = new SummonerState
        {
            DemiIndex = DemiIndex,
            EgiIndex = EgiIndex,
            EgisThisCycle = EgisThisCycle,
            ActiveDemi = ActiveDemi,
            TranceCastsLeft = TranceCastsLeft,
            EnkindleReady = EnkindleReady,
            AstralFlowReady = AstralFlowReady,
            ActiveEgi = ActiveEgi,
            GemshineLeft = GemshineLeft,
            Aetherflow = Aetherflow,
            IfritFavorCasts = IfritFavorCasts,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Summoner's rules: what is legal, what it is worth, and where the loop goes next.
public sealed class SummonerSim : IJobSim
{
    public string JobName => "Summoner";

    public MainAttribute MainAttribute => MainAttribute.Intelligence;

    public CombatRole Role => CombatRole.Caster;

    public int MainStatModifier { get; init; } = 115;

    public int HastePercent => 0;

    /// Zero, like every other caster - a Summoner's swings land for one point apiece.
    public int AutoAttackPotency => 0;

    /// The magic damage term, shared with the other casters - see XivMath.MaimAndMend.
    public double TraitMultiplier => XivMath.MaimAndMend;

    public IReadOnlyDictionary<string, ActionDef> Actions => SummonerData.Actions;

    public SimState CreateState()
    {
        var state = new SummonerState();

        foreach (var (name, action) in SummonerData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    public bool CanUse(ActionDef action, SimState state)
    {
        var smn = (SummonerState)state;

        return action.Name switch
        {
            Smn.SummonSolarBahamut or Smn.SummonBahamut or Smn.SummonPhoenix =>
                smn.ActiveDemi is null && smn.ActiveEgi is null
                && SummonerData.DemiCycle[smn.DemiIndex] == action.Name,

            Smn.UmbralImpulse or Smn.AstralImpulse or Smn.FountainOfFire =>
                smn.ActiveDemi is not null
                && SummonerData.FillerFor(smn.ActiveDemi) == action.Name
                && smn.TranceCastsLeft > 0,

            Smn.UmbralFlare or Smn.AstralFlare or Smn.BrandOfPurgatory =>
                smn.ActiveDemi is not null
                && SummonerData.AreaFillerFor(smn.ActiveDemi) == action.Name
                && smn.TranceCastsLeft > 0,

            Smn.EnkindleSolarBahamut or Smn.EnkindleBahamut or Smn.EnkindlePhoenix =>
                smn.ActiveDemi is not null
                && SummonerData.EnkindleFor(smn.ActiveDemi) == action.Name
                && smn.EnkindleReady,

            Smn.Sunflare or Smn.Deathflare or Smn.Rekindle =>
                smn.ActiveDemi is not null
                && SummonerData.AstralFlowFor(smn.ActiveDemi) == action.Name
                && smn.AstralFlowReady,

            Smn.SummonIfrit or Smn.SummonTitan or Smn.SummonGaruda =>
                smn.ActiveDemi is null && smn.ActiveEgi is null
                && smn.EgisThisCycle < SummonerData.EgiCycle.Length
                && SummonerData.EgiCycle[smn.EgiIndex] == action.Name,

            Smn.RubyRite or Smn.TopazRite or Smn.EmeraldRite =>
                smn.ActiveEgi is not null
                && SummonerData.GemshineFor(smn.ActiveEgi) == action.Name
                && smn.GemshineLeft > 0,

            Smn.RubyCatastrophe or Smn.TopazCatastrophe or Smn.EmeraldCatastrophe =>
                smn.ActiveEgi is not null
                && SummonerData.AreaGemshineFor(smn.ActiveEgi) == action.Name
                && smn.GemshineLeft > 0,

            Smn.CrimsonCyclone => smn.HasStatus(Smn.IfritsFavor) && smn.IfritFavorCasts == 0,
            Smn.CrimsonStrike => smn.HasStatus(Smn.IfritsFavor) && smn.IfritFavorCasts == 1,

            Smn.Slipstream => smn.HasStatus(Smn.GarudasFavor),
            Smn.MountainBuster => smn.HasStatus(Smn.TitansFavor),

            Smn.Ruin4 => smn.HasStatus(Smn.FurtherRuin),
            Smn.SearingFlash => smn.HasStatus(Smn.RubysGlimmer),

            Smn.Necrotize => smn.Aetherflow > 0,

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state)
        => SummonerData.IsPetAction(action.Name)
            ? action.Potency * SummonerData.PetPotencyMultiplier
            : action.Potency;

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var smn = (SummonerState)state;

        switch (action.Name)
        {
            case Smn.SummonSolarBahamut:
            case Smn.SummonBahamut:
            case Smn.SummonPhoenix:
                smn.ActiveDemi = action.Name;
                smn.TranceCastsLeft = SummonerData.TranceCasts;
                smn.EnkindleReady = true;
                smn.AstralFlowReady = true;
                smn.DemiIndex = (smn.DemiIndex + 1) % SummonerData.DemiCycle.Length;
                smn.EgisThisCycle = 0;

                if (action.Name == Smn.SummonSolarBahamut)
                    smn.ApplyStatus(Smn.RefulgentLux, 30.0);

                return;

            case Smn.UmbralFlare:
            case Smn.AstralFlare:
            case Smn.BrandOfPurgatory:
            case Smn.UmbralImpulse:
            case Smn.AstralImpulse:
            case Smn.FountainOfFire:
                if (smn.ActiveDemi is not null
                    && SummonerData.TranceCasts - smn.TranceCastsLeft < SummonerData.WavesPerTrance)
                {
                    var (wave, potency) = SummonerData.WaveFor(smn.ActiveDemi);
                    recordHit(wave, potency * SummonerData.PetPotencyMultiplier, false, false);
                }

                smn.TranceCastsLeft--;

                if (smn.TranceCastsLeft <= 0)
                    smn.ActiveDemi = null;

                return;

            case Smn.EnkindleSolarBahamut:
            case Smn.EnkindleBahamut:
            case Smn.EnkindlePhoenix:
                smn.EnkindleReady = false;
                return;

            case Smn.Sunflare:
            case Smn.Deathflare:
            case Smn.Rekindle:
                smn.AstralFlowReady = false;
                return;

            case Smn.SummonIfrit:
            case Smn.SummonTitan:
            case Smn.SummonGaruda:
                smn.ActiveEgi = action.Name;
                smn.GemshineLeft = SummonerData.GemshineCasts;
                smn.EgiIndex = (smn.EgiIndex + 1) % SummonerData.EgiCycle.Length;
                smn.EgisThisCycle++;

                if (action.Name == Smn.SummonIfrit)
                {
                    smn.ApplyStatus(Smn.IfritsFavor, SummonerData.FavorDuration);
                    smn.IfritFavorCasts = 0;
                }
                else if (action.Name == Smn.SummonGaruda)
                {
                    smn.ApplyStatus(Smn.GarudasFavor, SummonerData.FavorDuration);
                }

                return;

            case Smn.TopazCatastrophe:
            case Smn.TopazRite:
                smn.ApplyStatus(Smn.TitansFavor, SummonerData.FavorDuration);
                SpendGemshine(smn);
                return;

            case Smn.RubyCatastrophe:
            case Smn.EmeraldCatastrophe:
            case Smn.RubyRite:
            case Smn.EmeraldRite:
                SpendGemshine(smn);
                return;

            case Smn.CrimsonCyclone:
                smn.IfritFavorCasts = 1;
                SpendGemshine(smn);
                return;

            case Smn.CrimsonStrike:
                smn.IfritFavorCasts = 0;
                smn.RemoveStatus(Smn.IfritsFavor);
                SpendGemshine(smn);
                return;

            case Smn.Slipstream:
                smn.RemoveStatus(Smn.GarudasFavor);
                return;

            case Smn.MountainBuster:
                smn.RemoveStatus(Smn.TitansFavor);
                return;

            case Smn.EnergyDrain:
                smn.Aetherflow = SummonerData.AetherflowStacks;
                smn.ApplyStatus(Smn.FurtherRuin, SummonerData.FurtherRuinDuration);
                return;

            case Smn.Necrotize:
                smn.Aetherflow--;
                return;

            case Smn.Ruin4:
                smn.RemoveStatus(Smn.FurtherRuin);
                return;

            case Smn.SearingLightAction:
                smn.ApplyStatus(Smn.SearingLightStatus, SummonerData.SearingLightDuration,
                    damageMulti: SummonerData.SearingLightBonus);
                smn.ApplyStatus(Smn.RubysGlimmer, SummonerData.RubysGlimmerDuration);
                return;

            case Smn.SearingFlash:
                smn.RemoveStatus(Smn.RubysGlimmer);
                return;
        }
    }

    /// Spends one of the attuned egi's four casts, releasing the attunement when they run out.
    private static void SpendGemshine(SummonerState smn)
    {
        smn.GemshineLeft--;

        if (smn.GemshineLeft <= 0)
            smn.ActiveEgi = null;
    }
}
