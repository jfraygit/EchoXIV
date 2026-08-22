namespace EchoGlam.Shared;

/// A publisher, as a card or a header.
public class ProfileSummary
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// The character's home world.
    public string HomeWorld { get; set; } = string.Empty;

    public bool HasAvatar { get; set; }
    public bool HasBanner { get; set; }

    /// Changes whenever the avatar or banner does, and rides in their URLs.
    public string ImageStamp { get; set; } = string.Empty;

    public int Followers { get; set; }
    public int Following { get; set; }
    public int Published { get; set; }

    /// Whether the asking installation owns this profile, and whether it follows it.
    public bool Mine { get; set; }
    public bool IsFollowing { get; set; }
}

/// A profile page: the header, plus what the person has published and saved.
public sealed class ProfileDetail : ProfileSummary
{
    public string Bio { get; set; } = string.Empty;

    public List<GlamourSummary> Glamours { get; set; } = [];

    /// What they have favourited.
    public List<GlamourSummary> Favourites { get; set; } = [];
}

/// What the plugin posts to create or update its own profile.
public sealed class ProfileSubmission
{
    public string OwnerKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string HomeWorld { get; set; } = string.Empty;
}

/// The answer to a vote or favourite, so the caller can draw the new state without asking for the whole entry
/// again.
public sealed class ToggleResult
{
    public bool Active { get; set; }
    public int Count { get; set; }
}

/// A report against a glamour or a profile.
public sealed class ReportSubmission
{
    public string OwnerKey { get; set; } = string.Empty;

    /// The glamour id or profile id being reported.
    public string TargetId { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

/// What a report can be about.
public static class ReportReasons
{
    public const string NotAGlamour = "Not a Glamour";
    public const string Offensive = "Offensive or Hateful";
    public const string Sexual = "Sexual Content";
    public const string Modded = "Modded Clothing";
    public const string Spam = "Spam or Advertising";

    public static readonly string[] All =
        [NotAGlamour, Offensive, Sexual, Modded, Spam];

    public static bool IsValid(string? reason) => reason is not null && Array.IndexOf(All, reason) >= 0;

    /// How much free text a report may carry.
    public const int MaximumNoteLength = 300;
}
