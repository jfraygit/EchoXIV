using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Ninja;

/// Where Ninja's numbers come from.
internal static class NinjaDataProvenance;

/// Status, pool and action names.
public static class Nin
{
    public const string MudraCharges = "Mudra";

    public const string ShadowWalker = "Shadow Walker";
    public const string Kassatsu = "Kassatsu";
    public const string Meisui = "Meisui";
    public const string Bunshin = "Bunshin";
    public const string PhantomReady = "Phantom Kamaitachi Ready";
    public const string RaijuReady = "Raiju Ready";
    public const string Higi = "Higi";
    public const string TenriJindoReady = "Tenri Jindo Ready";
    public const string TenChiJin = "Ten Chi Jin";

    public const string KunaisBane = "Kunai's Bane";
    public const string Dokumori = "Dokumori";

    public const string SpinningEdge = "Spinning Edge";
    public const string GustSlash = "Gust Slash";
    public const string AeolianEdge = "Aeolian Edge";
    public const string ArmorCrush = "Armor Crush";
    public const string PhantomKamaitachi = "Phantom Kamaitachi";
    public const string FleetingRaiju = "Fleeting Raiju";
    public const string FumaShuriken = "Fuma Shuriken";
    public const string Raiton = "Raiton";
    public const string Suiton = "Suiton";
    public const string HyoshoRanryu = "Hyosho Ranryu";
    public const string TcjFuma = "Fuma Shuriken (TCJ)";
    public const string TcjRaiton = "Raiton (TCJ)";
    public const string TcjSuiton = "Suiton (TCJ)";
    public const string KassatsuAction = "Kassatsu";
    public const string DokumoriAction = "Dokumori";
    public const string KunaisBaneAction = "Kunai's Bane";
    public const string DreamWithinADream = "Dream Within a Dream";
    public const string Bhavacakra = "Bhavacakra";
    public const string ZeshoMeppo = "Zesho Meppo";
    public const string TenChiJinAction = "Ten Chi Jin";
    public const string TenriJindo = "Tenri Jindo";
    public const string MeisuiAction = "Meisui";
    public const string BunshinAction = "Bunshin";

    public const string DeathBlossom = "Death Blossom";
    public const string HakkeMujinsatsu = "Hakke Mujinsatsu";
    public const string Katon = "Katon";
    public const string GokaMekkyaku = "Goka Mekkyaku";
    public const string HellfrogMedium = "Hellfrog Medium";
    public const string DeathfrogMedium = "Deathfrog Medium";
}

/// Ninja's level 100 action table, patch 7.55, single target only.
public static class NinjaData
{
    public const int MaxNinki = 100;
    public const int MaxKazematoi = 5;
    public const int MaxRaijuStacks = 3;

    /// Ninki each of the two spenders costs.
    public const int SpenderCost = 50;

    /// Bunshin's shadow deals this much per weaponskill it copies, before the pet penalty.
    public const int BunshinShadowPotency = 160;

    /// And HALF that when the weaponskill it copies is an area one, which the shadow was not doing.
    public const int BunshinAreaShadowPotency = 80;

    /// Aeolian Edge gains this much when it spends a Kazematoi stack.
    public const int KazematoiBonus = 100;

    /// What a pet's listed potency is actually worth.
    public const double PetPotencyMultiplier = 0.92;

    /// Kassatsu's bonus to the ninjutsu that consumes it.
    public const double KassatsuMulti = 1.30;

    /// Kunai's Bane, self only - which is why pet damage divides it back out.
    public const double KunaisBaneMulti = 1.10;

    /// Dokumori, a damage-taken debuff on the target and so applied to everything.
    public const double DokumoriMulti = 1.05;

    /// How far apart Ten Chi Jin's ninjutsu land.
    public const double TenChiJinRecast = 1.0;

    /// Mudra pool: two charges on a 20s recast, shared by every ninjutsu that costs one.
    public const double MudraRecast = 20.0;
    public const int MudraMaxCharges = 2;

