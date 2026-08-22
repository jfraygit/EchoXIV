using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using EchoRoleplay.Shared;
using Newtonsoft.Json;

namespace EchoRoleplay.Game;






/// Sends a report about somebody's profile.
public static class ProfileReporter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public const int MaximumDetail = RelayLimits.ReportDetail;

    /// How much of a profile is attached as evidence.
    private const int MaximumSnapshot = RelayLimits.ReportSnapshot;

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

    public static void Send(ProfileReport report, string configDirectory)
    {
        if (State == SendState.Sending)
            return;

        State = SendState.Sending;
        LastError = string.Empty;

        var webhook = DevWebhook.Url(configDirectory);

        _ = Task.Run(async () =>
        {
            try
            {
                var ok = webhook.Length > 0
                    ? await PostToWebhook(report, webhook).ConfigureAwait(false)
                    : await PostToRelay(report).ConfigureAwait(false);

                State = ok ? SendState.Sent : SendState.Failed;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                State = SendState.Failed;
                Plugin.Log.Error(ex, "[EchoRoleplay] Report failed to send");
            }
        });
    }

    private static async Task<bool> PostToRelay(ProfileReport report)
    {
        var json = JsonConvert.SerializeObject(report);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Http.PostAsync($"{RelayEndpoints.BaseUrl}/report", content).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return true;

        LastError = $"The relay returned {(int)response.StatusCode}.";
        return false;
    }

    /// Development only - see DevWebhook.
    private static async Task<bool> PostToWebhook(ProfileReport report, string webhook)
    {
        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title = $"Profile reported: {report.Reason}",

                    color = 0xE05555,
                    fields = new[]
                    {
                        new { name = "Subject", value = Field(report.Subject), inline = true },
                        new { name = "Reporter", value = Field(report.Reporter), inline = true },
                        new { name = "Detail", value = Field(report.Detail), inline = false },
                        new { name = "Profile", value = Field(Clip(report.Snapshot, 1000)), inline = false },
                        new { name = "Version", value = Field(report.Version), inline = true },
                    },
                },
            },
        };

        var json = JsonConvert.SerializeObject(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Http.PostAsync(webhook, content).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return true;

        LastError = $"The webhook returned {(int)response.StatusCode}.";
        return false;
    }

    /// Discord rejects an embed field with an empty value, which would fail the whole post over an optional
    /// box somebody left blank.
    private static string Field(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string Clip(string value, int limit) =>
        value.Length <= limit ? value : value[..limit] + "...";

    /// The profile's own words, flattened for a report.
    public static string Snapshot(RoleplayProfile profile)
    {
        var text = new StringBuilder();

        void Add(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || text.Length >= MaximumSnapshot)
                return;

            text.Append(label).Append(": ").Append(value.Trim()).Append('\n');
        }

        Add("Name", profile.Name);
        Add("Nickname", profile.Nickname);
        Add("House", profile.HouseName);
        Add("Title", profile.Title);
        Add("Pronouns", profile.Pronouns);
        Add("Race", profile.Race);
        Add("Age", profile.Age);
        Add("Occupation", profile.Occupation);
        Add("Appearance", profile.Appearance);
        Add("Personality", profile.Personality);
        Add("Backstory", profile.Backstory);
        Add("Rumours", profile.Rumours);
        Add("Hooks", profile.Hooks);

        foreach (var field in profile.CustomFields)
            Add(field.Label, field.Value);

        foreach (var status in profile.Statuses)
            Add($"Status '{status.Label}'", status.Detail);

        return Clip(text.ToString().TrimEnd(), MaximumSnapshot);
    }
}
