using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoRoleplay.Shared;
using Newtonsoft.Json;

namespace EchoRoleplay.Game;

/// The HTTP half of talking to the relay.
public sealed class RelayClient : IDisposable
{
    /// Longer than a page load, shorter than somebody notices.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient http = new() { Timeout = Timeout };

    /// How this installation identifies itself when writing.
    private readonly Func<string> ownerKeyOf;

    private string ownerKey => ownerKeyOf();

    public RelayClient(Func<string> ownerKey)
    {
        ownerKeyOf = ownerKey;

        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"EchoRoleplay/{typeof(RelayClient).Assembly.GetName().Version?.ToString() ?? "dev"}");
    }

    /// What an index poll produced.
    public enum IndexOutcome
    {
        Fetched,
        Unchanged,
        Failed,
    }

    public readonly record struct IndexResult(IndexOutcome Outcome, ProfileIndexResponse? Index, string? ETag);

    /// Tier 1.
    public async Task<IndexResult> IndexAsync(string world, string? etag, string viewer, CancellationToken cancel)
    {
        try
        {
            var url = $"{RelayEndpoints.BaseUrl}/index/{Uri.EscapeDataString(world)}";

            if (viewer.Length > 0)
                url += $"?viewer={Uri.EscapeDataString(viewer)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrEmpty(etag))
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return new IndexResult(IndexOutcome.Unchanged, null, etag);

            if (!response.IsSuccessStatusCode)
                return new IndexResult(IndexOutcome.Failed, null, null);

            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            var index = JsonConvert.DeserializeObject<ProfileIndexResponse>(body);

            return index is null
                ? new IndexResult(IndexOutcome.Failed, null, null)
                : new IndexResult(IndexOutcome.Fetched, index, response.Headers.ETag?.ToString());
        }
        catch (Exception)
        {
            return new IndexResult(IndexOutcome.Failed, null, null);
        }
    }

    /// Tier 2.
    public async Task<List<ProfileCard>?> CardsAsync(IReadOnlyCollection<string> hashes, CancellationToken cancel)
    {
        if (hashes.Count == 0)
            return [];

        try
        {
            var payload = JsonConvert.SerializeObject(new CardRequest { Hashes = [.. hashes] });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await http
                .PostAsync($"{RelayEndpoints.BaseUrl}/cards", content, cancel).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<CardResponse>(body)?.Cards;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// Tier 3.
    public async Task<ProfileEnvelope?> ProfileAsync(string id, string viewer, CancellationToken cancel)
    {
        try
        {
            var url = $"{RelayEndpoints.BaseUrl}/profiles/{Uri.EscapeDataString(id)}";

            if (viewer.Length > 0)
                url += $"?viewer={Uri.EscapeDataString(viewer)}";

            using var response = await http.GetAsync(url, cancel).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<ProfileEnvelope>(body);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// Publishes this character's profile.
    public async Task<PublishAck> PublishAsync(string name, string world, RoleplayProfile profile, CancellationToken cancel)
    {
        try
        {
            var payload = JsonConvert.SerializeObject(new PublishRequest
            {
                Name = name,
                World = world,
                Profile = profile,
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{RelayEndpoints.BaseUrl}/profiles")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            request.Headers.TryAddWithoutValidation("X-EchoRoleplay-Owner", ownerKey);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            var ack = JsonConvert.DeserializeObject<PublishAck>(body);

            return ack ?? new PublishAck { Error = $"The relay returned {(int)response.StatusCode}." };
        }
        catch (Exception ex)
        {
            return new PublishAck { Error = ex.Message };
        }
    }

    /// Asks somebody to be friends.
    public Task<FriendAck> FriendRequestAsync(string name, string world, string fromCharacter, CancellationToken cancel) =>
        PostAsync<FriendAck>("/friends/request",
            new FriendRequest { Name = name, World = world, FromCharacter = fromCharacter }, cancel);

    /// Accepts or declines one.
    public Task<FriendAck> FriendRespondAsync(string id, bool accept, string asCharacter, CancellationToken cancel) =>
        PostAsync<FriendAck>("/friends/respond",
            new FriendResponse { Id = id, Accept = accept, AsCharacter = asCharacter }, cancel);

    /// Ends a friendship, or withdraws a request.
    public Task<FriendAck> FriendRemoveAsync(string id, CancellationToken cancel) =>
        PostAsync<FriendAck>("/friends/remove", new FriendResponse { Id = id }, cancel);

    /// Everything this installation's friendships look like, or null if the relay could not be reached.
    public async Task<FriendList?> FriendsAsync(CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{RelayEndpoints.BaseUrl}/friends");
            request.Headers.TryAddWithoutValidation("X-EchoRoleplay-Owner", ownerKey);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);

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

    /// One owner-keyed POST, for the several that differ only in route and payload.
    private async Task<T> PostAsync<T>(string route, object payload, CancellationToken cancel)
        where T : new()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{RelayEndpoints.BaseUrl}{route}")
            {
                Content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"),
            };

            request.Headers.TryAddWithoutValidation("X-EchoRoleplay-Owner", ownerKey);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<T>(body) ?? new T();
        }
        catch (Exception)
        {
            return new T();
        }
    }

    /// Uploads a portrait against a published profile.
    public async Task<PortraitAck> UploadPortraitAsync(string relayId, byte[] jpeg, CancellationToken cancel)
    {
        try
        {
            using var content = new ByteArrayContent(jpeg);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{RelayEndpoints.BaseUrl}/profiles/{Uri.EscapeDataString(relayId)}/portrait")
            {
                Content = content,
            };

            request.Headers.TryAddWithoutValidation("X-EchoRoleplay-Owner", ownerKey);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<PortraitAck>(body)
                   ?? new PortraitAck { Error = $"The relay returned {(int)response.StatusCode}." };
        }
        catch (Exception ex)
        {
            return new PortraitAck { Error = ex.Message };
        }
    }

    /// Fetches somebody's portrait.
    public async Task<byte[]?> PortraitAsync(string relayId, CancellationToken cancel)
    {
        try
        {
            using var response = await http
                .GetAsync($"{RelayEndpoints.BaseUrl}/profiles/{Uri.EscapeDataString(relayId)}/portrait", cancel)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// Removes it.
    public async Task<bool> DeletePortraitAsync(string relayId, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete, $"{RelayEndpoints.BaseUrl}/profiles/{Uri.EscapeDataString(relayId)}/portrait");

            request.Headers.TryAddWithoutValidation("X-EchoRoleplay-Owner", ownerKey);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// Asks for a code to put on this character's Lodestone profile.
    public async Task<VerifyBeginResponse> VerifyBeginAsync(string name, string world, CancellationToken cancel)
    {
        try
        {
            var payload = JsonConvert.SerializeObject(new VerifyBeginRequest { Name = name, World = world });

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{RelayEndpoints.BaseUrl}/verify/begin")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            request.Headers.TryAddWithoutValidation("X-EchoRoleplay-Owner", ownerKey);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<VerifyBeginResponse>(body)
                   ?? new VerifyBeginResponse { Error = $"The relay returned {(int)response.StatusCode}." };
        }
        catch (Exception ex)
        {
            return new VerifyBeginResponse { Error = ex.Message };
        }
    }

    /// Asks the relay to go and look at the pasted Lodestone page.
    public async Task<VerifyCheckResponse> VerifyCheckAsync(
        string name, string world, string url, CancellationToken cancel)
    {
        try
        {
            var payload = JsonConvert.SerializeObject(
                new VerifyCheckRequest { Name = name, World = world, Url = url });

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{RelayEndpoints.BaseUrl}/verify/check")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            request.Headers.TryAddWithoutValidation("X-EchoRoleplay-Owner", ownerKey);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<VerifyCheckResponse>(body)
                   ?? new VerifyCheckResponse { Message = $"The relay returned {(int)response.StatusCode}." };
        }
        catch (Exception ex)
        {
            return new VerifyCheckResponse { Message = ex.Message };
        }
    }

    /// Asks how a queued check is getting on.
    public async Task<VerifyCheckResponse> VerifyStatusAsync(string name, string world, CancellationToken cancel)
    {
        try
        {
            var payload = JsonConvert.SerializeObject(new VerifyStatusRequest { Name = name, World = world });

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{RelayEndpoints.BaseUrl}/verify/status")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            request.Headers.TryAddWithoutValidation("X-EchoRoleplay-Owner", ownerKey);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<VerifyCheckResponse>(body) ?? new VerifyCheckResponse();
        }
        catch (Exception)
        {
            return new VerifyCheckResponse();
        }
    }

    /// Hands the relay this installation's whole block list, as hashes.
    public async Task<bool> SetBlocksAsync(IReadOnlyCollection<string> hashes, CancellationToken cancel)
    {
        try
        {
            var payload = JsonConvert.SerializeObject(new BlockList { Hashes = hashes.ToList() });

            using var request = new HttpRequestMessage(HttpMethod.Put, $"{RelayEndpoints.BaseUrl}/blocks")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            request.Headers.TryAddWithoutValidation("X-EchoRoleplay-Owner", ownerKey);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// Takes this installation's profile back off the relay.
    public async Task<bool> WithdrawAsync(string id, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete, $"{RelayEndpoints.BaseUrl}/profiles/{Uri.EscapeDataString(id)}");

            request.Headers.TryAddWithoutValidation("X-EchoRoleplay-Owner", ownerKey);

            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose() => http.Dispose();
}
