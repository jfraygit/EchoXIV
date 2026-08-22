using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using EchoGlam.Shared;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace EchoGlam.Game;

/// Sends what this character is wearing to its friends, and draws theirs.
public sealed class LookSharing : IDisposable
{
    private readonly Plugin plugin;
    private readonly CancellationTokenSource stopping = new();

    public LookSharing(Plugin plugin) => this.plugin = plugin;


    /// How often the local look is checked for a change.
    private const long PublishCheckMs = 1000;

    /// Re-sent even when nothing changed, because the relay holds looks in memory with an expiry and nothing
    /// survives its restart.
    private const long HeartbeatMs = 120000;

    /// How quickly a friend's outfit change appears.
    private const long PollIntervalMs = 3000;

    /// How long after an animation a look may still be said to have been caused by it.
    private const long AnimationBelongsMs = 10000;

    /// How recently a look must have been published for its animation to be worth playing to somebody seeing
    /// that person for the first time.
    private const int FreshLookMs = 8000;

    /// How often what is on screen is reconciled against what was polled.
    private const long ApplyIntervalMs = 250;

    /// The least time between two redraws of the same character.
    private const long RedrawCooldownMs = 2000;

    /// The backstop on waiting for a friend's swap animation to end.
    private const long LongestAnimationMs = 4000;

    private long nextPublishCheck;
    private long nextPoll;
    private long nextApply;
    private long lastPublished;

    private bool publishing;
    private bool polling;

    private string publishedStamp = string.Empty;


    /// What each friend is wearing, by character key.
    private Dictionary<string, SharedLook> looks = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Claim
    {
        /// What they were drawn wearing before anything was written.
        public required SharedLook Baseline { get; init; }

        /// The stamp of the look last written, so an unchanged friend is skipped.
        public string Applied { get; set; } = string.Empty;

        /// Whether an appearance was written, so a restore knows to redraw.
        public bool Appearance { get; set; }

        public long LastRedraw { get; set; }

        /// A look whose animation is playing, waiting before the clothes change.
        public SharedLook? Pending { get; set; }

        /// The floor: the hold the wearer chose.
        public long PendingUntil { get; set; }

        /// The backstop, past which the clothes change whatever the animation is doing.
        public long PendingCap { get; set; }

        public ushort PendingAnimation { get; set; }
    }

    private readonly Dictionary<string, Claim> claims = new(StringComparer.OrdinalIgnoreCase);

    /// How many friends are currently being drawn.
    public int Drawn => claims.Count;


    /// Called from the framework update, after the Wardrobe has had its say about the local character - so
    /// what is published is what was actually drawn this frame rather than what was drawn last frame.
    public void Tick()
    {
        var now = Environment.TickCount64;

        if (!plugin.Friendships.SharingEnabled)
        {
            ReleaseAll();
            publishedStamp = string.Empty;
            return;
        }

        if (now >= nextPublishCheck)
        {
            nextPublishCheck = now + PublishCheckMs;
            PublishIfChanged(now);
        }

        if (now >= nextPoll && !polling)
        {
            nextPoll = now + PollIntervalMs;
            Poll();
        }

        var pending = false;

        foreach (var claim in claims.Values)
        {
            if (claim.Pending is not null)
            {
                pending = true;
                break;
            }
        }

        if (pending || now >= nextApply)
        {
            nextApply = now + ApplyIntervalMs;
            ApplyToVisible(now);
        }
    }


    private unsafe void PublishIfChanged(long now)
    {
        if (publishing)
            return;

        var player = Plugin.ObjectTable.LocalPlayer;

        if (player == null)
            return;

        var key = CharacterHash.Key(player.Name.TextValue, player.HomeWorld.Value.Name.ExtractText());

        if (key.Length == 0)
            return;

        var look = LookReader.Read((Character*)player.Address, key, includeAppearance: true);

        var changed = !string.Equals(look.Stamp, publishedStamp, StringComparison.Ordinal);
        var due = now - lastPublished >= HeartbeatMs;

        if (!changed && !due)
            return;

        if (changed
            && plugin.Animations.LastPlayed != 0
            && now - plugin.Animations.LastPlayedAt <= AnimationBelongsMs)
        {
            look.AnimationId = plugin.Animations.LastPlayed;
            look.AnimationHold = plugin.Animations.LastHold;
        }

        lastPublished = now;
        Send(look);
    }

    /// Sends a look that has already been read.
    private void Send(SharedLook look)
    {
        publishing = true;

        _ = Task.Run(async () =>
        {
            var ack = await GalleryClient.PublishLookAsync(plugin.Gallery.OwnerKey, look, stopping.Token)
                .ConfigureAwait(false);

            if (ack.Ok)
                publishedStamp = look.Stamp;

            publishing = false;
        });
    }


