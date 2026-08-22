using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Dancer;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Dnc
{
    public const string SilkenSymmetry = "Silken Symmetry";
    public const string SilkenFlow = "Silken Flow";
    public const string FlourishingSymmetry = "Flourishing Symmetry";
    public const string FlourishingFlow = "Flourishing Flow";
    public const string ThreefoldFanDance = "Threefold Fan Dance";
    public const string FourfoldFanDance = "Fourfold Fan Dance";
    public const string FlourishingStarfall = "Flourishing Starfall";
    public const string FlourishingFinish = "Flourishing Finish";
    public const string LastDanceReady = "Last Dance Ready";
    public const string DanceOfTheDawnReady = "Dance of the Dawn Ready";
    public const string FinishingMoveReady = "Finishing Move Ready";
    public const string StandardFinishBuff = "Standard Finish";
    public const string TechnicalFinishBuff = "Technical Finish";
    public const string Devilment = "Devilment";

    public const string Cascade = "Cascade";
    public const string Fountain = "Fountain";
    public const string ReverseCascade = "Reverse Cascade";
    public const string Fountainfall = "Fountainfall";
    public const string SaberDance = "Saber Dance";
    public const string DanceOfTheDawn = "Dance of the Dawn";
    public const string StarfallDance = "Starfall Dance";
    public const string Tillana = "Tillana";
    public const string LastDance = "Last Dance";
    public const string FinishingMove = "Finishing Move";

    public const string StandardStep = "Standard Step";
    public const string TechnicalStep = "Technical Step";
    public const string StandardFinish = "Double Standard Finish";
    public const string TechnicalFinish = "Quadruple Technical Finish";

    /// The four dance steps.
    public const string DanceStep = "Dance Step";

    /// The four steps under their real names, for DISPLAY ONLY.
    public const string Emboite = "Emboite";

    public const string Entrechat = "Entrechat";

    public const string Jete = "Jete";

    public const string Pirouette = "Pirouette";

    /// The four display-only step names.
    public static readonly string[] NamedSteps = [Emboite, Entrechat, Jete, Pirouette];

    public const string FanDance = "Fan Dance";
    public const string FanDanceIII = "Fan Dance III";
    public const string FanDanceIV = "Fan Dance IV";

    public const string Windmill = "Windmill";
    public const string Bladeshower = "Bladeshower";
    public const string RisingWindmill = "Rising Windmill";
    public const string Bloodshower = "Bloodshower";
    public const string FanDanceII = "Fan Dance II";
    public const string Flourish = "Flourish";
    public const string DevilmentAction = "Devilment";
}

/// Dancer's action table.
public static class DancerData
{
    /// The falloff on almost everything Dancer has: 60% off each enemy after the first.
    public const double DancerFalloff = 0.60;

    /// Starfall Dance alone loses 75% - the exception that stops the constant above being a rule.
    public const double StarfallFalloff = 0.75;


    public const int MaxEsprit = 100;
    public const int SaberDanceCost = 50;

    /// Esprit above which Saber Dance outranks the proc weaponskills.
    public const int EspritSpendThreshold = 80;
    public const int MaxFeathers = 4;

    /// Esprit the two combo starters grant outright.
    public const int EspritPerCombo = 5;

    /// Esprit the two proc weaponskills grant outright.
    public const int EspritPerProc = 10;

    /// Esprit Tillana hands back.
    public const int TillanaEsprit = 50;

    /// Esprit per second from ONE other player feeding the gauge - the one calibrated number here.
    public const double ExternalEspritPerSecond = 575.0 / 416.0;


    /// Cascade and Fountain each grant their proc half the time; so do the two that follow.
    public const double ProcChance = 0.50;

    public const double ProcDuration = 30.0;


    /// Standard Step and Technical Step both occupy 1.5 seconds of global cooldown.
    public const double StepRecast = 1.5;

    /// Each individual step is a flat second, unscaled by Skill Speed.
    public const double DanceStepRecast = 1.0;

    public const int StandardSteps = 2;
    public const int TechnicalSteps = 4;

    /// How long before the pull Standard Step is danced.
    public const double PrePullStandardStep = 15.0;

    /// How near Technical Step has to be before Flourish is held for the burst it feeds.
    public const double FlourishBurstHold = 15.0;


    /// A two-step Standard Finish is 5%; a four-step Technical Finish is another 5%.
    public const double StandardFinishMulti = 1.05;

    public const double TechnicalFinishMulti = 1.05;

    public const double StandardFinishDuration = 60.0;
    public const double TechnicalFinishDuration = 20.0;

    public const double DevilmentDuration = 20.0;

    /// Devilment is 20% critical hit rate AND 20% direct hit rate - both, not either.
    public const double DevilmentCritBonus = 0.20;

    public const double DevilmentDirectHitBonus = 0.20;

    public const double ReadyDuration = 30.0;

    public static ActionDef Get(string name) => Actions[name];

