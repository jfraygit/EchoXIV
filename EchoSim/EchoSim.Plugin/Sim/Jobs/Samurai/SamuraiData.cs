using EchoSim.Sim.Engine;

namespace EchoSim.Sim.Jobs.Samurai;

/// Status and action names, kept in one place so typos surface at compile time.
public static class Sam
{
    public const string Fugetsu = "Fugetsu";
    public const string Fuka = "Fuka";
    public const string Meikyo = "Meikyo Shisui";
    public const string Tendo = "Tendo";
    public const string TsubameReady = "Tsubame-gaeshi Ready";
    public const string TendoKaeshiReady = "Tendo Kaeshi Ready";
    public const string OgiReady = "Ogi Namikiri Ready";
    public const string KaeshiNamikiriReady = "Kaeshi Namikiri Ready";
    public const string ZanshinReady = "Zanshin Ready";

    public const string Gyofu = "Gyofu";

    public const string Jinpu = "Jinpu";
    public const string Gekko = "Gekko";

    public const string Shifu = "Shifu";
    public const string Kasha = "Kasha";

    public const string Yukikaze = "Yukikaze";

    public const string Higanbana = "Higanbana";
    public const string MidareSetsugekka = "Midare Setsugekka";
    public const string KaeshiSetsugekka = "Kaeshi: Setsugekka";
    public const string TendoSetsugekka = "Tendo Setsugekka";
    public const string TendoKaeshiSetsugekka = "Tendo Kaeshi Setsugekka";

    public const string OgiNamikiri = "Ogi Namikiri";
    public const string KaeshiNamikiri = "Kaeshi: Namikiri";

    public const string Shinten = "Hissatsu: Shinten";
    public const string Gyoten = "Hissatsu: Gyoten";
    public const string Senei = "Hissatsu: Senei";
    public const string Shoha = "Shoha";
    public const string Zanshin = "Zanshin";
    public const string MeikyoAction = "Meikyo Shisui";
    public const string Ikishoten = "Ikishoten";

    public const string HiganbanaDot = "Higanbana (DOT)";

    public const string Fuko = "Fuko";
    public const string Mangetsu = "Mangetsu";
    public const string Oka = "Oka";
    public const string Kyuten = "Hissatsu: Kyuten";
    public const string Guren = "Hissatsu: Guren";
    public const string TenkaGoken = "Tenka Goken";
    public const string KaeshiGoken = "Kaeshi: Goken";
    public const string TendoGoken = "Tendo Goken";
    public const string TendoKaeshiGoken = "Tendo Kaeshi Goken";
}

/// Samurai's action table.
public static class SamuraiData
{
    /// The falloff on Samurai's four cleaving abilities and weaponskills: 40% off each enemy after the first,
    /// shared by Shoha, Zanshin, Ogi Namikiri and Kaeshi: Namikiri.
    public const double SamuraiFalloff = 0.40;

    /// Fugetsu, from Jinpu or from a Gekko pressed under Meikyo Shisui.
    public const double FugetsuMulti = 1.13;

    public const double FugetsuDuration = 40.0;
    public const double FukaDuration = 40.0;

    /// Meikyo Shisui's own window, and the Tendo it grants at level 100.
    public const double MeikyoDuration = 20.0;

    public const double TendoDuration = 30.0;

    /// Weaponskills Meikyo Shisui lets you execute without their combo prerequisite.
    public const int MeikyoStacks = 3;

    public const double TsubameDuration = 30.0;
    public const double IkishotenBuffDuration = 30.0;

    /// Higanbana's damage-over-time: 50 potency a tick for 60s, so twenty ticks.
    public const double HiganbanaDotDuration = 60.0;

    public const int HiganbanaTickPotency = 50;
    public const int HiganbanaTicks = 20;

    public const int MaxKenki = 100;
    public const int MaxMeditation = 3;

    public const int ShintenCost = 25;
    public const int GyotenCost = 10;
    public const int SeneiCost = 25;
    public const int ZanshinCost = 50;

    public static ActionDef Get(string name) => Actions[name];

