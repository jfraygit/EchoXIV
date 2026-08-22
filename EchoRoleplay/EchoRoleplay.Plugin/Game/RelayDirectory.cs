using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using EchoRoleplay.Shared;

namespace EchoRoleplay.Game;

/// Everybody else's profiles: which characters have one, what is on their card, and the full sheet for the
/// ones somebody opened.
public sealed class RelayDirectory : IDisposable
{
    private sealed class WorldIndex
    {
        /// Character hash to the current version of their profile.
        public Dictionary<string, string> Versions = new(StringComparer.Ordinal);

        public string? ETag;
        public DateTime LastPolled = DateTime.MinValue;
        public DateTime LastSeen = DateTime.UtcNow;
        public bool Polling;
    }

    private readonly RelayClient client;
    private readonly ProfileCache cache;
    private readonly IObjectTable objects;

    private readonly ConcurrentDictionary<string, WorldIndex> worlds = new(StringComparer.OrdinalIgnoreCase);

    /// Cards by character hash, with the shallow profile already built.
    private readonly ConcurrentDictionary<string, (ProfileCard Card, RoleplayProfile Shallow)> cards =
        new(StringComparer.Ordinal);

    /// Full profiles, by character hash.
    private readonly ConcurrentDictionary<string, ProfileEnvelope> full = new(StringComparer.Ordinal);

    /// Hashes waiting for a card, and ids waiting for a full profile.
    private readonly ConcurrentDictionary<string, byte> pendingCards = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> fetching = new(StringComparer.Ordinal);

    private readonly CancellationTokenSource stopping = new();

    private DateTime lastCardFlush = DateTime.MinValue;
    private DateTime lastScan = DateTime.MinValue;

    /// How often a world's index is re-asked for.
    private static readonly TimeSpan IndexInterval = TimeSpan.FromSeconds(30);

    /// How long pending hashes are allowed to accumulate before a batch goes out.
    private static readonly TimeSpan CardBatchDelay = TimeSpan.FromSeconds(1.5);

