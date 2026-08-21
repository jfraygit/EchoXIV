using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace EchoNav;

/// What gets sent.
public sealed record BugReport
{
    /// Which plugin this came from.
    public string Plugin { get; init; } = "EchoNav";

    /// Optional, and free text.
    public string Name { get; init; } = string.Empty;

    public required string Description { get; init; }

    public string Version { get; init; } = string.Empty;
    public uint Territory { get; init; }
    public string MeshState { get; init; } = string.Empty;
    public string JourneyStage { get; init; } = string.Empty;
    public string MovementStatus { get; init; } = string.Empty;
    public bool UsingHook { get; init; }
    public bool UsingLiveCamera { get; init; }
    public bool Calibrated { get; init; }
}

public sealed record BugReportAck
{
    public string Error { get; init; } = string.Empty;
}

/// Sends a user-written bug report to the project's Discord channel, by way of the relay.
public static class BugReporter
{
    public const string DiscordInvite = "https://discord.gg/nJauXrNWx3";

    /// Matches EchoSim's arrangement: one route per plugin under the same host.
    private const string RelayBaseUrl = "https://echoxiv.com/api/echonav";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

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

                var response = await Http.PostAsync($"{RelayBaseUrl}/bugreport", content).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    State = SendState.Failed;

                    var ack = TryRead(body);
                    LastError = string.IsNullOrWhiteSpace(ack?.Error)
                        ? $"The relay returned {(int)response.StatusCode}."
                        : ack!.Error;

                    return;
                }

                State = SendState.Sent;
            }
            catch (Exception ex)
            {
                State = SendState.Failed;
                LastError = ex.Message;
                Plugin.Log.Error(ex, "[EchoNav] Bug report failed to send");
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
