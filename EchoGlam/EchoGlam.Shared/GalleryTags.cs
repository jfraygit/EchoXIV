namespace EchoGlam.Shared;

/// The tag vocabulary, and it is closed on purpose.
public static class GalleryTags
{
    /// The canonical list, in the order the filter row draws them.
    public static readonly string[] All =
    [
        "Casual",
        "Formal",
        "Elegant",
        "Cute",
        "Dark",
        "Edgy",
        "Gothic",
        "Punk",
        "Regal",
        "Rustic",
        "Minimal",
        "Ornate",
        "Monochrome",
        "Colourful",

        "Armour",
        "Knight",
        "Mage",
        "Witch",
        "Rogue",
        "Pirate",
        "Uniform",
        "Maid",
        "Idol",
        "Tribal",
        "Steampunk",
        "Kimono",
        "Swimwear",

        "Seasonal",
        "Festival",
        "Nightlife",
        "Cosplay",
        "Roleplay",
        "Wedding",
    ];

    /// How many tags one entry may carry.
    public const int MaximumPerGlamour = 4;

    public static bool IsValid(string? tag) => tag is not null && Array.IndexOf(All, tag) >= 0;

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
