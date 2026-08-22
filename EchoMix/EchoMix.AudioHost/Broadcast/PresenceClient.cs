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

/// A standing, otherwise-idle connection to the relay whose only purpose is giving FollowedDjWentLive
/// somewhere to be pushed to (see RelayServer's presenceConnections and
/// RelayProtocol.RegisterPresenceMessage's own doc comment) - unlike everything else in
/// EchoMix.AudioHost.Broadcast, this isn't tied to actually broadcasting or listening to a show, and it
/// self-heals its own connection with no external driver needed (IpcServer just calls Start() once and
/// DrainNotification() every status tick - it never has to babysit reconnection the way the plugin's own
/// EnsureAudioHostConnectedAsync does for the local pipe).
public sealed class PresenceClient : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);

    private readonly string characterName;
    private readonly CancellationTokenSource cts = new();
    private readonly Task runTask;
    private volatile FollowedDjWentLiveMessage? latestNotification;

    public PresenceClient(string characterName)
    {
        this.characterName = characterName;
        runTask = Task.Run(() => RunAsync(cts.Token));
    }

    /// Returns (and clears) the most recent notification received since the last drain, or null if nothing
    /// new has arrived - same dirty-flag-by-another-name idiom as pendingSongRequests elsewhere in IpcServer,
    /// just for a single most-recent value rather than a list (missing an intermediate one if two arrive
    /// between drains is an acceptable trade for how rare that would be in practice).
    public FollowedDjWentLiveMessage? DrainNotification() => Interlocked.Exchange(ref latestNotification, null);

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var tcpClient = new TcpClient { NoDelay = true };
                await tcpClient.ConnectAsync(RelayConfig.DefaultHost, RelayConfig.DefaultPort, token);

                using var ssl = new SslStream(tcpClient.GetStream(), false, (_, cert, _, _) => RelayTls.ValidatePinnedCertificate(cert));
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = RelayConfig.DefaultHost,
                    EnabledSslProtocols = SslProtocols.None,
                }, token);

                var registerBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                    RelayEnvelope.For(RelayMessageType.RegisterPresence, new RegisterPresenceMessage { CharacterName = characterName })));
                await FrameIO.WriteFrameAsync(ssl, FrameIO.ControlFrame, registerBytes, token);

                while (!token.IsCancellationRequested)
                {
                    var frame = await FrameIO.ReadFrameAsync(ssl, token);
                    if (frame == null || frame.Value.Type != FrameIO.ControlFrame)
                        break;

                    var envelope = JsonConvert.DeserializeObject<RelayEnvelope>(Encoding.UTF8.GetString(frame.Value.Payload));
                    if (envelope?.Type == RelayMessageType.FollowedDjWentLive)
                        latestNotification = envelope.ReadPayload<FollowedDjWentLiveMessage>();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }

            try
            {
                await Task.Delay(ReconnectDelay, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        try
        {
            await runTask;
        }
        catch
        {
        }

        cts.Dispose();
    }
}
