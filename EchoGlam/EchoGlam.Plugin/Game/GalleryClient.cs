using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoGlam.Shared;
using Newtonsoft.Json;

namespace EchoGlam.Game;

/// What the browse grid is asking the relay for.
public sealed class GalleryQuery
{
    public string Sort { get; set; } = GlamourSort.Trending;
    public string? Text { get; set; }
    public uint? JobId { get; set; }
    public byte? Race { get; set; }
    public byte? Sex { get; set; }
    public string? Tag { get; set; }
    public uint? ItemId { get; set; }

    /// Restricts to one publisher, by public profile id.
    public string? AuthorId { get; set; }

    public int Page { get; set; }

    /// A shallow copy, so a filter change starts from the current query without the caller having to restate
    /// the six fields it did not touch.
    public GalleryQuery Clone() => (GalleryQuery)MemberwiseClone();
}

/// Limits and vocabularies the relay is currently running with.
public sealed class GallerySettings
{
    public string[] Tags { get; set; } = GalleryTags.All;
    public int MaximumPerGlamour { get; set; } = GalleryTags.MaximumPerGlamour;
    public string[] ReportReasons { get; set; } = Shared.ReportReasons.All;
    public bool SubmissionsOpen { get; set; }
    public int MaximumImages { get; set; } = 3;
    public int MaximumTitle { get; set; } = 60;
    public int MaximumDescription { get; set; } = 600;
    public int MaximumPerOwner { get; set; } = 20;

    /// How many of your glamours can be taken down before you stop being able to publish.
    public int BanThreshold { get; set; } = 3;
}

/// Talks to the gallery relay.
public static class GalleryClient
{
    private static string BaseUrl => RelayEndpoints.BaseUrl;

    /// One client for the whole plugin.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string OwnerHeader = "X-EchoGlam-Owner";


    public static Task<GlamourPage> BrowseAsync(GalleryQuery query, string ownerKey, CancellationToken cancel)
    {
        var url = new StringBuilder($"{BaseUrl}/glamours?sort={Uri.EscapeDataString(query.Sort)}");
        url.Append($"&page={Math.Max(0, query.Page)}");

        if (!string.IsNullOrWhiteSpace(query.Text))
            url.Append($"&q={Uri.EscapeDataString(query.Text)}");

        if (query.JobId is { } job and > 0)
            url.Append($"&job={job}");

        if (query.Race is { } race and > 0)
            url.Append($"&race={race}");

        if (query.Sex is { } sex)
            url.Append($"&sex={sex}");

        if (!string.IsNullOrWhiteSpace(query.Tag))
            url.Append($"&tag={Uri.EscapeDataString(query.Tag)}");

        if (query.ItemId is { } item and > 0)
            url.Append($"&item={item}");

        if (!string.IsNullOrWhiteSpace(query.AuthorId))
            url.Append($"&author={Uri.EscapeDataString(query.AuthorId)}");

        return RequireAsync<GlamourPage>(HttpMethod.Get, url.ToString(), ownerKey, null, cancel);
    }

    /// What this installation has saved.
    public static Task<GlamourPage> FavouritesAsync(string ownerKey, CancellationToken cancel) =>
        RequireAsync<GlamourPage>(HttpMethod.Get, $"{BaseUrl}/favourites", ownerKey, null, cancel);

    /// One entry with its gear list, or null when it is no longer on the board.
    public static Task<GlamourDetail?> DetailAsync(string id, string ownerKey, CancellationToken cancel) =>
        OptionalAsync<GlamourDetail>(
            HttpMethod.Get, $"{BaseUrl}/glamours/{Uri.EscapeDataString(id)}", ownerKey, null, cancel);

    public static Task<ProfileDetail?> ProfileAsync(string id, string ownerKey, CancellationToken cancel) =>
        OptionalAsync<ProfileDetail>(
            HttpMethod.Get, $"{BaseUrl}/profiles/{Uri.EscapeDataString(id)}", ownerKey, null, cancel);

    /// The caller's own profile, created empty if this installation has never had one.
    public static Task<ProfileDetail> MyProfileAsync(string ownerKey, CancellationToken cancel) =>
        RequireAsync<ProfileDetail>(HttpMethod.Get, $"{BaseUrl}/profiles/me", ownerKey, null, cancel);

    /// The relay's current limits and vocabularies.
    public static async Task<GallerySettings> SettingsAsync(CancellationToken cancel)
    {
        try
        {
            return await RequireAsync<GallerySettings>(HttpMethod.Get, $"{BaseUrl}/tags", string.Empty, null, cancel)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return new GallerySettings();
        }
    }


