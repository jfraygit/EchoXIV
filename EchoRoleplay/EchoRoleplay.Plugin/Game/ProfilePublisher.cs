using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EchoRoleplay.Shared;

namespace EchoRoleplay.Game;

/// Putting a profile on the relay, and taking it back off.
public sealed class ProfilePublisher
{
    private readonly RelayClient client;
    private readonly RelayDirectory relay;
    private readonly ProfileStore store;
    private readonly Configuration configuration;
    private readonly Portraits portraits;

    public ProfilePublisher(
        RelayClient client,
        RelayDirectory relay,
        ProfileStore store,
        Configuration configuration,
        Portraits portraits)
    {
        this.client = client;
        this.relay = relay;
        this.store = store;
        this.configuration = configuration;
        this.portraits = portraits;
    }

    /// The relay's own id for this profile, or empty when it has not been published from here.
    public string RelayIdFor(string profileId) =>
        profileId.Length > 0 && configuration.PublishedIds.TryGetValue(profileId, out var id) ? id : string.Empty;

    public enum Activity
    {
        Idle,
        Working,
        Done,
        Failed,
    }

    public Activity State { get; private set; } = Activity.Idle;

    /// The last thing that happened, in words a player can act on.
    public string Message { get; private set; } = string.Empty;

    /// Whether this character's profile is on the relay, according to the relay.
    public bool IsPublished(string characterKey) => relay.IsPublished(characterKey);

    /// Whether the relay has been heard from at all, so the UI can say "not published" and "cannot tell"
    /// differently.
    public bool RelayReached => relay.Reached;

    public void Reset()
    {
        State = Activity.Idle;
        Message = string.Empty;
    }


    /// Set by the store whenever anything about any profile changes.
    private bool changed = true;

    /// A stamp of what was last sent, so a change that is not really a change does not cost a publish.
    private string sentStamp = string.Empty;

    private long dirtySince;
    private long lastPublishedAt;

    /// How long everything has to stop changing before an automatic republish.
    private const long QuietPeriodMs = 6000;

    /// And a floor between publishes, whatever happens.
    private const long MinimumIntervalMs = 30000;

    /// Tells the publisher something changed.
    public void NoteChanged() => changed = true;

    /// Keeps a published profile matching what the player actually wrote.
    public void Sync(string characterKey)
    {
        if (characterKey.Length == 0 || State == Activity.Working)
            return;

        if (!IsPublished(characterKey))
        {
            dirtySince = 0;
            sentStamp = string.Empty;
            return;
        }

        if (changed)
        {
            changed = false;

            var profile = store.ForCharacter(characterKey);

            if (profile is null)
                return;

            var stamp = Stamp(profile);

            if (!string.Equals(stamp, sentStamp, StringComparison.Ordinal))
            {
                if (dirtySince == 0)
                    dirtySince = Environment.TickCount64;
            }
            else
            {
                dirtySince = 0;
            }
        }

        if (dirtySince == 0)
            return;

        var now = Environment.TickCount64;

        if (now - dirtySince < QuietPeriodMs || now - lastPublishedAt < MinimumIntervalMs)
            return;

        dirtySince = 0;
        Publish(characterKey, quiet: true);
    }

    /// What was sent, reduced to eight characters.
    private static string Stamp(RoleplayProfile profile) =>
        ProfileVersion.Of(Newtonsoft.Json.JsonConvert.SerializeObject(profile));


    /// Publishes whichever profile this character is wearing.
    public void Publish(string characterKey, bool quiet = false)
    {
        if (State == Activity.Working)
            return;

        var profile = store.ForCharacter(characterKey);

        if (profile is null)
        {
            Fail("Wear this profile on a character first.");
            return;
        }

        var at = characterKey.LastIndexOf('@');

        if (at <= 0 || at == characterKey.Length - 1)
        {
            Fail("This character has not finished loading yet.");
            return;
        }

        var name = characterKey[..at];
        var world = characterKey[(at + 1)..];

        State = Activity.Working;

        if (!quiet)
            Message = "Publishing...";

        var snapshot = Copy(profile);
        var profileId = profile.Id;
        var stamp = Stamp(snapshot);

        _ = Task.Run(async () =>
        {
            var ack = await client.PublishAsync(name, world, snapshot, CancellationToken.None)
                .ConfigureAwait(false);

            lastPublishedAt = Environment.TickCount64;

            if (ack.Ok)
            {
                Remember(profileId, ack.Id);

                portraits.Upload(profileId, ack.Id);

                sentStamp = stamp;

                State = Activity.Done;

                if (!quiet)
                    Message = "Published. Anybody with EchoRoleplay can read it now.";

                relay.RefreshSoon();
            }
            else
            {
                State = Activity.Failed;
                Message = ack.Error.Length > 0 ? ack.Error : "The relay would not take it.";

                sentStamp = string.Empty;
            }
        });
    }

    /// Takes it back off.
    public void Withdraw(string characterKey)
    {
        if (State == Activity.Working)
            return;

        var profile = store.ForCharacter(characterKey);

        if (profile is null)
        {
            Fail("There is nothing here to withdraw.");
            return;
        }

        if (!configuration.PublishedIds.TryGetValue(profile.Id, out var id) || id.Length == 0)
            id = relay.CardIdFor(characterKey);

        if (id.Length == 0)
        {
            Fail("Nothing of yours is published on this character.");
            return;
        }

        State = Activity.Working;
        Message = "Withdrawing...";

        var profileId = profile.Id;

        _ = Task.Run(async () =>
        {
            var ok = await client.WithdrawAsync(id, CancellationToken.None).ConfigureAwait(false);

            if (ok)
            {
                Forget(profileId);

                sentStamp = string.Empty;
                dirtySince = 0;

                State = Activity.Done;
                Message = "Withdrawn.";
                relay.RefreshSoon();
            }
            else
            {
                State = Activity.Failed;
                Message = "This is published by a different installation, so it cannot be withdrawn here.";
            }
        });
    }

    private void Fail(string message)
    {
        State = Activity.Failed;
        Message = message;
    }

    private void Remember(string profileId, string relayId)
    {
        foreach (var stale in configuration.PublishedIds
                     .Where(e => e.Value == relayId && e.Key != profileId)
                     .Select(e => e.Key)
                     .ToList())
        {
            configuration.PublishedIds.Remove(stale);
        }

        configuration.PublishedIds[profileId] = relayId;
        configuration.Save();
    }

    private void Forget(string profileId)
    {
        if (configuration.PublishedIds.Remove(profileId))
            configuration.Save();
    }

    /// A detached copy of a profile, for handing to a background task.
    private static RoleplayProfile Copy(RoleplayProfile source) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<RoleplayProfile>(
            Newtonsoft.Json.JsonConvert.SerializeObject(source))!;
}
