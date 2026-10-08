using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using EchoMix.Plugin.UI.Controls;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI;

/// Shared shape for every brief, self-dismissing toast in the plugin - floats in a fixed screen corner
/// (configurable via Configuration.FollowToastAnchor, shared by every toast type despite the name - see that
/// property's own doc comment) regardless of where the main window is, fades itself in and back out on a
/// timer with no user action needed, and never steals focus (NoFocusOnAppearing) or blocks anything
/// underneath it.
public abstract class ToastWindow : Window
{
    private const float FadeInSeconds = 0.3f;
    private const float FadeOutSeconds = 0.6f;
    private const float ScreenMargin = 16f;

    private const float WidthUnscaled = 330f;
    private const float PadX = 14f;
    private const float PadY = 12f;
    private const float AccentBarWidth = 3f;
    private const float TitleGap = 3f;
    private const float BodyGap = 2f;
    private const float ActionGap = 10f;
    private const float ActionHeight = 28f;
    private const float StackGap = 8f;

    /// How far a toast travels as it fades in, unscaled.
    private const float SlideDistance = 14f;

    /// A representative unscaled footprint, for the Settings position preview.
    internal static readonly Vector2 NominalSize = new(WidthUnscaled, 96f);

    /// What a toast says.
    protected readonly record struct ToastContent(
        string Title,
        Vector4 Accent,
        string? Subject = null,
        string? Body = null,
        string? ActionLabel = null,
        FontAwesomeIcon? ActionIcon = null);

    /// Read once per frame in PreDraw, so a subclass can just return a record built from its own fields
    /// without caching anything itself.
    protected abstract ToastContent Content { get; }

    /// Called when the action button is clicked.
    protected virtual void OnAction()
    {
    }

    /// How long the toast stays fully visible before it starts fading out - overridable per toast type (a
    /// maintenance notice is worth holding onscreen longer than a "DJ went live" ping).
    protected virtual float HoldSeconds => 5f;

    /// Cap on wrapped body lines.
    protected virtual int BodyMaxLines => 3;

    private float TotalSeconds => FadeInSeconds + HoldSeconds + FadeOutSeconds;

    protected readonly Plugin plugin;
    private float elapsed;
    private float alpha;

    private ToastContent frameContent;
    private string? wrappedSubject;
    private string[] wrappedBody = Array.Empty<string>();
    private float stackOffset;
    private bool stacksUpward;
    private bool hovered;
    private bool dismissed;

    protected float Scale => Math.Clamp(plugin.Configuration.UiScale, 0.75f, 1.5f);

    private bool UseV2 => plugin.Configuration.UseNewDesign;

    protected ToastWindow(Plugin plugin, string windowId) : base(windowId)
    {
        this.plugin = plugin;
    }

    protected void ShowToast()
    {
        elapsed = 0f;
        hovered = false;
        dismissed = false;

        stackOffset = float.NaN;
        IsOpen = true;
    }

    /// Skips straight to the fade-out, as if the hold had just run out.
    protected void DismissNow()
    {
        dismissed = true;
        elapsed = MathF.Max(elapsed, FadeInSeconds + HoldSeconds);
    }