    /// The two actions whose damage is dealt by the shadow rather than by the player.
    public static readonly HashSet<string> PetActions = [Nin.PhantomKamaitachi, Nin.Bunshin];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Nin.SpinningEdge] = new()
        {
            Name = Nin.SpinningEdge,
            Kind = ActionKind.Gcd,
            Potency = 300,
            IsComboStarter = true,
        },
        [Nin.GustSlash] = new()
        {
            Name = Nin.GustSlash,
            Kind = ActionKind.Gcd,
            Potency = 240,
            ComboPotency = 400,
            ComboFrom = Nin.SpinningEdge,
        },
        [Nin.AeolianEdge] = new()
        {
            Name = Nin.AeolianEdge,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 280,
            ComboPotency = 460,
            ComboFrom = Nin.GustSlash,
        },
        [Nin.ArmorCrush] = new()
        {
            Name = Nin.ArmorCrush,
            PositionalBonus = 60,            Kind = ActionKind.Gcd,
            Potency = 300,
            ComboPotency = 500,
            ComboFrom = Nin.GustSlash,
        },

        [Nin.DeathBlossom] = new()
        {
            Name = Nin.DeathBlossom,
            Kind = ActionKind.Gcd,
            Potency = 100,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },
        [Nin.HakkeMujinsatsu] = new()
        {
            Name = Nin.HakkeMujinsatsu,
            Kind = ActionKind.Gcd,
            Potency = 100,
            ComboPotency = 120,
            ComboFrom = Nin.DeathBlossom,
            MaxTargets = ActionDef.AllNearby,
        },
        [Nin.Katon] = new()
        {
            Name = Nin.Katon,
            Kind = ActionKind.Ninjutsu,
            Potency = 350,
            MudraCost = 2,
            SharesCooldownWith = [Nin.MudraCharges],
            MaxTargets = ActionDef.AllNearby,
        },
        [Nin.GokaMekkyaku] = new()
        {
            Name = Nin.GokaMekkyaku,
            Kind = ActionKind.Ninjutsu,
            Potency = 850,
            MudraCost = 2,
            MaxTargets = ActionDef.AllNearby,
        },
        [Nin.HellfrogMedium] = new()
        {
            Name = Nin.HellfrogMedium,
            Kind = ActionKind.OffGcd,
            Potency = 250,
            Cooldown = 1,
            SharesCooldownWith = [Nin.Bhavacakra],
            MaxTargets = ActionDef.AllNearby,
        },
        [Nin.DeathfrogMedium] = new()
        {
            Name = Nin.DeathfrogMedium,
            Kind = ActionKind.OffGcd,
            Potency = 400,
            Cooldown = 1,
            SharesCooldownWith = [Nin.Bhavacakra],
            MaxTargets = ActionDef.AllNearby,
        },
        [Nin.PhantomKamaitachi] = new()
        {
            Name = Nin.PhantomKamaitachi,
            Kind = ActionKind.Gcd,
            Potency = 700,
            PreservesCombo = true,
            MaxTargets = ActionDef.AllNearby,
        },
        [Nin.FleetingRaiju] = new()
        {
            Name = Nin.FleetingRaiju,
            Kind = ActionKind.Gcd,
            Potency = 700,
            PreservesCombo = true,
        },

        [Nin.FumaShuriken] = new()
        {
            Name = Nin.FumaShuriken,
            Kind = ActionKind.Ninjutsu,
            Potency = 500,
            MudraCost = 1,
            SharesCooldownWith = [Nin.MudraCharges],
        },
        [Nin.Raiton] = new()
        {
            Name = Nin.Raiton,
            Kind = ActionKind.Ninjutsu,
            Potency = 740,
            MudraCost = 2,
            SharesCooldownWith = [Nin.MudraCharges],
        },
        [Nin.Suiton] = new()
        {
            Name = Nin.Suiton,
            Kind = ActionKind.Ninjutsu,
            Potency = 580,
            MudraCost = 3,
            SharesCooldownWith = [Nin.MudraCharges],
        },
        [Nin.HyoshoRanryu] = new()
        {
            Name = Nin.HyoshoRanryu,
            Kind = ActionKind.Ninjutsu,
            Potency = 1300,
            MudraCost = 2,
        },

        [Nin.TcjFuma] = new()
        {
            Name = Nin.TcjFuma,
            Kind = ActionKind.Ninjutsu,
            Potency = 500,
            FixedRecast = TenChiJinRecast,
        },
        [Nin.TcjRaiton] = new()
        {
            Name = Nin.TcjRaiton,
            Kind = ActionKind.Ninjutsu,
            Potency = 740,
            FixedRecast = TenChiJinRecast,
        },
        [Nin.TcjSuiton] = new()
        {
            Name = Nin.TcjSuiton,
            Kind = ActionKind.Ninjutsu,
            Potency = 580,
            FixedRecast = TenChiJinRecast,
        },

        [Nin.KassatsuAction] = new()
        {
            Name = Nin.KassatsuAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 60,
        },
        [Nin.DokumoriAction] = new()
        {
            Name = Nin.DokumoriAction,
            Kind = ActionKind.OffGcd,
            Potency = 400,
            Cooldown = 120,
            MaxTargets = ActionDef.AllNearby,
        },
        [Nin.KunaisBaneAction] = new()
        {
            Name = Nin.KunaisBaneAction,
            Kind = ActionKind.OffGcd,
            Potency = 700,
            Cooldown = 60,
            MaxTargets = ActionDef.AllNearby,
        },
        [Nin.DreamWithinADream] = new()
        {
            Name = Nin.DreamWithinADream,
            Kind = ActionKind.OffGcd,
            Potency = 180,
            Cooldown = 60,
        },
        [Nin.Bhavacakra] = new()
        {
            Name = Nin.Bhavacakra,
            Kind = ActionKind.OffGcd,
            Potency = 400,
            Cooldown = 1,
        },
        [Nin.ZeshoMeppo] = new()
        {
            Name = Nin.ZeshoMeppo,
            Kind = ActionKind.OffGcd,
            Potency = 700,
            Cooldown = 1,
            SharesCooldownWith = [Nin.Bhavacakra],
        },
        [Nin.TenChiJinAction] = new()
        {
            Name = Nin.TenChiJinAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 120,
        },
        [Nin.TenriJindo] = new()
        {
            Name = Nin.TenriJindo,
            Kind = ActionKind.OffGcd,
            Potency = 1100,
            Cooldown = 1,
            MaxTargets = ActionDef.AllNearby,
        },
        [Nin.MeisuiAction] = new()
        {
            Name = Nin.MeisuiAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 120,
        },
        [Nin.BunshinAction] = new()
        {
            Name = Nin.BunshinAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = 90,
        },

        [Buffs.PotionAction] = new()
        {
            Name = Buffs.PotionAction,
            Kind = ActionKind.OffGcd,
            Potency = 0,
            Cooldown = PlayerStats.PotionRecast,
        },
    };

    public static ActionDef Get(string name) => Actions[name];
}
