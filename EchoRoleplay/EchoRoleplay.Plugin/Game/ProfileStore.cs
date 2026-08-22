using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EchoRoleplay.Shared;
using Newtonsoft.Json;

namespace EchoRoleplay.Game;

/// What is on disk.
public sealed class ProfileBook
{
    public List<RoleplayProfile> Profiles { get; set; } = [];

    /// Which profile each character uses, keyed by "Name@World".
    public Dictionary<string, string> ActiveByCharacter { get; set; } = [];
}

/// The player's own profiles, kept in the plugin's configuration directory.
public sealed class ProfileStore
{
    private readonly string path;
    private ProfileBook book = new();

    /// Set when the file could not be read, which disables writing for the session.
    private bool readFailed;

    public ProfileStore(string configDirectory)
    {
        path = Path.Combine(configDirectory, "profiles.json");
        Load();
    }

    /// True when the file on disk could not be parsed.
    public bool ReadOnly => readFailed;

    /// Raised after anything here changes - an edit, a binding, a deletion.
    public event Action? Changed;

    public IReadOnlyList<RoleplayProfile> All => book.Profiles;

    public int Count => book.Profiles.Count;

    public RoleplayProfile? ById(string id) =>
        book.Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    /// The profile this character is currently using, or null if they have none bound.
    public RoleplayProfile? ForCharacter(string characterKey)
    {
        if (string.IsNullOrEmpty(characterKey))
            return null;

        if (!book.ActiveByCharacter.TryGetValue(characterKey, out var id))
            return null;

        return ById(id);
    }

    public string? BoundProfileId(string characterKey) =>
        !string.IsNullOrEmpty(characterKey) && book.ActiveByCharacter.TryGetValue(characterKey, out var id) ? id : null;

    /// Points a character at a profile, or at none.
    public void Bind(string characterKey, string? profileId)
    {
        if (string.IsNullOrEmpty(characterKey))
            return;

        if (string.IsNullOrEmpty(profileId))
            book.ActiveByCharacter.Remove(characterKey);
        else
            book.ActiveByCharacter[characterKey] = profileId;

        Persist();
    }

    public RoleplayProfile Create(string profileName)
    {
        var profile = new RoleplayProfile
        {
            ProfileName = Trim(profileName, ProfileLimits.ProfileName),
        };

        book.Profiles.Add(profile);
        Persist();
        return profile;
    }

    /// Copies a profile, bindings excluded.
    public RoleplayProfile Duplicate(RoleplayProfile source)
    {
        var json = JsonConvert.SerializeObject(source);
        var copy = JsonConvert.DeserializeObject<RoleplayProfile>(json)!;

        copy.Id = Guid.NewGuid().ToString("N");
        copy.ProfileName = Trim($"{source.ProfileName} (copy)", ProfileLimits.ProfileName);
        copy.UpdatedUtc = DateTime.UtcNow;

        book.Profiles.Add(copy);
        Persist();
        return copy;
    }

    public void Delete(string id)
    {
        if (book.Profiles.RemoveAll(p => string.Equals(p.Id, id, StringComparison.Ordinal)) == 0)
            return;

        foreach (var key in book.ActiveByCharacter.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList())
            book.ActiveByCharacter.Remove(key);

        Persist();
    }

    /// Records a change and writes it out.
    public void Touch(RoleplayProfile profile)
    {
        profile.UpdatedUtc = DateTime.UtcNow;
        Persist();
    }

    /// Records a change and writes it out shortly, rather than now.
    public void TouchLater(RoleplayProfile profile)
    {
        profile.UpdatedUtc = DateTime.UtcNow;
        saveDue = true;
        saveRequestedAt = Environment.TickCount64;
    }

    private bool saveDue;
    private long saveRequestedAt;

    /// How long typing has to stop before the file is written, in milliseconds.
    private const long SaveDelayMs = 700;

    /// Writes out a deferred change once the delay has passed.
    public void FlushPending(bool force = false)
    {
        if (!saveDue)
            return;

        if (!force && Environment.TickCount64 - saveRequestedAt < SaveDelayMs)
            return;

        saveDue = false;
        Persist();
    }

    /// Clears out any status whose time is up, across every profile.
    public bool PruneExpired(DateTime utcNow)
    {
        var changed = false;

        foreach (var profile in book.Profiles)
        {
            if (profile.PruneExpiredStatuses(utcNow))
                changed = true;
        }

        if (changed)
            Persist();

        return changed;
    }

    /// "Name@World", the key everything uses to mean one character.
    public static string CharacterKey(string name, string homeWorld) =>
        string.IsNullOrEmpty(name) || string.IsNullOrEmpty(homeWorld) ? string.Empty : $"{name}@{homeWorld}";

    private static string Trim(string value, int limit)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= limit ? trimmed : trimmed[..limit];
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path))
                return;

            book = JsonConvert.DeserializeObject<ProfileBook>(File.ReadAllText(path)) ?? new ProfileBook();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoRoleplay] Could not read {path}. Profiles will not be saved this session.");
            readFailed = true;
        }
    }

    private void Persist()
    {
        saveDue = false;

        Changed?.Invoke();

        if (readFailed)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(book, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoRoleplay] Could not write {path}");
        }
    }
}
