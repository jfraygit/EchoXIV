using System;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace EchoNav.Game;

/// The search for something that says which copy of the zone this is.
public sealed unsafe class InstanceReader
{
    /// When the oldest director in play started, as a unix timestamp, or 0 if none of them say.
    public long DirectorStart { get; private set; }

    /// How many directors are in the list, and where the answer came from.
    public int DirectorCount { get; private set; }

    public string Source { get; private set; } = "not read";

    /// How many times DirectorStart has changed since the plugin loaded, ignoring the first reading and any
    /// zone change.
    public int Changes { get; private set; }

    /// The instance number the game reports - zero in Occult Crescent.
    public uint PublicInstanceId { get; private set; }

    private uint lastTerritory;

    /// Framework thread only.
    public void Tick(uint territory)
    {
        if (territory != lastTerritory)
        {
            lastTerritory = territory;
            DirectorStart = 0;
            Changes = 0;
        }

        PublicInstanceId = ReadPublicInstance();

        var (start, count, source) = ReadDirector();
        DirectorCount = count;
        Source = source;

        if (start == DirectorStart)
            return;

        if (DirectorStart != 0)
            Changes++;

        DirectorStart = start;
    }

    private static (long Start, int Count, string Source) ReadDirector()
    {
        try
        {
            var framework = EventFramework.Instance();
            if (framework == null)
                return (0, 0, "no event framework");

            ref var list = ref framework->DirectorModule.DirectorList;
            var count = (int)list.LongCount;

            var oldest = 0L;
            for (var i = 0; i < count; i++)
            {
                var director = list[i].Value;
                if (director == null)
                    continue;

                var start = director->DirectorStartTimestamp;
                if (start > 0 && (oldest == 0 || start < oldest))
                    oldest = start;
            }

            if (oldest > 0)
                return (oldest, count, "oldest director");

            var publicContent = framework->GetPublicContentDirector();
            if (publicContent != null && publicContent->DirectorStartTimestamp > 0)
                return (publicContent->DirectorStartTimestamp, count, "public");

            var active = framework->DirectorModule.ActiveContentDirector;
            if (active != null && active->DirectorStartTimestamp > 0)
                return (active->DirectorStartTimestamp, count, "active");

            return (0, count, "none");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Could not read the content director");
            return (0, 0, "threw");
        }
    }

    private static uint ReadPublicInstance()
    {
        try
        {
            var state = UIState.Instance();
            return state == null ? 0u : (uint)state->PublicInstance.InstanceId;
        }
        catch
        {
            return 0;
        }
    }
}

/// Enough to tell one copy of the zone from another, and to say how old this one is.
public readonly record struct PotInstance(int Visit)
{
    /// Whether a stored record was made without the player having left since.
    public bool Matches(Configuration configuration) => configuration.PotLastSpawnVisit == Visit;

    public void StampOnto(Configuration configuration) => configuration.PotLastSpawnVisit = Visit;
}
