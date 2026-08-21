using System.Collections.Generic;
using System.Linq;

namespace EchoNav.Game;

/// Remembers the order encounters turned up in, so the list can be sorted by age rather than by distance.
public sealed class TargetOrder
{
    private readonly Dictionary<string, long> firstSeen = [];
    private long sequence;

    /// Takes a reading of what is live.
    public void Observe(IEnumerable<NavTarget> live)
    {
        var present = new HashSet<string>();
        var arrived = new List<NavTarget>();

        foreach (var target in live)
        {
            present.Add(target.Key);

            if (!firstSeen.ContainsKey(target.Key))
                arrived.Add(target);
        }

        foreach (var target in arrived.OrderByDescending(t => t.SecondsRemaining))
            firstSeen[target.Key] = ++sequence;

        if (firstSeen.Count == present.Count)
            return;

        foreach (var key in firstSeen.Keys.Where(key => !present.Contains(key)).ToList())
            firstSeen.Remove(key);
    }

    /// Where a target sits in the arrival order.
    public long RankOf(NavTarget target) =>
        firstSeen.TryGetValue(target.Key, out var rank) ? rank : long.MaxValue;
}
