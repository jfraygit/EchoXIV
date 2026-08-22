using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Pictomancer;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Pct
{
    public const string Aetherhues = "Aetherhues";
    public const string SubtractivePalette = "Subtractive Palette";
    public const string SubtractiveSpectrum = "Subtractive Spectrum";
    public const string MonochromeTones = "Monochrome Tones";
    public const string HammerTime = "Hammer Time";
    public const string Hyperphantasia = "Hyperphantasia";
    public const string Inspiration = "Inspiration";
    public const string RainbowBright = "Rainbow Bright";
    public const string Starstruck = "Starstruck";
    public const string StarryMuseBuff = "Starry Muse";

    public const string FireInRed = "Fire in Red";
    public const string AeroInGreen = "Aero in Green";
    public const string WaterInBlue = "Water in Blue";

    public const string BlizzardInCyan = "Blizzard in Cyan";
    public const string StoneInYellow = "Stone in Yellow";
    public const string ThunderInMagenta = "Thunder in Magenta";

    public const string Fire2InRed = "Fire II in Red";
    public const string Aero2InGreen = "Aero II in Green";
    public const string Water2InBlue = "Water II in Blue";

    public const string Blizzard2InCyan = "Blizzard II in Cyan";
    public const string Stone2InYellow = "Stone II in Yellow";
    public const string Thunder2InMagenta = "Thunder II in Magenta";

    public const string CometInBlack = "Comet in Black";
    public const string HolyInWhite = "Holy in White";
    public const string RainbowDrip = "Rainbow Drip";
    public const string StarPrism = "Star Prism";

    public const string HammerStamp = "Hammer Stamp";
    public const string HammerBrush = "Hammer Brush";
    public const string PolishingHammer = "Polishing Hammer";

    public const string CreatureMotif = "Creature Motif";
    public const string WeaponMotif = "Weapon Motif";
    public const string LandscapeMotif = "Landscape Motif";

    public const string LivingMuse = "Living Muse";
    public const string StrikingMuse = "Striking Muse";
    public const string StarryMuse = "Starry Muse";
    public const string MogOfTheAges = "Mog of the Ages";
    public const string RetributionOfTheMadeen = "Retribution of the Madeen";

    public const string SubtractivePaletteAction = "Subtractive Palette";
    public const string SwiftcastAction = "Swiftcast";
}

/// Pictomancer's action table, taken from the game's own sheets via tools/gamedata.
public static class PictomancerData
{
    /// The falloff on most of Pictomancer's cleaving actions: 70% off each enemy after the first.
    public const double PictomancerFalloff = 0.70;

    /// Holy in White and Comet in Black lose 65% rather than 70%.
    public const double PaintFalloff = 0.65;

    /// Rainbow Drip loses 85% - the harshest falloff anywhere in this project.
    public const double RainbowDripFalloff = 0.85;



    /// What a Subtractive Palette costs.
    public const int SubtractivePaletteCost = 50;

    /// Palette Gauge from a Water in Blue, and ONLY from a Water in Blue.
    public const int PaletteGaugePerWater = 25;

    public const int MaxPaletteGauge = 100;

    /// Subtractive Palette stacks, spent on the cool trio one apiece.
    public const int SubtractivePaletteStacks = 3;

    /// White Paint stacks, from Water in Blue and Thunder in Magenta.
    public const int MaxWhitePaint = 5;

    /// Hammer Time stacks from a Striking Muse - one whole hammer chain.
    public const int HammerTimeStacks = 3;

    /// Hyperphantasia stacks from a Starry Muse, spent by aetherhue spells.
    public const int HyperphantasiaStacks = 5;


    public const double AetherhuesDuration = 30.0;
    public const double SubtractiveSpectrumDuration = 30.0;
    public const double HammerTimeDuration = 30.0;
    public const double HyperphantasiaDuration = 30.0;
    public const double RainbowBrightDuration = 30.0;
    public const double StarstruckDuration = 20.0;
    public const double StarryMuseDuration = 20.0;

    /// Starry Muse's damage bonus to the Pictomancer and everyone nearby.
    public const double StarryMuseBonus = 1.05;

