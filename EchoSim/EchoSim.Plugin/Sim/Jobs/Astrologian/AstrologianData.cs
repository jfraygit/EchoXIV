using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Astrologian;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Ast
{
    public const string Divining = "Divining";

    /// The party's damage buff.
    public const string DivinationStatus = "Divination";

    public const string FallMalefic = "Fall Malefic";
    public const string Gravity2 = "Gravity II";
    public const string Combust3 = "Combust III";
    public const string CombustDot = "Combust III (DOT)";
    public const string Macrocosmos = "Macrocosmos";

    public const string DivinationAction = "Divination";
    public const string Oracle = "Oracle";
    public const string LordOfCrowns = "Lord of Crowns";
    public const string AstralDraw = "Astral Draw";
    public const string UmbralDraw = "Umbral Draw";
    public const string EarthlyStar = "Earthly Star";
    public const string StellarDetonation = "Stellar Detonation";
    public const string StellarExplosion = "Stellar Explosion";
    public const string Lightspeed = "Lightspeed";
}

/// Astrologian's action table, taken from the game's own sheets via tools/gamedata.
public static class AstrologianData
{
    /// Macrocosmos loses 40% per extra enemy.
    public const double MacrocosmosFalloff = 0.40;

    /// Oracle loses 50%.
    public const double OracleFalloff = 0.50;


    /// 70 a tick for thirty seconds, from Magick Mastery.
    public const int CombustTickPotency = 70;

    public const int CombustTicks = 10;

    public const double CombustDuration = 30.0;

    /// How early Combust III may be re-applied, in seconds - about one global cooldown.
    public const double CombustRefreshLead = 2.5;


    /// "Increases damage dealt by party members by 6%." Twenty seconds.
    public const double DivinationBonus = 1.06;

    public const double DivinationDuration = 20.0;

    /// Divining, from Enhanced Divination, lasting thirty seconds.
    public const double DiviningDuration = 30.0;


    /// Ten seconds from placement to Giant Dominance, and the full 310 only after it.
    public const double GiantDominanceDelay = 10.0;

    public const int StellarExplosionPotency = 310;

    /// How long a placed star lasts before it explodes by itself: ten seconds of Earthly Dominance and ten
    /// more of Giant Dominance.
    public const double EarthlyStarLifetime = 20.0;

    /// How far before the star's own expiry the rotation presses the button, in seconds.
    public const double StellarDetonationLead = 2.5;

    /// Where the star goes before the fight starts: four seconds ahead of the pull.
    public const double PrePullStarLead = 4.0;


    /// Astral Draw, Umbral Draw, and back.
    public const double DrawRecast = 55.0;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {

        [Ast.Gravity2] = new ActionDef
        {
            Name = Ast.Gravity2,
            Kind = ActionKind.Gcd,
            Potency = 140,
            CastTime = 1.5,
            MaxTargets = ActionDef.AllNearby,
        },

        [Ast.FallMalefic] = new ActionDef
        {
            Name = Ast.FallMalefic,
            Kind = ActionKind.Gcd,
            Potency = 270,
            CastTime = 1.5,
        },

        [Ast.Combust3] = new ActionDef
        {
            Name = Ast.Combust3,
            Kind = ActionKind.Gcd,
            Potency = 0,
        },

        [Ast.Macrocosmos] = new ActionDef
        {
            Name = Ast.Macrocosmos,
            MaxTargets = ActionDef.AllNearby,
            Falloff = MacrocosmosFalloff,
            Kind = ActionKind.Gcd,
            Potency = 270,
            Cooldown = 180.0,
        },


        [Ast.DivinationAction] = new ActionDef
        {
            Name = Ast.DivinationAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 120.0,
        },

        [Ast.Oracle] = new ActionDef
        {
            Name = Ast.Oracle,
            MaxTargets = ActionDef.AllNearby,
            Falloff = OracleFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 860,
        },

        [Ast.LordOfCrowns] = new ActionDef
        {
            Name = Ast.LordOfCrowns,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.OffGcd,
            Potency = 400,
        },

        [Ast.AstralDraw] = new ActionDef
        {
            Name = Ast.AstralDraw,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = DrawRecast,
            SharesCooldownWith = [Ast.UmbralDraw],
        },

        [Ast.UmbralDraw] = new ActionDef
        {
            Name = Ast.UmbralDraw,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = DrawRecast,
            SharesCooldownWith = [Ast.AstralDraw],
        },

        [Ast.EarthlyStar] = new ActionDef
        {
            Name = Ast.EarthlyStar,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 60.0,
        },

        [Ast.StellarDetonation] = new ActionDef
        {
            Name = Ast.StellarDetonation,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.OffGcd,
            Potency = StellarExplosionPotency,
        },

        [Ast.Lightspeed] = new ActionDef
        {
            Name = Ast.Lightspeed,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 90.0,
            MaxCharges = 2,
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
