using EchoSim.Sim.Engine;
using EchoSim.Sim.Jobs.Astrologian;
using EchoSim.Sim.Jobs.Bard;
using EchoSim.Sim.Jobs.BlackMage;
using EchoSim.Sim.Jobs.Dancer;
using EchoSim.Sim.Jobs.DarkKnight;
using EchoSim.Sim.Jobs.Dragoon;
using EchoSim.Sim.Jobs.Gunbreaker;
using EchoSim.Sim.Jobs.Machinist;
using EchoSim.Sim.Jobs.Monk;
using EchoSim.Sim.Jobs.Ninja;
using EchoSim.Sim.Jobs.Paladin;
using EchoSim.Sim.Jobs.Reaper;
using EchoSim.Sim.Jobs.Pictomancer;
using EchoSim.Sim.Jobs.RedMage;
using EchoSim.Sim.Jobs.Sage;
using EchoSim.Sim.Jobs.Samurai;
using EchoSim.Sim.Jobs.Scholar;
using EchoSim.Sim.Jobs.Summoner;
using EchoSim.Sim.Jobs.Viper;
using EchoSim.Sim.Jobs.Warrior;
using EchoSim.Sim.Jobs.WhiteMage;

namespace EchoSim.Sim.Jobs;

/// Everything a simulated job supplies, looked up by ClassJob id.
public sealed record JobDefinition(
    uint ClassJobId,
    string Name,
    Func<IJobSim> CreateSim,
    Func<IReadOnlyList<double>, IRotation> CreateRotation,
    Func<StatPreset> ReferenceGear,
    (string Timing, string Action, string Why)[] PrePullSteps,
    IReadOnlyList<string> OpenerSteps)
{
    /// A global cooldown this job builds its gear around, or null when it simply takes what it gets.
    public double? TargetGcd { get; init; }
}

public static class JobRegistry
{
    public const uint NinjaId = 30;
    public const uint MonkId = 20;
    public const uint DragoonId = 22;
    public const uint SamuraiId = 34;
    public const uint ReaperId = 39;
    public const uint ViperId = 41;
    public const uint MachinistId = 31;
    public const uint BardId = 23;
    public const uint DancerId = 38;
    public const uint BlackMageId = 25;
    public const uint RedMageId = 35;

    public const uint PictomancerId = 42;

    public const uint SummonerId = 27;

    public const uint WhiteMageId = 24;
    public const uint ScholarId = 28;
    public const uint AstrologianId = 33;
    public const uint SageId = 40;

    public const uint PaladinId = 19;
    public const uint WarriorId = 21;
    public const uint DarkKnightId = 32;
    public const uint GunbreakerId = 37;

