namespace EchoGlam.Shared;

/// The tag vocabulary, and it is closed on purpose.
public static class GalleryTags
{
    /// One named band of the vocabulary: the heading the picker draws, and the words under it.
    public readonly record struct Group(string Name, string[] Tags);

    /// The vocabulary, in the order the picker draws it.
    public static readonly Group[] Groups =
    [
        new("Mood",
        [
            "Casual",
            "Formal",
            "Elegant",
            "Glamorous",
            "Cute",
            "Cosy",
            "Dark",
            "Edgy",
            "Gothic",
            "Punk",
            "Regal",
            "Rustic",
            "Minimal",
            "Ornate",
            "Whimsical",
            "Vintage",
        ]),

        new("Theme",
        [
            "Fantasy",
            "Modern",
            "Futuristic",
            "Steampunk",
            "Victorian",
            "Military",
            "Nautical",
            "Tribal",
            "Eastern",
            "Desert",
            "Celestial",
            "Floral",
            "Eerie",
            "Angelic",
            "Demonic",
            "Mechanical",
        ]),

        new("Character",
        [
            "Knight",
            "Warrior",
            "Mage",
            "Witch",
            "Necromancer",
            "Rogue",
            "Assassin",
            "Pirate",
            "Sailor",
            "Noble",
            "Scholar",
            "Priest",
            "Shrine Maiden",
            "Hunter",
            "Soldier",
            "Detective",
            "Vampire",
            "Fae",
            "Mermaid",
            "Samurai",
            "Maid",
            "Idol",
        ]),

        new("Garment",
        [
            "Armour",
            "Plate",
            "Chainmail",
            "Leather",
            "Robes",
            "Coat",
            "Cloak",
            "Suit",
            "Dress",
            "Gown",
            "Skirt",
            "Kimono",
            "Yukata",
            "Uniform",
            "Apron",
            "Swimwear",
            "Masked",
            "Eyewear",
        ]),

        new("Palette",
        [
            "Black",
            "White",
            "Red",
            "Blue",
            "Green",
            "Purple",
            "Pink",
            "Gold",
            "Silver",
            "Monochrome",
            "Colourful",
            "Pastel",
        ]),

        new("Occasion",
        [
            "Everyday",
            "Adventuring",
            "Combat",
            "Nightlife",
            "Festival",
            "Seasonal",
            "Wedding",
            "Cosplay",
            "Roleplay",
            "Performance",
            "Courtly",
            "Travel",
        ]),
    ];

    /// The canonical flat list, in the order the groups declare it.
    public static readonly string[] All = [.. Groups.SelectMany(g => g.Tags)];

    /// How many tags one entry may carry.
    public const int MaximumPerGlamour = 6;

    public static bool IsValid(string? tag) => tag is not null && Array.IndexOf(All, tag) >= 0;

    /// Which group a tag belongs to, or null for one this build has never heard of.
    public static string? GroupOf(string tag)
    {
        foreach (var group in Groups)
        {
            foreach (var candidate in group.Tags)
            {
                if (string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase))
                    return group.Name;
            }
        }

        return null;
    }

    /// Keeps the recognised tags from a submission, in canonical order, capped and deduped.
    public static string[] Clean(IEnumerable<string>? tags)
    {
        if (tags is null)
            return [];

        var kept = new List<string>();

        foreach (var tag in All)
        {
            if (kept.Count >= MaximumPerGlamour)
                break;

            foreach (var offered in tags)
            {
                if (!string.Equals(offered, tag, StringComparison.OrdinalIgnoreCase))
                    continue;

                kept.Add(tag);
                break;
            }
        }

        return [.. kept];
    }
}
