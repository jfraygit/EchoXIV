using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Pictomancer;

/// Pictomancer's canvases, gauges and paint.
public sealed class PictomancerState : SimState
{
    public int PaletteGauge { get; set; }

    public int WhitePaint { get; set; }

    public int BlackPaint { get; set; }

    /// Where the aetherhue trio is: 0 Fire, 1 Aero, 2 Water.
    public int AetherhueIndex { get; set; }

    /// Where the subtractive trio is: 0 Blizzard, 1 Stone, 2 Thunder.
    public int SubtractiveIndex { get; set; }

    /// Which hammer comes next: 0 Stamp, 1 Brush, 2 Polishing.
    public int HammerIndex { get; set; }

    public bool CreatureCanvas { get; set; }

    public bool WeaponCanvas { get; set; }

    public bool LandscapeCanvas { get; set; }

    /// How many Living Muses have been spent, which is what decides the portrait.
    public int MusesSpent { get; set; }

    public bool MoogleReady { get; set; }

    public bool MadeenReady { get; set; }

    public void GainPalette(int amount)
    {
        var room = PictomancerData.MaxPaletteGauge - PaletteGauge;
        if (amount > room)
            Count("palette.overcapped", amount - room);

        PaletteGauge = System.Math.Min(PictomancerData.MaxPaletteGauge, PaletteGauge + amount);
    }

    public void GainWhitePaint()
    {
        if (WhitePaint >= PictomancerData.MaxWhitePaint)
        {
            Count("whitepaint.overcapped", 1);
            return;
        }

        WhitePaint++;
    }

    public override SimState Clone()
    {
        var copy = new PictomancerState
        {
            PaletteGauge = PaletteGauge,
            WhitePaint = WhitePaint,
            BlackPaint = BlackPaint,
            AetherhueIndex = AetherhueIndex,
            SubtractiveIndex = SubtractiveIndex,
            HammerIndex = HammerIndex,
            CreatureCanvas = CreatureCanvas,
            WeaponCanvas = WeaponCanvas,
            LandscapeCanvas = LandscapeCanvas,
            MusesSpent = MusesSpent,
            MoogleReady = MoogleReady,
            MadeenReady = MadeenReady,
        };

        CopyInto(copy);
        return copy;
    }
}

/// Pictomancer's rules: what is legal, what it is worth, and what it does to five resources.
public sealed class PictomancerSim : IJobSim
{
    public string JobName => "Pictomancer";

    public MainAttribute MainAttribute => MainAttribute.Intelligence;

    public CombatRole Role => CombatRole.Caster;

    public int MainStatModifier { get; init; } = 115;

    public int HastePercent => 0;

    /// Zero, and measured the same way Red Mage's was: the reference parse logs 118 auto-attack swings for
    /// 118 total damage.
    public int AutoAttackPotency => 0;

    /// The magic damage term.
    public double TraitMultiplier => XivMath.MaimAndMend;

    public IReadOnlyDictionary<string, ActionDef> Actions => PictomancerData.Actions;

    public SimState CreateState()
    {
        var state = new PictomancerState();

        foreach (var (name, action) in PictomancerData.Actions)
        {
            if (action.Cooldown > 0)
                state.RegisterCooldown(name, action.Cooldown, action.MaxCharges);
        }

        return state;
    }

    /// Inspiration shortens every cast and recast in the job except the motifs.
    public int TransientHastePercent(SimState state)
        => state.HasStatus(Pct.Inspiration) ? (int)PictomancerData.InspirationHaste : 0;

    /// Rainbow Bright makes Rainbow Drip instant.
    public double CastTimeOf(ActionDef action, SimState state)
        => action.Name == Pct.RainbowDrip && state.HasStatus(Pct.RainbowBright) ? 0 : action.CastTime;

    /// The hammers, and only the hammers.
    public bool IsAutoCrit(ActionDef action, SimState state)
        => PictomancerData.HammerChain.Contains(action.Name);

    public bool IsAutoDirectHit(ActionDef action, SimState state)
        => PictomancerData.HammerChain.Contains(action.Name);

    public bool CanUse(ActionDef action, SimState state)
    {
        var pct = (PictomancerState)state;

        return action.Name switch
        {
            Pct.FireInRed or Pct.Fire2InRed
                => !pct.HasStatus(Pct.SubtractivePalette) && pct.AetherhueIndex == 0,
            Pct.AeroInGreen or Pct.Aero2InGreen
                => !pct.HasStatus(Pct.SubtractivePalette) && pct.AetherhueIndex == 1,
            Pct.WaterInBlue or Pct.Water2InBlue
                => !pct.HasStatus(Pct.SubtractivePalette) && pct.AetherhueIndex == 2,

            Pct.BlizzardInCyan or Pct.Blizzard2InCyan
                => pct.HasStatus(Pct.SubtractivePalette) && pct.SubtractiveIndex == 0,
            Pct.StoneInYellow or Pct.Stone2InYellow
                => pct.HasStatus(Pct.SubtractivePalette) && pct.SubtractiveIndex == 1,
            Pct.ThunderInMagenta or Pct.Thunder2InMagenta
                => pct.HasStatus(Pct.SubtractivePalette) && pct.SubtractiveIndex == 2,

            Pct.CometInBlack => pct.BlackPaint > 0,
            Pct.HolyInWhite => pct.WhitePaint > 0 && !pct.HasStatus(Pct.MonochromeTones),

            Pct.HammerStamp => pct.HasStatus(Pct.HammerTime) && pct.HammerIndex == 0,
            Pct.HammerBrush => pct.HasStatus(Pct.HammerTime) && pct.HammerIndex == 1,
            Pct.PolishingHammer => pct.HasStatus(Pct.HammerTime) && pct.HammerIndex == 2,

            Pct.StarPrism => pct.HasStatus(Pct.Starstruck),

            Pct.CreatureMotif => !pct.CreatureCanvas,
            Pct.WeaponMotif => !pct.WeaponCanvas,
            Pct.LandscapeMotif => !pct.LandscapeCanvas,

            Pct.LivingMuse => pct.CreatureCanvas,
            Pct.StrikingMuse => pct.WeaponCanvas,
            Pct.StarryMuse => pct.LandscapeCanvas,
            Pct.MogOfTheAges => pct.MoogleReady,
            Pct.RetributionOfTheMadeen => pct.MadeenReady,

            Pct.SubtractivePaletteAction => !pct.HasStatus(Pct.SubtractivePalette)
                                            && (pct.HasStatus(Pct.SubtractiveSpectrum)
                                                || pct.PaletteGauge >= PictomancerData.SubtractivePaletteCost),

            _ => true,
        };
    }

