using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI;

/// A brief "so-and-so joined your show" toast for the host, gated by Configuration.NotifyOnListenerJoin - see
/// Plugin.OnFrameworkUpdate for how a join is detected (diffed against the last-seen listener roster, since
/// ListenerRosterChangedMessage only ever carries the full current roster rather than a discrete join/leave
/// event).
public sealed class ListenerJoinedToast : ToastWindow
{
    private string characterName = string.Empty;

    protected override Vector2 ToastSize => new(300f, 90f);

    public ListenerJoinedToast(Plugin plugin) : base(plugin, "###echomix-listenerjoinedtoast")
    {
    }

    public void Show(string characterName)
    {
        this.characterName = characterName;
        ShowToast();
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Scale);
        var colorCount = Theme.Push();

        ImGui.TextColored(Theme.CyanAccent, "Listener Joined");
        ImGui.TextColored(Theme.Text, UiHelpers.TruncateToWidth(characterName, ImGui.GetContentRegionAvail().X));
        ImGui.TextDisabled("joined your show!");

        Theme.Pop(colorCount);
    }
}