    private static readonly Dictionary<uint, JobDefinition> Registry = new()
    {
        [NinjaId] = new JobDefinition(
            NinjaId,
            "Ninja",
            () => new NinjaSim(),
            times => new NinjaStandardRotation(potionTimes: times),
            StatPresets.NinjaBis,
            NinjaStandardRotation.PrePullSteps,
            NinjaStandardRotation.OpenerSteps)
        {
            TargetGcd = 2.09,
        },

        [MonkId] = new JobDefinition(
            MonkId,
            "Monk",
            () => new MonkSim(),
            times => new MonkStandardRotation(potionTimes: times),
            StatPresets.MonkBis,
            MonkStandardRotation.PrePullSteps,
            MonkStandardRotation.OpenerSteps)
        {
            TargetGcd = 1.94,
        },

        [DragoonId] = new JobDefinition(
            DragoonId,
            "Dragoon",
            () => new DragoonSim(),
            times => new DragoonStandardRotation(potionTimes: times),
            StatPresets.DragoonBis,
            DragoonStandardRotation.PrePullSteps,
            DragoonStandardRotation.OpenerSteps),

        [SamuraiId] = new JobDefinition(
            SamuraiId,
            "Samurai",
            () => new SamuraiSim(),
            times => new SamuraiStandardRotation(potionTimes: times),
            StatPresets.SamuraiBis,
            SamuraiStandardRotation.PrePullSteps,
            SamuraiStandardRotation.OpenerSteps)
        {
            TargetGcd = 2.14,
        },

        [ReaperId] = new JobDefinition(
            ReaperId,
            "Reaper",
            () => new ReaperSim(),
            times => new ReaperStandardRotation(potionTimes: times),
            StatPresets.ReaperBis,
            ReaperStandardRotation.PrePullSteps,
            ReaperStandardRotation.OpenerSteps)
        {
        },

        [ViperId] = new JobDefinition(
            ViperId,
            "Viper",
            () => new ViperSim(),
            times => new ViperStandardRotation(potionTimes: times),
            StatPresets.ViperBis,
            ViperStandardRotation.PrePullSteps,
            ViperStandardRotation.OpenerSteps)
        {
            TargetGcd = 2.09,
        },

        [MachinistId] = new JobDefinition(
            MachinistId,
            "Machinist",
            () => new MachinistSim(),
            times => new MachinistStandardRotation(potionTimes: times),
            StatPresets.MachinistBis,
            MachinistStandardRotation.PrePullSteps,
            MachinistStandardRotation.OpenerSteps),

        [BardId] = new JobDefinition(
            BardId,
            "Bard",
            () => new BardSim(),
            times => new BardStandardRotation(potionTimes: times),
            StatPresets.BardBis,
            BardStandardRotation.PrePullSteps,
            BardStandardRotation.OpenerSteps)
        {
        },

        [DancerId] = new JobDefinition(
            DancerId,
            "Dancer",
            () => new DancerSim(),
            times => new DancerStandardRotation(potionTimes: times),
            StatPresets.DancerBis,
            DancerStandardRotation.PrePullSteps,
            DancerStandardRotation.OpenerSteps),

        [BlackMageId] = new JobDefinition(
            BlackMageId,
            "Black Mage",
            () => new BlackMageSim(),
            times => new BlackMageStandardRotation(potionTimes: times),
            StatPresets.BlackMageBis,
            BlackMageStandardRotation.PrePullSteps,
            BlackMageStandardRotation.OpenerSteps)
        {
            TargetGcd = 2.45,
        },

        [RedMageId] = new JobDefinition(
            RedMageId,
            "Red Mage",
            () => new RedMageSim(),
            times => new RedMageStandardRotation(potionTimes: times),
            StatPresets.RedMageBis,
            RedMageStandardRotation.PrePullSteps,
            RedMageStandardRotation.OpenerSteps)
        {
        },

        [PictomancerId] = new JobDefinition(
            PictomancerId,
            "Pictomancer",
            () => new PictomancerSim(),
            times => new PictomancerStandardRotation(potionTimes: times),
            StatPresets.PictomancerBis,
            PictomancerStandardRotation.PrePullSteps,
            PictomancerStandardRotation.OpenerSteps)
        {
        },

        [SummonerId] = new JobDefinition(
            SummonerId,
            "Summoner",
            () => new SummonerSim(),
            times => new SummonerStandardRotation(potionTimes: times),
            StatPresets.SummonerBis,
            SummonerStandardRotation.PrePullSteps,
            SummonerStandardRotation.OpenerSteps)
        {
        },

        [WhiteMageId] = new JobDefinition(
            WhiteMageId,
            "White Mage",
            () => new WhiteMageSim(),
            times => new WhiteMageStandardRotation(potionTimes: times),
            StatPresets.WhiteMageBis,
            WhiteMageStandardRotation.PrePullSteps,
            WhiteMageStandardRotation.OpenerSteps),

        [ScholarId] = new JobDefinition(
            ScholarId,
            "Scholar",
            () => new ScholarSim(),
            times => new ScholarStandardRotation(potionTimes: times),
            StatPresets.ScholarBis,
            ScholarStandardRotation.PrePullSteps,
            ScholarStandardRotation.OpenerSteps),

        [AstrologianId] = new JobDefinition(
            AstrologianId,
            "Astrologian",
            () => new AstrologianSim(),
            times => new AstrologianStandardRotation(potionTimes: times),
            StatPresets.AstrologianBis,
            AstrologianStandardRotation.PrePullSteps,
            AstrologianStandardRotation.OpenerSteps),

        [SageId] = new JobDefinition(
            SageId,
            "Sage",
            () => new SageSim(),
            times => new SageStandardRotation(potionTimes: times),
            StatPresets.SageBis,
            SageStandardRotation.PrePullSteps,
            SageStandardRotation.OpenerSteps),

        [PaladinId] = new JobDefinition(
            PaladinId,
            "Paladin",
            () => new PaladinSim(),
            times => new PaladinStandardRotation(potionTimes: times),
            StatPresets.PaladinBis,
            PaladinStandardRotation.PrePullSteps,
            PaladinStandardRotation.OpenerSteps),

        [WarriorId] = new JobDefinition(
            WarriorId,
            "Warrior",
            () => new WarriorSim(),
            times => new WarriorStandardRotation(potionTimes: times),
            StatPresets.WarriorBis,
            WarriorStandardRotation.PrePullSteps,
            WarriorStandardRotation.OpenerSteps),

        [DarkKnightId] = new JobDefinition(
            DarkKnightId,
            "Dark Knight",
            () => new DarkKnightSim(),
            times => new DarkKnightStandardRotation(potionTimes: times),
            StatPresets.DarkKnightBis,
            DarkKnightStandardRotation.PrePullSteps,
            DarkKnightStandardRotation.OpenerSteps),

        [GunbreakerId] = new JobDefinition(
            GunbreakerId,
            "Gunbreaker",
            () => new GunbreakerSim(),
            times => new GunbreakerStandardRotation(potionTimes: times),
            StatPresets.GunbreakerBis,
            GunbreakerStandardRotation.PrePullSteps,
            GunbreakerStandardRotation.OpenerSteps),
    };

