using System.Security.Cryptography;
using System.Text;

namespace EchoGlam.Shared;

/// How a character is named on the wire, when the relay has to look one up.
public static class CharacterHash
{
    public const int Length = 16;

    /// Hashes a "Name@World" key.
    public static string Of(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey))
            return string.Empty;

        var normalised = characterKey.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("echoglam:" + normalised));

        return Convert.ToHexString(hash)[..Length].ToLowerInvariant();
    }

    /// The same thing from the two halves, for callers that never assembled the key.
    public static string Of(string name, string world) => Of(Key(name, world));

    /// "Name@World", the one form everything keys a character on.
    public static string Key(string name, string world) =>
        string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world)
            ? string.Empty
            : $"{name.Trim()}@{world.Trim()}";
}

/// Telling the relay which character this installation is currently playing, so that a friend request naming
/// that character can find it.
public sealed class ShareRegistration
{
    /// The character to register.
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;

    /// False means forget everything this installation registered.
    public bool Enabled { get; set; }
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

/// Answering one, or withdrawing one.
public sealed class FriendResponse
{
    public string Id { get; set; } = string.Empty;

    public bool Accept { get; set; }

    /// Which of this installation's characters is answering.
    public string AsCharacter { get; set; } = string.Empty;
}

/// What came back.
public sealed class FriendAck
{
    public bool Ok { get; set; }

    /// The relay's own words when it did not work - not sharing, already friends, already asked.
    public string Error { get; set; } = string.Empty;
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
