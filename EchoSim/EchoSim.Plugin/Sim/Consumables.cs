namespace EchoSim.Sim;

public enum SubStat
{
    Crit,
    Determination,
    DirectHit,

    /// The speed substat: Skill Speed on physical gear, Spell Speed on magical gear.
    Speed,

    Tenacity,


    /// Healer MP regeneration.
    Piety,
}

/// Parsing for SubStat, which has to outlive its own member names.
public static class SubStats
{
    /// Reads a stat name, ACCEPTING THE SPELLINGS OLDER CONFIGURATIONS USED.
    public static bool TryParse(string? name, out SubStat stat)
    {
        stat = default;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Equals("SkillSpeed", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SpellSpeed", StringComparison.OrdinalIgnoreCase))
        {
            stat = SubStat.Speed;
            return true;
        }

        return Enum.TryParse(name, ignoreCase: true, out stat);
    }

    /// Reads a whole configuration dictionary keyed by stat name, DROPPING ANYTHING IT CANNOT READ RATHER
    /// THAN THROWING.
    public static Dictionary<SubStat, int> Parse(IReadOnlyDictionary<string, int>? stored)
    {
        var result = new Dictionary<SubStat, int>();

        if (stored is null)
            return result;

        foreach (var (key, value) in stored)
            if (TryParse(key, out var stat))
                result[stat] = result.GetValueOrDefault(stat) + value;

        return result;
    }
}

/// One stat line on a food item: a percentage of the stat coming off your gear, hard-capped.
public readonly record struct FoodParam(SubStat Stat, int Percent, int Cap)
{
    /// Bonus this line actually grants against a given gear stat.
    public int BonusFor(int gearStat) => System.Math.Min(Cap, (int)System.Math.Floor(gearStat * (Percent / 100.0)));
}

/// A food item.
public sealed class FoodDef
{
    public required string Name { get; init; }
    public uint ItemId { get; init; }
    public uint IconId { get; init; }
    public required IReadOnlyList<FoodParam> Params { get; init; }

    public static readonly FoodDef None = new() { Name = "No food", Params = [] };

    /// Caramel Popcorn, the melee raid food at patch 7.5.
    public static readonly FoodDef CaramelPopcorn = new()
    {
        Name = "Caramel Popcorn (HQ)",
        Params =
        [
            new FoodParam(SubStat.Determination, 10, 151),
            new FoodParam(SubStat.Crit, 10, 91),
        ],
    };

    /// The rest of the patch 7.5 battle food, at high quality, from the game's own ItemFood sheet.
    public static readonly FoodDef Nachos = new()
    {
        Name = "Nachos (HQ)",
        Params = [new FoodParam(SubStat.Crit, 10, 151), new FoodParam(SubStat.Speed, 10, 91)],
    };

    public static readonly FoodDef PopotoPotage = new()
    {
        Name = "Popoto Potage (HQ)",
        Params = [new FoodParam(SubStat.Crit, 10, 151), new FoodParam(SubStat.Speed, 10, 91)],
    };

    public static readonly FoodDef RockFistedPopotoStew = new()
    {
        Name = "Rock-fisted Popoto Stew (HQ)",
        Params = [new FoodParam(SubStat.DirectHit, 10, 151), new FoodParam(SubStat.Determination, 10, 91)],
    };

    public static readonly FoodDef RockFistedPopotoSalad = new()
    {
        Name = "Rock-fisted Popoto Salad (HQ)",
        Params = [new FoodParam(SubStat.Speed, 10, 151), new FoodParam(SubStat.DirectHit, 10, 91)],
    };

    public static readonly FoodDef ClamCake = new()
    {
        Name = "Clam Cake (HQ)",
        Params = [new FoodParam(SubStat.Tenacity, 10, 151), new FoodParam(SubStat.Determination, 10, 91)],
    };

    public static readonly FoodDef QuahogChowder = new()
    {
        Name = "Quahog Chowder (HQ)",
        Params = [new FoodParam(SubStat.Determination, 10, 151), new FoodParam(SubStat.Tenacity, 10, 91)],
    };

    public static readonly FoodDef RroneekAuGratin = new()
    {
        Name = "Rroneek au Gratin (HQ)",
        Params = [new FoodParam(SubStat.Speed, 10, 151), new FoodParam(SubStat.DirectHit, 10, 91)],
    };

    /// Every battle food worth considering for damage, for BestFor to choose between.
    public static readonly FoodDef[] Battle =
    [
        CaramelPopcorn, Nachos, PopotoPotage, RockFistedPopotoStew,
        RockFistedPopotoSalad, ClamCake, QuahogChowder, RroneekAuGratin,
    ];

