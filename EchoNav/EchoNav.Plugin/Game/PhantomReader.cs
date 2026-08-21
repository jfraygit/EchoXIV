using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace EchoNav.Game;

/// The character's phantom job standing, snapshotted for the UI.
public sealed record PhantomSnapshot
{
    /// False when the zone isn't running Occult Crescent, in which case none of the rest means anything.
    public bool Available { get; init; }

    /// Level per job, indexed by job id - see PhantomJobs.Names.
    public IReadOnlyList<byte> Levels { get; init; } = [];

    public byte CurrentJob { get; init; }

    /// The zone's own progression, which is what monsters are measured against here rather than the
    /// character's job level - that is synced and identical for everyone.
    public uint KnowledgePoints { get; init; }

    public uint KnowledgeNeeded { get; init; }

    public byte KnowledgeLevel { get; init; }

    /// The bytes FFXIVClientStructs has not named, so the knowledge level can be found by looking rather than
    /// by guessing which mapped field might secretly be it.
    public IReadOnlyList<byte> UnknownBytes { get; init; } = [];

    public byte LevelOf(int jobId) => jobId >= 0 && jobId < Levels.Count ? Levels[jobId] : (byte)0;

    public static PhantomSnapshot Unavailable { get; } = new();
}

/// Reads phantom job levels and changes phantom job.
public static unsafe class PhantomReader
{
    public static PhantomSnapshot Read()
    {
        try
        {
            var content = PublicContentOccultCrescent.GetInstance();
            if (content == null)
                return PhantomSnapshot.Unavailable;

            ref var state = ref content->State;
            var levels = state.SupportJobLevels;

            var copy = new byte[levels.Length];
            for (var i = 0; i < levels.Length; i++)
                copy[i] = levels[i];

            return new PhantomSnapshot
            {
                Available = true,
                Levels = copy,
                CurrentJob = state.CurrentSupportJob,
                KnowledgePoints = state.CurrentKnowledge,
                KnowledgeNeeded = state.NeededKnowledge,
                KnowledgeLevel = state.KnowledgeLevelSync,
                UnknownBytes = ReadGaps(ref state),
            };
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Could not read phantom job state");
            return PhantomSnapshot.Unavailable;
        }
    }

    /// The unmapped stretches of the state block: 0x70 to 0x75, between the currency counts and the job
    /// levels, and 0x93 to 0x97 after the level sync.
    private static byte[] ReadGaps(ref FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.OccultCrescentState state)
    {
        var raw = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref state);
        var bytes = new byte[11];

        for (var i = 0; i < 6; i++)
            bytes[i] = raw[0x70 + i];

        for (var i = 0; i < 5; i++)
            bytes[6 + i] = raw[0x93 + i];

        return bytes;
    }

    /// Equips a phantom job.
    public static bool ChangeJob(byte jobId)
    {
        try
        {
            var agent = AgentMKDSupportJobList.Instance();
            if (agent == null)
                return false;

            agent->ChangeSupportJob(jobId);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[EchoNav] Could not change phantom job to {jobId}");
            return false;
        }
    }
}
