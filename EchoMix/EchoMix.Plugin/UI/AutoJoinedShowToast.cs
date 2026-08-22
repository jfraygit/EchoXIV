using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI;

/// A brief "auto-joined a nearby show" toast for the listener - fires once AutoJoinTracker's own
/// Join/SwitchTo decision is actually confirmed connected (see Plugin.OnFrameworkUpdate's
/// pendingAutoJoinToastRoomCode), never for a session silently adopted from an already-manual join.
public sealed class AutoJoinedShowToast : ToastWindow
{
    private string djName = string.Empty;

    protected override Vector2 ToastSize => new(260f, 76f);

    public AutoJoinedShowToast(Plugin plugin) : base(plugin, "###echomix-autojoinedtoast")
    {
    }

    public void Show(string djName)
    {
        this.djName = djName;
        ShowToast();
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Scale);
        var colorCount = Theme.Push();

        ImGui.TextColored(Theme.CyanAccent, "Auto-Joined (Beta)");
        ImGui.TextColored(Theme.Text, UiHelpers.TruncateToWidth(djName, ImGui.GetContentRegionAvail().X));
        ImGui.TextDisabled("Joined Show");

        Theme.Pop(colorCount);
    }
}
