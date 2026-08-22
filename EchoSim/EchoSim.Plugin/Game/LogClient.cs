using System.Net.Http;
using EchoSim.Shared;
using Newtonsoft.Json;

namespace EchoSim.Game;

/// Fetches parsed fights from the EchoSim relay.
public static class LogClient
{
    /// Live or dev, decided at compile time - see RelayEndpoints.
    private static string BaseUrl => RelayEndpoints.BaseUrl;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// Pulls the report code out of whatever the user pasted.
    public static string? ParseReportCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var text = input.Trim();

        var marker = text.IndexOf("/reports/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
            text = text[(marker + "/reports/".Length)..];

        foreach (var separator in new[] { '#', '?', '/' })
        {
            var at = text.IndexOf(separator);
            if (at > 0)
                text = text[..at];
        }

        text = text.Trim();

        return text.Length is >= 8 and <= 32 && text.All(char.IsLetterOrDigit) ? text : null;
    }

    public static async Task<LogReportInfo> GetReportAsync(string code, CancellationToken cancel)
        => await GetAsync<LogReportInfo>($"{BaseUrl}/report/{code}", cancel).ConfigureAwait(false);

    public static async Task<LogFightDetail> GetFightAsync(string code, int fightId, int sourceId, CancellationToken cancel)
        => await GetAsync<LogFightDetail>($"{BaseUrl}/report/{code}/{fightId}/{sourceId}", cancel).ConfigureAwait(false);

    private static async Task<T> GetAsync<T>(string url, CancellationToken cancel)
    {
        using var response = await Http.GetAsync(url, cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserialise<LogError>(body);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error?.Message)
                    ? $"The log service returned {(int)response.StatusCode}."
                    : error.Message);
        }

        return TryDeserialise<T>(body)
               ?? throw new InvalidOperationException("The log service returned something unreadable.");
    }

    private static T? TryDeserialise<T>(string body)
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(body);
        }
        catch
        {
            return default;
        }
    }
}
