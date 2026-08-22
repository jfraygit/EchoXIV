using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace EchoGlam.Game;

/// What gets sent.
public sealed record BugReport
{
    /// Which plugin this came from.
    public string Plugin { get; init; } = "EchoGlam";

    /// Optional, and free text.
    public string Name { get; init; } = string.Empty;

    public required string Description { get; init; }

    public string Version { get; init; } = string.Empty;

    /// Live or dev.
    public string Relay { get; init; } = string.Empty;

    public string Tab { get; init; } = string.Empty;

    public float Scale { get; init; }

    /// Whether the customise byte layout verified on this client.
    public bool AppearanceAvailable { get; init; }

    public int SlotsOverridden { get; init; }

    public int CatalogueSize { get; init; }
}

public sealed record BugReportAck
{
    public bool Ok { get; init; }

    /// Whether it also reached Discord.
    public bool Delivered { get; init; }

    public string Error { get; init; } = string.Empty;
}

/// Sends a user-written bug report to the project's Discord channel, by way of the relay.
public static class BugReporter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// The relay's own cap is 16KB for the whole document; this is the text box's share of it, and it is
    /// enforced by the box rather than by rejecting a report somebody has written.
    public const int MaximumDescription = 2000;

    public const int MaximumName = 64;

    public enum SendState
    {
        Idle,
        Sending,
        Sent,
        Failed,
    }

    public static SendState State { get; private set; } = SendState.Idle;

    public static string LastError { get; private set; } = string.Empty;

    public static void Reset()
    {
        State = SendState.Idle;
        LastError = string.Empty;
    }

    /// Posts the report.
    public static void Send(BugReport report)
    {
        if (State == SendState.Sending || string.IsNullOrWhiteSpace(report.Description))
            return;

        State = SendState.Sending;
        LastError = string.Empty;

        _ = Task.Run(async () =>
        {
            try
            {
                var json = JsonConvert.SerializeObject(report);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await Http
                    .PostAsync($"{RelayEndpoints.BaseUrl}/bugreport", content)
                    .ConfigureAwait(false);

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var ack = TryRead(body);
                    LastError = string.IsNullOrWhiteSpace(ack?.Error)
                        ? $"The relay returned {(int)response.StatusCode}."
                        : ack!.Error;

                    State = SendState.Failed;
                    return;
                }

                State = SendState.Sent;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                State = SendState.Failed;
                Plugin.Log.Error(ex, "[EchoGlam] Bug report failed to send");
            }
        });
    }

    private static BugReportAck? TryRead(string body)
    {
        try
        {
            return JsonConvert.DeserializeObject<BugReportAck>(body);
        }
        catch
        {
            return null;
        }
    }
}
