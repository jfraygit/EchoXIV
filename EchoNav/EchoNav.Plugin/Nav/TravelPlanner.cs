using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace EchoNav.Nav;

/// A way of getting somewhere: either walk the whole distance, or walk to a shard, teleport, and walk the
/// rest.
public sealed record TravelPlan
{
    public required IReadOnlyList<Vector3> Route { get; init; }
    public required float Cost { get; init; }

    /// Set only when the plan teleports.
    public KnownAetheryte? FromShard { get; init; }
    public KnownAetheryte? ToShard { get; init; }

    /// Where the trip actually ends.
    public Vector3 FinalDestination { get; init; }

    /// Whether the trip starts by using Occult Return to get to base camp.
    public bool UsesReturn { get; init; }

    public bool UsesTeleport => ToShard != null;
}

/// Decides whether the zone's aetheryte network beats walking.
public static class TravelPlanner
{
    /// What a teleport costs, expressed as the distance that would take the same time.
    private const float TeleportCostInYalms = 250f;

    /// baseCamp: The zone's main aetheryte, where Occult Return lands.
    public static TravelPlan Plan(
        MeshRoutePlanner planner, IReadOnlyList<KnownAetheryte> shards, Vector3 from, Vector3 destination,
        KnownAetheryte? baseCamp = null, bool returnAvailable = false)
    {
        var direct = planner.Plan(from, destination);
        var directCost = Length(from, direct);

        var asCrowFlies = Vector3.Distance(from, destination);
        var detourFactor = asCrowFlies > 1f ? Math.Clamp(directCost / asCrowFlies, 1f, 3f) : 1.5f;

        var best = new TravelPlan { Route = direct, Cost = directCost, FinalDestination = destination };
        if (shards.Count < 2)
            return best;

        var departure = Nearest(shards, from, detourFactor);
        var arrival = BestArrival(planner, shards, destination, detourFactor);

        if (arrival.Shard == null)
            return best;

        if (departure.Shard != null && departure.Shard.Position != arrival.Shard.Position)
        {
            var viaCost = departure.Cost + TeleportCostInYalms + arrival.Cost;
            if (viaCost < best.Cost)
            {
                best = new TravelPlan
                {
                    Route = planner.Plan(from, departure.Shard.Position),
                    Cost = viaCost,
                    FromShard = departure.Shard,
                    ToShard = arrival.Shard,
                    FinalDestination = destination,
                };
            }
        }

        if (returnAvailable && baseCamp != null)
        {
            var needsHop = baseCamp.Position != arrival.Shard.Position;
            var afterArrival = needsHop
                ? TeleportCostInYalms + arrival.Cost
                : Vector3.Distance(baseCamp.Position, destination) * detourFactor;

            var returnCost = ReturnAction.CostInYalms + afterArrival;
            if (returnCost < best.Cost)
            {
                best = new TravelPlan
                {
                    Route = [destination],
                    Cost = returnCost,
                    UsesReturn = true,
                    FromShard = baseCamp,
                    ToShard = needsHop ? arrival.Shard : null,
                    FinalDestination = destination,
                };
            }
        }

        return best;
    }

    /// How many arrival shards to measure properly rather than estimate.
    private const int VerifiedArrivals = 3;

    /// The best shard to arrive at, measured rather than estimated.
    private static (KnownAetheryte? Shard, float Cost) BestArrival(
        MeshRoutePlanner planner, IReadOnlyList<KnownAetheryte> shards, Vector3 destination, float detourFactor)
    {
        var shortlist = shards
            .Select(shard => (Shard: shard, Estimate: Vector3.Distance(shard.Position, destination) * detourFactor))
            .OrderBy(entry => entry.Estimate)
            .Take(VerifiedArrivals);

        KnownAetheryte? best = null;
        var bestCost = float.MaxValue;

        foreach (var (shard, _) in shortlist)
        {
            var cost = Length(shard.Position, planner.Plan(shard.Position, destination));
            if (cost >= bestCost)
                continue;

            bestCost = cost;
            best = shard;
        }

        return (best, bestCost);
    }

    /// The shard nearest a point, and roughly what walking there costs.
    private static (KnownAetheryte? Shard, float Cost) Nearest(
        IReadOnlyList<KnownAetheryte> shards, Vector3 point, float detourFactor)
    {
        KnownAetheryte? best = null;
        var bestDistance = float.MaxValue;

        foreach (var shard in shards)
        {
            var distance = Vector3.Distance(shard.Position, point);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = shard;
        }

        return (best, bestDistance * detourFactor);
    }

    /// Total length of a route, counting the leg from the current position to its first point - which is
    /// often the longest unguided stretch of the whole trip.
    private static float Length(Vector3 from, IReadOnlyList<Vector3> route)
    {
        var total = 0f;
        var previous = from;

        foreach (var point in route)
        {
            total += Vector3.Distance(previous, point);
            previous = point;
        }

        return total;
    }
}
