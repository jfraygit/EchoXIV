using System.Net.Http;
using System.Text;
using EchoSim.Shared;
using Newtonsoft.Json;

namespace EchoSim.Game;

/// Talks to the relay's leaderboard endpoints.
public static class LeaderboardClient
{
    /// Live or dev, decided at compile time - see RelayEndpoints.
    private static string BaseUrl => RelayEndpoints.BaseUrl;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<LeaderboardBoard> GetBoardAsync(
        string encounter, uint jobId, int limit, string ownerKey, CancellationToken cancel)
    {
        var url = $"{BaseUrl}/leaderboard/{encounter}/{jobId}?limit={limit}&owner={Uri.EscapeDataString(ownerKey)}";

        using var response = await Http.GetAsync(url, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Explain(body, response.StatusCode.ToString()));

        return JsonConvert.DeserializeObject<LeaderboardBoard>(body)
               ?? throw new InvalidOperationException("The leaderboard sent back nothing readable.");
    }

    /// One player's profile, or null when that character has no scores.
    public static async Task<LeaderboardProfile?> GetProfileAsync(
        string character, string world, string ownerKey, CancellationToken cancel)
    {
        var url = $"{BaseUrl}/leaderboard/profile/{Uri.EscapeDataString(world)}/{Uri.EscapeDataString(character)}"
                  + $"?owner={Uri.EscapeDataString(ownerKey)}";

        using var response = await Http.GetAsync(url, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Explain(body, response.StatusCode.ToString()));

        return JsonConvert.DeserializeObject<LeaderboardProfile>(body)
               ?? throw new InvalidOperationException("The profile came back unreadable.");
    }

    public static Task<LeaderboardSubmitResult> SubmitAsync(
        LeaderboardSubmission submission, CancellationToken cancel)
        => PostAsync($"{BaseUrl}/leaderboard/submit", submission, cancel);


    private static async Task<LeaderboardSubmitResult> PostAsync(
        string url, LeaderboardSubmission payload, CancellationToken cancel)
    {
        using var content = new StringContent(
            JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

        using var response = await Http.PostAsync(url, content, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new LeaderboardSubmitResult
            {
                Accepted = false,
                Reason = Explain(body, response.StatusCode.ToString()),
            };
        }

        return JsonConvert.DeserializeObject<LeaderboardSubmitResult>(body)
               ?? new LeaderboardSubmitResult { Accepted = false, Reason = "No answer from the leaderboard." };
    }

    /// Pulls the server's own sentence out of an error body, falling back to the status.
    private static string Explain(string body, string fallback)
    {
        try
        {
            var error = JsonConvert.DeserializeObject<LogError>(body);
            if (!string.IsNullOrWhiteSpace(error?.Message))
                return error.Message;
        }
        catch
        {
        }

        return fallback;
    }
}
