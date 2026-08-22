using System.Net.Http;
using Dalamud.Interface.Textures.TextureWraps;

namespace EchoSim.Game;

/// Character headshots, fetched from Square Enix's own image host and kept as textures.
public static class Portraits
{
    /// Where Lodestone keeps character images.
    private const string ImageHostSuffix = ".finalfantasyxiv.com";

    /// How many portraits are held at once.
    private const int Capacity = 48;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly Dictionary<string, IDalamudTextureWrap?> Loaded = [];
    private static readonly HashSet<string> InFlight = [];
    private static readonly Queue<string> Order = new();
    private static readonly object Gate = new();

    /// The texture for a character's headshot, or null while it loads and forever if there isn't one.
    public static IDalamudTextureWrap? Get(string character, string world)
    {
        if (string.IsNullOrWhiteSpace(character) || string.IsNullOrWhiteSpace(world))
            return null;

        var key = $"{character.ToLowerInvariant()}|{world.ToLowerInvariant()}";

        lock (Gate)
        {
            if (Loaded.TryGetValue(key, out var texture))
                return texture;

            if (!InFlight.Add(key))
                return null;
        }

        _ = Task.Run(() => LoadAsync(key, character, world));
        return null;
    }

    private static async Task LoadAsync(string key, string character, string world)
    {
        IDalamudTextureWrap? texture = null;

        try
        {
            var url = await Lodestone.AvatarAsync(character, world, CancellationToken.None)
                .ConfigureAwait(false);

            if (url is null)
            {
            }
            else if (!Allowed(url))
            {
                Plugin.Log.Warning($"EchoSim: refused a portrait from an unexpected host: {url}");
            }
            else
            {
                var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                texture = await Plugin.TextureProvider.CreateFromImageAsync(bytes).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"EchoSim: could not load a portrait: {ex.Message}");
        }

        lock (Gate)
        {
            InFlight.Remove(key);

            Loaded[key] = texture;
            Order.Enqueue(key);

            while (Order.Count > Capacity)
            {
                var oldest = Order.Dequeue();
                if (!Loaded.Remove(oldest, out var evicted))
                    continue;

                evicted?.Dispose();
            }
        }
    }

    private static bool Allowed(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed)
           && parsed.Scheme == Uri.UriSchemeHttps
           && parsed.Host.EndsWith(ImageHostSuffix, StringComparison.OrdinalIgnoreCase);

    /// Releases every held texture.
    public static void Dispose()
    {
        lock (Gate)
        {
            foreach (var texture in Loaded.Values)
                texture?.Dispose();

            Loaded.Clear();
            Order.Clear();
            InFlight.Clear();
        }
    }
}
