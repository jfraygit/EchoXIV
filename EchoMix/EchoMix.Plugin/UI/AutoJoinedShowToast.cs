namespace EchoMix.Plugin.UI;

/// A brief "auto-joined a nearby show" toast for the listener - fires once AutoJoinTracker's own
/// Join/SwitchTo decision is actually confirmed connected (see Plugin.OnFrameworkUpdate's
/// pendingAutoJoinToastRoomCode), never for a session silently adopted from an already-manual join.
public sealed class AutoJoinedShowToast : ToastWindow
{
    private string djName = string.Empty;

    protected override ToastContent Content => new(
        "Auto-Joined (Beta)",
        Theme.CyanAccent,
        Subject: djName,
        Body: "Joined Show");

    public AutoJoinedShowToast(Plugin plugin) : base(plugin, "###echomix-autojoinedtoast")
    {
    }

    public void Show(string djName)
    {
        this.djName = djName;
        ShowToast();
    }
}
