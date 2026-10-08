using Dalamud.Interface;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI;

/// A brief "DJ went live" toast for a followed profile - see RelayProtocol.FollowedDjWentLiveMessage's own
/// doc comment for what actually triggers this.
public sealed class FollowNotificationToast : ToastWindow
{
    private string djName = string.Empty;
    private string roomCode = string.Empty;

    protected override ToastContent Content => new(
        "Now Live",
        Theme.CyanAccent,
        Subject: djName,
        Body: "just started a show!",
        ActionLabel: "Join",
        ActionIcon: FontAwesomeIcon.SignInAlt);

    public FollowNotificationToast(Plugin plugin) : base(plugin, "###echomix-followtoast")
    {
    }

    public void Show(string djName, string roomCode)
    {
        this.djName = djName;
        this.roomCode = roomCode;
        ShowToast();
    }

    protected override void OnAction()
    {
        if (!plugin.Configuration.ListenerAutoJoinNearbyShows)
        {
            plugin.AudioHostClient.Send(MessageType.ConnectToRemote, new ConnectToRemoteCommand
            {
                RoomCode = roomCode,
                Password = string.Empty,
                CharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
            });
        }

        DismissNow();
    }
}