    /// Every job with a simulation behind it.
    public static IReadOnlyCollection<uint> Implemented => Registry.Keys;

    public static bool IsImplemented(uint classJobId) => Registry.ContainsKey(classJobId);

    public static JobDefinition? For(uint classJobId) => Registry.GetValueOrDefault(classJobId);

    /// The job to fall back on when the selected one has no simulation - Ninja, being the one that has been
    /// checked hardest.
    public static JobDefinition Default => Registry[NinjaId];

    /// The selected job's definition, or the default when it isn't implemented yet.
    public static JobDefinition ForOrDefault(uint classJobId) => For(classJobId) ?? Default;

    /// The job a log names, resolved to a definition.
    public static JobDefinition? ByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var wanted = Squash(name);
        return Registry.Values.FirstOrDefault(definition => Squash(definition.Name) == wanted);
    }

    private static string Squash(string name) => name.Replace(" ", string.Empty).ToLowerInvariant();

    /// Every implemented job, so a check can cover the roster without listing it again.
    public static IEnumerable<JobDefinition> All() => Registry.Values;

    /// Measured once per job and kept, because the answer is a property of the rotation.
    private static readonly Dictionary<uint, AutoCritProfile> AutoCritCache = [];

    private static readonly Lock AutoCritLock = new();

    /// How much of this job's damage arrives through guaranteed critical and direct hits, for the gear
    /// objective - see GearMath.DamageIndex.
    public static AutoCritProfile AutoCritProfileFor(uint classJobId)
    {
        lock (AutoCritLock)
        {
            if (AutoCritCache.TryGetValue(classJobId, out var cached))
                return cached;

            var profile = default(AutoCritProfile);

            if (Registry.TryGetValue(classJobId, out var definition))
            {
                try
                {
                    var job = definition.CreateSim();
                    var stats = definition.ReferenceGear().ToPlayerStats(job, partyBonus: false);
                    var result = new Simulator().Run(job, definition.CreateRotation([]), stats, 180.0);
                    profile = GearMath.AutoCritShares(result);
                }
                catch
                {
                    profile = default;
                }
            }

            AutoCritCache[classJobId] = profile;
            return profile;
        }
    }
}