    public static readonly IReadOnlyDictionary<string, ActionDef> Actions = new Dictionary<string, ActionDef>
    {
        [Sam.Gyofu] = new ActionDef
        {
            Name = Sam.Gyofu,
            Kind = ActionKind.Gcd,
            Potency = 240,
            IsComboStarter = true,
        },

        [Sam.Jinpu] = new ActionDef
        {
            Name = Sam.Jinpu,
            Kind = ActionKind.Gcd,
            Potency = 140,
            ComboPotency = 300,
            ComboFrom = Sam.Gyofu,
        },

        [Sam.Gekko] = new ActionDef
        {
            Name = Sam.Gekko,
            PositionalBonus = 50,            Kind = ActionKind.Gcd,
            Potency = 160,
            ComboPotency = 420,
            ComboFrom = Sam.Jinpu,
        },

        [Sam.Shifu] = new ActionDef
        {
            Name = Sam.Shifu,
            Kind = ActionKind.Gcd,
            Potency = 140,
            ComboPotency = 300,
            ComboFrom = Sam.Gyofu,
        },

        [Sam.Kasha] = new ActionDef
        {
            Name = Sam.Kasha,
            PositionalBonus = 50,            Kind = ActionKind.Gcd,
            Potency = 160,
            ComboPotency = 420,
            ComboFrom = Sam.Shifu,
        },

        [Sam.Yukikaze] = new ActionDef
        {
            Name = Sam.Yukikaze,
            Kind = ActionKind.Gcd,
            Potency = 160,
            ComboPotency = 340,
            ComboFrom = Sam.Gyofu,
        },

        [Sam.Higanbana] = new ActionDef
        {
            Name = Sam.Higanbana,
            Kind = ActionKind.Gcd,
            Potency = 200,
        },

        [Sam.MidareSetsugekka] = new ActionDef
        {
            Name = Sam.MidareSetsugekka,
            Kind = ActionKind.Gcd,
            Potency = 680,
        },

        [Sam.KaeshiSetsugekka] = new ActionDef
        {
            Name = Sam.KaeshiSetsugekka,
            Kind = ActionKind.Gcd,
            Potency = 680,
        },

        [Sam.TendoSetsugekka] = new ActionDef
        {
            Name = Sam.TendoSetsugekka,
            Kind = ActionKind.Gcd,
            Potency = 1100,
        },

        [Sam.TendoKaeshiSetsugekka] = new ActionDef
        {
            Name = Sam.TendoKaeshiSetsugekka,
            Kind = ActionKind.Gcd,
            Potency = 1100,
        },

        [Sam.OgiNamikiri] = new ActionDef
        {
            Name = Sam.OgiNamikiri,
            Kind = ActionKind.Gcd,
            Potency = 1000,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SamuraiFalloff,
        },

        [Sam.KaeshiNamikiri] = new ActionDef
        {
            Name = Sam.KaeshiNamikiri,
            Kind = ActionKind.Gcd,
            Potency = 1000,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SamuraiFalloff,
        },

        [Sam.Fuko] = new ActionDef
        {
            Name = Sam.Fuko,
            Kind = ActionKind.Gcd,
            Potency = 100,
            IsComboStarter = true,
            MaxTargets = ActionDef.AllNearby,
        },

        [Sam.Mangetsu] = new ActionDef
        {
            Name = Sam.Mangetsu,
            Kind = ActionKind.Gcd,
            Potency = 100,
            ComboPotency = 120,
            ComboFrom = Sam.Fuko,
            MaxTargets = ActionDef.AllNearby,
        },

        [Sam.Oka] = new ActionDef
        {
            Name = Sam.Oka,
            Kind = ActionKind.Gcd,
            Potency = 100,
            ComboPotency = 120,
            ComboFrom = Sam.Fuko,
            MaxTargets = ActionDef.AllNearby,
        },

        [Sam.TenkaGoken] = new ActionDef
        {
            Name = Sam.TenkaGoken,
            Kind = ActionKind.Gcd,
            Potency = 300,
            MaxTargets = ActionDef.AllNearby,
        },

        [Sam.KaeshiGoken] = new ActionDef
        {
            Name = Sam.KaeshiGoken,
            Kind = ActionKind.Gcd,
            Potency = 300,
            MaxTargets = ActionDef.AllNearby,
        },

        [Sam.TendoGoken] = new ActionDef
        {
            Name = Sam.TendoGoken,
            Kind = ActionKind.Gcd,
            Potency = 410,
            MaxTargets = ActionDef.AllNearby,
        },

        [Sam.TendoKaeshiGoken] = new ActionDef
        {
            Name = Sam.TendoKaeshiGoken,
            Kind = ActionKind.Gcd,
            Potency = 410,
            MaxTargets = ActionDef.AllNearby,
        },

        [Sam.Shinten] = new ActionDef
        {
            Name = Sam.Shinten,
            Kind = ActionKind.OffGcd,
            Potency = 250,
            Cooldown = 1.0,
        },

        [Sam.Gyoten] = new ActionDef
        {
            Name = Sam.Gyoten,
            Kind = ActionKind.OffGcd,
            Potency = 100,
            Cooldown = 5.0,
        },

        [Sam.Kyuten] = new ActionDef
        {
            Name = Sam.Kyuten,
            Kind = ActionKind.OffGcd,
            Potency = 100,
            Cooldown = 1.0,
            SharesCooldownWith = [Sam.Shinten],
            MaxTargets = ActionDef.AllNearby,
        },

        [Sam.Senei] = new ActionDef
        {
            Name = Sam.Senei,
            Kind = ActionKind.OffGcd,
            Potency = 800,
            Cooldown = 60.0,
        },

        [Sam.Guren] = new ActionDef
        {
            Name = Sam.Guren,
            Kind = ActionKind.OffGcd,
            Potency = 400,
            Cooldown = 60.0,
            SharesCooldownWith = [Sam.Senei],
            MaxTargets = ActionDef.AllNearby,
        },

        [Sam.Shoha] = new ActionDef
        {
            Name = Sam.Shoha,
            Kind = ActionKind.OffGcd,
            Potency = 640,
            Cooldown = 15.0,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SamuraiFalloff,
        },

        [Sam.Zanshin] = new ActionDef
        {
            Name = Sam.Zanshin,
            Kind = ActionKind.OffGcd,
            Potency = 940,
            Cooldown = 1.0,
            MaxTargets = ActionDef.AllNearby,
            Falloff = SamuraiFalloff,
        },

        [Sam.MeikyoAction] = new ActionDef
        {
            Name = Sam.MeikyoAction,
            Kind = ActionKind.OffGcd,
            Cooldown = 55.0,
            MaxCharges = 2,
        },

        [Sam.Ikishoten] = new ActionDef
        {
            Name = Sam.Ikishoten,
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
