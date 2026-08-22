using EchoSim.Sim;

namespace EchoSim;

/// One job's gear, melds, relic and food - everything that describes a set rather than a preference.
public sealed class JobSetup
{
    public StatPreset Stats { get; set; } = new();

    public bool StatsFromCharacterSheet { get; set; }

    public Dictionary<string, uint> GearSelection { get; set; } = [];

    public Dictionary<string, List<int>> MateriaSelection { get; set; } = [];

    public RelicAllocation Relic { get; set; } = new();

    public Dictionary<string, int> InferredRelicStats { get; set; } = [];

    public StatPreset? BaselineStats { get; set; }

    public Dictionary<string, uint> BaselineGear { get; set; } = [];

    public Dictionary<string, List<int>> BaselineMateria { get; set; } = [];

    public Dictionary<string, int> BaselineRelicStats { get; set; } = [];

    public Dictionary<string, int> ModelError { get; set; } = [];

    public Dictionary<string, int> ModelCorrection { get; set; } = [];

    /// Per job, because the meal is.
    public uint FoodItemId { get; set; }

    /// Takes a deep copy of whatever the configuration currently has live.
    public static JobSetup Capture(Configuration config) => new()
    {
        Stats = config.Stats.Clone(),
        StatsFromCharacterSheet = config.StatsFromCharacterSheet,
        GearSelection = new Dictionary<string, uint>(config.GearSelection),
        MateriaSelection = CopyMelds(config.MateriaSelection),
        Relic = config.Relic.Clone(),
        InferredRelicStats = new Dictionary<string, int>(config.InferredRelicStats),
        BaselineStats = config.BaselineStats?.Clone(),
        BaselineGear = new Dictionary<string, uint>(config.BaselineGear),
        BaselineMateria = CopyMelds(config.BaselineMateria),
        BaselineRelicStats = new Dictionary<string, int>(config.BaselineRelicStats),
        ModelError = new Dictionary<string, int>(config.ModelError),
        ModelCorrection = new Dictionary<string, int>(config.ModelCorrection),
        FoodItemId = config.FoodItemId,
    };

    /// Writes this set back over the configuration's live fields, again as a deep copy.
    public void ApplyTo(Configuration config)
    {
        config.Stats = Stats.Clone();
        config.StatsFromCharacterSheet = StatsFromCharacterSheet;
        config.GearSelection = new Dictionary<string, uint>(GearSelection);
        config.MateriaSelection = CopyMelds(MateriaSelection);
        config.Relic = Relic.Clone();
        config.InferredRelicStats = new Dictionary<string, int>(InferredRelicStats);
        config.BaselineStats = BaselineStats?.Clone();
        config.BaselineGear = new Dictionary<string, uint>(BaselineGear);
        config.BaselineMateria = CopyMelds(BaselineMateria);
        config.BaselineRelicStats = new Dictionary<string, int>(BaselineRelicStats);
        config.ModelError = new Dictionary<string, int>(ModelError);
        config.ModelCorrection = new Dictionary<string, int>(ModelCorrection);
        config.FoodItemId = FoodItemId;
    }

    /// What a job with nothing saved starts from: empty, with the stat line left to be rebuilt from the
    /// (empty) gear rather than inheriting the previous job's numbers.
    public static void Reset(Configuration config)
    {
        config.StatsFromCharacterSheet = false;
        config.GearSelection.Clear();
        config.MateriaSelection.Clear();
        config.Relic.Clear();
        config.InferredRelicStats.Clear();
        config.BaselineStats = null;
        config.BaselineGear.Clear();
        config.BaselineMateria.Clear();
        config.BaselineRelicStats.Clear();
        config.ModelError.Clear();
        config.ModelCorrection.Clear();
        config.FoodItemId = 0;
    }

    /// The meld lists are lists, so copying the dictionary alone still shares every value in it.
    private static Dictionary<string, List<int>> CopyMelds(IReadOnlyDictionary<string, List<int>> source)
        => source.ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value));
}