    /// How often the object table is walked.
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);

    /// How many worlds are tracked at once.
    private const int MaximumWorlds = 8;

    public RelayDirectory(
        RelayClient client, ProfileCache cache, IObjectTable objects, Func<string> localCharacterKey)
    {
        this.client = client;
        this.cache = cache;
        this.objects = objects;
        this.localCharacterKey = localCharacterKey;
    }

    /// Who this client is, for the one route that says so.
    private readonly Func<string> localCharacterKey;

    /// This character as the relay knows it - a hash, never a name.
    private string Viewer => CharacterHash.Of(localCharacterKey());

    /// Whether anything has been heard from the relay this session.
    public bool Reached { get; private set; }

    public int KnownProfiles => worlds.Values.Sum(w => w.Versions.Count);

    public int CardsHeld => cards.Count;


    /// The best profile currently held for a character - the full sheet if it has been fetched, otherwise the
    /// card, otherwise nothing.
    public RoleplayProfile? Lookup(string characterKey)
    {
        var hash = CharacterHash.Of(characterKey);

        if (hash.Length == 0)
            return null;

        if (full.TryGetValue(hash, out var envelope))
            return envelope.Profile;

        return cards.TryGetValue(hash, out var held) ? held.Shallow : null;
    }

    /// Whether what Lookup would return is the whole sheet rather than a card.
    public bool HasFull(string characterKey) => full.ContainsKey(CharacterHash.Of(characterKey));

    /// Whether the relay is currently serving a profile for this character.
    public bool IsPublished(string characterKey)
    {
        var hash = CharacterHash.Of(characterKey);

        if (hash.Length == 0)
            return false;

        foreach (var world in worlds.Values)
        {
            if (world.Versions.ContainsKey(hash))
                return true;
        }

        return false;
    }

    /// Whether the relay says this character is verified, or null when there is no card to read it from.
    public bool? CardVerified(string characterKey)
    {
        var hash = CharacterHash.Of(characterKey);

        return hash.Length > 0 && cards.TryGetValue(hash, out var held) ? held.Card.Verified : null;
    }

    /// The relay's own id for a character's published profile, or empty.
    public string CardIdFor(string characterKey)
    {
        var hash = CharacterHash.Of(characterKey);

        return hash.Length > 0 && cards.TryGetValue(hash, out var held) ? held.Card.Id : string.Empty;
    }

    /// The content stamp of a character's portrait, or empty when they have none or no card has been fetched
    /// for them.
    public string PortraitStamp(string characterKey)
    {
        var hash = CharacterHash.Of(characterKey);

        return hash.Length > 0 && cards.TryGetValue(hash, out var held)
            ? held.Card.PortraitStamp
            : string.Empty;
    }

    /// Re-polls every index on the next tick.
    public void RefreshSoon()
    {
        foreach (var world in worlds.Values)
            world.LastPolled = DateTime.MinValue;
    }

    /// Asks for a character's full profile, if they have one and it is not held already.
    public void Request(string characterKey)
    {
        var hash = CharacterHash.Of(characterKey);

        if (hash.Length == 0 || full.ContainsKey(hash))
            return;

        if (!cards.TryGetValue(hash, out var held))
            return;

        var card = held.Card;

        if (!fetching.TryAdd(card.Id, 0))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var cached = cache.Read(card.Id, card.Version);

                if (cached is not null)
                {
                    full[hash] = cached;
                    return;
                }

                var envelope = await client.ProfileAsync(card.Id, Viewer, stopping.Token).ConfigureAwait(false);

                if (envelope is null)
                    return;

                full[hash] = envelope;
                cache.Write(envelope);
            }
            catch (Exception)
            {
            }
            finally
            {
                fetching.TryRemove(card.Id, out _);
            }
        });
    }


    /// Walks the object table, polls indexes, and flushes card batches.
    public void Tick()
    {
        var now = DateTime.UtcNow;

        if (now - lastScan >= ScanInterval)
        {
            lastScan = now;
            Scan(now);
        }

        PollIndexes(now);
        FlushCards(now);
    }

    /// Notes every visible player: which world to track, and who needs a card.
    private void Scan(DateTime now)
    {
        foreach (var obj in objects)
        {
            if (obj is not IPlayerCharacter player)
                continue;

            var world = player.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;

            if (world.Length == 0)
                continue;

            var index = worlds.GetOrAdd(world, _ => new WorldIndex());
            index.LastSeen = now;

            var hash = CharacterHash.Of(ProfileDirectory.KeyFor(player));

            if (hash.Length == 0 || !index.Versions.TryGetValue(hash, out var version))
                continue;

            if (cards.TryGetValue(hash, out var held) && held.Card.Version == version)
                continue;

            pendingCards.TryAdd(hash, 0);
        }

        Evict(now);
    }

    /// Drops worlds nobody nearby is from any more, so a session that crosses several data centres does not
    /// keep polling all of them.
    private void Evict(DateTime now)
    {
        if (worlds.Count <= MaximumWorlds)
            return;

        foreach (var stale in worlds.OrderBy(w => w.Value.LastSeen).Take(worlds.Count - MaximumWorlds).ToList())
            worlds.TryRemove(stale.Key, out _);
    }

    /// Forgets the card and the sheet for anybody no longer in any index.
    private void DropForgotten()
    {
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var world in worlds.Values)
        {
            foreach (var hash in world.Versions.Keys)
                known.Add(hash);
        }

        foreach (var hash in cards.Keys.ToList())
        {
            if (!known.Contains(hash))
                cards.TryRemove(hash, out _);
        }

        foreach (var hash in full.Keys.ToList())
        {
            if (!known.Contains(hash))
                full.TryRemove(hash, out _);
        }
    }

    private void PollIndexes(DateTime now)
    {
        foreach (var (world, index) in worlds)
        {
            if (index.Polling || now - index.LastPolled < IndexInterval)
                continue;

            index.Polling = true;
            index.LastPolled = now;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await client.IndexAsync(world, index.ETag, Viewer, stopping.Token).ConfigureAwait(false);

                    switch (result.Outcome)
                    {
                        case RelayClient.IndexOutcome.Fetched when result.Index is { } fetched:
                            var versions = new Dictionary<string, string>(fetched.Entries.Count, StringComparer.Ordinal);

                            foreach (var token in fetched.Entries)
                            {
                                if (IndexToken.TryRead(token, out var hash, out var version))
                                    versions[hash] = version;
                            }

                            index.Versions = versions;
                            index.ETag = result.ETag;
                            Reached = true;

                            DropForgotten();

                            break;

                        case RelayClient.IndexOutcome.Unchanged:
                            Reached = true;
                            break;
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    index.Polling = false;
                }
            });
        }
    }

    private void FlushCards(DateTime now)
    {
        if (pendingCards.IsEmpty)
            return;

        if (pendingCards.Count < RelayLimits.CardBatch && now - lastCardFlush < CardBatchDelay)
            return;

        lastCardFlush = now;

        var batch = new List<string>(Math.Min(pendingCards.Count, RelayLimits.CardBatch));

        foreach (var hash in pendingCards.Keys)
        {
            if (batch.Count >= RelayLimits.CardBatch)
                break;

            if (pendingCards.TryRemove(hash, out _))
                batch.Add(hash);
        }

        if (batch.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var fetched = await client.CardsAsync(batch, stopping.Token).ConfigureAwait(false);

                if (fetched is null)
                {
                    foreach (var hash in batch)
                        pendingCards.TryAdd(hash, 0);

                    return;
                }

                Reached = true;

                foreach (var card in fetched)
                {
                    cards[card.Hash] = (card, Shallow(card));

                    if (full.TryGetValue(card.Hash, out var held) && held.Version != card.Version)
                        full.TryRemove(card.Hash, out _);
                }
            }
            catch (Exception)
            {
                foreach (var hash in batch)
                    pendingCards.TryAdd(hash, 0);
            }
        });
    }

    /// A card as a profile, with everything a card does not carry left empty.
    private static RoleplayProfile Shallow(ProfileCard card) => new()
    {
        Id = card.Id,
        Name = card.Name,
        Title = card.Title,
        NameColour = card.NameColour,
        RpStatus = card.RpStatus,
        Statuses = [.. card.Statuses],
    };

    public void Dispose()
    {
        stopping.Cancel();
        stopping.Dispose();
    }
}
