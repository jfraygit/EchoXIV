using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.BlackMage;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Blm
{
    public const string Thunderhead = "Thunderhead";
    public const string Firestarter = "Firestarter";
    public const string LeyLinesStatus = "Ley Lines";
    public const string TriplecastStatus = "Triplecast";
    public const string SwiftcastStatus = "Swiftcast";
    public const string HighThunderDot = "High Thunder (DOT)";

    public const string Fire3 = "Fire III";
    public const string Fire4 = "Fire IV";
    public const string Despair = "Despair";
    public const string FlareStar = "Flare Star";

    public const string Blizzard3 = "Blizzard III";
    public const string Blizzard4 = "Blizzard IV";

    public const string Paradox = "Paradox";
    public const string Xenoglossy = "Xenoglossy";
    public const string HighThunder = "High Thunder";

    public const string Transpose = "Transpose";
    public const string Manafont = "Manafont";
    public const string Amplifier = "Amplifier";
    public const string LeyLinesAction = "Ley Lines";
    public const string TriplecastAction = "Triplecast";
    public const string SwiftcastAction = "Swiftcast";

    /// High Thunder's damage-over-time, emitted under its own name.
    public const string HighThunderTicks = "High Thunder (DOT)";

    public const string Foul = "Foul";
    public const string HighThunder2 = "High Thunder II";
    public const string HighThunder2Ticks = "High Thunder II (DOT)";

    public const string Flare = "Flare";
    public const string Freeze = "Freeze";
}

/// Which element a spell counts as, for damage, MP cost and the elemental gauge alike.
public enum Aspect
{
    None,
    Fire,
    Ice,
}

/// Black Mage's action table.
public static class BlackMageData
{
    /// Flare Star's falloff: 65% off each enemy after the first, the second-harshest figure in the project
    /// after Viper's 75%.
    public const double FlareStarFalloff = 0.65;

    /// Foul's falloff, 25%, against Flare Star's 65% - the two are nothing like each other.
    public const double FoulFalloff = 0.25;

    /// Flare's falloff, 30%.
    public const double FlareFalloff = 0.30;

    /// Astral Souls granted by one Flare: THREE, which is what makes the area fire phase three global
    /// cooldowns long instead of seven.
    public const int FlareAstralSouls = 3;

    /// The least MP Flare will accept, the same floor Despair has.
    public const int FlareMinimumMp = 800;

    /// What Flare costs with at least one Umbral Heart: two thirds of whatever is on the bar.
    public const double FlareCostWithHearts = 2.0 / 3.0;

    /// The MP the area fire phase has to leave the ice with, derived rather than chosen.
    public static readonly int AreaFirePhaseMp =
        (int)System.Math.Round(FlareMinimumMp / (1.0 - FlareCostWithHearts));


    public const int MaxMp = 10_000;

    /// Astral Fire and Umbral Ice both cap at three stacks.
    public const int MaxElementStacks = 3;

    /// Umbral Hearts, each of which cancels the Astral Fire cost doubling for one Fire spell.
    public const int MaxUmbralHearts = 3;

    /// Astral Souls needed for a Flare Star, and the cleanest arithmetic in the parse.
    public const int AstralSoulsForFlareStar = 6;

    /// Level 100 holds three Polyglot.
    public const int MaxPolyglot = 3;

    /// Seconds between Polyglot grants while an element is up.
    public const double PolyglotInterval = 30.0;


    /// How long Astral Fire or Umbral Ice lasts without being refreshed.
    public const double ElementDuration = 15.0;

    /// What Astral Fire does to a Fire spell, by stack count.
    public static readonly double[] AstralFireOnFire = [1.0, 1.60, 1.60, 1.80];

    /// What Umbral Ice does to a Fire spell - a 30% cut.
    public const double UmbralIceOnFire = 0.70;

    /// Enochian: +27% magic damage while Astral Fire or Umbral Ice is up.
    public const double Enochian = 1.27;

    /// What the elemental gauge does to an ICE spell: nothing, in either direction.
    public const double AnyElementOnIce = 1.0;


    /// Seconds between server ticks, which is when MP arrives.
    public const double MpTickInterval = 3.0;

    /// MP restored per tick under Umbral Ice, indexed by stack count.
    public static readonly int[] UmbralIceMpPerTick = [200, 3200, 4700, 6200];

    /// Astral Fire stops MP recovery dead.
    public const int AstralFireMpPerTick = 0;

    /// The least MP Despair will accept before it can be cast at all.
    public const int DespairMinimumMp = 800;


    /// Ley Lines cuts cast AND recast by this much, which is why it arrives as transient haste.
    public const int LeyLinesHastePercent = 15;

    public const double LeyLinesDuration = 30.0;


