using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI;

/// Tells a host DJ their broadcast connection dropped and is auto-reconnecting (or how that attempt
/// resolved), as a toast rather than text buried in the Broadcast tab's GO LIVE panel - a DJ mixing from the
/// Deck view would otherwise never see it at all.
public sealed class BroadcastReconnectToast : ToastWindow
{
    private string title = string.Empty;
    private string message = string.Empty;
    private Vector4 accent = Theme.OrangeAccent;

    protected override float HoldSeconds => 10f;
    protected override Vector2 ToastSize => new(320f, 90f);
    protected override Vector4 BorderAccent => accent;

    public BroadcastReconnectToast(Plugin plugin) : base(plugin, "###echomix-broadcastreconnecttoast")
    {
    }

    public void ShowDropped(string? roomCode)
    {
        title = "Connection Dropped";
        message = string.IsNullOrEmpty(roomCode)
            ? "Your connection to the relay dropped - trying to reconnect automatically..."
            : $"Your connection to the relay dropped - trying to get room {roomCode} back automatically...";
        accent = Theme.OrangeAccent;
        ShowToast();
    }

    public void ShowRecovered(string? roomCode)
    {
        title = "Back Live";
        message = string.IsNullOrEmpty(roomCode)
            ? "Reconnected - your show is back live."
            : $"Reconnected - room {roomCode} is back live.";
        accent = Theme.CyanAccent;
        ShowToast();
    }

    public void ShowGaveUp(string? error)
    {
        title = "Couldn't Reconnect";
        message = string.IsNullOrEmpty(error)
            ? "Gave up trying to reconnect - go live again to restart your show."
            : error;
        accent = Theme.OrangeAccent;
        ShowToast();
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Scale);
        var colorCount = Theme.Push();

        ImGui.TextColored(accent, title);
        ImGui.TextWrapped(message);

        Theme.Pop(colorCount);
    }
}
