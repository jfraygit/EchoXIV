using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EchoRoleplay.Shared;
using Newtonsoft.Json;

namespace EchoRoleplay.Game;

/// One person you have looked up.
public sealed class Contact
{
    /// "Name@HomeWorld".
    public string CharacterKey { get; set; } = string.Empty;

    /// The roleplay name they were using when you read them.
    public string Name { get; set; } = string.Empty;

    public DateTime FirstMetUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastReadUtc { get; set; } = DateTime.UtcNow;

    /// Your own note about them.
    public string Note { get; set; } = string.Empty;

    /// Kept to hand.
    public bool Favourite { get; set; }

    /// Somebody you actually know, rather than somebody you once read.
    public bool Friend { get; set; }

    /// Never show me this person.
    public bool Blocked { get; set; }
}

/// Everyone whose profile you have opened, and what you wrote about them.
public sealed class ContactBook
{
    private readonly string path;
    private Dictionary<string, Contact> contacts = [];

    private bool readFailed;
    private bool saveDue;
    private long saveRequestedAt;

    private const long SaveDelayMs = 700;

    public ContactBook(string configDirectory)
    {
        path = Path.Combine(configDirectory, "contacts.json");
        Load();
    }

    public bool ReadOnly => readFailed;

    public int Count => contacts.Count;

    /// Everyone, most recently read first - which is the order somebody is looking for when they open this,
    /// because the person they want is usually one they just met.
    public IEnumerable<Contact> All =>
        contacts.Values.OrderByDescending(c => c.LastReadUtc);

    public IEnumerable<Contact> Friends => All.Where(c => c.Friend);

    public IEnumerable<Contact> Favourites => All.Where(c => c.Favourite);

    public IEnumerable<Contact> Blocks => All.Where(c => c.Blocked);

    /// Whether this character is somebody the player knows.
    public bool IsFriend(string characterKey) =>
        characterKey.Length > 0 && contacts.TryGetValue(characterKey, out var found) && found.Friend;

    public Contact? ByKey(string characterKey) =>
        characterKey.Length > 0 && contacts.TryGetValue(characterKey, out var found) ? found : null;

    /// Whether this character is blocked.
    public bool IsBlocked(string characterKey) =>
        characterKey.Length > 0 && contacts.TryGetValue(characterKey, out var found) && found.Blocked;

    /// Blocks or unblocks somebody, recording them first if they are not known.
    public void SetBlocked(string characterKey, string name, bool blocked)
    {
        if (string.IsNullOrEmpty(characterKey))
            return;

        if (!contacts.TryGetValue(characterKey, out var contact))
        {
            contact = new Contact
            {
                CharacterKey = characterKey,
                Name = string.IsNullOrWhiteSpace(name) ? characterKey.Split('@')[0] : name,
            };

            contacts[characterKey] = contact;
        }

        if (contact.Blocked != blocked)
            BlockRevision++;

        contact.Blocked = blocked;
        Persist();
    }

    /// Bumped whenever the set of blocked characters actually changes.
    public int BlockRevision { get; private set; }

    public void SetFavourite(string characterKey, bool favourite)
    {
        if (!contacts.TryGetValue(characterKey, out var contact))
            return;

        contact.Favourite = favourite;
        Persist();
    }

    /// Marks somebody a friend, or unmarks them.
    public void Remember(string characterKey, string name, bool friend)
    {
        if (string.IsNullOrEmpty(characterKey) || contacts.ContainsKey(characterKey))
            return;

        contacts[characterKey] = new Contact
        {
            CharacterKey = characterKey,
            Name = name,
            Friend = friend,
        };

        Persist();
    }

    public void SetFriend(string characterKey, bool friend)
    {
        if (!contacts.TryGetValue(characterKey, out var contact))
            return;

        contact.Friend = friend;
        Persist();
    }

    /// Records having read somebody's profile.
    public void Met(string characterKey, RoleplayProfile profile)
    {
        if (string.IsNullOrEmpty(characterKey))
            return;

        var name = !string.IsNullOrWhiteSpace(profile.Name)
            ? profile.Name
            : characterKey.Split('@')[0];

        if (contacts.TryGetValue(characterKey, out var existing))
        {
            existing.Name = name;
            existing.LastReadUtc = DateTime.UtcNow;
        }
        else
        {
            contacts[characterKey] = new Contact
            {
                CharacterKey = characterKey,
                Name = name,
            };
        }

        Persist();
    }

    /// Writes a note, on a delay - a note is typed, and typing is keystrokes.
    public void Note(string characterKey, string note)
    {
        if (!contacts.TryGetValue(characterKey, out var contact))
            return;

        contact.Note = note;
        saveDue = true;
        saveRequestedAt = Environment.TickCount64;
    }

    public void Forget(string characterKey)
    {
        if (contacts.Remove(characterKey))
            Persist();
    }

    /// Writes out a deferred change once typing has stopped.
    public void FlushPending(bool force = false)
    {
        if (!saveDue)
            return;

        if (!force && Environment.TickCount64 - saveRequestedAt < SaveDelayMs)
            return;

        Persist();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path))
                return;

            contacts =
                JsonConvert.DeserializeObject<Dictionary<string, Contact>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoRoleplay] Could not read {path}. Contacts will not be saved this session.");
            readFailed = true;
        }
    }

    private void Persist()
    {
        saveDue = false;

        if (readFailed)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(contacts, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"[EchoRoleplay] Could not write {path}");
        }
    }
}
