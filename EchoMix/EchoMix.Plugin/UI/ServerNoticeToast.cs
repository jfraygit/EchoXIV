using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Controls;

namespace EchoMix.Plugin.UI;

/// A brief relay maintenance notice (see RelayServer.BroadcastServerNoticeToEveryoneAsync) as a toast instead
/// of the old full-window takeover, which forced the main window open and hijacked whatever view/minimized
/// state the DJ was in - a small, self-fading toast is a much less disruptive way to say "the relay's about
/// to restart." Held onscreen longer than FollowNotificationToast's default (a maintenance warning is worth
/// more of a DJ's attention than a "so-and-so went live" ping), but still dismisses itself automatically
/// either way - see ToastWindow for the shared fade/position/chrome behavior.
public sealed class ServerNoticeToast : ToastWindow
{
    private string? lastShownNotice;
    private string noticeText = string.Empty;

    protected override float HoldSeconds => 10f;
    protected override Vector2 ToastSize => new(340f, 130f);
    protected override Vector4 BorderAccent => Theme.OrangeAccent;

    public ServerNoticeToast(Plugin plugin) : base(plugin, "###echomix-noticetoast")
    {
    }

    /// No-ops for a blank/already-shown notice - lets a caller just pass whatever the latest status snapshot
    /// says every frame (same idiom FollowNotificationToast.Show's own caller in Plugin.OnFrameworkUpdate
    /// uses) without needing to track "is this new" itself.
    public void ShowIfNew(string? notice)
    {
        if (string.IsNullOrEmpty(notice) || notice == lastShownNotice)
            return;

        lastShownNotice = notice;
        noticeText = notice;
        ShowToast();
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Scale);
        var colorCount = Theme.Push();

        ImGui.TextColored(Theme.OrangeAccent, "Notice");
        ImGui.TextWrapped(noticeText);
        ImGui.Spacing();

        var buttonSize = new Vector2(ImGui.GetContentRegionAvail().X, 26f * Scale);
        if (PanelButton.Draw("##dismissServerNotice", plugin.Fonts.Icon, FontAwesomeIcon.Check, "Got It", buttonSize, Theme.OrangeAccent))
            DismissNow();

        Theme.Pop(colorCount);
    }
}
