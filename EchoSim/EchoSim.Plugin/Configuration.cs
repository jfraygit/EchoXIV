using Dalamud.Configuration;
using EchoSim.Sim;

namespace EchoSim;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// Fight length in seconds.
    public double FightDuration { get; set; } = DefaultFightDuration;

    /// How many enemies the simulated fight is against.
    public int Targets { get; set; } = 1;

    /// Whether positionals are assumed to land at three or more targets.
    public bool AssumePositionals { get; set; }

    public const double DefaultFightDuration = 600;

    /// Whether to apply the +5% main stat a full party grants.
    public bool PartyBonus { get; set; }

    /// The stat line being simulated.
    public StatPreset Stats { get; set; } = StatPresets.NinjaBis();

    /// Potion timings as typed by the user, in minutes ("0, 5, 10:30").
    public string PotionTimingsText { get; set; } = "0, 5, 10";

    /// Index into PotionDef.All.
    public int PotionGradeIndex { get; set; }

    /// Item ids chosen per gear slot, keyed by Game.GearSlot name.
    public Dictionary<string, uint> GearSelection { get; set; } = [];

    /// Materia melded into each slot, as encoded Game.MateriaOption values.
    public Dictionary<string, List<int>> MateriaSelection { get; set; } = [];

    /// Substats chosen on a customisable relic weapon.
    public RelicAllocation Relic { get; set; } = new();

    /// True when Stats came from the character sheet rather than being built from the gear catalogue.
    public bool StatsFromCharacterSheet { get; set; }

    /// Relic substats worked out by subtracting all known contributions from the character totals.
    public Dictionary<string, int> InferredRelicStats { get; set; } = [];


    /// A snapshot of the gear read off the character, kept so any set built afterwards can be shown as a diff
    /// against what you're actually wearing.
    public StatPreset? BaselineStats { get; set; }

    public Dictionary<string, uint> BaselineGear { get; set; } = [];

    public Dictionary<string, List<int>> BaselineMateria { get; set; } = [];

    public Dictionary<string, int> BaselineRelicStats { get; set; } = [];

    public bool HasBaseline => BaselineStats is not null;

    /// Per-stat difference between the modelled version of your equipped gear and the character sheet.
    public Dictionary<string, int> ModelError { get; set; } = [];

    /// Per-stat offset that makes a catalogue-built set line up with the character sheet - racial bonus and
    /// any residual the item data doesn't carry.
    public Dictionary<string, int> ModelCorrection { get; set; } = [];

    /// Rewrites stat names this build no longer spells the same way, ONCE, ON LOAD.
    public Dictionary<uint, JobSetup> JobSetups { get; set; } = [];

    /// Files the live fields under the job they belong to.
    public void CaptureActiveSetup() => JobSetups[SelectedJobId] = JobSetup.Capture(this);

    /// Makes jobId the selected job, filing the outgoing set and restoring that job's own - or clearing to
    /// empty when it has none saved yet.
    public bool ApplySetup(uint jobId)
    {
        CaptureActiveSetup();

        SelectedJobId = jobId;

        if (JobSetups.TryGetValue(jobId, out var saved))
        {
            saved.ApplyTo(this);
            return true;
        }

        JobSetup.Reset(this);
        return false;
    }

    /// Returns: True if anything was rewritten, so the caller can persist it.
    public bool Migrate()
    {
        var changed = false;

        changed |= Canonicalise(InferredRelicStats);
        changed |= Canonicalise(BaselineRelicStats);
        changed |= Canonicalise(ModelError);
        changed |= Canonicalise(ModelCorrection);

        foreach (var setup in JobSetups.Values)
        {
            changed |= Canonicalise(setup.InferredRelicStats);
            changed |= Canonicalise(setup.BaselineRelicStats);
            changed |= Canonicalise(setup.ModelError);
            changed |= Canonicalise(setup.ModelCorrection);
        }

        if (!JobSetups.ContainsKey(SelectedJobId))
        {
            CaptureActiveSetup();
            changed = true;
        }

        return changed;

        static bool Canonicalise(Dictionary<string, int> stored)
        {
            var changed = false;

            foreach (var key in stored.Keys.ToList())
            {
                if (!SubStats.TryParse(key, out var stat))
                    continue;

                var canonical = stat.ToString();
                if (canonical == key)
                    continue;

                stored[canonical] = stored.GetValueOrDefault(canonical) + stored[key];
                stored.Remove(key);
                changed = true;
            }

            return changed;
        }
    }

    /// ClassJob row id of the job being simulated.
    public uint SelectedJobId { get; set; } = Game.JobList.Ninja;

    /// Item id of the chosen food, or 0 for none.
    public uint FoodItemId { get; set; }

    /// Whether to show the live execution score during combat.
    public bool ShowLiveScore { get; set; }

    /// Whether the panel stays up between pulls instead of vanishing when combat drops.
    public bool LiveScoreAlwaysOn { get; set; }

    /// Where the live panel was last left, or UnsetPosition for "never placed".
    public float LiveScoreX { get; set; } = UnsetPosition;

    public float LiveScoreY { get; set; } = UnsetPosition;

    /// Whether a kill is posted to the public leaderboards.
    public bool ShareToLeaderboards { get; set; }

    /// A random value made once, on the first run that needs it, and never shown to anyone.
    public string LeaderboardOwnerKey { get; set; } = string.Empty;

    /// The encounter last looked at on the Leaderboards tab, so it reopens where it was left.
    public string LeaderboardEncounter { get; set; } = "fru";


    /// Window size in pixels.
    public float WindowWidth { get; set; } = 760;

    public float WindowHeight { get; set; } = 680;

    public const float DefaultWindowWidth = 760;
    public const float DefaultWindowHeight = 680;

    /// Stops the window being dragged around, matching EchoMix's lock.
    public bool WindowLocked { get; set; }

    /// The newest changelog version the player has actually had open in front of them.
    public string? LastSeenChangelogVersion { get; set; }

    /// Last on-screen position of each mode, kept apart so minimising and restoring each return to where that
    /// mode was left rather than dragging the other one around with them.
    public float FullWindowX { get; set; } = UnsetPosition;

    public float FullWindowY { get; set; } = UnsetPosition;

    public float MinimisedX { get; set; } = UnsetPosition;

    public float MinimisedY { get; set; } = UnsetPosition;

    public const float UnsetPosition = -99999f;

    /// Whether the window was left collapsed to its small box.
    public bool WindowMinimised { get; set; }

    public bool WindowOpen { get; set; } = true;

    /// Accent colour, stored as components because Vector4 doesn't round-trip cleanly.
    public float AccentR { get; set; } = 0.98f;

    public float AccentG { get; set; } = 0.72f;

    public float AccentB { get; set; } = 0.22f;


    /// ClassJob ids of the other seven party members.
    public uint[] PartyJobs { get; set; } = new uint[7];

    /// Whether a Dancer in the party has you as their partner.
    public bool DancePartner { get; set; }

    /// Whether an Astrologian in the party is putting its damage card on you.
    public bool CardTarget { get; set; }

    /// Assume every party buff lands on the two-minute burst windows.
    public bool AlignPartyBuffs { get; set; } = true;

    /// Whether to apply party raid buffs at all.
    public bool UsePartyBuffs { get; set; }

    /// Parsed potion timings, or an empty list if the text doesn't validate.
    public List<double> PotionTimes
        => PotionTimings.TryParse(PotionTimingsText, FightDuration, out var times, out _) ? times : [];

    public PotionDef Potion => PotionDef.All[System.Math.Clamp(PotionGradeIndex, 0, PotionDef.All.Length - 1)];
}
