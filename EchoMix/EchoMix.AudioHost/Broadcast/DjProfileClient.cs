using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoMix.Shared;
using Newtonsoft.Json;

namespace EchoMix.AudioHost.Broadcast;

/// One-shot relay round trips for DJ List profiles - list/get/save/delete/report all mirror
/// PublicShowsClient/BugReportClient's dial-send-read-one-reply-disconnect shape exactly (factored into one
/// shared RoundTripAsync helper here since there are five near-identical ones).
public static class DjProfileClient
{
    private const int ImageChunkSize = 64 * 1024;

    public static Task<(bool Success, string? Error, DjProfilesSnapshotMessage? Result)> RequestProfilesAsync(RequestDjProfilesMessage request) =>
        RoundTripAsync<DjProfilesSnapshotMessage>(RelayMessageType.RequestDjProfiles, request, RelayMessageType.DjProfilesSnapshot);

    public static Task<(bool Success, string? Error, DjProfileDetailSnapshotMessage? Result)> GetDetailAsync(GetDjProfileDetailMessage request) =>
        RoundTripAsync<DjProfileDetailSnapshotMessage>(RelayMessageType.GetDjProfileDetail, request, RelayMessageType.DjProfileDetailSnapshot);

    public static Task<(bool Success, string? Error, DjProfileSaveResultMessage? Result)> SaveAsync(SaveDjProfileMessage request) =>
        RoundTripAsync<DjProfileSaveResultMessage>(RelayMessageType.SaveDjProfile, request, RelayMessageType.DjProfileSaveResult);

    public static Task<(bool Success, string? Error, DjProfileDeleteResultMessage? Result)> DeleteAsync(DeleteDjProfileMessage request) =>
        RoundTripAsync<DjProfileDeleteResultMessage>(RelayMessageType.DeleteDjProfile, request, RelayMessageType.DjProfileDeleteResult);

    public static Task<(bool Success, string? Error, DjProfileLikeResultMessage? Result)> ToggleLikeAsync(ToggleDjProfileLikeMessage request) =>
        RoundTripAsync<DjProfileLikeResultMessage>(RelayMessageType.ToggleDjProfileLike, request, RelayMessageType.DjProfileLikeResult);

    public static Task<(bool Success, string? Error, DjProfileFollowResultMessage? Result)> ToggleFollowAsync(ToggleDjProfileFollowMessage request) =>
        RoundTripAsync<DjProfileFollowResultMessage>(RelayMessageType.ToggleDjProfileFollow, request, RelayMessageType.DjProfileFollowResult);

    public static Task<(bool Success, string? Error, DjProfileReportAckMessage? Result)> ReportAsync(SubmitDjProfileReportMessage request) =>
        RoundTripAsync<DjProfileReportAckMessage>(RelayMessageType.SubmitDjProfileReport, request, RelayMessageType.DjProfileReportAck);

    public static Task<(bool Success, string? Error, ProfileLinkCodeResultMessage? Result)> GenerateLinkCodeAsync(GenerateProfileLinkCodeMessage request) =>
        RoundTripAsync<ProfileLinkCodeResultMessage>(RelayMessageType.GenerateProfileLinkCode, request, RelayMessageType.ProfileLinkCodeResult);

    public static Task<(bool Success, string? Error, ProfileLinkRedeemResultMessage? Result)> RedeemLinkCodeAsync(RedeemProfileLinkCodeMessage request) =>
        RoundTripAsync<ProfileLinkRedeemResultMessage>(RelayMessageType.RedeemProfileLinkCode, request, RelayMessageType.ProfileLinkRedeemResult);

    public static Task<(bool Success, string? Error, ProfileUnlinkResultMessage? Result)> UnlinkCharacterAsync(UnlinkProfileCharacterMessage request) =>
        RoundTripAsync<ProfileUnlinkResultMessage>(RelayMessageType.UnlinkProfileCharacter, request, RelayMessageType.ProfileUnlinkResult);

    public static async Task<(bool Success, string? Error)> UploadImageAsync(string profileId, string characterName, string slot, string filePath)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }

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

            var requestId = Guid.NewGuid();
            var totalChunks = Math.Max(1, (int)Math.Ceiling(bytes.Length / (double)ImageChunkSize));

            for (var i = 0; i < totalChunks; i++)
            {
                var offset = i * ImageChunkSize;
                var length = Math.Min(ImageChunkSize, bytes.Length - offset);
                var chunkBytes = new byte[length];
                Array.Copy(bytes, offset, chunkBytes, 0, length);

                var chunk = new DjProfileImageChunkMessage
                {
                    ProfileId = profileId,
                    CharacterName = characterName,
                    Slot = slot,
                    RequestId = requestId,
                    ChunkIndex = i,
                    TotalChunks = totalChunks,
                    DataBase64 = Convert.ToBase64String(chunkBytes),
                };

                var frameBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(RelayEnvelope.For(RelayMessageType.DjProfileImageChunk, chunk)));
                await FrameIO.WriteFrameAsync(ssl, FrameIO.ControlFrame, frameBytes);
            }

            var frame = await FrameIO.ReadFrameAsync(ssl, timeoutCts.Token);
            if (frame == null || frame.Value.Type != FrameIO.ControlFrame)
                return (false, "Relay closed the connection before responding.");

            var envelope = JsonConvert.DeserializeObject<RelayEnvelope>(Encoding.UTF8.GetString(frame.Value.Payload));
            if (envelope?.Type != RelayMessageType.DjProfileImageAck)
                return (false, "Relay sent an unexpected response.");

            var ack = envelope.ReadPayload<DjProfileImageAckMessage>();
            return (ack.Success, ack.Error);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<(bool Success, string? Error, T? Result)> RoundTripAsync<T>(string requestType, object request, string expectedResponseType) where T : class
    {
        try
        {
            using var tcpClient = new TcpClient { NoDelay = true };
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await tcpClient.ConnectAsync(RelayConfig.DefaultHost, RelayConfig.DefaultPort, timeoutCts.Token);

            using var ssl = new SslStream(tcpClient.GetStream(), false, (_, cert, _, _) => RelayTls.ValidatePinnedCertificate(cert));
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = RelayConfig.DefaultHost,
                EnabledSslProtocols = SslProtocols.None,
            }, timeoutCts.Token);

            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(RelayEnvelope.For(requestType, request)));
            await FrameIO.WriteFrameAsync(ssl, FrameIO.ControlFrame, bytes);

            var frame = await FrameIO.ReadFrameAsync(ssl, timeoutCts.Token);
            if (frame == null || frame.Value.Type != FrameIO.ControlFrame)
                return (false, "Relay closed the connection before responding.", null);

            var envelope = JsonConvert.DeserializeObject<RelayEnvelope>(Encoding.UTF8.GetString(frame.Value.Payload));
            if (envelope?.Type != expectedResponseType)
                return (false, "Relay sent an unexpected response.", null);

            return (true, null, envelope.ReadPayload<T>());
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }
}
