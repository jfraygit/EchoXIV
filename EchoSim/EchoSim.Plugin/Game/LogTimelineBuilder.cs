using EchoSim.Shared;
using EchoSim.Sim.Analysis;
using EchoSim.Sim.Engine;
using EchoSim.Sim.Jobs.Ninja;

namespace EchoSim.Game;

/// Turns a fetched log into the shape the analysis works on.
public static class LogTimelineBuilder
{
    /// Ten Chi Jin's window.
    private const double TenChiJinWindow = 6.5;

    public static CombatTimeline Build(LogFightDetail fight, out List<string> unmapped)
    {
        var unresolved = new HashSet<string>();
        var casts = new List<TimelineCast>();

        foreach (var cast in fight.Casts.OrderBy(c => c.Time))
        {
            var id = (uint)cast.AbilityId;
            var name = GameData.ActionName(id);

            if (string.IsNullOrEmpty(name))
            {
                unresolved.Add(string.IsNullOrEmpty(cast.Ability)
                    ? $"id {cast.AbilityId}"
                    : cast.Ability);
                continue;
            }

            casts.Add(new TimelineCast(cast.Time, name, GameData.IsGcdAction(id), 0));
        }

        RelabelTenChiJin(casts);

        unmapped = [.. unresolved];

        return new CombatTimeline
        {
            JobName = fight.Job,
            Duration = fight.Duration,
            Source = $"{fight.FightName} - {fight.PlayerName}",
            Casts = casts,
            Downtime = [.. fight.Downtime.Select(d => new DowntimeWindow(d.Start, d.End))],
            Deaths = [.. fight.Deaths.Select(d => new DeathWindow(d.Time, d.ResumedAt, d.ResurrectedAt, d.ByLimitBreak))],
        };
    }

    /// Ten Chi Jin's three ninjutsu are logged as ordinary Fuma Shuriken, Raiton and Suiton - the game
    /// doesn't give them separate ids.
    private static void RelabelTenChiJin(List<TimelineCast> casts)
    {
        for (var i = 0; i < casts.Count; i++)
        {
            if (casts[i].Action != Nin.TenChiJinAction)
                continue;

            var relabelled = 0;

            for (var j = i + 1; j < casts.Count && relabelled < 3; j++)
            {
                if (casts[j].Time - casts[i].Time > TenChiJinWindow)
                    break;

                var replacement = casts[j].Action switch
                {
                    Nin.FumaShuriken => Nin.TcjFuma,
                    Nin.Raiton => Nin.TcjRaiton,
                    Nin.Suiton => Nin.TcjSuiton,
                    _ => null,
                };

                if (replacement is null)
                    continue;

                casts[j] = casts[j] with { Action = replacement };
                relabelled++;
            }
        }
    }

}
