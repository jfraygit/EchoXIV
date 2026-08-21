using System;
using System.Collections.Generic;
using System.Numerics;

namespace EchoNav.Game;

/// What the window shows about the pot cycle.
public sealed record PotForecast
{
    /// Whether a pot is running right now.
    public bool IsUp { get; init; }

    /// Seconds until the next one spawns, or 0 when there is nothing to go on.
    public long SecondsUntilNext { get; init; }

    /// "North" or "South".
    public string NextSide { get; init; } = string.Empty;

    /// Where the next one spawns, so it can be travelled to before it does.
    public Vector3? NextPosition { get; init; }

    /// The long version, for the tooltip.
    public string Detail { get; init; } = string.Empty;

    public bool Known => SecondsUntilNext > 0;

    public static PotForecast Unknown { get; } = new()
    {
        Detail = "Waiting to see a pot. The cycle is worked out from the last one, so this fills in " +
                 "the first time one appears.\nA fresh instance's first pot is North, twenty minutes in.",
    };
}

/// Tracks the pot cycle - the FATE everyone in the zone plans around.
public sealed class PotTracker(Configuration configuration)
{
    public const string North = "North";
    public const string South = "South";

    /// The pot FATEs, and which spawn point each belongs to.
    private static readonly Dictionary<string, string> PotFates = new()
    {
        ["Daylight Pottery"] = North,
        ["In a Pot of Bother"] = South,
    };

    /// Spawn to spawn.
    private const long CycleSeconds = 30 * 60;


    public PotForecast Forecast { get; private set; } = PotForecast.Unknown;

    /// instance: The instance's own id where the game reports one, and a count of zone entries where it
    /// doesn't - see PotInstance.
    public void Observe(IReadOnlyList<NavTarget> fates, PotInstance instance)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        NavTarget? pot = null;
        var side = string.Empty;

        foreach (var fate in fates)
        {
            if (fate.Kind != NavTargetKind.Fate || !PotFates.TryGetValue(fate.Name, out var which))
                continue;

            pot = fate;
            side = which;
            break;
        }

        if (pot != null)
            Record(pot, side, now, instance);

        Forecast = Predict(pot != null, now, instance);
    }

    private void Record(NavTarget pot, string side, long now, PotInstance instance)
    {
        Learn(side, pot.Position);

        var spawned = pot.StartedAtEpoch;

        if (spawned <= 0)
        {
            if (configuration.PotLastSpawnSide == side
                && instance.Matches(configuration)
                && now - configuration.PotLastSpawnEpoch < CycleSeconds)
                return;

            spawned = now;
        }

        if (configuration.PotLastSpawnEpoch == spawned
            && configuration.PotLastSpawnSide == side
            && instance.Matches(configuration))
            return;

        configuration.PotLastSpawnEpoch = spawned;
        configuration.PotLastSpawnSide = side;
        instance.StampOnto(configuration);
        configuration.Save();
    }

    /// Records where a side's pot spawns, the first time one is seen there.
    private void Learn(string side, Vector3 position)
    {
        var known = side == North ? configuration.PotNorthPosition : configuration.PotSouthPosition;
        if (known.HasValue)
            return;

        if (side == North)
            configuration.PotNorthPosition = position;
        else
            configuration.PotSouthPosition = position;

        configuration.Save();
    }

    private PotForecast Predict(bool isUp, long now, PotInstance instance)
    {
        var last = configuration.PotLastSpawnEpoch;
        var lastSide = configuration.PotLastSpawnSide;

        if (last <= 0 || string.IsNullOrEmpty(lastSide) || !instance.Matches(configuration))
        {
            return PotForecast.Unknown with
            {
                IsUp = isUp,
                Detail = last > 0
                    ? "The cycle runs separately in each instance, and the last pot on record was " +
                      "seen in a different one. It fills back in the first time a pot appears " +
                      "here.\nA fresh instance's first pot is North, twenty minutes in."
                    : PotForecast.Unknown.Detail,
            };
        }

        var next = last + CycleSeconds;
        var side = Opposite(lastSide);

        while (next <= now)
        {
            next += CycleSeconds;
            side = Opposite(side);
        }

        var position = side == North ? configuration.PotNorthPosition : configuration.PotSouthPosition;
        var remaining = next - now;

        return new PotForecast
        {
            IsUp = isUp,
            SecondsUntilNext = remaining,
            NextSide = side,
            NextPosition = position,
            Detail = $"{(isUp ? "A pot is up now. " : string.Empty)}The next one is {side}, in " +
                     $"{remaining / 60}:{remaining % 60:D2}.\n" +
                     $"Pots alternate between the two spots every {CycleSeconds / 60} minutes, timed " +
                     "from the last spawn.\n" +
                     (position != null
                         ? "Click to head there now."
                         : $"No pot has been seen at the {side.ToLowerInvariant()} spot yet, so there " +
                           "is nowhere to walk to - that fills in the first time one appears there."),
        };
    }

    private static string Opposite(string side) => side == North ? South : North;
}
