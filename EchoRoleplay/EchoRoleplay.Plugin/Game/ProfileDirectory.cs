using Dalamud.Game.ClientState.Objects.SubKinds;
using EchoRoleplay.Shared;

namespace EchoRoleplay.Game;

/// Answers the one question everything visible in the world asks: given a character, what is their profile?
/// AN INTERFACE FROM DAY ONE, WITH ONLY A LOCAL IMPLEMENTATION BEHIND IT - AND PHASE 4 PROVED IT EARNED THAT.
public sealed class ProfileDirectory
{
    private readonly ProfileStore store;

    /// The block list, consulted before anything else.
    private readonly ContactBook contacts;

    /// Whether to answer every lookup with the local player's own profile.
    public bool MirrorLocalProfile { get; set; }

    /// Everybody else's profiles, off the relay.
    private RelayDirectory? relay;

    /// Whether the player has asked to see friends only.
    private readonly Func<bool> onlyFriends;

    public ProfileDirectory(ProfileStore store, ContactBook contacts, Func<bool> onlyFriends)
    {
        this.store = store;
        this.contacts = contacts;
        this.onlyFriends = onlyFriends;
    }

    /// Hands this the relay, once there is one.
    public void Attach(RelayDirectory directory) => relay = directory;

    /// Asks for somebody's full sheet rather than their card.
    public void Request(string characterKey)
    {
        if (!contacts.IsBlocked(characterKey))
            relay?.Request(characterKey);
    }

    /// Whether this character has proved the character is theirs, through the Lodestone.
    public bool IsVerified(string characterKey, string localCharacterKey)
    {
        if (string.IsNullOrEmpty(characterKey) || contacts.IsBlocked(characterKey))
            return false;

        if (MirrorLocalProfile
            && !string.Equals(characterKey, localCharacterKey, System.StringComparison.Ordinal))
        {
            return relay?.CardVerified(localCharacterKey) == true;
        }

        return relay?.CardVerified(characterKey) == true;
    }

    /// Whether what Lookup returns for this character is the whole sheet.
    public bool HasFullProfile(string characterKey) =>
        string.IsNullOrEmpty(characterKey)
        || relay is null
        || relay.HasFull(characterKey)
        || store.ForCharacter(characterKey) is not null;

    /// The profile for a character key, or null if nobody here has one.
    public RoleplayProfile? Lookup(string characterKey, string localCharacterKey)
    {
        if (string.IsNullOrEmpty(characterKey))
            return null;

        if (contacts.IsBlocked(characterKey))
            return null;

        if (string.Equals(characterKey, localCharacterKey, System.StringComparison.Ordinal))
            return store.ForCharacter(localCharacterKey);

        if (onlyFriends() && !contacts.IsFriend(characterKey))
            return null;

        if (relay?.Lookup(characterKey) is { } published)
            return published;

        return MirrorLocalProfile ? store.ForCharacter(localCharacterKey) : null;
    }

    /// The key for a player object, or empty when the game has not filled them in yet.
    public static string KeyFor(IPlayerCharacter player)
    {
        var name = player.Name.TextValue;
        var world = player.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;

        return ProfileStore.CharacterKey(name, world);
    }
}
