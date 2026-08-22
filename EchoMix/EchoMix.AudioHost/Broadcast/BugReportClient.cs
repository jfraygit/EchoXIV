using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoMix.Shared;
using Newtonsoft.Json;

namespace EchoMix.AudioHost.Broadcast;

/// One-shot: dials the relay, submits a bug report, and disconnects - doesn't register as a host or join a
/// room, since a report might come from a DJ who isn't currently live.
public static class BugReportClient
{
    public static async Task<(bool Success, string? Error)> SubmitAsync(SubmitBugReportMessage report)
    {
        try
        {
            using var tcpClient = new TcpClient { NoDelay = true };
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await tcpClient.ConnectAsync(RelayConfig.DefaultHost, RelayConfig.DefaultPort, timeoutCts.Token);

            using var ssl = new SslStream(tcpClient.GetStream(), false, (_, cert, _, _) => RelayTls.ValidatePinnedCertificate(cert));
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = RelayConfig.DefaultHost,
                EnabledSslProtocols = SslProtocols.None,
            }, timeoutCts.Token);

            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(RelayEnvelope.For(RelayMessageType.SubmitBugReport, report)));
            await FrameIO.WriteFrameAsync(ssl, FrameIO.ControlFrame, bytes);

            var frame = await FrameIO.ReadFrameAsync(ssl, timeoutCts.Token);
            if (frame == null || frame.Value.Type != FrameIO.ControlFrame)
                return (false, "Relay closed the connection before responding.");

            var envelope = JsonConvert.DeserializeObject<RelayEnvelope>(Encoding.UTF8.GetString(frame.Value.Payload));
            if (envelope?.Type != RelayMessageType.BugReportAck)
                return (false, "Relay sent an unexpected response.");

            var ack = envelope.ReadPayload<BugReportAckMessage>();
            return (ack.Accepted, ack.Error);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