    /// Inspiration's cast and recast reduction, and it is measured rather than assumed.
    public const double InspirationHaste = 25;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Pct.FireInRed] = new ActionDef
        {
            Name = Pct.FireInRed,
            Kind = ActionKind.Gcd,
            Potency = 490,
            CastTime = 1.5,
        },

        [Pct.AeroInGreen] = new ActionDef
        {
            Name = Pct.AeroInGreen,
            Kind = ActionKind.Gcd,
            Potency = 530,
            CastTime = 1.5,
        },

        [Pct.WaterInBlue] = new ActionDef
        {
            Name = Pct.WaterInBlue,
            Kind = ActionKind.Gcd,
            Potency = 570,
            CastTime = 1.5,
        },

        [Pct.BlizzardInCyan] = new ActionDef
        {
            Name = Pct.BlizzardInCyan,
            Kind = ActionKind.Gcd,
            Potency = 860,
            CastTime = 2.3,
            BaseRecast = 3.3,
        },

        [Pct.StoneInYellow] = new ActionDef
        {
            Name = Pct.StoneInYellow,
            Kind = ActionKind.Gcd,
            Potency = 900,
            CastTime = 2.3,
            BaseRecast = 3.3,
        },

        [Pct.ThunderInMagenta] = new ActionDef
        {
            Name = Pct.ThunderInMagenta,
            Kind = ActionKind.Gcd,
            Potency = 940,
            CastTime = 2.3,
            BaseRecast = 3.3,
        },

        [Pct.Fire2InRed] = new ActionDef
        {
            Name = Pct.Fire2InRed,
            Kind = ActionKind.Gcd,
            Potency = 180,
            CastTime = 1.5,
            MaxTargets = ActionDef.AllNearby,
        },

        [Pct.Aero2InGreen] = new ActionDef
        {
            Name = Pct.Aero2InGreen,
            Kind = ActionKind.Gcd,
            Potency = 200,
            CastTime = 1.5,
            MaxTargets = ActionDef.AllNearby,
        },

        [Pct.Water2InBlue] = new ActionDef
        {
            Name = Pct.Water2InBlue,
            Kind = ActionKind.Gcd,
            Potency = 220,
            CastTime = 1.5,
            MaxTargets = ActionDef.AllNearby,
        },

        [Pct.Blizzard2InCyan] = new ActionDef
        {
            Name = Pct.Blizzard2InCyan,
            Kind = ActionKind.Gcd,
            Potency = 360,
            CastTime = 2.3,
            BaseRecast = 3.3,
            MaxTargets = ActionDef.AllNearby,
        },

        [Pct.Stone2InYellow] = new ActionDef
        {
            Name = Pct.Stone2InYellow,
            Kind = ActionKind.Gcd,
            Potency = 380,
            CastTime = 2.3,
            BaseRecast = 3.3,
            MaxTargets = ActionDef.AllNearby,
        },

        [Pct.Thunder2InMagenta] = new ActionDef
        {
            Name = Pct.Thunder2InMagenta,
            Kind = ActionKind.Gcd,
            Potency = 400,
            CastTime = 2.3,
            BaseRecast = 3.3,
            MaxTargets = ActionDef.AllNearby,
        },

        [Pct.CometInBlack] = new ActionDef
        {
            Name = Pct.CometInBlack,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PaintFalloff,
            Kind = ActionKind.Gcd,
            Potency = 940,
            BaseRecast = 3.3,
        },

        [Pct.HolyInWhite] = new ActionDef
        {
            Name = Pct.HolyInWhite,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PaintFalloff,
            Kind = ActionKind.Gcd,
            Potency = 570,
        },

        [Pct.HammerStamp] = new ActionDef
        {
            Name = Pct.HammerStamp,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PictomancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 560,
        },

        [Pct.HammerBrush] = new ActionDef
        {
            Name = Pct.HammerBrush,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PictomancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 580,
        },

        [Pct.PolishingHammer] = new ActionDef
        {
            Name = Pct.PolishingHammer,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PictomancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 600,
        },

        [Pct.RainbowDrip] = new ActionDef
        {
            Name = Pct.RainbowDrip,
            MaxTargets = ActionDef.AllNearby,
            Falloff = RainbowDripFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1000,
            CastTime = 4.0,
        },

        [Pct.StarPrism] = new ActionDef
        {
            Name = Pct.StarPrism,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PictomancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1100,
        },

        [Pct.CreatureMotif] = new ActionDef
        {
            Name = Pct.CreatureMotif,
            Kind = ActionKind.Gcd,
            CastTime = 3.0,
            FixedRecast = 4.0,
        },

        [Pct.WeaponMotif] = new ActionDef
        {
            Name = Pct.WeaponMotif,
            Kind = ActionKind.Gcd,
            CastTime = 3.0,
            FixedRecast = 4.0,
        },

        [Pct.LandscapeMotif] = new ActionDef
        {
            Name = Pct.LandscapeMotif,
            Kind = ActionKind.Gcd,
            CastTime = 3.0,
            FixedRecast = 4.0,
        },

        [Pct.LivingMuse] = new ActionDef
        {
            Name = Pct.LivingMuse,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PictomancerFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 800,
            Cooldown = 40.0,
            MaxCharges = 3,

            LogAliases = ["Pom Muse", "Winged Muse", "Clawed Muse", "Fanged Muse"],
        },

        [Pct.StrikingMuse] = new ActionDef
        {
            Name = Pct.StrikingMuse,
            Kind = ActionKind.OffGcd,
            Cooldown = 60.0,
            MaxCharges = 2,
        },

        [Pct.StarryMuse] = new ActionDef
        {
            Name = Pct.StarryMuse,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Pct.MogOfTheAges] = new ActionDef
        {
            Name = Pct.MogOfTheAges,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PictomancerFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 1000,
            Cooldown = 30.0,
        },

        [Pct.RetributionOfTheMadeen] = new ActionDef
        {
            Name = Pct.RetributionOfTheMadeen,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PictomancerFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 1100,
            Cooldown = 30.0,
        },

        [Pct.SubtractivePaletteAction] = new ActionDef
        {
            Name = Pct.SubtractivePaletteAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 1.0,
        },

        [Pct.SwiftcastAction] = new ActionDef
        {
            Name = Pct.SwiftcastAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 60.0,
        },

        [Buffs.PotionAction] = new ActionDef
        {
            Name = Buffs.PotionAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = PlayerStats.PotionRecast,
        },
    };

    /// The aetherhue trio in the order the gauge walks them.
    public static readonly string[] AetherhueCycle = [Pct.FireInRed, Pct.AeroInGreen, Pct.WaterInBlue];

    /// The subtractive trio, likewise.
    public static readonly string[] SubtractiveCycle = [Pct.BlizzardInCyan, Pct.StoneInYellow, Pct.ThunderInMagenta];

    /// The hammer chain in order.
    public static readonly string[] HammerChain = [Pct.HammerStamp, Pct.HammerBrush, Pct.PolishingHammer];

    /// The area trios, in the same order as the two above.
    public static readonly string[] AreaAetherhueCycle =
        [Pct.Fire2InRed, Pct.Aero2InGreen, Pct.Water2InBlue];

    public static readonly string[] AreaSubtractiveCycle =
        [Pct.Blizzard2InCyan, Pct.Stone2InYellow, Pct.Thunder2InMagenta];

    /// The area paint that stands in for a given single-target one, or null if there is none.
    public static string? AreaTwin(string name)
    {
        var index = Array.IndexOf(AetherhueCycle, name);
        if (index >= 0)
            return AreaAetherhueCycle[index];

        index = Array.IndexOf(SubtractiveCycle, name);
        return index >= 0 ? AreaSubtractiveCycle[index] : null;
    }

    /// Whether an action spends a Hyperphantasia stack: ANY DAMAGING SPELL, not just the aetherhue trios.
    public static bool SpendsHyperphantasia(string name)
        => Actions.TryGetValue(name, out var action)
           && action.Kind == ActionKind.Gcd
           && action.Potency > 0;
}
