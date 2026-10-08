using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;

namespace EchoMix.Plugin.UI.Cosmetics;

/// Badge artwork, decoded once from the assembly's embedded resources and held for the session.
public sealed class BadgeTextures : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, IDalamudTextureWrap?> cache = new();
    private readonly HashSet<string> loading = new();
    private bool disposed;

    /// The badge's texture, or null while it is still decoding or if it could not be read.
    public IDalamudTextureWrap? Get(string resourceName)
    {
        bool hasEntry;
        IDalamudTextureWrap? texture;
        var shouldLoad = false;

        lock (gate)
        {
            if (disposed)
                return null;

            hasEntry = cache.TryGetValue(resourceName, out texture);
            if (!hasEntry)
                shouldLoad = loading.Add(resourceName);
        }

        if (shouldLoad)
            _ = LoadAsync(resourceName);

        return hasEntry ? texture : null;
    }

    private async Task LoadAsync(string resourceName)
    {
        IDalamudTextureWrap? wrap = null;

        try
        {
            await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                wrap = await Plugin.TextureProvider.CreateFromImageAsync(buffer.ToArray());
            }
        }
        catch (Exception)
        {
        }

        var drop = false;

        lock (gate)
        {
            if (disposed)
            {
                drop = true;
            }
            else
            {
                cache[resourceName] = wrap;
                loading.Remove(resourceName);
            }
        }

        if (drop)
            wrap?.Dispose();
    }

    public void Dispose()
    {
        List<IDalamudTextureWrap?> wraps;

        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            wraps = new List<IDalamudTextureWrap?>(cache.Values);
            cache.Clear();
            loading.Clear();
        }

        foreach (var wrap in wraps)
            wrap?.Dispose();
    }
}
