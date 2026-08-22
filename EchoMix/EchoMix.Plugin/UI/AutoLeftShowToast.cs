using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI;

/// A brief "auto-left a show" toast for the listener - the mirror of AutoJoinedShowToast, fired specifically
/// when AutoJoinTracker's own Leave decision (ran out of range, not a manual Disconnect click) is actually
/// confirmed disconnected.
public sealed class AutoLeftShowToast : ToastWindow
{
    private string djName = string.Empty;

    protected override Vector2 ToastSize => new(260f, 76f);

    protected override Vector4 BorderAccent => Theme.OrangeAccent;

    public AutoLeftShowToast(Plugin plugin) : base(plugin, "###echomix-autolefttoast")
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

        ImGui.TextColored(Theme.OrangeAccent, "Auto-Left (Beta)");
        ImGui.TextColored(Theme.Text, UiHelpers.TruncateToWidth(djName, ImGui.GetContentRegionAvail().X));
        ImGui.TextDisabled("Left Show");

        Theme.Pop(colorCount);
    }
}
