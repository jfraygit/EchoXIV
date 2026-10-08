namespace EchoMix.Plugin.UI;

/// A brief "so-and-so joined your show" toast for the host, gated by Configuration.NotifyOnListenerJoin - see
/// Plugin.OnFrameworkUpdate for how a join is detected (diffed against the last-seen listener roster, since
/// ListenerRosterChangedMessage only ever carries the full current roster rather than a discrete join/leave
/// event).
public sealed class ListenerJoinedToast : ToastWindow
{
    private string characterName = string.Empty;

    protected override ToastContent Content => new(
        "Listener Joined",
        Theme.CyanAccent,
        Subject: characterName,
        Body: "joined your show!");

    public ListenerJoinedToast(Plugin plugin) : base(plugin, "###echomix-listenerjoinedtoast")
    {
    }

    public void Show(string characterName)
    {
        this.characterName = characterName;
        ShowToast();
    }
}