    private void Poll()
    {
        polling = true;

        _ = Task.Run(async () =>
        {
            try
            {
                var page = await GalleryClient.FriendLooksAsync(plugin.Gallery.OwnerKey, stopping.Token)
                    .ConfigureAwait(false);

                if (page is null)
                    return;

                var fresh = new Dictionary<string, SharedLook>(StringComparer.OrdinalIgnoreCase);

                foreach (var look in page.Looks)
                {
                    if (look.Character.Length > 0)
                        fresh[look.Character] = look;
                }

                looks = fresh;
            }
            catch (Exception)
            {
            }
            finally
            {
                polling = false;
            }
        });
    }


    private unsafe void ApplyToVisible(long now)
    {
        var held = looks;
        var localAddress = Plugin.ObjectTable.LocalPlayer?.Address ?? nint.Zero;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IPlayerCharacter person || obj.Address == localAddress)
                continue;

            var key = CharacterHash.Key(person.Name.TextValue, person.HomeWorld.Value.Name.ExtractText());

            if (key.Length == 0 || !held.TryGetValue(key, out var look))
                continue;

            if (plugin.Friendships.IsBlocked(key))
                continue;

            seen.Add(key);
            ApplyTo((Character*)person.Address, key, look, now);
        }

        var lost = claims.Keys.Where(key => !seen.Contains(key)).ToList();

        foreach (var key in lost)
            Release(key);
    }

    private unsafe void ApplyTo(Character* character, string key, SharedLook look, long now)
    {
        var first = !claims.TryGetValue(key, out var claim);

        if (first || claim is null)
        {
            claim = new Claim
            {
                Baseline = LookReader.Read(character, key, includeAppearance: true),
            };

            claims[key] = claim;
        }

        if (claim.Pending is { } pending)
        {
            if (now < claim.PendingUntil)
                return;

            if (now < claim.PendingCap && Animations.IsPlayingOn(character, claim.PendingAnimation))
                return;

            claim.Pending = null;
            claim.PendingAnimation = 0;
            look = pending;
        }
        else
        {
            if (string.Equals(claim.Applied, look.Stamp, StringComparison.Ordinal))
                return;

            var fresh = look.AgeMs <= FreshLookMs;

            if ((!first || fresh) && look.AnimationId != 0)
            {
                PlayOn(character, look.AnimationId);

                claim.Pending = look;
                claim.PendingAnimation = look.AnimationId;
                claim.PendingUntil = now + HoldMs(look.AnimationHold);
                claim.PendingCap = now + LongestAnimationMs;
                return;
            }
        }

        var appearanceChanged = LookReader.Apply(character, look, withAppearance: true);

        claim.Applied = look.Stamp;

        if (!appearanceChanged)
            return;

        claim.Appearance = true;

        if (now - claim.LastRedraw < RedrawCooldownMs)
            return;

        claim.LastRedraw = now;
        LookReader.Redraw(character);
    }

    /// Plays a timeline on somebody else's character.
    private static unsafe void PlayOn(Character* character, ushort timelineId)
    {
        try
        {
            character->Timeline.PlayActionTimeline(timelineId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoGlam] Could not play a friend's animation {timelineId}");
        }
    }

    /// How long to hold the clothes back, clamped.
    private static long HoldMs(float seconds) =>
        (long)(Math.Clamp(seconds, 0f, 5f) * 1000f);


    /// Puts one character back the way it was found, if it is still on screen.
    private unsafe void Release(string key)
    {
        if (!claims.TryGetValue(key, out var claim))
            return;

        claims.Remove(key);

        var person = Plugin.ObjectTable
            .OfType<IPlayerCharacter>()
            .FirstOrDefault(p => string.Equals(
                CharacterHash.Key(p.Name.TextValue, p.HomeWorld.Value.Name.ExtractText()),
                key, StringComparison.OrdinalIgnoreCase));

        if (person == null)
            return;

        var character = (Character*)person.Address;

        if (LookReader.Apply(character, claim.Baseline, claim.Appearance) || claim.Appearance)
            LookReader.Redraw(character);
    }

    /// Puts everybody back.
    public void ReleaseAll()
    {
        if (claims.Count == 0)
            return;

        foreach (var key in claims.Keys.ToList())
            Release(key);

        claims.Clear();
    }

    public void Dispose()
    {
        stopping.Cancel();
        stopping.Dispose();
    }
}
