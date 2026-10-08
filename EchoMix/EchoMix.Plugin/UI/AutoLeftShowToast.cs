namespace EchoMix.Plugin.UI;

/// A brief "auto-left a show" toast for the listener - the mirror of AutoJoinedShowToast, fired specifically
/// when AutoJoinTracker's own Leave decision (ran out of range, not a manual Disconnect click) is actually
/// confirmed disconnected.
public sealed class AutoLeftShowToast : ToastWindow
{
    private string djName = string.Empty;

    protected override ToastContent Content => new(
        "Auto-Left (Beta)",
        Theme.OrangeAccent,
        Subject: djName,
        Body: "Left Show");

    public AutoLeftShowToast(Plugin plugin) : base(plugin, "###echomix-autolefttoast")
    {
    }

    public void Show(string djName)
    {
        this.djName = djName;
        ShowToast();
    }
}
