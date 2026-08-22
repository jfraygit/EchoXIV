using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace EchoSim.Game;

/// Finds a character's headshot on the Lodestone, from the player's own machine.
public static class Lodestone
{
    private const string SearchUrl = "https://na.finalfantasyxiv.com/lodestone/character/";

    /// Long enough that nobody re-looks-up a character in one sitting, which is all that is needed: this
    /// cache does not survive a restart, and a portrait is not worth persisting to disk.
    private static readonly TimeSpan Freshness = TimeSpan.FromHours(12);

    /// Failures expire far sooner than successes.
    private static readonly TimeSpan FailureFreshness = TimeSpan.FromMinutes(30);

    /// One lookup at a time.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                      ?? "unknown";

        http.DefaultRequestHeaders.Add("User-Agent", $"EchoSim/{version} (FFXIV Dalamud plugin)");

        return http;
    }

    private readonly record struct Cached(string? Url, DateTime At, bool Found);

    private static readonly Dictionary<string, Cached> Results = [];
    private static readonly object CacheGate = new();

    /// The headshot URL for a character, or null when there isn't one to be had.
    public static async Task<string?> AvatarAsync(string character, string world, CancellationToken cancel)
    {
        var key = $"{character.ToLowerInvariant()}|{world.ToLowerInvariant()}";

        if (Fresh(key) is { } hit)
            return hit.Url;

        await Gate.WaitAsync(cancel).ConfigureAwait(false);

        try
        {
            if (Fresh(key) is { } queued)
                return queued.Url;

            var url = await SearchAsync(character, world, cancel).ConfigureAwait(false);

            Remember(key, url);
            return url;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"EchoSim: Lodestone lookup failed for {character} ({world}): {ex.Message}");

            Remember(key, null);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Cached? Fresh(string key)
    {
        lock (CacheGate)
        {
            if (!Results.TryGetValue(key, out var entry))
                return null;

            var limit = entry.Found ? Freshness : FailureFreshness;
            return DateTime.UtcNow - entry.At < limit ? entry : null;
        }
    }

    private static void Remember(string key, string? url)
    {
        lock (CacheGate)
            Results[key] = new Cached(url, DateTime.UtcNow, url is not null);
    }

    private static async Task<string?> SearchAsync(string character, string world, CancellationToken cancel)
    {
        var url = $"{SearchUrl}?q={Uri.EscapeDataString(character)}&worldname={Uri.EscapeDataString(world)}";

        var html = await Http.GetStringAsync(url, cancel).ConfigureAwait(false);

        foreach (var entry in Entries(html))
        {
            if (!string.Equals(entry.Name, character, StringComparison.OrdinalIgnoreCase))
                continue;

            var home = entry.World.Split('[')[0].Trim();
            if (!string.Equals(home, world, StringComparison.OrdinalIgnoreCase))
                continue;

            return entry.Avatar;
        }

        return null;
    }

    /// Each real search result: the anchors with class="entry__link", and nothing else.
    private static IEnumerable<(string Avatar, string Name, string World)> Entries(string html)
    {
        foreach (Match anchor in EntryPattern.Matches(html))
        {
            var block = anchor.Value;

            var avatar = AvatarPattern.Match(block);
            var name = NamePattern.Match(block);
            var world = WorldPattern.Match(block);

            if (!avatar.Success || !name.Success || !world.Success)
                continue;

            yield return (
                avatar.Groups[1].Value,
                WebUtility.HtmlDecode(name.Groups[1].Value).Trim(),
                WebUtility.HtmlDecode(world.Groups[1].Value).Trim());
        }
    }

    /// One result, from its anchor to the end of it.
    private static readonly Regex EntryPattern = new(
        """class="entry__link".*?</a>""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AvatarPattern = new(
        """entry__chara__face"><img src="([^"]+)""",
        RegexOptions.Compiled);

    private static readonly Regex NamePattern = new(
        """class="entry__name">([^<]+)</p>""",
        RegexOptions.Compiled);

    private static readonly Regex WorldPattern = new(
        """class="entry__world">(?:<i[^>]*></i>)?([^<]+)</p>""",
        RegexOptions.Compiled);
}
