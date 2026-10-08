using Dalamud.Interface;

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

    /// The one toast whose body is free text written by whoever sent the notice, so it gets the most room.
    protected override int BodyMaxLines => 6;

    protected override ToastContent Content => new(
        "Notice",
        Theme.OrangeAccent,
        Body: noticeText,
        ActionLabel: "Got It",
        ActionIcon: FontAwesomeIcon.Check);

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

    /// Shows a notice unconditionally, for Settings' test button.
    internal void ShowTestNotice(string notice)
    {
        lastShownNotice = notice;
        noticeText = notice;
        ShowToast();
    }

    protected override void OnAction() => DismissNow();
}
