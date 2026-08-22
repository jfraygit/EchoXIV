using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Bard;

/// Which song is playing.
public enum Song
{
    None,

    /// The Wanderer's Minuet.
    Minuet,

    /// Mage's Ballad.
    Ballad,

    /// Army's Paeon.
    Paeon,
}

/// Status and action names, kept in one place so typos surface at compile time.
public static class Brd
{
    public const string HawksEye = "Hawk's Eye";
    public const string RagingStrikes = "Raging Strikes";
    public const string BattleVoice = "Battle Voice";
    public const string RadiantFinale = "Radiant Finale";
    public const string BarrageStatus = "Barrage";
    public const string ResonantArrowReady = "Resonant Arrow Ready";
    public const string RadiantEncoreReady = "Radiant Encore Ready";
    public const string BlastArrowReady = "Blast Arrow Ready";

    public const string BurstShot = "Burst Shot";
    public const string RefulgentArrow = "Refulgent Arrow";
    public const string CausticBite = "Caustic Bite";
    public const string Stormbite = "Stormbite";
    public const string IronJaws = "Iron Jaws";
    public const string ApexArrow = "Apex Arrow";
    public const string BlastArrow = "Blast Arrow";
    public const string ResonantArrow = "Resonant Arrow";
    public const string RadiantEncore = "Radiant Encore";

    public const string HeartbreakShot = "Heartbreak Shot";
    public const string EmpyrealArrow = "Empyreal Arrow";
    public const string Sidewinder = "Sidewinder";
    public const string PitchPerfect = "Pitch Perfect";
    public const string BarrageAction = "Barrage";
    public const string RagingStrikesAction = "Raging Strikes";
    public const string BattleVoiceAction = "Battle Voice";
    public const string RadiantFinaleAction = "Radiant Finale";

    public const string WanderersMinuet = "The Wanderer's Minuet";
    public const string MagesBallad = "Mage's Ballad";
    public const string ArmysPaeon = "Army's Paeon";

    /// The ten seconds of haste that outlive Army's Paeon.
    public const string ArmysMuse = "Army's Muse";

    public const string CausticBiteDot = "Caustic Bite (DOT)";
    public const string StormbiteDot = "Stormbite (DOT)";

    public const string Ladonsbite = "Ladonsbite";
    public const string Shadowbite = "Shadowbite";
    public const string RainOfDeath = "Rain of Death";
}

/// Bard's action table.
public static class BardData
{
    /// The falloff on Bard's cleaving actions: 50% off each enemy after the first, shared by Pitch Perfect,
    /// Blast Arrow, Resonant Arrow and Radiant Encore.
    public const double BardFalloff = 0.50;


    /// Chance a Burst Shot, Iron Jaws or a damage-over-time application grants Hawk's Eye.
    public const double HawksEyeChance = 0.35;

    public const double HawksEyeDuration = 30.0;

    /// Soul Voice per Repertoire, and the gauge's cap.
    public const int SoulVoicePerRepertoire = 5;

    public const int MaxSoulVoice = 100;

    /// Soul Voice Apex Arrow needs, and the level at which it grants Blast Arrow Ready.
    public const int ApexMinimumGauge = 20;

    public const int BlastArrowThreshold = 80;

    /// Seconds between Repertoire grants while a song plays.
    public const double RepertoireInterval = 3.0 / 0.8;

    /// Pitch Perfect caps at three stacks; Army's Paeon's haste at four.
    public const int MaxPitchPerfect = 3;

    public const int MaxPaeonStacks = 4;

    /// Haste per Army's Paeon Repertoire stack, as a percentage.
    public const int PaeonHastePerStack = 4;

    /// Army's Muse runs ten seconds from the moment the next song starts.
    public const double ArmysMuseDuration = 10.0;

    /// Army's Muse: the haste that outlives Army's Paeon, by the Repertoire held when it ended.
    public static int MuseHasteFor(int repertoire) => repertoire switch
    {
        <= 0 => 0,
        1 => 1,
        2 => 2,
        3 => 4,
        _ => 12,
    };

    /// Seconds Mage's Ballad's Repertoire takes off Heartbreak Shot.
    public const double BalladRecastReduction = 7.5;


    public const double SongDuration = 45.0;

    /// How long each song is actually held before the next one replaces it.
    public const double MinuetHold = 43.0;

    /// Seconds left on Radiant Encore Ready below which it is cast regardless of the burst.
    public const double EncoreHoldFloor = 6.0;

    public const double BalladHold = 41.0;

    public const double SongCycle = 120.0;


    public const int CausticBiteTickPotency = 20;
    public const int StormbiteTickPotency = 25;

    /// Both damage-over-time effects run 45 seconds.
    public const double DotDuration = 45.0;

    public const double TickInterval = 3.0;

    /// Forty-five seconds of three-second ticks.
    public const int DotTicks = (int)(DotDuration / TickInterval);

    public const int CausticBiteDotTotal = CausticBiteTickPotency * DotTicks;
    public const int StormbiteDotTotal = StormbiteTickPotency * DotTicks;


    public const double RagingStrikesMulti = 1.15;
    public const double RagingStrikesDuration = 20.0;
    public const double BattleVoiceDirectHitBonus = 0.20;
    public const double BattleVoiceDuration = 20.0;
    public const double RadiantFinaleDuration = 20.0;
    public const double BarrageDuration = 10.0;
    public const double ReadyDuration = 30.0;
    public const double BlastArrowReadyDuration = 10.0;

    /// Refulgent Arrow strikes three times under Barrage.
    public const int BarrageHits = 3;

