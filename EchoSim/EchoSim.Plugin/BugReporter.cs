using System.Net.Http;
using System.Text;
using EchoSim.Game;
using EchoSim.Shared;
using Newtonsoft.Json;

namespace EchoSim;

/// Sends a user-written bug report to the project's Discord channel, by way of the relay.
public static class BugReporter
{
    public const string DiscordInvite = "https://discord.gg/nJauXrNWx3";

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

                var response = await Http
                    .PostAsync($"{RelayEndpoints.BaseUrl}/bugreport", content)
                    .ConfigureAwait(false);

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
                Plugin.Log.Error(ex, "Bug report failed to send");
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
