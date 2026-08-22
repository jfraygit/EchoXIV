using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Controls;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI;

/// A brief "DJ went live" toast for a followed profile - see RelayProtocol.FollowedDjWentLiveMessage's own
/// doc comment for what actually triggers this.
public sealed class FollowNotificationToast : ToastWindow
{
    private string djName = string.Empty;
    private string roomCode = string.Empty;

    protected override Vector2 ToastSize => new(300f, 112f);

    public FollowNotificationToast(Plugin plugin) : base(plugin, "###echomix-followtoast")
    {
    }

    public void Show(string djName, string roomCode)
    {
        this.djName = djName;
        this.roomCode = roomCode;
        ShowToast();
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Scale);
        var colorCount = Theme.Push();

        ImGui.TextColored(Theme.CyanAccent, "Now Live");
        ImGui.TextColored(Theme.Text, UiHelpers.TruncateToWidth(djName, ImGui.GetContentRegionAvail().X));
        ImGui.TextDisabled("just started a show!");
        ImGui.Spacing();

        var buttonSize = new Vector2(ImGui.GetContentRegionAvail().X, 28f * Scale);
        if (PanelButton.Draw("##joinFollowedShow", plugin.Fonts.Icon, FontAwesomeIcon.SignInAlt, "Join", buttonSize, Theme.OrangeAccent))
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

        Theme.Pop(colorCount);
    }
}
