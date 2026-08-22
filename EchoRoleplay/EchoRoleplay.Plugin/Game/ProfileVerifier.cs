using System;
using System.Threading;
using System.Threading.Tasks;
using EchoRoleplay.Shared;

namespace EchoRoleplay.Game;

/// Proving a character is yours, through the Lodestone.
public sealed class ProfileVerifier
{
    private readonly RelayClient client;
    private readonly RelayDirectory relay;
    private readonly Configuration configuration;

    public ProfileVerifier(RelayClient client, RelayDirectory relay, Configuration configuration)
    {
        this.client = client;
        this.relay = relay;
        this.configuration = configuration;
    }

    public enum Step
    {
        /// Nothing started.
        Idle,

        /// Waiting on the relay, either for a code or for a check.
        Working,

        /// A code is in hand and the player is off pasting it into the Lodestone.
        AwaitingCode,

        /// Handed over, and somebody is reading the page.
        Checking,
    }

    public Step State { get; private set; } = Step.Idle;

    /// The code to paste, while there is one.
    public string Code { get; private set; } = string.Empty;

    /// What the relay last said, in its own words.
    public string Message { get; private set; } = string.Empty;

    public bool Failed { get; private set; }

    /// Whether the relay says verification cannot work at all right now.
    public bool? Available { get; private set; }

    /// Whether this character has been proved.
    public bool IsVerified(string characterKey) =>
        relay.CardVerified(characterKey) ?? LocallyNoted(characterKey);

    /// This installation's own note, but only if it was made against the relay this build talks to - see
    /// Configuration.VerifiedOnRelay.
    private bool LocallyNoted(string characterKey)
    {
        if (!string.Equals(configuration.VerifiedOnRelay, RelayEndpoints.BaseUrl, StringComparison.Ordinal))
        {
            if (configuration.VerifiedCharacters.Count > 0)
            {
                configuration.VerifiedCharacters.Clear();
                configuration.VerifiedOnRelay = RelayEndpoints.BaseUrl;
                configuration.Save();
            }

            return false;
        }

        return configuration.VerifiedCharacters.Contains(characterKey);
    }

    public void Cancel()
    {
        State = Step.Idle;
        Code = string.Empty;
        Message = string.Empty;
        Failed = false;
        pendingFor = string.Empty;
        checkingSince = 0;
    }

    /// Asks for a code.
    public void Begin(string characterKey)
    {
        if (State == Step.Working || !Split(characterKey, out var name, out var world))
            return;

        State = Step.Working;
        Message = string.Empty;
        Failed = false;

        _ = Task.Run(async () =>
        {
            var answer = await client.VerifyBeginAsync(name, world, CancellationToken.None).ConfigureAwait(false);

            Available = answer.Available;

            if (answer.Ok && answer.Code.Length > 0)
            {
                Code = answer.Code;
                State = Step.AwaitingCode;
            }
            else
            {
                State = Step.Idle;

                Failed = answer.Available;
                Message = answer.Available
                    ? answer.Error.Length > 0 ? answer.Error : "The relay would not start a check."
                    : string.Empty;
            }
        });
    }

    /// Hands the relay a Lodestone address and asks it to look.
    public void Check(string characterKey, string url)
    {
        if (State == Step.Working || !Split(characterKey, out var name, out var world))
            return;

        if (string.IsNullOrWhiteSpace(url))
        {
            Failed = true;
            Message = "Paste your character's Lodestone address first.";
            return;
        }

        var previous = State;

        State = Step.Working;
        Message = "Checking the Lodestone...";
        Failed = false;

        _ = Task.Run(async () =>
        {
            var answer = await client.VerifyCheckAsync(name, world, url, CancellationToken.None)
                .ConfigureAwait(false);

            Message = answer.Message;
            Failed = answer.State == VerifyState.Rejected;

            switch (answer.State)
            {
                case VerifyState.Waiting:
                    State = Step.Checking;
                    pendingFor = characterKey;
                    nextPoll = Environment.TickCount64 + FirstPollDelayMs;
                    break;

                case VerifyState.Verified:
                    Succeeded(characterKey);
                    break;

                default:
                    State = previous;
                    break;
            }
        });
    }

    /// Which character has a check outstanding, or empty.
    private string pendingFor = string.Empty;

    private long nextPoll;

    /// Long enough for a queued check to have been picked up, short enough that somebody watching the window
    /// sees it resolve rather than wondering.
    private const long PollIntervalMs = 4000;

    /// The first ask is sooner - the check may have been answered immediately.
    private const long FirstPollDelayMs = 2000;

    /// How long to keep asking before giving up on an answer.
    private const long GiveUpAfterMs = 180000;

    private long checkingSince;

    /// Collects the verdict for an outstanding check.
    public void Tick()
    {
        if (State != Step.Checking || pendingFor.Length == 0)
            return;

        var now = Environment.TickCount64;

        if (checkingSince == 0)
            checkingSince = now;

        if (now - checkingSince > GiveUpAfterMs)
        {
            State = Step.AwaitingCode;
            checkingSince = 0;
            pendingFor = string.Empty;
            Failed = true;
            Message = "Nobody has been able to check this yet. Your code is still good - try again shortly.";
            return;
        }

        if (now < nextPoll)
            return;

        nextPoll = now + PollIntervalMs;

        if (!Split(pendingFor, out var name, out var world))
            return;

        var character = pendingFor;

        _ = Task.Run(async () =>
        {
            var answer = await client.VerifyStatusAsync(name, world, CancellationToken.None)
                .ConfigureAwait(false);

            switch (answer.State)
            {
                case VerifyState.Verified:
                    Succeeded(character);
                    break;

                case VerifyState.Rejected:
                    State = Step.AwaitingCode;
                    checkingSince = 0;
                    pendingFor = string.Empty;
                    Failed = true;
                    Message = answer.Message;
                    break;
            }
        });
    }

    private void Succeeded(string characterKey)
    {
        Remember(characterKey);

        Code = string.Empty;
        State = Step.Idle;
        pendingFor = string.Empty;
        checkingSince = 0;
        Failed = false;
        Message = string.Empty;

        relay.RefreshSoon();
    }

    private void Remember(string characterKey)
    {
        var moved = !string.Equals(
            configuration.VerifiedOnRelay, RelayEndpoints.BaseUrl, StringComparison.Ordinal);

        if (moved)
        {
            configuration.VerifiedCharacters.Clear();
            configuration.VerifiedOnRelay = RelayEndpoints.BaseUrl;
        }

        if (configuration.VerifiedCharacters.Add(characterKey) || moved)
            configuration.Save();
    }

    /// "Name@World" back into its two halves.
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
}