    public const double ThunderheadDuration = 30.0;
    public const double FirestarterDuration = 30.0;
    public const double SwiftcastDuration = 10.0;
    public const double TriplecastDuration = 15.0;
    public const int TriplecastCharges = 3;

    /// High Thunder's damage-over-time: 60 potency a tick for thirty seconds.
    public const int HighThunderDotPotency = 60;

    public const double HighThunderDotDuration = 30.0;

    public const double DotTickInterval = 3.0;

    /// Which element each spell counts as.
    public static readonly IReadOnlyDictionary<string, Aspect> Aspects = new Dictionary<string, Aspect>
    {
        [Blm.Fire3] = Aspect.Fire,
        [Blm.Fire4] = Aspect.Fire,
        [Blm.Despair] = Aspect.Fire,
        [Blm.FlareStar] = Aspect.Fire,
        [Blm.Flare] = Aspect.Fire,
        [Blm.Blizzard3] = Aspect.Ice,
        [Blm.Blizzard4] = Aspect.Ice,
        [Blm.Freeze] = Aspect.Ice,
    };

    public static Aspect AspectOf(string name) => Aspects.GetValueOrDefault(name, Aspect.None);

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Blm.Fire3] = new ActionDef
        {
            Name = Blm.Fire3,
            Kind = ActionKind.Gcd,
            Potency = 290,
            CastTime = 3.5,
        },

        [Blm.Fire4] = new ActionDef
        {
            Name = Blm.Fire4,
            Kind = ActionKind.Gcd,
            Potency = 300,
            CastTime = 2.0,
        },

        [Blm.Despair] = new ActionDef
        {
            Name = Blm.Despair,
            Kind = ActionKind.Gcd,
            Potency = 350,
        },

        [Blm.FlareStar] = new ActionDef
        {
            Name = Blm.FlareStar,
            MaxTargets = ActionDef.AllNearby,
            Falloff = FlareStarFalloff,
            Kind = ActionKind.Gcd,
            Potency = 500,
            CastTime = 2.0,
        },

        [Blm.Flare] = new ActionDef
        {
            Name = Blm.Flare,
            Kind = ActionKind.Gcd,
            Potency = 240,
            MaxTargets = ActionDef.AllNearby,
            Falloff = FlareFalloff,
            CastTime = 2.0,
        },

        [Blm.Freeze] = new ActionDef
        {
            Name = Blm.Freeze,
            Kind = ActionKind.Gcd,
            Potency = 120,
            MaxTargets = ActionDef.AllNearby,
            CastTime = 2.0,
        },

        [Blm.Blizzard3] = new ActionDef
        {
            Name = Blm.Blizzard3,
            Kind = ActionKind.Gcd,
            Potency = 290,
            CastTime = 3.5,
        },

        [Blm.Blizzard4] = new ActionDef
        {
            Name = Blm.Blizzard4,
            Kind = ActionKind.Gcd,
            Potency = 300,
            CastTime = 2.0,
        },

        [Blm.Paradox] = new ActionDef
        {
            Name = Blm.Paradox,
            Kind = ActionKind.Gcd,
            Potency = 540,
        },

        [Blm.Xenoglossy] = new ActionDef
        {
            Name = Blm.Xenoglossy,
            Kind = ActionKind.Gcd,
            Potency = 890,
        },

        [Blm.Foul] = new ActionDef
        {
            Name = Blm.Foul,
            Kind = ActionKind.Gcd,
            Potency = 600,
            MaxTargets = ActionDef.AllNearby,
            Falloff = FoulFalloff,
        },

        [Blm.HighThunder] = new ActionDef
        {
            Name = Blm.HighThunder,
            Kind = ActionKind.Gcd,
            Potency = 150,
        },

        [Blm.HighThunder2] = new ActionDef
        {
            Name = Blm.HighThunder2,
            Kind = ActionKind.Gcd,
            Potency = 100,
            MaxTargets = ActionDef.AllNearby,
        },

        [Blm.Transpose] = new ActionDef
        {
            Name = Blm.Transpose,
            Kind = ActionKind.OffGcd,
            Cooldown = 5.0,
        },

        [Blm.Manafont] = new ActionDef
        {
            Name = Blm.Manafont,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Blm.Amplifier] = new ActionDef
        {
            Name = Blm.Amplifier,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Blm.LeyLinesAction] = new ActionDef
        {
            Name = Blm.LeyLinesAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
            MaxCharges = 2,
        },

        [Blm.TriplecastAction] = new ActionDef
        {
            Name = Blm.TriplecastAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 60.0,
            MaxCharges = 2,
        },

        [Blm.SwiftcastAction] = new ActionDef
        {
            Name = Blm.SwiftcastAction,
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
}
