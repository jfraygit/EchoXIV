namespace EchoGlam.Shared;

/// One slot of a published glamour.
public sealed class GlamourSlotDto
{
    public int Slot { get; set; }
    public uint ItemId { get; set; }
    public byte Stain0 { get; set; }
    public byte Stain1 { get; set; }
}

/// What a card in the browse grid needs, and nothing more.
public class GlamourSummary
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// Character name and world, as recorded at submission.
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorWorld { get; set; } = string.Empty;

    /// The author's public profile id, for linking a card to its profile.
    public string AuthorId { get; set; } = string.Empty;

    public int Votes { get; set; }
    public int Favourites { get; set; }

    /// The job the look was built around, or 0 for none.
    public uint JobId { get; set; }

    /// The author's race, tribe and sex at submission, from the customise array.
    public byte Race { get; set; }
    public byte Tribe { get; set; }
    public byte Sex { get; set; }

    /// From the curated vocabulary in GalleryTags.
    public string[] Tags { get; set; } = [];

    /// How many screenshots this entry has.
    public int ImageCount { get; set; }

    /// Changes whenever the images do, and is part of every image URL.
    public string ImageStamp { get; set; } = string.Empty;

    /// Unix seconds.
    public long PublishedUtc { get; set; }
    public long UpdatedUtc { get; set; }

    /// Whether the asking installation has voted for or favourited this, when it sent an owner key.
    public bool Voted { get; set; }
    public bool Favourited { get; set; }

    /// Whether the asking installation published it.
    public bool Mine { get; set; }
}

/// Everything on the detail page: the summary, plus the actual look.
public sealed class GlamourDetail : GlamourSummary
{
    public string Description { get; set; } = string.Empty;

    public List<GlamourSlotDto> Slots { get; set; } = [];

    /// The author's full appearance, base64 of the twenty-six customise bytes, or null.
    public string? Appearance { get; set; }
}

/// One page of the browse grid.
public sealed class GlamourPage
{
    public List<GlamourSummary> Entries { get; set; } = [];

    /// How many entries match the query in total, so the grid can say "page 2 of 9" rather than discovering
    /// the end by walking off it.
    public int Total { get; set; }

    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// How the browse grid is ordered.
public static class GlamourSort
{
    /// Recent interest, decayed by age.
    public const string Trending = "trending";

    public const string Top = "top";
    public const string Newest = "newest";
    public const string Favourites = "favourites";

    public static readonly string[] All = [Trending, Top, Newest, Favourites];

    public static bool IsValid(string? sort) => sort is not null && Array.IndexOf(All, sort) >= 0;
}

/// What the plugin posts to publish or update a glamour.
public sealed class GlamourSubmission
{
    /// Empty to publish a new entry, or an existing id to update it in place.
    public string Id { get; set; } = string.Empty;

    public string OwnerKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string AuthorName { get; set; } = string.Empty;
    public string AuthorWorld { get; set; } = string.Empty;

    public uint JobId { get; set; }

    public byte Race { get; set; }
    public byte Tribe { get; set; }
    public byte Sex { get; set; }

    public string[] Tags { get; set; } = [];

    public List<GlamourSlotDto> Slots { get; set; } = [];

    public string? Appearance { get; set; }

    /// For an update: what the entry's screenshots should be afterwards, in order.
    public int[]? Images { get; set; }

    /// In Images, the next uploaded file rather than one already stored.
    public const int NewImage = -1;
}

/// The relay's answer to a submission.
public sealed class GlamourSubmitResult
{
    public bool Accepted { get; set; }

    /// A sentence written for the player, shown as-is.
    public string Reason { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;
}

/// An error body, in the one shape every endpoint uses.
public sealed class GalleryError
{
    public string Message { get; set; } = string.Empty;

    public static GalleryError From(string message) => new() { Message = message };
}