    /// Radiant Finale's damage bonus by the number of distinct Coda it consumed.
    public static double RadiantFinaleMulti(int coda) => coda switch
    {
        >= 3 => 1.06,
        2 => 1.04,
        1 => 1.02,
        _ => 1.0,
    };

    /// Radiant Encore's potency by the number of Coda the Radiant Finale before it consumed.
    public static int RadiantEncorePotency(int coda) => coda switch
    {
        >= 3 => 1100,
        2 => 800,
        _ => 700,
    };

    /// Apex Arrow's potency at a given Soul Voice: 140 at the 20 minimum, 700 at 100, linear between.
    public static double ApexArrowPotency(int soulVoice)
    {
        var gauge = System.Math.Clamp(soulVoice, ApexMinimumGauge, MaxSoulVoice);
        return 140 + (gauge - ApexMinimumGauge) * (560.0 / (MaxSoulVoice - ApexMinimumGauge));
    }

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Brd.Ladonsbite] = new ActionDef
        {
            Name = Brd.Ladonsbite,
            Kind = ActionKind.Gcd,
            Potency = 140,
            MaxTargets = ActionDef.AllNearby,
        },

        [Brd.Shadowbite] = new ActionDef
        {
            Name = Brd.Shadowbite,
            Kind = ActionKind.Gcd,
            Potency = 200,
            MaxTargets = ActionDef.AllNearby,
        },

        [Brd.RainOfDeath] = new ActionDef
        {
            Name = Brd.RainOfDeath,
            Kind = ActionKind.OffGcd,
            Potency = 100,
            SharesCooldownWith = [Brd.HeartbreakShot],
            MaxTargets = ActionDef.AllNearby,
        },

        [Brd.BurstShot] = new ActionDef
        {
            Name = Brd.BurstShot,
            Kind = ActionKind.Gcd,
            Potency = 220,
            PreservesCombo = true,
        },

        [Brd.RefulgentArrow] = new ActionDef
        {
            Name = Brd.RefulgentArrow,
            Kind = ActionKind.Gcd,
            Potency = 280,
            PreservesCombo = true,
        },

        [Brd.CausticBite] = new ActionDef
        {
            Name = Brd.CausticBite,
            Kind = ActionKind.Gcd,
            Potency = 150,
            PreservesCombo = true,
        },

        [Brd.Stormbite] = new ActionDef
        {
            Name = Brd.Stormbite,
            Kind = ActionKind.Gcd,
            Potency = 100,
            PreservesCombo = true,
        },

        [Brd.IronJaws] = new ActionDef
        {
            Name = Brd.IronJaws,
            Kind = ActionKind.Gcd,
            Potency = 100,
            PreservesCombo = true,
        },

        [Brd.ApexArrow] = new ActionDef
        {
            Name = Brd.ApexArrow,
            MaxTargets = ActionDef.AllNearby,            Kind = ActionKind.Gcd,
            Potency = 700,
            PreservesCombo = true,
        },

        [Brd.BlastArrow] = new ActionDef
        {
            Name = Brd.BlastArrow,
            MaxTargets = ActionDef.AllNearby,
            Falloff = BardFalloff,
            Kind = ActionKind.Gcd,
            Potency = 700,
            PreservesCombo = true,
        },

        [Brd.ResonantArrow] = new ActionDef
        {
            Name = Brd.ResonantArrow,
            MaxTargets = ActionDef.AllNearby,
            Falloff = BardFalloff,
            Kind = ActionKind.Gcd,
            Potency = 640,
            PreservesCombo = true,
        },

        [Brd.RadiantEncore] = new ActionDef
        {
            Name = Brd.RadiantEncore,
            MaxTargets = ActionDef.AllNearby,
            Falloff = BardFalloff,
            Kind = ActionKind.Gcd,
            Potency = 1100,
            PreservesCombo = true,
        },

        [Brd.HeartbreakShot] = new ActionDef
        {
            Name = Brd.HeartbreakShot,
            Kind = ActionKind.OffGcd,
            Potency = 180,
            Cooldown = 15.0,
            MaxCharges = 3,
        },

        [Brd.EmpyrealArrow] = new ActionDef
        {
            Name = Brd.EmpyrealArrow,
            Kind = ActionKind.OffGcd,
            Potency = 260,
            Cooldown = 15.0,
        },

        [Brd.Sidewinder] = new ActionDef
        {
            Name = Brd.Sidewinder,
            Kind = ActionKind.OffGcd,
            Potency = 400,
            Cooldown = 60.0,
        },

        [Brd.PitchPerfect] = new ActionDef
        {
            Name = Brd.PitchPerfect,
            MaxTargets = ActionDef.AllNearby,
            Falloff = BardFalloff,
            Kind = ActionKind.OffGcd,
            Potency = 360,
            Cooldown = 1.0,
        },

        [Brd.BarrageAction] = new ActionDef
        {
            Name = Brd.BarrageAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Brd.RagingStrikesAction] = new ActionDef
        {
            Name = Brd.RagingStrikesAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Brd.BattleVoiceAction] = new ActionDef
        {
            Name = Brd.BattleVoiceAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Brd.RadiantFinaleAction] = new ActionDef
        {
            Name = Brd.RadiantFinaleAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 110.0,
        },

        [Brd.WanderersMinuet] = new ActionDef
        {
            Name = Brd.WanderersMinuet,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Brd.MagesBallad] = new ActionDef
        {
            Name = Brd.MagesBallad,
            Kind = ActionKind.OffGcd,
            Cooldown = 120.0,
        },

        [Brd.ArmysPaeon] = new ActionDef
        {
            Name = Brd.ArmysPaeon,
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
