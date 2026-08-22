using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.WhiteMage;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Whm
{
    public const string SacredSight = "Sacred Sight";
    public const string PresenceOfMindStatus = "Presence of Mind";

    public const string Glare3 = "Glare III";
    public const string Holy3 = "Holy III";
    public const string Glare4 = "Glare IV";
    public const string Dia = "Dia";
    public const string DiaDot = "Dia (DOT)";
    public const string AfflatusMisery = "Afflatus Misery";
    public const string AfflatusRapture = "Afflatus Rapture";

    public const string Assize = "Assize";
    public const string PresenceOfMindAction = "Presence of Mind";
}

/// White Mage's action table, taken from the game's own sheets via tools/gamedata.
public static class WhiteMageData
{
    /// Afflatus Misery loses 50% per extra enemy.
    public const double MiseryFalloff = 0.50;

    /// Glare IV loses 40%.
    public const double Glare4Falloff = 0.40;


    /// One lily every twenty seconds, to a maximum of three.
    public const double LilyInterval = 20.0;

    public const int MaxLilies = 3;

    /// Afflatus Solace or Rapture, three times, blooms the Blood Lily.
    public const int LiliesPerBloom = 3;


    /// Dia's damage-over-time, at 85 a tick for thirty seconds.
    public const int DiaTickPotency = 85;

    public const int DiaTicks = 10;

    public const double DiaDuration = 30.0;

    /// How early Dia may be refreshed - one global cooldown, so it can take the slot at the boundary rather
    /// than queue behind a burst window.
    public const double DiaRefreshLead = 2.5;


    /// "Reduces spell cast time and recast time, and auto-attack delay by 20%." Fifteen seconds.
    public const int PresenceOfMindHaste = 20;

    public const double PresenceOfMindDuration = 15.0;

    /// Three stacks of Sacred Sight, from Enhanced Presence of Mind, lasting thirty seconds.
    public const int SacredSightStacks = 3;

    public const double SacredSightDuration = 30.0;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {

        [Whm.Holy3] = new ActionDef
        {
            Name = Whm.Holy3,
            Kind = ActionKind.Gcd,
            Potency = 150,
            CastTime = 1.5,
            MaxTargets = ActionDef.AllNearby,
        },

        [Whm.Glare3] = new ActionDef
        {
            Name = Whm.Glare3,
            Kind = ActionKind.Gcd,
            Potency = 350,
            CastTime = 1.5,
        },

        [Whm.Glare4] = new ActionDef
        {
            Name = Whm.Glare4,
            MaxTargets = ActionDef.AllNearby,
            Falloff = Glare4Falloff,
            Kind = ActionKind.Gcd,
            Potency = 640,
        },

        [Whm.Dia] = new ActionDef
        {
            Name = Whm.Dia,
            Kind = ActionKind.Gcd,
            Potency = DiaTickPotency,
        },

        [Whm.AfflatusMisery] = new ActionDef
        {
            Name = Whm.AfflatusMisery,
            MaxTargets = ActionDef.AllNearby,
            Falloff = MiseryFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1400,
        },

        [Whm.AfflatusRapture] = new ActionDef
        {
            Name = Whm.AfflatusRapture,
            Kind = ActionKind.Gcd,
            Potency = 0,
        },


        [Whm.Assize] = new ActionDef
        {
            Name = Whm.Assize,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.OffGcd,
            Potency = 400,
            Cooldown = 40.0,
        },

        [Whm.PresenceOfMindAction] = new ActionDef
        {
            Name = Whm.PresenceOfMindAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 120.0,
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