    /// One of the four display-only step entries.
    private static ActionDef NamedStep(string name) => new()
    {
        Name = name,
        Kind = ActionKind.Gcd,
        Potency = 0,
        FixedRecast = DanceStepRecast,
        PreservesCombo = true,
    };

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Dnc.Windmill] = new ActionDef
        {
            Name = Dnc.Windmill,
            Kind = ActionKind.Gcd,
            Potency = 120,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Dnc.Bladeshower] = new ActionDef
        {
            Name = Dnc.Bladeshower,
            Kind = ActionKind.Gcd,
            Potency = 100,
            ComboPotency = 160,
            ComboFrom = Dnc.Windmill,
            MaxTargets = ActionDef.AllNearby,
        },

        [Dnc.RisingWindmill] = new ActionDef
        {
            Name = Dnc.RisingWindmill,
            Kind = ActionKind.Gcd,
            Potency = 160,
            MaxTargets = ActionDef.AllNearby,
        },

        [Dnc.Bloodshower] = new ActionDef
        {
            Name = Dnc.Bloodshower,
            Kind = ActionKind.Gcd,
            Potency = 200,
            MaxTargets = ActionDef.AllNearby,
        },

        [Dnc.FanDanceII] = new ActionDef
        {
            Name = Dnc.FanDanceII,
            Kind = ActionKind.OffGcd,
            Potency = 100,
            SharesCooldownWith = [Dnc.FanDance],
            MaxTargets = ActionDef.AllNearby,
        },

        [Dnc.Cascade] = new ActionDef
        {
            Name = Dnc.Cascade,
            Kind = ActionKind.Gcd,
            Potency = 220,
            IsComboStarter = true,
        },

        [Dnc.Fountain] = new ActionDef
        {
            Name = Dnc.Fountain,
            Kind = ActionKind.Gcd,
            Potency = 120,
            ComboPotency = 280,
            ComboFrom = Dnc.Cascade,
        },

        [Dnc.ReverseCascade] = new ActionDef
        {
            Name = Dnc.ReverseCascade,
            Kind = ActionKind.Gcd,
            Potency = 280,
            PreservesCombo = true,
        },

        [Dnc.Fountainfall] = new ActionDef
        {
            Name = Dnc.Fountainfall,
            Kind = ActionKind.Gcd,
            Potency = 340,
            PreservesCombo = true,
        },

        [Dnc.SaberDance] = new ActionDef
        {
            Name = Dnc.SaberDance,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 540,
            PreservesCombo = true,
        },

        [Dnc.DanceOfTheDawn] = new ActionDef
        {
            Name = Dnc.DanceOfTheDawn,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1000,
            PreservesCombo = true,
        },

        [Dnc.StarfallDance] = new ActionDef
        {
            Name = Dnc.StarfallDance,
            MaxTargets = ActionDef.AllNearby,
            Falloff = StarfallFalloff,
            Kind = ActionKind.Gcd,
            Potency = 600,
            PreservesCombo = true,
        },

        [Dnc.Tillana] = new ActionDef
        {
            Name = Dnc.Tillana,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 600,
            PreservesCombo = true,
        },

        [Dnc.LastDance] = new ActionDef
        {
            Name = Dnc.LastDance,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 540,
            PreservesCombo = true,
        },

        [Dnc.FinishingMove] = new ActionDef
        {
            Name = Dnc.FinishingMove,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 850,
            Cooldown = 30.0,

            SharesCooldownWith = [Dnc.StandardStep],
            PreservesCombo = true,
        },

        [Dnc.StandardStep] = new ActionDef
        {
            Name = Dnc.StandardStep,
            Kind = ActionKind.Gcd,
            Cooldown = 30.0,
            FixedRecast = StepRecast,
            PreservesCombo = true,
        },

        [Dnc.TechnicalStep] = new ActionDef
        {
            Name = Dnc.TechnicalStep,
            Kind = ActionKind.Gcd,
            Cooldown = 120.0,
            FixedRecast = StepRecast,
            PreservesCombo = true,
        },

        [Dnc.DanceStep] = new ActionDef
        {
            Name = Dnc.DanceStep,
            Kind = ActionKind.Gcd,
            Potency = 0,
            FixedRecast = DanceStepRecast,
            PreservesCombo = true,
        },

        [Dnc.Emboite] = NamedStep(Dnc.Emboite),
        [Dnc.Entrechat] = NamedStep(Dnc.Entrechat),
        [Dnc.Jete] = NamedStep(Dnc.Jete),
        [Dnc.Pirouette] = NamedStep(Dnc.Pirouette),

        [Dnc.StandardFinish] = new ActionDef
        {
            Name = Dnc.StandardFinish,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 850,
            FixedRecast = StepRecast,
            PreservesCombo = true,
        },

        [Dnc.TechnicalFinish] = new ActionDef
        {
            Name = Dnc.TechnicalFinish,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DancerFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1300,
            FixedRecast = StepRecast,
            PreservesCombo = true,
        },

        [Dnc.FanDance] = new ActionDef
        {
            Name = Dnc.FanDance,
            Kind = ActionKind.OffGcd,
            Potency = 180,
            Cooldown = 1.0,
        },

        [Dnc.FanDanceIII] = new ActionDef
        {
            Name = Dnc.FanDanceIII,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DancerFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 220,
            Cooldown = 1.0,
        },

        [Dnc.FanDanceIV] = new ActionDef
        {
            Name = Dnc.FanDanceIV,
            MaxTargets = ActionDef.AllNearby,
            Falloff = DancerFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 460,
            Cooldown = 1.0,
        },

        [Dnc.Flourish] = new ActionDef
        {
            Name = Dnc.Flourish,
            Kind = ActionKind.OffGcd,
            Cooldown = 60.0,
        },

        [Dnc.DevilmentAction] = new ActionDef
        {
            Name = Dnc.DevilmentAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
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
