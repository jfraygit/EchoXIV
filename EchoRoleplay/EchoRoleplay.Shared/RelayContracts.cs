using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace EchoRoleplay.Shared;

/// The public name of a character, hashed.
public static class CharacterHash
{
    public const int Length = 16;

    /// Hashes a "Name@World" key.
    public static string Of(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey))
            return string.Empty;

        var normalised = characterKey.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("echorp:" + normalised));

        return Convert.ToHexString(hash)[..Length].ToLowerInvariant();
    }

    /// The same thing from the two halves, for callers that never assembled the key.
    public static string Of(string name, string world) => Of(Key(name, world));

    /// "Name@World", the one form everything in this plugin keys on.
    public static string Key(string name, string world) =>
        string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world)
            ? string.Empty
            : $"{name.Trim()}@{world.Trim()}";
}

/// One line of the index: who has a profile, and which version of it.
public static class IndexToken
{
    /// 16 characters of hash, then 8 of version.
    public const int Length = CharacterHash.Length + ProfileVersion.Length;

    public static string Make(string hash, string version) => hash + version;

    /// Just the character hash out of a token, or empty.
    public static string HashOf(string token) =>
        token is { Length: Length } ? token[..CharacterHash.Length] : string.Empty;

    public static bool TryRead(string token, out string hash, out string version)
    {
        if (token is null || token.Length != Length)
        {
            hash = string.Empty;
            version = string.Empty;
            return false;
        }

        hash = token[..CharacterHash.Length];
        version = token[CharacterHash.Length..];
        return true;
    }
}

/// What "which version of this profile" means on the wire.
public static class ProfileVersion
{
    public const int Length = 8;

    public static string Of(string canonicalJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson ?? string.Empty));
        return Convert.ToHexString(hash)[..Length].ToLowerInvariant();
    }
}

/// Tier 1.
public sealed class ProfileIndexResponse
{
    /// The world this covers, as the game spells it.
    public string World { get; set; } = string.Empty;

    /// When the relay built this, so a client can show how stale its picture is without having to have
    /// recorded when it asked.
    public DateTime GeneratedUtc { get; set; }

    public List<string> Entries { get; set; } = [];
}

/// Tier 2, asking.
public sealed class CardRequest
{
    public List<string> Hashes { get; set; } = [];
}

/// Tier 2, answering.
public sealed class ProfileCard
{
    /// Which hash this answers, so a batch response needs no ordering guarantee.
    public string Hash { get; set; } = string.Empty;

    /// The id to ask for at tier 3.
    public string Id { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// The profile's own colour, for the places a profile is read deliberately.
    public float[]? NameColour { get; set; }

    public RpStatus RpStatus { get; set; } = RpStatus.Unspecified;

    /// The status row, which is the one thing here that is drawn into the world.
    public List<RoleplayStatus> Statuses { get; set; } = [];

    /// Whether this character proved ownership through the Lodestone.
    public bool Verified { get; set; }

    /// The content stamp of their portrait, or empty for none.
    public string PortraitStamp { get; set; } = string.Empty;
}

public sealed class CardResponse
{
    public List<ProfileCard> Cards { get; set; } = [];
}

/// Tier 3.
public sealed class ProfileEnvelope
{
    public string Id { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    public bool Verified { get; set; }

    /// The content stamp of their portrait, or empty for none.
    public string PortraitStamp { get; set; } = string.Empty;

    public DateTime PublishedUtc { get; set; }

    public RoleplayProfile Profile { get; set; } = new();
}

/// Publishing.
public sealed class PublishRequest
{
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;

    public RoleplayProfile Profile { get; set; } = new();
}

public sealed class PublishAck
{
    public bool Ok { get; set; }