    public override void PreDraw()
    {
        elapsed += ImGui.GetIO().DeltaTime;

        if (hovered && !dismissed)
            elapsed = MathF.Min(elapsed, FadeInSeconds + HoldSeconds);

        if (elapsed >= TotalSeconds)
            IsOpen = false;

        alpha = Math.Clamp(
            elapsed < FadeInSeconds
                ? elapsed / FadeInSeconds
                : elapsed < FadeInSeconds + HoldSeconds
                    ? 1f
                    : 1f - ((elapsed - FadeInSeconds - HoldSeconds) / FadeOutSeconds),
            0f, 1f);

        Metrics.Scale = Scale;

        frameContent = Content;
        var size = MeasureAndCacheLayout();

        var viewport = ImGui.GetMainViewport();
        var basePos = ResolvePosition(plugin.Configuration, viewport.WorkPos, viewport.WorkSize, size);

        stacksUpward = basePos.Y + (size.Y / 2f) > viewport.WorkPos.Y + (viewport.WorkSize.Y / 2f);

        var slot = ToastStack.Claim(size.Y, StackGap * Scale);
        stackOffset = float.IsNaN(stackOffset) ? slot : Motion.Approach(stackOffset, slot, Motion.SpeedSlow);

        var travel = stackOffset;
        if (UseV2)
        {
            var entering = Math.Clamp(elapsed / FadeInSeconds, 0f, 1f);
            travel += (1f - Motion.EaseOutCubic(entering)) * SlideDistance * Scale;
        }

        basePos.Y += stacksUpward ? -travel : travel;

        Position = new Vector2(
            basePos.X,
            Math.Clamp(basePos.Y, viewport.WorkPos.Y, viewport.WorkPos.Y + MathF.Max(0f, viewport.WorkSize.Y - size.Y)));
        PositionCondition = ImGuiCond.Always;

        Size = size * UiHelpers.WindowSizeCompensation;
        SizeCondition = ImGuiCond.Always;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        var accent = frameContent.Accent;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, UseV2 ? Metrics.RadiusCard : 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, UseV2 ? Metrics.Hairline : 1.5f);
        ImGui.PushStyleColor(ImGuiCol.Border, Semantic.Alpha(accent, UseV2 ? 0.45f : 0.6f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, UseV2 ? Elevation.Overlay : Theme.Panel);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    /// Wraps the frame's content at font scale 1, caches the result for Draw, and returns the exact window
    /// size it needs.
    private Vector2 MeasureAndCacheLayout()
    {
        var inner = WidthUnscaled - (PadX * 2f);
        var lineHeight = ImGui.GetTextLineHeight();

        wrappedSubject = string.IsNullOrEmpty(frameContent.Subject)
            ? null
            : UiHelpers.TruncateToWidth(frameContent.Subject, inner);

        wrappedBody = string.IsNullOrEmpty(frameContent.Body)
            ? Array.Empty<string>()
            : UiHelpers.WrapToWidth(frameContent.Body, inner, BodyMaxLines);

        var height = (PadY * 2f) + lineHeight;

        if (wrappedSubject != null)
            height += TitleGap + lineHeight;

        if (wrappedBody.Length > 0)
            height += BodyGap + (wrappedBody.Length * lineHeight);

        if (frameContent.ActionLabel != null)
            height += ActionGap + ActionHeight;

        return new Vector2(WidthUnscaled, MathF.Round(height)) * Scale;
    }

    /// One layout for both looks, two palettes.
    public override void Draw()
    {
        ImGui.SetWindowFontScale(Scale);

        hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var accent = frameContent.Accent;
        var lineHeight = ImGui.GetTextLineHeight();

        if (UseV2)
            DrawV2Chrome(drawList, pos, size, accent);

        var x = pos.X + (PadX * Scale);
        var y = pos.Y + (PadY * Scale);

        Chrome.Text(drawList, new Vector2(x, y), ImGui.GetColorU32(accent), frameContent.Title);
        y += lineHeight;

        if (wrappedSubject != null)
        {
            y += TitleGap * Scale;
            Chrome.Text(drawList, new Vector2(x, y),
                ImGui.GetColorU32(UseV2 ? Semantic.TextPrimary : Theme.Text), wrappedSubject);
            y += lineHeight;
        }

        if (wrappedBody.Length > 0)
        {
            var bodyColor = ImGui.GetColorU32(
                UseV2 ? Semantic.TextTertiary : Semantic.Alpha(Theme.Text, 0.5f));

            y += BodyGap * Scale;
            foreach (var line in wrappedBody)
            {
                Chrome.Text(drawList, new Vector2(x, y), bodyColor, line);
                y += lineHeight;
            }
        }

        if (UseV2)
            DrawRemainingBar(drawList, pos, size, accent);

        if (frameContent.ActionLabel is not { } label)
            return;

        y += ActionGap * Scale;
        ImGui.SetCursorScreenPos(Chrome.Snap(new Vector2(x, y)));

        var buttonSize = new Vector2(size.X - (PadX * 2f * Scale), ActionHeight * Scale);
        var clicked = UseV2
            ? Fields.Button(label, Fields.ButtonStyle.Primary, buttonSize.X,
                icon: frameContent.ActionIcon, height: buttonSize.Y, idSuffix: WindowName)
            : PanelButton.Draw($"##toastAction{WindowName}", plugin.Fonts.Icon,
                frameContent.ActionIcon ?? FontAwesomeIcon.Check, label, buttonSize, accent);

        if (clicked)
            OnAction();
    }

    /// The 2.0 chrome: an accent bar on the leading edge - the same "this is an aside" mark the 2.0 note's
    /// callouts use - and the ramp's lit top edge.
    private void DrawV2Chrome(ImDrawListPtr drawList, Vector2 pos, Vector2 size, Vector4 accent)
    {
        var rounding = Metrics.RadiusCard;

        drawList.PushClipRect(pos, new Vector2(pos.X + (AccentBarWidth * Scale), pos.Y + size.Y), true);
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Semantic.Alpha(accent, 0.85f)), rounding);
        drawList.PopClipRect();

