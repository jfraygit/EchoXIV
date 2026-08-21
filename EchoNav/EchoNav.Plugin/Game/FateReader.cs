using System.Collections.Generic;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Plugin.Services;

namespace EchoNav.Game;

/// Turns Dalamud's FATE table into NavTargets.
public sealed class FateReader(IFateTable fateTable)
{
    public IReadOnlyList<NavTarget> Read()
    {
        var results = new List<NavTarget>();

        foreach (var fate in fateTable)
        {
            if (fate.State is FateState.Ended or FateState.Failed)
                continue;

            results.Add(new NavTarget
            {
                Kind = NavTargetKind.Fate,
                Id = fate.FateId,
                Name = fate.Name.TextValue,
                TypeLabel = "FATE",
                Position = fate.Position,
                Radius = fate.Radius,
                Progress = fate.Progress,
                SecondsRemaining = fate.TimeRemaining,
                StartedAtEpoch = fate.StartTimeEpoch,
                StateLabel = fate.State.ToString(),
                IsEngageable = fate.State == FateState.Running,
                MapIconId = fate.MapIconId,
                Level = fate.Level,
            });
        }

        return results;
    }
}
