using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Scholar;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Sch
{
    public const string ImpactImminent = "Impact Imminent";

    /// A DEBUFF ON THE TARGET, not a buff on the Scholar - which is why the name is shared with the entry in
    /// PartyBuffs.
    public const string ChainStratagemStatus = "Chain Stratagem";

    public const string Broil4 = "Broil IV";
    public const string ArtOfWar2 = "Art of War II";
    public const string Biolysis = "Biolysis";
    public const string BiolysisDot = "Biolysis (DOT)";
    public const string Ruin2 = "Ruin II";

    public const string ChainStratagemAction = "Chain Stratagem";
    public const string BanefulImpaction = "Baneful Impaction";
    public const string BanefulImpactionDot = "Baneful Impaction (DOT)";
    public const string AetherflowAction = "Aetherflow";
    public const string EnergyDrain = "Energy Drain";
    public const string Dissipation = "Dissipation";
}

/// Scholar's action table, taken from the game's own sheets via tools/gamedata.
public static class ScholarData
{

    /// 85 a tick for thirty seconds, from Tactician's Mastery.
    public const int BiolysisTickPotency = 85;

    public const int BiolysisTicks = 10;

    public const double BiolysisDuration = 30.0;

    /// How early Biolysis may be re-applied, in seconds - about one global cooldown.
    public const double BiolysisRefreshLead = 2.5;


    /// 140 a tick for fifteen seconds.
    public const int BanefulImpactionTickPotency = 140;

    public const int BanefulImpactionTicks = 5;

    public const double BanefulImpactionDuration = 15.0;


    /// Three stacks, from Aetherflow itself and from Dissipation alike.
    public const int AetherflowStacks = 3;


    /// "Increases rate at which target takes critical hits by 10%." Twenty seconds.
    public const double ChainStratagemCritBonus = 0.10;

    public const double ChainStratagemDuration = 20.0;

    /// Impact Imminent, from Enhanced Chain Stratagem, lasting thirty seconds.
    public const double ImpactImminentDuration = 30.0;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {

        [Sch.ArtOfWar2] = new ActionDef
        {
            Name = Sch.ArtOfWar2,
            Kind = ActionKind.Gcd,
            Potency = 180,
            CastTime = 1.5,
            MaxTargets = ActionDef.AllNearby,
        },

        [Sch.Broil4] = new ActionDef
        {
            Name = Sch.Broil4,
            Kind = ActionKind.Gcd,
            Potency = 320,
            CastTime = 1.5,
        },

        [Sch.Biolysis] = new ActionDef
        {
            Name = Sch.Biolysis,
            Kind = ActionKind.Gcd,
            Potency = 0,
        },

        [Sch.Ruin2] = new ActionDef
        {
            Name = Sch.Ruin2,
            Kind = ActionKind.Gcd,
            Potency = 220,
        },


        [Sch.ChainStratagemAction] = new ActionDef
        {
            Name = Sch.ChainStratagemAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 120.0,
        },

        [Sch.BanefulImpaction] = new ActionDef
        {
            Name = Sch.BanefulImpaction,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.OffGcd,
            Potency = 0,
        },

        [Sch.EnergyDrain] = new ActionDef
        {
            Name = Sch.EnergyDrain,
            Kind = ActionKind.OffGcd,
            Potency = 100,
        },

        [Sch.AetherflowAction] = new ActionDef
        {
            Name = Sch.AetherflowAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 60.0,
        },

        [Sch.Dissipation] = new ActionDef
        {
            Name = Sch.Dissipation,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 180.0,
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
