using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace EchoRoleplay.Game;

/// Portraits: the player's own on disk, everybody else's off the relay, and both as textures.
public sealed class Portraits : IDisposable
{
    private readonly RelayClient client;
    private readonly string directory;

    /// Textures by cache key - "own:{profileId}" or "{relayId}:{stamp}".
    private readonly ConcurrentDictionary<string, IDalamudTextureWrap?> textures = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, byte> fetching = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource stopping = new();

    public Portraits(RelayClient client, string configDirectory)
    {
        this.client = client;
        directory = Path.Combine(configDirectory, "portraits");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoRoleplay] The portrait folder could not be opened");
        }
    }


    /// Where one of this installation's own portraits lives.
    private string OwnPath(string profileId) => Path.Combine(directory, $"own-{profileId}.jpg");

    public bool HasOwn(string profileId) =>
        profileId.Length > 0 && File.Exists(OwnPath(profileId));

    /// Writes a freshly cropped portrait and forgets any texture made from the old one.
    public bool SaveOwn(string profileId, byte[] jpeg)
    {
        if (profileId.Length == 0 || jpeg.Length == 0)
            return false;

        try
        {
            File.WriteAllBytes(OwnPath(profileId), jpeg);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoRoleplay] The portrait could not be saved");
            return false;
        }

        Forget("own:" + profileId);
        return true;
    }

    public void DeleteOwn(string profileId)
    {
        try
        {
            File.Delete(OwnPath(profileId));
        }
        catch (Exception)
        {
        }

        Forget("own:" + profileId);
    }

    /// The bytes to upload, or null when there is no portrait for this profile.
    public byte[]? ReadOwn(string profileId)
    {
        try
        {
            var path = OwnPath(profileId);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }


    /// How big a portrait texture is, in pixels.
    private const int DisplaySide = 160;

    /// A drawable texture from stored bytes, resized on the way in.
    private async Task<IDalamudTextureWrap?> TextureFrom(byte[] bytes)
    {
        var resized = ImageProcessor.ToBgra(bytes, DisplaySide);

        if (resized is not { } image)
        {
            return await Plugin.TextureProvider
                .CreateFromImageAsync(bytes, cancellationToken: stopping.Token).ConfigureAwait(false);
        }

        return await Plugin.TextureProvider.CreateFromRawAsync(
            RawImageSpecification.Bgra32(image.Side, image.Side),
            image.Pixels,
            cancellationToken: stopping.Token).ConfigureAwait(false);
    }

    /// This installation's own portrait as a texture, loading it on first use.
    public IDalamudTextureWrap? Own(string profileId)
    {
        if (profileId.Length == 0)
            return null;

        var key = "own:" + profileId;

        if (textures.TryGetValue(key, out var held))
            return held;

        if (!fetching.TryAdd(key, 0))
            return null;

        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = ReadOwn(profileId);

                textures[key] = bytes is null ? null : await TextureFrom(bytes).ConfigureAwait(false);
            }
            catch (Exception)
            {
                textures[key] = null;
            }
            finally
            {
                fetching.TryRemove(key, out _);
            }
        });

        return null;
    }

    /// Somebody else's portrait as a texture, fetching and caching it on first use.
    public IDalamudTextureWrap? Remote(string relayId, string stamp)
    {
        if (relayId.Length == 0 || stamp.Length == 0)
            return null;

        var key = relayId + ":" + stamp;

        if (textures.TryGetValue(key, out var held))
            return held;

        if (!fetching.TryAdd(key, 0))
            return null;

        _ = Task.Run(async () =>
        {
            try
            {
                var path = Path.Combine(directory, $"{relayId}-{stamp}.jpg");
                byte[]? bytes = null;

                if (File.Exists(path))
                {
                    bytes = await File.ReadAllBytesAsync(path, stopping.Token).ConfigureAwait(false);
                }
                else
                {
                    bytes = await client.PortraitAsync(relayId, stopping.Token).ConfigureAwait(false);

                    if (bytes is not null)
                    {
                        try
                        {
                            await File.WriteAllBytesAsync(path, bytes, stopping.Token).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                textures[key] = bytes is null ? null : await TextureFrom(bytes).ConfigureAwait(false);
            }
            catch (Exception)
            {
                textures[key] = null;
            }
            finally
            {
                fetching.TryRemove(key, out _);
            }
        });

        return null;
    }


    /// Sends this profile's portrait to the relay, if there is one and somewhere to send it.
    public void Upload(string profileId, string relayId)
    {
        if (profileId.Length == 0 || relayId.Length == 0)
            return;

        var bytes = ReadOwn(profileId);

        if (bytes is null)
            return;

        _ = Task.Run(async () =>
        {
            var ack = await client.UploadPortraitAsync(relayId, bytes, stopping.Token).ConfigureAwait(false);

            if (!ack.Ok)
                Plugin.Log.Warning($"[EchoRoleplay] The portrait was not accepted: {ack.Error}");
        });
    }

    private void Forget(string key)
    {
        if (textures.TryRemove(key, out var texture))
            texture?.Dispose();
    }

    public void Dispose()
    {
        stopping.Cancel();

        foreach (var texture in textures.Values)
            texture?.Dispose();

        textures.Clear();
        stopping.Dispose();
    }
}