    public double EffectivePotency(ActionDef action, SimState state) => action.Potency;

    public void OnExecuted(ActionDef action, SimState state, HitRecorder recordHit)
    {
        var pct = (PictomancerState)state;

        if (PictomancerData.SpendsHyperphantasia(action.Name) && pct.HasStatus(Pct.Hyperphantasia))
        {
            pct.ConsumeStack(Pct.Hyperphantasia);

            if (!pct.HasStatus(Pct.Hyperphantasia))
                pct.ApplyStatus(Pct.RainbowBright, PictomancerData.RainbowBrightDuration);
        }

        switch (action.Name)
        {
            case Pct.FireInRed:
            case Pct.AeroInGreen:
            case Pct.Fire2InRed:
            case Pct.Aero2InGreen:
                pct.AetherhueIndex++;
                return;

            case Pct.WaterInBlue:
            case Pct.Water2InBlue:
                pct.AetherhueIndex = 0;
                pct.GainPalette(PictomancerData.PaletteGaugePerWater);
                pct.GainWhitePaint();
                return;

            case Pct.BlizzardInCyan:
            case Pct.StoneInYellow:
            case Pct.Blizzard2InCyan:
            case Pct.Stone2InYellow:
                pct.SubtractiveIndex++;
                pct.ConsumeStack(Pct.SubtractivePalette);
                return;

            case Pct.ThunderInMagenta:
            case Pct.Thunder2InMagenta:
                pct.SubtractiveIndex = 0;
                pct.ConsumeStack(Pct.SubtractivePalette);
                pct.GainWhitePaint();
                return;

            case Pct.CometInBlack:
                pct.BlackPaint--;
                return;

            case Pct.HolyInWhite:
                pct.WhitePaint--;
                return;

            case Pct.HammerStamp:
            case Pct.HammerBrush:
                pct.HammerIndex++;
                pct.ConsumeStack(Pct.HammerTime);
                return;

            case Pct.PolishingHammer:
                pct.HammerIndex = 0;
                pct.ConsumeStack(Pct.HammerTime);
                return;

            case Pct.StarPrism:
                pct.RemoveStatus(Pct.Starstruck);
                return;

            case Pct.RainbowDrip:
                pct.GainWhitePaint();
                pct.RemoveStatus(Pct.RainbowBright);
                return;

            case Pct.CreatureMotif:
                pct.CreatureCanvas = true;
                return;

            case Pct.WeaponMotif:
                pct.WeaponCanvas = true;
                return;

            case Pct.LandscapeMotif:
                pct.LandscapeCanvas = true;
                return;

            case Pct.LivingMuse:
                pct.CreatureCanvas = false;
                pct.MusesSpent++;

                if (pct.MusesSpent % 4 == 2)
                    pct.MoogleReady = true;
                else if (pct.MusesSpent % 4 == 0)
                    pct.MadeenReady = true;

                return;

            case Pct.MogOfTheAges:
                pct.MoogleReady = false;
                return;

            case Pct.RetributionOfTheMadeen:
                pct.MadeenReady = false;
                return;

            case Pct.StrikingMuse:
                pct.WeaponCanvas = false;
                pct.HammerIndex = 0;
                pct.ApplyStatus(Pct.HammerTime, PictomancerData.HammerTimeDuration,
                    stacks: PictomancerData.HammerTimeStacks);
                return;

            case Pct.StarryMuse:
                pct.LandscapeCanvas = false;
                pct.ApplyStatus(Pct.StarryMuseBuff, PictomancerData.StarryMuseDuration,
                    damageMulti: PictomancerData.StarryMuseBonus);
                pct.ApplyStatus(Pct.Inspiration, PictomancerData.StarryMuseDuration);
                pct.ApplyStatus(Pct.Hyperphantasia, PictomancerData.HyperphantasiaDuration,
                    stacks: PictomancerData.HyperphantasiaStacks);
                pct.ApplyStatus(Pct.SubtractiveSpectrum, PictomancerData.SubtractiveSpectrumDuration);
                pct.ApplyStatus(Pct.Starstruck, PictomancerData.StarstruckDuration);
                return;

            case Pct.SubtractivePaletteAction:
                if (pct.HasStatus(Pct.SubtractiveSpectrum))
                    pct.RemoveStatus(Pct.SubtractiveSpectrum);
                else
                    pct.PaletteGauge -= PictomancerData.SubtractivePaletteCost;

                pct.SubtractiveIndex = 0;
                pct.ApplyStatus(Pct.SubtractivePalette, duration: 3600,
                    stacks: PictomancerData.SubtractivePaletteStacks);

                if (pct.WhitePaint > 0)
                {
                    pct.WhitePaint--;
                    pct.BlackPaint++;
                }

                return;
        }
    }
}