    /// The best meal for one job's gear, chosen on the same objective the meld solver uses.
    public static FoodDef BestFor(System.Func<FoodDef, PlayerStats> withFood, AutoCritProfile profile = default)
    {
        var best = None;
        var bestIndex = GearMath.DamageIndex(withFood(None), profile);

        foreach (var food in Battle)
        {
            var index = GearMath.DamageIndex(withFood(food), profile);
            if (index > bestIndex)
            {
                bestIndex = index;
                best = food;
            }
        }

        return best;
    }

    public int BonusFor(SubStat stat, int gearStat)
    {
        foreach (var p in Params)
        {
            if (p.Stat == stat)
                return p.BonusFor(gearStat);
        }

        return 0;
    }

    /// Short description of what this food gives, for the UI.
    public string Summary(StatPreset gear)
    {
        if (Params.Count == 0)
            return "no bonuses";

        return string.Join(", ", Params.Select(p =>
        {
            var gearStat = gear.StatFor(p.Stat);
            var bonus = p.BonusFor(gearStat);
            var capped = bonus >= p.Cap ? " (capped)" : string.Empty;
            return $"+{bonus} {Label(p.Stat)}{capped}";
        }));
    }

    public static string Label(SubStat stat) => stat switch
    {
        SubStat.Crit => "Crit",
        SubStat.Determination => "Det",
        SubStat.DirectHit => "DH",
        SubStat.Speed => "SkS",
        SubStat.Piety => "Pie",
        _ => "Ten",
    };
}

/// A Gemdraught.
public sealed record PotionDef(string Name, int Percent, int Cap)
{
    public static readonly PotionDef Grade4 = new("Grade 4 Gemdraught (HQ)", 10, 541);

    /// Grade 3, for anyone not paying current-tier prices.
    public static readonly PotionDef Grade3 = new("Grade 3 Gemdraught (HQ)", 10, 461);

    public static readonly PotionDef None = new("No potion", 0, 0);

    public static readonly PotionDef[] All = [Grade4, Grade3, None];

    public int BonusFor(int mainStat) => Percent == 0 ? 0 : System.Math.Min(Cap, (int)System.Math.Floor(mainStat * (Percent / 100.0)));
}

/// Where in a fight potions get used.
public sealed record PotionPlan(string Name, IReadOnlyList<double> Times)
{
    /// Opener pot only.
    public static readonly PotionPlan Opener = new("Opener", [0]);

    /// Two pots, the second lined up with the six-minute burst.
    public static readonly PotionPlan ZeroSix = new("0 / 6", [0, 360]);

    /// Three pots across a ten-minute fight, on the tightest legal spacing.
    public static readonly PotionPlan ZeroFiveTen = new("0 / 5 / 10", [0, 300, 600]);

    /// Pushed later to line up with a phase, for fights that run past ten minutes.
    public static readonly PotionPlan ZeroSixTenThirty = new("0 / 6 / 10:30", [0, 360, 630]);

    /// Six-minute spacing throughout, for longer fights.
    public static readonly PotionPlan ZeroSixTwelve = new("0 / 6 / 12", [0, 360, 720]);

    /// Clearing the timings box is how you get no potions, so None isn't offered as a preset.
    public static readonly PotionPlan[] All = [Opener, ZeroSix, ZeroFiveTen, ZeroSixTenThirty, ZeroSixTwelve];
}

/// Parses and validates hand-written potion timings.
public static class PotionTimings
{
    public static bool TryParse(string input, double fightDuration, out List<double> times, out string error)
    {
        times = [];
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return true;

        var tokens = input.Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (!TryParseOne(token, out var seconds))
            {
                error = $"Couldn't read \"{token}\". Use minutes, like 0, 5, 10:30.";
                return false;
            }

            times.Add(seconds);
        }

        times.Sort();

        for (var i = 1; i < times.Count; i++)
        {
            var gap = times[i] - times[i - 1];
            if (gap < PlayerStats.PotionRecast - 1e-6)
            {
                error = $"{Format(times[i - 1])} and {Format(times[i])} are {gap:F0}s apart - " +
                        $"Gemdraughts share a {PlayerStats.PotionRecast:F0}s recast.";
                return false;
            }
        }

        var past = times.Count(t => t >= fightDuration);
        if (past > 0)
            error = $"{past} timing(s) fall after the fight ends and will be ignored.";

        return true;
    }

    private static bool TryParseOne(string token, out double seconds)
    {
        seconds = 0;

        var colon = token.IndexOf(':');
        if (colon < 0)
        {
            if (!double.TryParse(token, out var minutes) || minutes < 0)
                return false;

            seconds = minutes * 60;
            return true;
        }

        if (!int.TryParse(token[..colon], out var m) || m < 0)
            return false;

        if (!int.TryParse(token[(colon + 1)..], out var s) || s is < 0 or >= 60)
            return false;

        seconds = (m * 60) + s;
        return true;
    }

    public static string Format(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    public static string Format(IReadOnlyList<double> times) => string.Join(", ", times.Select(Format));
}
