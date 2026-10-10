using System.Linq;

namespace EchoMix.Plugin;

/// The plugin's half of DJ listing ownership: which token proves this install owns which listing.
internal static class DjProfileOwnership
{
    /// Config keys are profile id and character joined by this.
    private const char KeySeparator = '|';

    private static string Key(string profileId, string characterName) => $"{profileId}{KeySeparator}{characterName}";

    /// The token to present for a listing, or null if this install holds none for it.
    public static string? TokenFor(Configuration configuration, string? profileId)
    {
        if (string.IsNullOrEmpty(profileId))
            return null;

        var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;
        if (characterName.Length > 0 && configuration.DjProfileOwnerTokens.TryGetValue(Key(profileId, characterName), out var own))
            return own;

        var prefix = profileId + KeySeparator;
        return configuration.DjProfileOwnerTokens
            .Where(kv => kv.Key.StartsWith(prefix, System.StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .FirstOrDefault();
    }

    /// Records a freshly minted token and persists immediately.
    public static void Remember(Configuration configuration, string profileId, string characterName, string token)
    {
        if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(token))
            return;

        var key = Key(profileId, characterName);
        if (configuration.DjProfileOwnerTokens.TryGetValue(key, out var existing) && existing == token)
            return;

        configuration.DjProfileOwnerTokens[key] = token;
        configuration.Save();
    }

    /// Drops every token this install holds for a listing, after a confirmed delete.
    public static void Forget(Configuration configuration, string? profileId)
    {
        if (string.IsNullOrEmpty(profileId))
            return;

        var prefix = profileId + KeySeparator;
        var stale = configuration.DjProfileOwnerTokens.Keys
            .Where(k => k.StartsWith(prefix, System.StringComparison.Ordinal))
            .ToList();

        if (stale.Count == 0)
            return;

        foreach (var key in stale)
            configuration.DjProfileOwnerTokens.Remove(key);

        configuration.Save();
    }
}
