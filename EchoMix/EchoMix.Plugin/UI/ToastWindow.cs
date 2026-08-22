using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace EchoMix.Plugin.UI;

/// Shared shape for every brief, self-dismissing toast in the plugin (see FollowNotificationToast,
/// ServerNoticeToast) - floats in a fixed screen corner (configurable via Configuration.FollowToastAnchor,
/// shared by every toast type despite the name - see that property's own doc comment) regardless of where the
/// main window is, fades itself in and back out on a timer with no user action needed, and never steals focus
/// (NoFocusOnAppearing) or blocks anything underneath it.
public abstract class ToastWindow : Window
{
    private const float FadeInSeconds = 0.3f;
    private const float FadeOutSeconds = 0.6f;
    private const float ScreenMargin = 16f;

    /// How long the toast stays fully visible before it starts fading out - overridable per toast type (a
    /// maintenance notice is worth holding onscreen longer than a "DJ went live" ping).
    protected virtual float HoldSeconds => 5f;

    /// Unscaled toast size - multiplied by Scale in PreDraw the same way every other sizing constant in this
    /// plugin is.
    protected abstract Vector2 ToastSize { get; }

    /// Tinges the border/comet - defaults to the same cyan the Follow toast uses; ServerNoticeToast overrides
    /// this to orange to match the relay-maintenance-notice color used elsewhere (e.g.
    protected virtual Vector4 BorderAccent => Theme.CyanAccent;

    private float TotalSeconds => FadeInSeconds + HoldSeconds + FadeOutSeconds;

    protected readonly Plugin plugin;
    private float elapsed;
    private float alpha;

    protected float Scale => Math.Clamp(plugin.Configuration.UiScale, 0.75f, 1.5f);

    protected ToastWindow(Plugin plugin, string windowId) : base(windowId)
    {
        this.plugin = plugin;
    }

    protected void ShowToast()
    {
        elapsed = 0f;
        IsOpen = true;
    }

    /// Same effect as timing out naturally - PreDraw closes the window next frame once elapsed clears
    /// TotalSeconds.
    protected void DismissNow() => elapsed = TotalSeconds;

    public override void PreDraw()
    {
        elapsed += ImGui.GetIO().DeltaTime;
        if (elapsed >= TotalSeconds)
            IsOpen = false;

        alpha = Math.Clamp(
            elapsed < FadeInSeconds
                ? elapsed / FadeInSeconds
                : elapsed < FadeInSeconds + HoldSeconds
                    ? 1f
                    : 1f - ((elapsed - FadeInSeconds - HoldSeconds) / FadeOutSeconds),
            0f, 1f);

        var viewport = ImGui.GetMainViewport();
        var size = ToastSize * Scale;
        Position = AnchorPosition(plugin.Configuration.FollowToastAnchor, viewport.WorkPos, viewport.WorkSize, size);
        PositionCondition = ImGuiCond.Always;
        Size = size * UiHelpers.WindowSizeCompensation;
        SizeCondition = ImGuiCond.Always;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.5f);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(BorderAccent.X, BorderAccent.Y, BorderAccent.Z, 0.6f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.Panel);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    /// Maps a 9-point anchor grid to an actual screen position for a `size`-sized window within the given
    /// viewport work area - see ToastAnchor's own doc comment for why this is a fixed grid rather than a
    /// free-dragged pixel position.
    private static Vector2 AnchorPosition(ToastAnchor anchor, Vector2 workPos, Vector2 workSize, Vector2 size)
    {
        var x = anchor switch
        {
            ToastAnchor.TopLeft or ToastAnchor.MiddleLeft or ToastAnchor.BottomLeft => workPos.X + ScreenMargin,
            ToastAnchor.TopCenter or ToastAnchor.MiddleCenter or ToastAnchor.BottomCenter => workPos.X + ((workSize.X - size.X) / 2f),
            _ => workPos.X + workSize.X - size.X - ScreenMargin,
        };

        var y = anchor switch
        {
            ToastAnchor.TopLeft or ToastAnchor.TopCenter or ToastAnchor.TopRight => workPos.Y + ScreenMargin,
            ToastAnchor.MiddleLeft or ToastAnchor.MiddleCenter or ToastAnchor.MiddleRight => workPos.Y + ((workSize.Y - size.Y) / 2f),
            _ => workPos.Y + workSize.Y - size.Y - ScreenMargin,
        };

        return new Vector2(x, y);
    }
}
