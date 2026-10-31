using System;
using System.Collections.Generic;
using EchoGlam.Shared;

namespace EchoGlam.Game;

/// A seeded friend list, for looking at a page that is otherwise only ever empty on a dev account.
internal static class FakeFriends
{
    /// Where a seeded person sits.
    private enum Where
    {
        /// An agreed friendship.
        Friend,

        /// Somebody who has asked you.
        Asking,

        /// Somebody you have asked.
        Asked,
    }

    /// One seeded person and everything the page can say about them.
    private readonly record struct Cast(
        string Name,
        string World,
        Where Where,
        int SeenMinutes,
        bool Sharing,
        bool Live,
        bool Blocked);

    /// Invented people on Light worlds.
    private static readonly Cast[] People =
    [
        new("Yuki Hoshizora", "Phoenix", Where.Friend, 0, true, true, false),
        new("Marguerite Fontaine", "Twintania", Where.Friend, 3, true, false, false),
        new("Rhen'to Tia", "Shiva", Where.Friend, 48, false, false, false),

        new("Wilhelmina Starfall", "Twintania", Where.Friend, 1, true, true, false),
        new("Tovald Steinbrecher", "Zodiark", Where.Friend, 1440, false, false, false),

        new("Nim Nim", "Lich", Where.Friend, 12, true, false, false),
        new("Zhen Yue", "Shiva", Where.Friend, -1, false, false, false),

        new("Oswin Blackbriar", "Zodiark", Where.Friend, 7, true, false, true),

        new("Sylvaine Aubert", "Raiden", Where.Friend, 180, false, false, false),
        new("Khadija Amari", "Alpha", Where.Friend, 10080, false, false, false),
        new("Pip Pepperwhistle", "Lich", Where.Friend, 26, true, false, false),
        new("Eirlys Morgan", "Phoenix", Where.Friend, -1, false, false, false),

        new("Aurelie Vaillancourt", "Twintania", Where.Asking, -1, false, false, false),
        new("Bob Wobbleson", "Odin", Where.Asking, -1, false, false, false),

        new("Tatu Tomo", "Odin", Where.Asked, -1, false, false, false),
        new("Garrick Hollowmere", "Alpha", Where.Asked, -1, false, false, false),
    ];

    /// Puts the fixture up.
    public static void Apply(Plugin plugin, int count)
    {
        if (Build.Diagnostics)
            Seed(plugin, count);
    }

    /// Takes it down and goes back to the relay's answer.
    public static void Clear(Plugin plugin)
    {
        if (Build.Diagnostics)
            Unseed(plugin);
    }

    private static void Seed(Plugin plugin, int count)
    {
        var now = DateTime.UtcNow;
        var fixture = new Friendships.Fixture();

        var sharing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var drawing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var friends = 0;

        for (var i = 0; i < People.Length; i++)
        {
            var person = People[i];

            if (person.Where == Where.Friend)
            {
                if (friends >= count)
                    continue;

                friends++;
            }

            var character = $"{person.Name}@{person.World}";

            var entry = new FriendEntry
            {
                Id = $"fake-{i:00}",
                Character = character,
                YourCharacter = $"{GalleryState.Character.Name}@{GalleryState.Character.World}",

                WhenUtc = now.AddDays(-3 - (i * 11)),
            };

            switch (person.Where)
            {
                case Where.Asking:
                    fixture.List.Incoming.Add(entry);
                    break;

                case Where.Asked:
                    fixture.List.Outgoing.Add(entry);
                    break;

                default:
                    fixture.List.Friends.Add(entry);
                    break;
            }

            if (person.SeenMinutes >= 0)
                fixture.Seen[character] = now.AddMinutes(-person.SeenMinutes);

            if (person.Blocked)
                fixture.Blocked.Add(character);

            if (person.Sharing && !person.Blocked)
                sharing.Add(character);

            if (person.Live && !person.Blocked)
                drawing.Add(character);
        }

        plugin.Friendships.ApplyFixture(fixture);
        plugin.Looks.ApplyFixture(sharing, drawing);
    }

    private static void Unseed(Plugin plugin)
    {
        plugin.Looks.ClearFixture();
        plugin.Friendships.ClearFixture();
    }

    /// How many of the cast are friends, so the control seeding them can bound its slider.
    public static int Most
    {
        get
        {
            var count = 0;

            foreach (var person in People)
            {
                if (person.Where == Where.Friend)
                    count++;
            }

            return count;
        }
    }
}
