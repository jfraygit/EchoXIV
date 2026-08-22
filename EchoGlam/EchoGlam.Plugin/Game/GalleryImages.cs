using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;

namespace EchoGlam.Game;

/// Screenshots from the relay, as textures the grid can draw.
public sealed class GalleryImages : IDisposable
{
    private sealed class Entry
    {
        public IDalamudTextureWrap? Texture;

        /// Set when the fetch or the decode failed.
        public bool Failed;

        /// Frame this was last drawn on, for eviction.
        public long LastUsed;
    }

    private readonly Dictionary<string, Entry> cache = [];
    private readonly Lock gate = new();
    private readonly CancellationTokenSource shutdown = new();

    private long frame;

    /// How many decoded screenshots to keep.
    private const int Capacity = 120;

    /// The texture for a URL, or null while it is on its way.
    public IDalamudTextureWrap? Get(string url) => Get(url, 0);

    /// The same, downsampled on the way in.
    public IDalamudTextureWrap? Get(string url, int maxEdge)
    {
        var key = maxEdge > 0 ? $"{url}#{maxEdge}" : url;

        lock (gate)
        {
            if (cache.TryGetValue(key, out var existing))
            {
                existing.LastUsed = frame;
                return existing.Texture;
            }

            var entry = new Entry { LastUsed = frame };
            cache[key] = entry;

            _ = Task.Run(() => LoadAsync(url, key, maxEdge, entry), shutdown.Token);
            return null;
        }
    }

    /// Whether this URL has been tried and failed, so a card can draw "unavailable" rather than a spinner
    /// that never resolves.
    public bool Failed(string url)
    {
        lock (gate)
            return cache.TryGetValue(url, out var entry) && entry.Failed;
    }

    private async Task LoadAsync(string url, string key, int maxEdge, Entry entry)
    {
        try
        {
            var bytes = await GalleryClient.ImageBytesAsync(url, shutdown.Token).ConfigureAwait(false);

            if (maxEdge > 0)
                bytes = Downsample(bytes, maxEdge);

            var texture = await Plugin.TextureProvider
                .CreateFromImageAsync(bytes, "EchoGlam gallery", shutdown.Token)
                .ConfigureAwait(false);

            lock (gate)
            {
                if (!cache.TryGetValue(key, out var current) || current != entry || shutdown.IsCancellationRequested)
                {
                    texture.Dispose();
                    return;
                }

                entry.Texture = texture;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            lock (gate)
                entry.Failed = true;

            Plugin.Log.Warning($"[EchoGlam] Gallery image failed: {url} - {ex.Message}");
        }
    }

    /// Resamples an encoded image down to fit an edge, and re-encodes it as PNG.
    private static byte[] Downsample(byte[] bytes, int maxEdge)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using var source = new Bitmap(input);

            var longest = Math.Max(source.Width, source.Height);
            if (longest <= maxEdge)
                return bytes;

            var ratio = (float)maxEdge / longest;
            var width = Math.Max(1, (int)MathF.Round(source.Width * ratio));
            var height = Math.Max(1, (int)MathF.Round(source.Height * ratio));

            using var target = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(target))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;

                using var attributes = new ImageAttributes();
                attributes.SetWrapMode(WrapMode.TileFlipXY);

                graphics.DrawImage(
                    source, new Rectangle(0, 0, width, height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            using var output = new MemoryStream();
            target.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[EchoGlam] Could not downsample an image: {ex.Message}");
            return bytes;
        }
    }

    /// Advances the frame counter and evicts the least recently drawn.
    public void Tick()
    {
        List<IDalamudTextureWrap> evicted = [];

        lock (gate)
        {
            frame++;

            if (cache.Count <= Capacity)
                return;

            foreach (var key in cache
                         .OrderBy(kv => kv.Value.LastUsed)
                         .Take(cache.Count - Capacity)
                         .Select(kv => kv.Key)
                         .ToList())
            {
                if (cache[key].Texture is { } texture)
                    evicted.Add(texture);

                cache.Remove(key);
            }
        }

        foreach (var texture in evicted)
            texture.Dispose();
    }

    public void Dispose()
    {
        shutdown.Cancel();

        List<IDalamudTextureWrap> textures;
        lock (gate)
        {
            textures = [.. cache.Values.Select(e => e.Texture).OfType<IDalamudTextureWrap>()];
            cache.Clear();
        }

        foreach (var texture in textures)
            texture.Dispose();

        shutdown.Dispose();
    }
}