    /// The URL of one screenshot.
    public static string ImageUrl(GlamourSummary entry, int index, bool card) =>
        $"{BaseUrl}/glamours/{Uri.EscapeDataString(entry.Id)}/image/{index}"
        + $"?card={(card ? 1 : 0)}&v={Uri.EscapeDataString(entry.ImageStamp)}";

    public static string AvatarUrl(ProfileSummary profile) =>
        $"{BaseUrl}/profiles/{Uri.EscapeDataString(profile.Id)}/avatar?v={Uri.EscapeDataString(profile.ImageStamp)}";

    /// An avatar by publisher id alone, for callers that have one without their profile.
    public static string AvatarUrl(string profileId) =>
        $"{BaseUrl}/profiles/{Uri.EscapeDataString(profileId)}/avatar";

    public static string BannerUrl(ProfileSummary profile) =>
        $"{BaseUrl}/profiles/{Uri.EscapeDataString(profile.Id)}/banner?v={Uri.EscapeDataString(profile.ImageStamp)}";

    public static async Task<byte[]> ImageBytesAsync(string url, CancellationToken cancel)
    {
        using var response = await Http.GetAsync(url, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false);
    }


    public static Task<ToggleResult> VoteAsync(string id, string ownerKey, CancellationToken cancel) =>
        RequireAsync<ToggleResult>(
            HttpMethod.Post, $"{BaseUrl}/glamours/{Uri.EscapeDataString(id)}/vote", ownerKey, Empty(), cancel);

    public static Task<ToggleResult> FavouriteAsync(string id, string ownerKey, CancellationToken cancel) =>
        RequireAsync<ToggleResult>(
            HttpMethod.Post, $"{BaseUrl}/glamours/{Uri.EscapeDataString(id)}/favourite", ownerKey, Empty(), cancel);

    public static Task<ToggleResult> FollowAsync(string profileId, string ownerKey, CancellationToken cancel) =>
        RequireAsync<ToggleResult>(
            HttpMethod.Post, $"{BaseUrl}/profiles/{Uri.EscapeDataString(profileId)}/follow", ownerKey, Empty(), cancel);

    public static Task ReportGlamourAsync(ReportSubmission report, CancellationToken cancel) =>
        RequireAsync<object>(
            HttpMethod.Post, $"{BaseUrl}/glamours/{Uri.EscapeDataString(report.TargetId)}/report",
            report.OwnerKey, Json(report), cancel);

    public static Task ReportProfileAsync(ReportSubmission report, CancellationToken cancel) =>
        RequireAsync<object>(
            HttpMethod.Post, $"{BaseUrl}/profiles/{Uri.EscapeDataString(report.TargetId)}/report",
            report.OwnerKey, Json(report), cancel);

    public static Task<ProfileDetail> UpdateProfileAsync(ProfileSubmission profile, CancellationToken cancel) =>
        RequireAsync<ProfileDetail>(
            HttpMethod.Post, $"{BaseUrl}/profiles", profile.OwnerKey, Json(profile), cancel);

    /// Sets or clears an avatar or banner.
    public static Task<ProfileDetail> SetPictureAsync(
        bool avatar, byte[]? jpeg, string ownerKey, CancellationToken cancel)
    {
        var url = $"{BaseUrl}/profiles/{(avatar ? "avatar" : "banner")}";

        if (jpeg is null)
            return RequireAsync<ProfileDetail>(HttpMethod.Post, url, ownerKey, Empty(), cancel);

        var form = new MultipartFormDataContent();
        var picture = new ByteArrayContent(jpeg);
        picture.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(picture, "image", "picture.jpg");

        return RequireAsync<ProfileDetail>(HttpMethod.Post, url, ownerKey, form, cancel);
    }

    /// Publishes or updates a glamour.
    public static async Task<GlamourSubmitResult> SubmitAsync(
        GlamourSubmission submission, IReadOnlyList<byte[]> images, CancellationToken cancel)
    {
        using var form = new MultipartFormDataContent();

        var document = new StringContent(JsonConvert.SerializeObject(submission));
        document.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(document, "submission");

        for (var i = 0; i < images.Count; i++)
        {
            var picture = new ByteArrayContent(images[i]);
            picture.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(picture, $"image{i}", $"shot{i}.jpg");
        }

        using var request = Build(HttpMethod.Post, $"{BaseUrl}/glamours", submission.OwnerKey, form);
        using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return new GlamourSubmitResult { Accepted = false, Reason = Explain(body, response.StatusCode.ToString()) };

        return JsonConvert.DeserializeObject<GlamourSubmitResult>(body)
               ?? new GlamourSubmitResult { Accepted = false, Reason = "The gallery sent back nothing readable." };
    }

