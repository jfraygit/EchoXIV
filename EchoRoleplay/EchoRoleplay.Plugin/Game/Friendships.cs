using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EchoRoleplay.Shared;

namespace EchoRoleplay.Game;

/// Friendships, which are agreed rather than declared.
public sealed class Friendships : IDisposable
{
    private readonly RelayClient client;
    private readonly ContactBook contacts;
    private readonly Func<string> localCharacter;

    private readonly CancellationTokenSource stopping = new();

    private long nextSync;
    private bool syncing;

    /// How often the list is refreshed.
    private const long SyncIntervalMs = 30000;

    public Friendships(RelayClient client, ContactBook contacts, Func<string> localCharacter)
    {
        this.client = client;
        this.contacts = contacts;
        this.localCharacter = localCharacter;
    }

    /// Requests waiting for an answer.
    public IReadOnlyList<FriendEntry> Incoming { get; private set; } = [];

    /// Requests this installation has sent and nobody has answered.
    public IReadOnlyList<FriendEntry> Outgoing { get; private set; } = [];

    /// Whether the relay has answered at all this session, so the UI can tell "no friends" from "not known
    /// yet".
    public bool Synced { get; private set; }

    /// The last thing the relay said about an action, for the player to read.
    public string Message { get; private set; } = string.Empty;

    public bool Failed { get; private set; }

    public void ClearMessage()
    {
        Message = string.Empty;
        Failed = false;
    }

    /// Refreshes when due.
    public void Tick()
    {
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
                var list = await client.FriendsAsync(stopping.Token).ConfigureAwait(false);

                if (list is null)
                    return;

                Synced = true;
                Incoming = list.Incoming;
                Outgoing = list.Outgoing;

                friendIds.Clear();

                foreach (var friend in list.Friends)
                {
                    if (friend.Character.Length > 0)
                        friendIds[friend.Character] = friend.Id;
                }

                Reconcile(list.Friends);
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

    /// Makes the local flags match what the relay says.
    private void Reconcile(List<FriendEntry> friends)
    {
        var current = friends
            .Select(f => f.Character)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var contact in contacts.All.ToList())
        {
            var should = current.Contains(contact.CharacterKey);

            if (contact.Friend != should)
                contacts.SetFriend(contact.CharacterKey, should);
        }

        foreach (var key in current)
        {
            if (contacts.ByKey(key) is null)
                contacts.Remember(key, string.Empty, friend: true);
        }
    }

    /// Asks somebody to be friends.
    public void Ask(string characterKey)
    {
        var from = localCharacter();

        if (from.Length == 0)
        {
            Fail("Log in before asking somebody.");
            return;
        }

        if (!Split(characterKey, out var name, out var world))
        {
            Fail("That character has not finished loading yet.");
            return;
        }

        Message = "Asking...";
        Failed = false;

        _ = Task.Run(async () =>
        {
            var ack = await client.FriendRequestAsync(name, world, from, stopping.Token).ConfigureAwait(false);

            Failed = !ack.Ok;
            Message = ack.Ok ? "Asked. They will see it when they next play." : ack.Error;

            if (ack.Ok)
                Sync();
        });
    }

    /// Answers a request.
    public void Answer(string id, bool accept)
    {
        var asCharacter = localCharacter();

        Message = accept ? "Accepting..." : "Declining...";
        Failed = false;

        _ = Task.Run(async () =>
        {
            var ack = await client.FriendRespondAsync(id, accept, asCharacter, stopping.Token).ConfigureAwait(false);

            Failed = !ack.Ok;
            Message = ack.Ok ? string.Empty : ack.Error;

            Sync();
        });
    }

    /// Ends a friendship, or withdraws a request.
    public void Remove(string id)
    {
        _ = Task.Run(async () =>
        {
            await client.FriendRemoveAsync(id, stopping.Token).ConfigureAwait(false);
            Sync();
        });
    }

    /// The relay's id for a friendship with this character, or empty.
    public string IdFor(string characterKey) =>
        friendIds.TryGetValue(characterKey, out var id) ? id : string.Empty;

    private readonly Dictionary<string, string> friendIds = new(StringComparer.Ordinal);

    private void Fail(string message)
    {
        Message = message;
        Failed = true;
    }

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
