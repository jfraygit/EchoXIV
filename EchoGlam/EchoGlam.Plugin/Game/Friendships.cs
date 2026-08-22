using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EchoGlam.Shared;
using Newtonsoft.Json;

namespace EchoGlam.Game;

/// Friendships, which are agreed rather than declared.
public sealed class Friendships : IDisposable
{
    private readonly Plugin plugin;
    private readonly string path;
    private readonly CancellationTokenSource stopping = new();

    private long nextSync;
    private bool syncing;

    /// The character this installation last told the relay it was playing, so a re-register only happens when
    /// it actually changes.
    private string registered = string.Empty;

    /// How often the list is refreshed.
    private const long SyncIntervalMs = 30000;

    public Friendships(Plugin plugin)
    {
        this.plugin = plugin;
        path = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "blocked.json");
        LoadBlocks();
    }

    /// Agreed friendships.
    public IReadOnlyList<FriendEntry> Friends { get; private set; } = [];

    /// Requests waiting for an answer, drawn in the Friends tab.
    public IReadOnlyList<FriendEntry> Incoming { get; private set; } = [];

    /// Requests this installation has sent and nobody has answered.
    public IReadOnlyList<FriendEntry> Outgoing { get; private set; } = [];

    /// Whether the relay has answered at all this session, so the UI can tell "no friends" from "not asked
    /// yet" and show the right empty state.
    public bool Synced { get; private set; }

    /// The last thing the relay said about an action, for the player to read.
    public string Message { get; private set; } = string.Empty;

    public bool Failed { get; private set; }

    /// True while an ask, answer or removal is in flight, so a button can say so rather than looking like it
    /// did nothing.
    public bool Busy { get; private set; }

    public void ClearMessage()
    {
        Message = string.Empty;
        Failed = false;
    }


    public bool SharingEnabled => plugin.Configuration.ShareGlamours;

    /// Turns sharing on or off.
    public void SetSharing(bool enabled)
    {
        plugin.Configuration.ShareGlamours = enabled;
        plugin.Configuration.Save();

        registered = string.Empty;

        var (name, world) = GalleryState.Character;

        Busy = true;
        Message = enabled ? "Turning sharing on..." : "Turning sharing off...";
        Failed = false;

        _ = Task.Run(async () =>
        {
            var ack = await GalleryClient.SharingAsync(
                plugin.Gallery.OwnerKey, name, world, enabled, stopping.Token).ConfigureAwait(false);

            Busy = false;
            Failed = !ack.Ok;
            Message = ack.Ok ? string.Empty : ack.Error;

            if (!ack.Ok)
                return;

            if (enabled)
                registered = CharacterHash.Key(name, world);

            Sync();
        });
    }


    /// Registers the character when it changes and refreshes the lists when due.
    public void Tick()
    {
        if (!SharingEnabled)
            return;

        var (name, world) = GalleryState.Character;
        var key = CharacterHash.Key(name, world);

        if (key.Length == 0)
            return;

        if (!string.Equals(key, registered, StringComparison.Ordinal))
        {
            registered = key;

            _ = Task.Run(async () =>
            {
                var ack = await GalleryClient.SharingAsync(
                    plugin.Gallery.OwnerKey, name, world, true, stopping.Token).ConfigureAwait(false);

                if (!ack.Ok)
                    registered = string.Empty;
            });
        }

        if (syncing || Environment.TickCount64 < nextSync)
            return;

        Sync();
    }

    /// Refreshes now - after asking, answering or removing, where waiting out the interval would make the
    /// button look like it had done nothing.
    public void Sync()
    {
        if (syncing)
            return;

        syncing = true;
        nextSync = Environment.TickCount64 + SyncIntervalMs;

        _ = Task.Run(async () =>
        {
            try
            {
                var list = await GalleryClient.FriendsAsync(plugin.Gallery.OwnerKey, stopping.Token)
                    .ConfigureAwait(false);

                if (list is null)
                    return;

                Synced = true;
                Friends = list.Friends;
                Incoming = list.Incoming;
                Outgoing = list.Outgoing;
            }
            catch (Exception)
            {
            }
            finally
            {
                syncing = false;
            }
        });
    }


    /// Asks somebody to be friends.
    public void Ask(string characterKey)
    {
        if (!SharingEnabled)
        {
            Fail("Turn on glamour sharing before adding friends.");
            return;
        }

        var (name, world) = GalleryState.Character;
        var from = CharacterHash.Key(name, world);

        if (from.Length == 0)
        {
            Fail("Log in before asking somebody.");
            return;
        }

        if (!Split(characterKey, out var theirName, out var theirWorld))
        {
            Fail("That character hasn't finished loading yet.");
            return;
        }

        Busy = true;
        Message = "Asking...";
        Failed = false;

        _ = Task.Run(async () =>
        {
            var ack = await GalleryClient.FriendRequestAsync(
                plugin.Gallery.OwnerKey, theirName, theirWorld, from, stopping.Token).ConfigureAwait(false);

            Busy = false;
            Failed = !ack.Ok;

            Message = ack.Ok ? "Asked. It'll show in their Friends tab within a minute." : ack.Error;

            if (ack.Ok)
                Sync();
        });
    }

    /// Answers a request.
    public void Answer(string id, bool accept)
    {
        var (name, world) = GalleryState.Character;
        var asCharacter = CharacterHash.Key(name, world);

        Busy = true;
        Message = accept ? "Accepting..." : "Declining...";
        Failed = false;

        _ = Task.Run(async () =>
        {
            var ack = await GalleryClient.FriendRespondAsync(
                plugin.Gallery.OwnerKey, id, accept, asCharacter, stopping.Token).ConfigureAwait(false);

            Busy = false;
            Failed = !ack.Ok;
            Message = ack.Ok ? string.Empty : ack.Error;

            Sync();
        });
    }

    /// Ends a friendship, or withdraws a request.
    public void Remove(string id)
    {
        Busy = true;
        Failed = false;

        _ = Task.Run(async () =>
        {
            var ack = await GalleryClient.FriendRemoveAsync(plugin.Gallery.OwnerKey, id, stopping.Token)
                .ConfigureAwait(false);

            Busy = false;

            if (!ack.Ok && ack.Error.Length > 0 && !ack.Error.Contains("already gone", StringComparison.Ordinal))
            {
                Failed = true;
                Message = ack.Error;
            }

            Sync();
        });
    }

    /// Whether this character is already a friend, or has been asked.
    public bool KnownTo(string characterKey) =>
        Friends.Any(f => Same(f.Character, characterKey))
        || Outgoing.Any(f => Same(f.Character, characterKey))
        || Incoming.Any(f => Same(f.Character, characterKey));

    public bool IsFriend(string characterKey) => Friends.Any(f => Same(f.Character, characterKey));

    /// The relay's id for the friendship with this character, or empty.
    public string IdFor(string characterKey) =>
        Friends.FirstOrDefault(f => Same(f.Character, characterKey))?.Id ?? string.Empty;


    private HashSet<string> blocked = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Blocked => blocked;

    /// Whether this character is blocked.
    public bool IsBlocked(string characterKey) =>
        characterKey.Length > 0 && blocked.Contains(characterKey);

    public void SetBlocked(string characterKey, bool value)
    {
        if (characterKey.Length == 0)
            return;

        var changed = value ? blocked.Add(characterKey) : blocked.Remove(characterKey);

        if (changed)
            SaveBlocks();
    }

    private void LoadBlocks()
    {
        try
        {
            if (!File.Exists(path))
                return;

            var list = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path)) ?? [];
            blocked = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoGlam] Could not read {path}. Blocks will not be saved this session.");
            readFailed = true;
        }
    }

    private bool readFailed;

    private void SaveBlocks()
    {
        if (readFailed)
            return;

        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(blocked.ToList(), Formatting.Indented));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoGlam] Could not write {path}");
        }
    }


    private void Fail(string message)
    {
        Message = message;
        Failed = true;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Split(string characterKey, out string name, out string world)
    {
        var at = characterKey.LastIndexOf('@');

        if (at <= 0 || at == characterKey.Length - 1)
        {
            name = string.Empty;
            world = string.Empty;
            return false;
        }

        name = characterKey[..at];
        world = characterKey[(at + 1)..];
        return true;
    }

    public void Dispose()
    {
        stopping.Cancel();
        stopping.Dispose();
    }
}
