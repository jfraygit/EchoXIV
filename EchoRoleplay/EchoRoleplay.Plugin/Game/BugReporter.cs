using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using EchoRoleplay.Shared;
using Newtonsoft.Json;

namespace EchoRoleplay.Game;





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
    public static void Send(BugReport report, string configDirectory)
    {
        if (State == SendState.Sending || string.IsNullOrWhiteSpace(report.Description))
            return;

        State = SendState.Sending;
        LastError = string.Empty;

        var webhook = DevWebhook.Url(configDirectory);

        _ = Task.Run(async () =>
        {
            try
            {
                State = webhook.Length > 0
                    ? await PostToWebhook(report, webhook).ConfigureAwait(false)
                    : await PostToRelay(report).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                State = SendState.Failed;
                Plugin.Log.Error(ex, "[EchoRoleplay] Bug report failed to send");
            }
        });
    }

    private static async Task<SendState> PostToRelay(BugReport report)
    {
        var json = JsonConvert.SerializeObject(report);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Http
            .PostAsync($"{RelayEndpoints.BaseUrl}/bugreport", content)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return SendState.Sent;

        var ack = TryRead(body);

        LastError = string.IsNullOrWhiteSpace(ack?.Error)
            ? $"The relay returned {(int)response.StatusCode}."
            : ack!.Error;

        return SendState.Failed;
    }

    /// Development only.
    private static async Task<SendState> PostToWebhook(BugReport report, string webhook)
    {
        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title = "Bug report",
                    color = 0x7C8AF7,
                    fields = new[]
                    {
                        new { name = "What happened", value = Field(report.Description), inline = false },
                        new { name = "From", value = Field(report.Name), inline = true },
                        new { name = "Version", value = Field(report.Version), inline = true },
                        new { name = "Relay", value = Field(report.Relay), inline = true },
                        new { name = "Tab", value = Field(report.Tab), inline = true },
                        new { name = "UI scale", value = $"{report.Scale:0.00}x", inline = true },
                    },
                },
            },
        };

        var json = JsonConvert.SerializeObject(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Http.PostAsync(webhook, content).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return SendState.Sent;

        LastError = $"The webhook returned {(int)response.StatusCode}.";
        return SendState.Failed;
    }

    /// Discord rejects an embed field with an empty value, which would fail the whole post over an optional
    /// box somebody left blank.
    private static string Field(string value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Length <= 1000 ? value : value[..1000] + "...";

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
