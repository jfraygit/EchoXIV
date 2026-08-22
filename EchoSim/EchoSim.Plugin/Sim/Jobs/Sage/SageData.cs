using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Sage;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Sge
{
    public const string EukrasiaStatus = "Eukrasia";

    public const string Dosis3 = "Dosis III";
    public const string Dyskrasia2 = "Dyskrasia II";
    public const string EukrasiaAction = "Eukrasia";
    public const string EukrasianDosis3 = "Eukrasian Dosis III";
    public const string EukrasianDosisDot = "Eukrasian Dosis III (DOT)";
    public const string Phlegma3 = "Phlegma III";
    public const string Toxikon2 = "Toxikon II";
    public const string Pneuma = "Pneuma";

    public const string Psyche = "Psyche";
}

/// Sage's action table, taken from the game's own sheets via tools/gamedata.
public static class SageData
{
    /// The falloff on Phlegma III, Toxikon II and Psyche: 50% off each enemy after the first.
    public const double SageFalloff = 0.50;

    /// Pneuma loses 40% rather than 50%.
    public const double PneumaFalloff = 0.40;


    /// The damage-over-time, at 90 a tick for thirty seconds.
    public const int EukrasianDosisTickPotency = 90;

    public const int EukrasianDosisTicks = 10;

    public const double EukrasianDosisDuration = 30.0;

    /// How long Eukrasia itself sits on the Sage waiting to be spent.
    public const double EukrasiaStatusDuration = 30.0;

    /// How far before the pull Eukrasia is pressed, in seconds.
    public const double PrePullEukrasiaLead = 5.0;

    /// How early the Eukrasia pair is STARTED, in seconds.
    public const double DotRefreshLead = EukrasiaRecast;

    /// Eukrasia's own recast: ONE SECOND, and the Action sheet is wrong about it.
    public const double EukrasiaRecast = 1.0;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {

        [Sge.Dyskrasia2] = new ActionDef
        {
            Name = Sge.Dyskrasia2,
            Kind = ActionKind.Gcd,
            Potency = 170,
            MaxTargets = ActionDef.AllNearby,
        },

        [Sge.Dosis3] = new ActionDef
        {
            Name = Sge.Dosis3,
            Kind = ActionKind.Gcd,
            Potency = 380,
            CastTime = 1.5,
        },

        [Sge.EukrasiaAction] = new ActionDef
        {
            Name = Sge.EukrasiaAction,
            Kind = ActionKind.Gcd,
            Potency = 0,
            BaseRecast = EukrasiaRecast,
        },

        [Sge.EukrasianDosis3] = new ActionDef
        {
            Name = Sge.EukrasianDosis3,
            Kind = ActionKind.Gcd,
            Potency = 0,
        },

        [Sge.Phlegma3] = new ActionDef
        {
            Name = Sge.Phlegma3,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SageFalloff,
            Kind = ActionKind.Gcd,
            Potency = 690,
            Cooldown = 40.0,
            MaxCharges = 2,
        },

        [Sge.Toxikon2] = new ActionDef
        {
            Name = Sge.Toxikon2,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SageFalloff,
            Kind = ActionKind.Gcd,
            Potency = 380,
        },

        [Sge.Pneuma] = new ActionDef
        {
            Name = Sge.Pneuma,
            MaxTargets = ActionDef.AllNearby,
            Falloff = PneumaFalloff,
            Kind = ActionKind.Gcd,
            Potency = 380,
            CastTime = 1.5,
            Cooldown = 120.0,
        },


        [Sge.Psyche] = new ActionDef
        {
            Name = Sge.Psyche,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SageFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 690,
            Cooldown = 60.0,
        },

        [Buffs.PotionAction] = new ActionDef
        {
            Name = Buffs.PotionAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 270.0,
        },
    };
}
