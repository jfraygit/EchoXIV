using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EchoGlam.Game;

/// What is known about one item's price on one world.
public enum MarketState
{
    /// Never asked.
    Unknown,

    Loading,

    /// Somebody is selling it, and MarketQuote.Cheapest is what for.
    Listed,

    /// The board knows the item and nobody has one up.
    Empty,

    /// The lookup failed, or the item has never been seen on that world at all.
    Unavailable,
}

public sealed record MarketQuote(MarketState State, uint Cheapest, DateTimeOffset? Updated)
{
    public static readonly MarketQuote Unknown = new(MarketState.Unknown, 0, null);
    public static readonly MarketQuote Loading = new(MarketState.Loading, 0, null);
}

/// Cheapest market board listing per item, from Universalis.
public sealed class MarketPrices
{
    /// Long enough that hovering along a list is one request per item, short enough that a price somebody
    /// acts on is from this play session rather than last week's.
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(30);

    /// Universalis asks callers to identify themselves so they can tell traffic apart and come talk to
    /// whoever is being expensive.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    static MarketPrices()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"EchoGlam/{version}");
    }

    private sealed record Entry(MarketQuote Quote, DateTime FetchedUtc);

    private readonly Dictionary<(string World, uint ItemId), Entry> cache = [];
    private readonly HashSet<(string World, uint ItemId)> inFlight = [];
    private readonly object gate = new();

    /// What is cached for this item right now, and starts a fetch if that is nothing.
    public MarketQuote Ask(uint itemId, string world)
    {
        if (itemId == 0 || string.IsNullOrEmpty(world))
            return MarketQuote.Unknown;

        var key = (world, itemId);

        lock (gate)
        {
            if (cache.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow - entry.FetchedUtc < Freshness)
                    return entry.Quote;

                cache.Remove(key);
            }

            if (!inFlight.Add(key))
                return MarketQuote.Loading;
        }

        _ = Task.Run(() => FetchAsync(key));

        return MarketQuote.Loading;
    }

    private async Task FetchAsync((string World, uint ItemId) key)
    {
        var quote = MarketQuote.Unknown;

        try
        {
            var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(key.World)}/{key.ItemId}"
                + "?listings=1&entries=0&fields=minPrice%2ClistingsCount%2ClastUploadTime";

            using var response = await Http.GetAsync(url).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                quote = new MarketQuote(MarketState.Unavailable, 0, null);
                return;
            }

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var json = JObject.Parse(body);

            var cheapest = json.Value<uint?>("minPrice") ?? 0;
            var count = json.Value<int?>("listingsCount") ?? 0;
            var uploaded = json.Value<long?>("lastUploadTime") ?? 0;

            var updated = uploaded > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(uploaded)
                : (DateTimeOffset?)null;

            quote = cheapest > 0 && count > 0
                ? new MarketQuote(MarketState.Listed, cheapest, updated)
                : new MarketQuote(MarketState.Empty, 0, updated);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[EchoGlam] Price lookup for {key.ItemId} on {key.World} failed: {ex.Message}");
            quote = new MarketQuote(MarketState.Unavailable, 0, null);
        }
        finally
        {
            lock (gate)
            {
                cache[key] = new Entry(quote, DateTime.UtcNow);
                inFlight.Remove(key);
            }
        }
    }

    /// The price as it goes on screen.
    public static string Gil(uint amount) => amount.ToString("N0", CultureInfo.InvariantCulture) + " gil";

    /// How long ago the board was last scanned, in the roughest terms that are still true.
    public static string Age(DateTimeOffset updated)
    {
        var elapsed = DateTimeOffset.UtcNow - updated;

        if (elapsed < TimeSpan.FromMinutes(2)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes} minutes ago";
        if (elapsed < TimeSpan.FromHours(2)) return "an hour ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours} hours ago";
        if (elapsed < TimeSpan.FromDays(2)) return "yesterday";

        return $"{(int)elapsed.TotalDays} days ago";
    }
}