        drawList.PushClipRect(pos, new Vector2(pos.X + size.X, pos.Y + MathF.Max(1f, rounding)), true);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(Elevation.TopEdge), rounding, ImDrawFlags.None, 1f);
        drawList.PopClipRect();
    }

    /// A hairline at the base, full width when the toast arrives and empty by the time it goes, so one about
    /// to vanish says so instead of just disappearing.
    private void DrawRemainingBar(ImDrawListPtr drawList, Vector2 pos, Vector2 size, Vector4 accent)
    {
        var rounding = Metrics.RadiusCard;
        var remaining = Math.Clamp(1f - (elapsed / TotalSeconds), 0f, 1f);
        var thickness = MathF.Max(1f, 2f * Scale);
        var left = pos.X + rounding;
        var span = MathF.Max(0f, size.X - (rounding * 2f));
        var top = pos.Y + size.Y - thickness - MathF.Max(1f, Scale);

        drawList.AddRectFilled(
            Chrome.Snap(new Vector2(left, top)),
            Chrome.Snap(new Vector2(left + span, top + thickness)),
            ImGui.GetColorU32(Semantic.Alpha(accent, 0.12f)), Metrics.RadiusSharp);

        if (remaining <= 0.001f)
            return;

        drawList.AddRectFilled(
            Chrome.Snap(new Vector2(left, top)),
            Chrome.Snap(new Vector2(left + (span * remaining), top + thickness)),
            ImGui.GetColorU32(Semantic.Alpha(accent, hovered ? 0.85f : 0.55f)), Metrics.RadiusSharp);
    }

    /// Maps a 9-point anchor grid to an actual screen position for a `size`-sized window within the given
    /// viewport work area - see ToastAnchor's own doc comment for why this is a fixed grid rather than a
    /// free-dragged pixel position.
    internal static Vector2 ResolvePosition(Configuration configuration, Vector2 workPos, Vector2 workSize, Vector2 size)
    {
        if (!configuration.ToastUseCustomPosition)
            return AnchorPosition(configuration.FollowToastAnchor, workPos, workSize, size);

        var maxX = MathF.Max(0f, workSize.X - size.X);
        var maxY = MathF.Max(0f, workSize.Y - size.Y);

        return new Vector2(
            workPos.X + Math.Clamp(configuration.ToastCustomX * workSize.X, 0f, maxX),
            workPos.Y + Math.Clamp(configuration.ToastCustomY * workSize.Y, 0f, maxY));
    }

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