    public static async Task<bool> DeleteAsync(string id, string ownerKey, CancellationToken cancel)
    {
        using var request = Build(HttpMethod.Delete, $"{BaseUrl}/glamours/{Uri.EscapeDataString(id)}", ownerKey, null);
        using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }


    /// A body-less POST still needs a body.
    private static HttpContent Empty() => new ByteArrayContent([]);

    private static HttpContent Json(object payload) =>
        new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

    private static HttpRequestMessage Build(HttpMethod method, string url, string ownerKey, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };

        if (!string.IsNullOrEmpty(ownerKey))
            request.Headers.Add(OwnerHeader, ownerKey);

        return request;
    }

    private static async Task<T> RequireAsync<T>(
        HttpMethod method, string url, string ownerKey, HttpContent? content, CancellationToken cancel)
    {
        using var request = Build(method, url, ownerKey, content);
        using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Explain(body, response.StatusCode.ToString()));

        if (typeof(T) == typeof(object))
            return default!;

        return JsonConvert.DeserializeObject<T>(body)
               ?? throw new InvalidOperationException("The gallery sent back nothing readable.");
    }

    private static async Task<T?> OptionalAsync<T>(
        HttpMethod method, string url, string ownerKey, HttpContent? content, CancellationToken cancel)
        where T : class
    {
        using var request = Build(method, url, ownerKey, content);
        using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Explain(body, response.StatusCode.ToString()));

        return JsonConvert.DeserializeObject<T>(body)
               ?? throw new InvalidOperationException("The gallery sent back nothing readable.");
    }


    /// Turns sharing on for the character being played, or off for the whole installation.
    public static Task<FriendAck> SharingAsync(
        string ownerKey, string name, string world, bool enabled, CancellationToken cancel) =>
        FriendPostAsync("/friends/sharing",
            new ShareRegistration { Name = name, World = world, Enabled = enabled }, ownerKey, cancel);

    /// Asks somebody to be friends.
    public static Task<FriendAck> FriendRequestAsync(
        string ownerKey, string name, string world, string fromCharacter, CancellationToken cancel) =>
        FriendPostAsync("/friends/request",
            new FriendRequest { Name = name, World = world, FromCharacter = fromCharacter }, ownerKey, cancel);

    /// Accepts or declines one.
    public static Task<FriendAck> FriendRespondAsync(
        string ownerKey, string id, bool accept, string asCharacter, CancellationToken cancel) =>
        FriendPostAsync("/friends/respond",
            new FriendResponse { Id = id, Accept = accept, AsCharacter = asCharacter }, ownerKey, cancel);

    /// Ends a friendship, or withdraws a request.
    public static Task<FriendAck> FriendRemoveAsync(string ownerKey, string id, CancellationToken cancel) =>
        FriendPostAsync("/friends/remove", new FriendResponse { Id = id }, ownerKey, cancel);

    /// Everything this installation's friendships look like, or null if the relay could not be reached.
    public static async Task<FriendList?> FriendsAsync(string ownerKey, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/friends");
            request.Headers.TryAddWithoutValidation(OwnerHeader, ownerKey);

            using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<FriendList>(body);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// Sends what this character is currently wearing.
    public static Task<FriendAck> PublishLookAsync(string ownerKey, SharedLook look, CancellationToken cancel) =>
        FriendPostAsync("/friends/look", look, ownerKey, cancel);

    /// What every friend is wearing, or null if the relay could not be reached.
    public static async Task<LookPage?> FriendLooksAsync(string ownerKey, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/friends/looks");
            request.Headers.TryAddWithoutValidation(OwnerHeader, ownerKey);

            using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<LookPage>(body);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// One owner-keyed POST, for the five that differ only in route and payload.
    private static async Task<FriendAck> FriendPostAsync(
        string route, object payload, string ownerKey, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{route}")
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"),
            };

            request.Headers.TryAddWithoutValidation(OwnerHeader, ownerKey);

            using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            var ack = JsonConvert.DeserializeObject<FriendAck>(body);

            return ack ?? new FriendAck { Error = $"The relay returned {(int)response.StatusCode}." };
        }
        catch (Exception ex)
        {
            return new FriendAck { Error = ex.Message };
        }
    }

    /// Pulls the relay's own sentence out of an error body, falling back to the status.
    private static string Explain(string body, string fallback)
    {
        try
        {
            var error = JsonConvert.DeserializeObject<GalleryError>(body);
            if (!string.IsNullOrWhiteSpace(error?.Message))
                return error.Message;
        }
        catch
        {
        }

        return fallback;
    }
}