    /// A sentence for the player when it did not work.
    public string Error { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

/// Asking somebody to be friends.
public sealed class FriendRequest
{
    /// Who is being asked.
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;

    /// Who is asking, as "Name@World".
    public string FromCharacter { get; set; } = string.Empty;
}

/// Who an installation has shut out, so the relay can make a block cut both ways.
public sealed class BlockList
{
    public List<string> Hashes { get; set; } = [];
}

public sealed class FriendAck
{
    public bool Ok { get; set; }

    /// The relay's own words when it did not work - not published, already friends, already asked.
    public string Error { get; set; } = string.Empty;
}

/// Answering one.
public sealed class FriendResponse
{
    public string Id { get; set; } = string.Empty;

    public bool Accept { get; set; }

    /// Which of this installation's characters is answering.
    public string AsCharacter { get; set; } = string.Empty;
}

/// One entry in somebody's friend list, in either direction.
public sealed class FriendEntry
{
    public string Id { get; set; } = string.Empty;

    /// The other person, as "Name@World".
    public string Character { get; set; } = string.Empty;

    /// Which of your own characters this is about.
    public string YourCharacter { get; set; } = string.Empty;

    public DateTime WhenUtc { get; set; }
}

/// Everything this installation's friendships currently look like.
public sealed class FriendList
{
    public List<FriendEntry> Friends { get; set; } = [];

    /// People who have asked you, waiting for an answer.
    public List<FriendEntry> Incoming { get; set; } = [];

    /// People you have asked, waiting on them.
    public List<FriendEntry> Outgoing { get; set; } = [];
}

/// What came back from uploading a portrait.
public sealed class PortraitAck
{
    public bool Ok { get; set; }

    /// The stored picture's content stamp.
    public string Stamp { get; set; } = string.Empty;

    /// A sentence for the player.
    public string Error { get; set; } = string.Empty;
}

/// Starting the Lodestone check: "give me a code for this character".
public sealed class VerifyBeginRequest
{
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
}

public sealed class VerifyBeginResponse
{
    public bool Ok { get; set; }

    /// The code to paste into the character's Lodestone profile.
    public string Code { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;

    /// Whether the relay can reach the Lodestone at all.
    public bool Available { get; set; } = true;
}

/// Finishing it: "here is my character's page, go and look".
public sealed class VerifyCheckRequest
{
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

/// How a check is getting on.
public enum VerifyState
{
    /// Nothing has been asked for this character.
    None = 0,

    /// Asked, and waiting on the check.
    Waiting = 1,

    Verified = 2,

    /// Looked at, and not verified.
    Rejected = 3,
}

public sealed class VerifyCheckResponse
{
    public bool Verified { get; set; }

    public VerifyState State { get; set; } = VerifyState.None;

    /// What to show the player.
    public string Message { get; set; } = string.Empty;
}

/// Asking how a check is getting on.
public sealed class VerifyStatusRequest
{
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
}



/// One page to be read.
public sealed class VerifyJob
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;

    /// The address the player pasted.
    public string Url { get; set; } = string.Empty;

    /// What to look for on the page.
    public string Code { get; set; } = string.Empty;

    public DateTime RequestedUtc { get; set; }
}

public sealed class VerifyJobList
{
    public List<VerifyJob> Jobs { get; set; } = [];
}

/// The verdict.
public sealed class VerifyResolveRequest
{
    public string Id { get; set; } = string.Empty;

    public bool Verified { get; set; }

    /// The character id off the page, kept because it is the only STABLE identity available - a claim on
    /// "Name@World" breaks on a rename or a world transfer and this does not.
    public string LodestoneId { get; set; } = string.Empty;

    /// Why, in words the player will be shown unchanged.
    public string Message { get; set; } = string.Empty;
}

/// Why somebody is being reported.
public enum ReportReason
{
    Hateful = 0,
    Sexual = 1,
    Harassment = 2,
    Impersonation = 3,
    Other = 4,
}

/// Where a report was filed from.
public sealed class ReportLocation
{
    public string Zone { get; set; } = string.Empty;

    /// The wider region, when it differs from the zone.
    public string Region { get; set; } = string.Empty;

    public uint TerritoryId { get; set; }

    /// Whether this was a private interior - a house, an apartment or a free company room.
    public bool Private { get; set; }

    public int Ward { get; set; }
    public int Plot { get; set; }
    public int Room { get; set; }

    /// A line for a moderator, or empty when nothing is known.
    public string Describe()
    {
        if (Zone.Length == 0)
            return string.Empty;

        var where = Region.Length > 0 ? $"{Zone}, {Region}" : Zone;

        if (!Private)
            return $"{where} (public)";

        var address = Ward > 0
            ? Room > 0 ? $"ward {Ward}, room {Room}" : Plot > 0 ? $"ward {Ward}, plot {Plot}" : $"ward {Ward}"
            : string.Empty;

        return address.Length > 0 ? $"{where} - private, {address}" : $"{where} (private)";
    }
}

/// One report, as it goes out.
public sealed class ProfileReport
{
    public string Plugin { get; set; } = "EchoRoleplay";

    /// Who is being reported, as "Name@HomeWorld".
    public string Subject { get; set; } = string.Empty;

    /// Who reported them.
    public string Reporter { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// What the reporter wants to say.
    public string Detail { get; set; } = string.Empty;

    /// The profile's own text, as it stood when reported.
    public string Snapshot { get; set; } = string.Empty;

    /// Where the reporter was when they filed it.
    public ReportLocation Location { get; set; } = new();

    /// Whether the reported profile carries a portrait, so a moderator knows to look at the picture as well
    /// as the words.
    public bool HasPortrait { get; set; }

    public string Version { get; set; } = string.Empty;
}

/// A bug report.
public sealed class BugReport
{
    /// Which plugin this came from.
    public string Plugin { get; set; } = "EchoRoleplay";

    /// Optional, and free text.
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// Live or dev.
    public string Relay { get; set; } = string.Empty;

    public string Tab { get; set; } = string.Empty;

    public float Scale { get; set; }
}

public sealed class BugReportAck
{
    public bool Ok { get; set; }

    /// Whether it also reached Discord.
    public bool Delivered { get; set; }

    public string Error { get; set; } = string.Empty;
}

/// Sizes both ends enforce.
public static class RelayLimits
{
    /// How many hashes one card batch may carry.
    public const int CardBatch = 256;

    /// A published profile, serialised.
    public const int PublishBytes = 64 * 1024;

    public const int ReportDetail = 1000;
    public const int ReportSnapshot = 6000;
}
