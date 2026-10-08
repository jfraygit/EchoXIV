using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using EchoMix.Plugin.Ipc;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;
using EchoMix.Plugin.UI.State;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Shell;

/// The 2.0 window: a persistent left nav rail, a status topbar, and a content area.
public sealed class EchoMixShellWindow : Window
{
    private readonly Plugin plugin;
    private readonly Screens.SettingsScreen settingsScreen;
    private readonly Screens.MixScreen mixScreen;
    private readonly Screens.BroadcastScreen broadcastScreen;
    private readonly Screens.ListenScreen listenScreen;
    private readonly Screens.LibraryScreen libraryScreen;
    private readonly Screens.BrowseScreen browseScreen;
    private readonly Screens.WelcomeScreen welcomeScreen;

    private static readonly Vector2 BaseSize = new(1072, 880);

    private static readonly Vector2 WelcomeSize = new(460, 380);

    private static readonly Vector2 MinimizedSize = new(220, 190);

    private Vector2 currentSize = BaseSize;

    private readonly IntroSequence intro = new();
    private readonly WelcomeToTwoScreen welcomeToTwo;

    /// True from the frame the window opens until the intro finishes.
    private bool introRunning;

    /// Tracks IsOpen so opening the window can restart the intro.
    private bool wasOpen;
    private Sty styleScope;

    private ShellDestination? pendingDestination;
    private float contentAlpha = 1f;

    /// Last frame's 1.0 view, so SyncDestinationFromView can tell an externally-driven navigation from one
    /// the shell itself just performed.
    private EchoMixView lastSeenView = EchoMixView.Deck;

    private float railMarkerOffset;
    private float railMarkerHeight;
    private bool railMarkerPlaced;

    private const float ReconnectedHoldSeconds = 4f;
    private AudioHostHealth lastEngineHealth = AudioHostHealth.Connected;
    private float reconnectedHold;

    /// Where the rail should point right now - the pending destination if a transition is in flight, so the
    /// highlight leads the content rather than lagging behind it.
    private ShellDestination SelectedDestination => pendingDestination ?? Router.Destination;

    /// Onboarding owns the whole window - no rail, no topbar.
    private bool IsWelcome => Router.CurrentView == EchoMixView.Welcome;

    private float Scale => Math.Clamp(plugin.Configuration.UiScale, 0.75f, 1.5f);
    private EchoMixRouter Router => plugin.Router;

    /// True when the current destination is a DJ-side one and the user picked Listener at Welcome.
    private bool IsListenerRole =>
        plugin.Configuration.LastChosenRole == UserRole.Listener
        && !plugin.AudioHostClient.LatestStatus.Broadcast.IsLive;

    public EchoMixShellWindow(Plugin plugin)
        : base("EchoMix###echomix-shell")
    {
        this.plugin = plugin;
        settingsScreen = new Screens.SettingsScreen(plugin);
        mixScreen = new Screens.MixScreen(plugin);
        broadcastScreen = new Screens.BroadcastScreen(plugin);
        listenScreen = new Screens.ListenScreen(plugin);
        libraryScreen = new Screens.LibraryScreen(plugin);
        browseScreen = new Screens.BrowseScreen(plugin);
        welcomeScreen = new Screens.WelcomeScreen(plugin);
        welcomeToTwo = new WelcomeToTwoScreen(plugin);
        Router.RailWidthCurrent = plugin.Configuration.ShellRailExpanded
            ? Metrics.RailWidth
            : Metrics.RailCollapsedWidth;
        Router.RailExpanded = plugin.Configuration.ShellRailExpanded;
    }

    /// Sizing and the whole-window style have to be set before Begin() renders the frame, same constraint
    /// DjDeckWindow.PreDraw documents: WindowBg/WindowRounding only affect the native frame if they're active
    /// before it's drawn, and SizeCondition.Always only takes effect every frame if Size is assigned every
    /// frame.
    public override void PreDraw()
    {
        Metrics.Scale = Scale * UiHelpers.GlobalFontScale;

        AmbientStyle.CaptureOnce();

        var padding = Router.IsMinimized && !IsWelcome ? AmbientStyle.WindowPadding : Vector2.Zero;
        styleScope = Sty.Shell().Var(ImGuiStyleVar.WindowPadding, padding);

        if (IsOpen && !wasOpen)
        {
            intro.Restart();
            introRunning = !Router.IsMinimized;
        }

        wasOpen = IsOpen;

        if (introRunning && intro.Finished)
            introRunning = false;

        var target = (introRunning ? WelcomeSize
            : IsWelcome ? WelcomeSize
            : Router.IsMinimized ? MinimizedSize
            : BaseSize) * Scale;
        currentSize = new Vector2(
            Motion.Approach(currentSize.X, target.X, Motion.SpeedSlow),
            Motion.Approach(currentSize.Y, target.Y, Motion.SpeedSlow));

        sizeSettled = Vector2.Distance(currentSize, target) <= 1f;

        Size = currentSize;
        SizeCondition = ImGuiCond.Always;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoCollapse;
        if (plugin.Configuration.IsWindowLocked)
            Flags |= ImGuiWindowFlags.NoMove;
    }

    public override void PostDraw() => styleScope.Dispose();

    private Vector2? expandedPosition;
    private Vector2? minimizedPosition;
    private bool wasMinimizedLastFrame;

    private Vector2? positionAnimTarget;

    private Vector2 positionAnimFrom;

    private bool sizeSettled = true;

    private bool awaitingToggleClickRelease;

    /// Remembers and restores the window's position per state, animating between the two.
    private void UpdateMinimizePosition()
    {
        var minimized = Router.IsMinimized;

        if (minimized != wasMinimizedLastFrame)
        {
            positionAnimTarget = minimized ? minimizedPosition : expandedPosition;
            positionAnimFrom = ImGui.GetWindowPos();
            wasMinimizedLastFrame = minimized;
            awaitingToggleClickRelease = true;

        }

        if (awaitingToggleClickRelease && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            awaitingToggleClickRelease = false;

        if (positionAnimTarget.HasValue && !awaitingToggleClickRelease && !plugin.Configuration.IsWindowLocked
            && ImGui.IsWindowFocused() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            positionAnimTarget = null;
        }

        if (positionAnimTarget is { } target)
        {
            positionAnimFrom = new Vector2(
                Motion.Approach(positionAnimFrom.X, target.X, Motion.SpeedSlow),
                Motion.Approach(positionAnimFrom.Y, target.Y, Motion.SpeedSlow));

            if (Vector2.Distance(positionAnimFrom, target) < 0.5f)
            {
                positionAnimFrom = target;
                ImGui.SetWindowPos(target);
                positionAnimTarget = null;
            }
            else
            {
                ImGui.SetWindowPos(Chrome.Snap(positionAnimFrom));
            }

            return;
        }

        if (!sizeSettled)
            return;

        var recorded = Chrome.Snap(ImGui.GetWindowPos());

        if (minimized)
            minimizedPosition = recorded;
        else
            expandedPosition = recorded;
    }

    /// Content first, then the border on top of everything.
    public override void Draw()
    {
        DrawBody();
        DrawFrame();
    }

    private void DrawBody()
    {
        plugin.DjDeckWindow.TickFrame();
        SyncDestinationFromView();

        UpdateMinimizePosition();

        plugin.DjDeckWindow.AdoptShellGeometry(ImGui.GetWindowPos(), ImGui.GetWindowSize());

        if (!Router.IsMinimized)
            plugin.DjDeckWindow.TickRelayResponses();

        if (introRunning)
        {
            intro.Draw(plugin.Fonts);
            return;
        }

        plugin.DjDeckWindow.DrawLegacyDialogs();

        listenScreen.DrawDialogs();
        libraryScreen.DrawDialogs();

        if (Router.IsMinimized && !IsWelcome)
        {
            plugin.DjDeckWindow.DrawLegacyMinimizedBody();
            return;
        }

        if (IsWelcome)
        {
            welcomeScreen.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            return;
        }

        KeepDestinationReachable();
        UpdateDestinationTransition();

        var railTarget = RailTargetWidth();
        Router.RailWidthCurrent = Motion.Approach(Router.RailWidthCurrent, railTarget, Motion.SpeedFast);

        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var railWidth = Router.RailWidthCurrent;

        DrawRail(windowPos, new Vector2(railWidth, windowSize.Y));

        ImGui.SetCursorScreenPos(windowPos + new Vector2(railWidth, 0f));
        DrawContentColumn(new Vector2(windowSize.X - railWidth, windowSize.Y));
    }

    /// Shared code can move 1.0's view out from under the shell - the Welcome cards choose a role,
    /// AudioHost's listen connection coming up forces the Listener view, the relay response pump navigates
    /// when a profile save resolves.
    private void SyncDestinationFromView()
    {
        if (Router.CurrentView == lastSeenView)
            return;

        lastSeenView = Router.CurrentView;

        if (Router.CurrentView == EchoMixView.Welcome)
            return;

        Router.Destination = ShellRoutes.DestinationFor(Router.CurrentView);
        pendingDestination = null;
        contentAlpha = 1f;
    }

    /// A listener who flips the toggle while the stored destination is a DJ-only one would land on a
    /// destination with no rail entry and no way back.
    private void KeepDestinationReachable()
    {
        if (!IsListenerRole)
            return;

        foreach (var item in ShellRoutes.Rail)
        {
            if (item.Destination != Router.Destination)
                continue;
            if (item.DjOnly)
                Router.Destination = ShellDestination.Listen;
            return;
        }
    }

    private float RailTargetWidth()
    {
        if (ShellRoutes.LegacyWantsFullWidth.Contains(Router.Destination))
            return Metrics.RailCollapsedWidth;

        return Router.RailExpanded ? Metrics.RailWidth : Metrics.RailCollapsedWidth;
    }

    /// EchoMix's signature cyan-to-orange gradient border, carried over from 1.0 - the window has no title
    /// bar and WindowBorderSize is off, so without it there's nothing separating the window from the game
    /// behind it, and it's a big part of what the plugin looks like.
    private void DrawFrame()
    {
        Chrome.DrawWindowBorder(
            ImGui.GetWindowDrawList(),
            ImGui.GetWindowPos(),
            ImGui.GetWindowSize(),
            ImGui.GetStyle().WindowRounding,
            3f * Scale);
    }

    private void DrawRail(Vector2 origin, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var collapsed = size.X < Metrics.RailWidth * 0.5f;

        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(Elevation.Surface),
            ImGui.GetStyle().WindowRounding, ImDrawFlags.RoundCornersLeft);
        drawList.AddLine(
            new Vector2(origin.X + size.X, origin.Y),
            new Vector2(origin.X + size.X, origin.Y + size.Y),
            ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);

        if (collapsed)
        {
            ImGui.SetCursorScreenPos(origin);
            if (ImGui.InvisibleButton("##railExpandStrip", new Vector2(MathF.Max(1f, size.X), size.Y)))
                SetRailExpanded(true);
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            DrawRailEdgeToggle(origin, size, collapsed: true);
            return;
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(Metrics.Lg, Metrics.Lg));

        using (Sty.New().Var(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            ImGui.BeginGroup();

            DrawWordmark(size.X - (Metrics.Lg * 2f));
            Surfaces.Gap(Metrics.Xl);

            var itemWidth = size.X - (Metrics.Lg * 2f);
            var firstRowTop = ImGui.GetCursorScreenPos().Y;
            var selectedTop = float.NaN;
            var selectedHeight = Metrics.RailItemHeight;

            drawList.ChannelsSplit(2);
            drawList.ChannelsSetCurrent(1);

            foreach (var item in ShellRoutes.Rail)
            {
                if (item.DjOnly && IsListenerRole)
                    continue;

                var rowTop = ImGui.GetCursorScreenPos().Y;
                DrawRailItem(item, itemWidth);

                if (item.Destination == SelectedDestination)
                {
                    selectedTop = rowTop;
                    selectedHeight = Metrics.RailItemHeight;
                }
            }

            drawList.ChannelsSetCurrent(0);

            if (!float.IsNaN(selectedTop))
            {
                var targetOffset = selectedTop - firstRowTop;
                if (!railMarkerPlaced)
                {
                    railMarkerOffset = targetOffset;
                    railMarkerHeight = selectedHeight;
                    railMarkerPlaced = true;
                }
                else
                {
                    railMarkerOffset = Motion.Approach(railMarkerOffset, targetOffset, Motion.SpeedFast);
                    railMarkerHeight = Motion.Approach(railMarkerHeight, selectedHeight, Motion.SpeedFast);
                }

                DrawRailMarker(
                    drawList,
                    new Vector2(origin.X + Metrics.Lg, firstRowTop + railMarkerOffset),
                    new Vector2(itemWidth, railMarkerHeight));
            }

            drawList.ChannelsMerge();

            ImGui.EndGroup();
        }

        DrawRailFooter(origin, size);
        DrawRailEdgeToggle(origin, size, collapsed: false);
    }

    /// The collapse/expand chevron, pinned to the rail's outer edge in BOTH states - and the only way to open
    /// or close the rail.
    private void DrawRailEdgeToggle(Vector2 origin, Vector2 size, bool collapsed)
    {
        var tabWidth = MathF.Round(14f * Metrics.Scale);
        var tabHeight = MathF.Round(46f * Metrics.Scale);
        var centreY = origin.Y + (size.Y * 0.5f);

        var min = Chrome.Snap(new Vector2(origin.X + size.X - (tabWidth * 0.5f), centreY - (tabHeight * 0.5f)));
        var max = min + new Vector2(tabWidth, tabHeight);

        ImGui.SetCursorScreenPos(min);
        if (ImGui.InvisibleButton("##railEdgeToggle", new Vector2(tabWidth, tabHeight)))
            SetRailExpanded(collapsed);

        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max,
            ImGui.GetColorU32(hovered ? Elevation.Overlay : Elevation.Raised), tabWidth * 0.5f);
        drawList.AddRect(min, max,
            ImGui.GetColorU32(hovered ? Semantic.Alpha(Semantic.Primary, 0.6f) : Elevation.Line),
            tabWidth * 0.5f, ImDrawFlags.None, Metrics.Hairline);

        var centre = Chrome.Snap(new Vector2(min.X + (tabWidth * 0.5f), centreY));
        var color = ImGui.GetColorU32(hovered ? Semantic.Primary : Semantic.TextSecondary);
        var h = MathF.Round(4f * Metrics.Scale);
        var w = MathF.Round(2.5f * Metrics.Scale);
        var dir = collapsed ? 1f : -1f;

        drawList.AddLine(centre + new Vector2(-w * dir, -h), centre + new Vector2(w * dir, 0f), color, Metrics.Hairline + 0.5f);
        drawList.AddLine(centre + new Vector2(w * dir, 0f), centre + new Vector2(-w * dir, h), color, Metrics.Hairline + 0.5f);

        Tip.Hovered(collapsed ? "Show Navigation" : "Hide Navigation",
            collapsed ? "Expand the sidebar." : "Collapse the sidebar to a thin strip.");
    }

    private void DrawWordmark(float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        using (TypeScale.Title())
        {
            Wordmark.Draw(drawList, pos);
            ImGui.Dummy(new Vector2(width, ImGui.GetTextLineHeight()));
        }
    }

    /// A badge on a rail row, for something waiting that the player has not seen.
    private void DrawRailBadge(
        ImDrawListPtr drawList, ShellRoutes.RailItem item, Vector2 iconCentre, Vector2 pos, Vector2 size, float width)
    {
        var badge = BadgeFor(item.Destination);
        if (!badge.Show)
            return;

        var pulse = 0.65f + (Motion.Pulse(0.8f) * 0.35f);
        var accent = Semantic.Alpha(Semantic.Warning, pulse);
        var count = badge.Count;

        if (!Router.RailExpanded || count <= 0)
        {
            var dot = Chrome.Snap(new Vector2(iconCentre.X + (Metrics.Md * 1.1f), iconCentre.Y - (Metrics.Md * 1.1f)));
            drawList.AddCircleFilled(dot, 4.5f * Metrics.Scale, ImGui.GetColorU32(Elevation.Surface));
            drawList.AddCircleFilled(dot, 3f * Metrics.Scale, ImGui.GetColorU32(accent));
            return;
        }

        using (TypeScale.Caption())
        {
            var label = count > 9 ? "9+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var textSize = ImGui.CalcTextSize(label);
            var height = MathF.Round(textSize.Y + Metrics.Xs);
            var pillWidth = MathF.Round(MathF.Max(height, textSize.X + Metrics.Md));
            var min = Chrome.Snap(new Vector2(
                pos.X + width - pillWidth - Metrics.Lg,
                pos.Y + ((size.Y - height) * 0.5f)));

            drawList.AddRectFilled(min, min + new Vector2(pillWidth, height),
                ImGui.GetColorU32(accent), Metrics.Pill(height));

            Chrome.Text(drawList,
                Chrome.CenterY(min.X + ((pillWidth - textSize.X) * 0.5f), min.Y, height, textSize.Y),
                ImGui.GetColorU32(Semantic.TextOnAccent), label);
        }
    }

    /// Whether a rail row has something unattended behind it, and how many if that is a sensible question.
    private readonly record struct RailBadge(bool Show, int Count);

    private RailBadge BadgeFor(ShellDestination destination)
    {
        if (destination == ShellDestination.Broadcast)
        {
            var pending = plugin.AudioHostClient.LatestPendingSongRequests.Count;
            return new RailBadge(pending > 0, pending);
        }

        if (destination == ShellDestination.Settings)
            return new RailBadge(ChangelogData.HasUnseen(plugin.Configuration), 0);

        return default;
    }

    private void DrawRailItem(ShellRoutes.RailItem item, float width)
    {
        var selected = Router.Destination == item.Destination;
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, Metrics.RailItemHeight);

        if (ImGui.InvisibleButton($"##rail_{item.Destination}", size))
            Navigate(item.Destination);

        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();

        if (hovered && !selected)
        {
            drawList.AddRectFilled(Chrome.Snap(pos), Chrome.Snap(pos + size),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.06f)), Metrics.RadiusSoft);
        }

        var iconColor = selected
            ? Semantic.Primary
            : hovered ? Semantic.TextPrimary : Semantic.TextSecondary;
        var textColor = selected
            ? Semantic.TextPrimary
            : hovered ? Semantic.TextPrimary : Semantic.TextSecondary;

        var iconCentre = Chrome.Snap(new Vector2(pos.X + Metrics.Xxl, pos.Y + (size.Y * 0.5f)));
        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, item.Icon, iconCentre, ImGui.GetColorU32(iconColor));

        DrawRailBadge(drawList, item, iconCentre, pos, size, width);

        using (TypeScale.Heading())
        {
            var textSize = ImGui.CalcTextSize(item.Label);
            Chrome.Text(
                drawList,
                Chrome.CenterY(pos.X + Metrics.Xxxl + Metrics.Md, pos.Y, size.Y, textSize.Y),
                ImGui.GetColorU32(textColor),
                item.Label);
        }

        ImGui.Dummy(new Vector2(0f, Metrics.Sm));
    }

    /// The sliding selection highlight: a tinted rounded rect with a leading accent bar running its full
    /// height.
    private static void DrawRailMarker(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
    {
        var min = Chrome.Snap(pos);
        var max = Chrome.Snap(pos + size);
        var rounding = Metrics.RadiusSoft;

        drawList.AddRectFilled(min, max,
            ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.16f)), rounding);

        var markerW = MathF.Round(4f * Metrics.Scale);
        drawList.AddRectFilled(
            min,
            new Vector2(min.X + markerW, max.Y),
            ImGui.GetColorU32(Semantic.Primary),
            rounding,
            ImDrawFlags.RoundCornersLeft);
    }

    /// Utility cluster pinned to the rail's bottom - the labelled home for things 1.0 hid behind unlabeled
    /// header icons.
    private void DrawRailFooter(Vector2 origin, Vector2 size)
    {
        var rowHeight = Metrics.RailItemHeight;
        var footerTop = origin.Y + size.Y - (rowHeight * 2f) - Metrics.Lg;

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddLine(
            new Vector2(origin.X + Metrics.Lg, footerTop - Metrics.Md),
            new Vector2(origin.X + size.X - Metrics.Lg, footerTop - Metrics.Md),
            ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + Metrics.Lg, footerTop));

        using var spacing = Sty.New().Var(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.BeginGroup();

        var width = size.X - (Metrics.Lg * 2f);

        var proximity = plugin.Configuration.IsProximityAudio;
        if (DrawFooterToggle("##shellProximity", FontAwesomeIcon.MapMarkerAlt, "Proximity", proximity, width,
                proximity
                    ? $"On. Listeners hear you positioned in the world, up to {plugin.Configuration.ProximityRange:0} yalms away. Right-click to change the range."
                    : "Off. Every listener hears you at full volume wherever they are. Right-click to set the range it will use.",
                out var proximityRightClicked))
        {
            plugin.Configuration.IsProximityAudio = !proximity;
            plugin.Configuration.Save();

            SendProximitySettings();
        }

        if (proximityRightClicked)
            ImGui.OpenPopup(ProximityRangePopupId);

        DrawProximityRangePopup();

        var locked = plugin.Configuration.IsWindowLocked;
        if (DrawFooterToggle("##shellLock", locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                locked ? "Locked" : "Unlocked", locked, width,
                locked
                    ? "The window is pinned and can't be dragged."
                    : "The window can be dragged. Lock it to avoid knocking it out of place mid-set.",
                out _))
        {
            plugin.Configuration.IsWindowLocked = !locked;
            plugin.Configuration.Save();
        }

        ImGui.EndGroup();
    }

    private const string ProximityRangePopupId = "##shellProximityRange";

    /// Set true while the range slider is being dragged, cleared when the mouse comes up - see
    /// DrawProximityRangePopup for why the send is deferred to that point.
    private bool proximityRangeDirty;

    /// How far your audio carries, on a right-click of the Proximity toggle.
    private void DrawProximityRangePopup()
    {
        var trackWidth = 150f * Metrics.Scale;
        var popupWidth = Fields.MeasureSliderRow("Range", trackWidth) + (Metrics.Md * 2f);
        ImGui.SetNextWindowSize(new Vector2(popupWidth, 0f));

        using var popupStyle = Sty.Popup()
            .Var(ImGuiStyleVar.WindowPadding, new Vector2(Metrics.Md, Metrics.Md))
            .Var(ImGuiStyleVar.ItemSpacing, new Vector2(Metrics.Md, Metrics.Xs));

        if (!ImGui.BeginPopup(ProximityRangePopupId, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            return;

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "How far your audio carries.");

        var range = plugin.Configuration.ProximityRange;
        if (Fields.Slider("##shellProximityRangeSlider", "Range", ref range, 5f, 100f, "{0:F0}y", 30f,
                trackWidth: trackWidth))
        {
            plugin.Configuration.ProximityRange = range;
            proximityRangeDirty = true;
        }

        if (proximityRangeDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            proximityRangeDirty = false;
            plugin.Configuration.Save();
            SendProximitySettings();
        }

        ImGui.EndPopup();
    }

    private void SendProximitySettings() =>
        plugin.AudioHostClient.Send(MessageType.SetProximityMode, new SetProximityModeCommand
        {
            IsProximityAudio = plugin.Configuration.IsProximityAudio,
            ProximityRange = plugin.Configuration.ProximityRange,
        });

    /// `rightClicked` is reported here rather than tested by the caller with BeginPopupContextItem, which
    /// keys off the LAST submitted item - and this method ends with a spacing Dummy, so by the time the
    /// caller got control the last item was the gap, not the toggle, and the context menu would never open.
    private bool DrawFooterToggle(
        string id,
        FontAwesomeIcon icon,
        string label,
        bool on,
        float width,
        string tooltip,
        out bool rightClicked)
    {
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, Metrics.RailItemHeight);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        if (hovered)
            drawList.AddRectFilled(pos, pos + size,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.06f)), Metrics.RadiusSoft);

        var iconColor = on ? Semantic.Primary : Semantic.TextSecondary;
        var iconCentre = Chrome.Snap(new Vector2(pos.X + Metrics.Xxl, pos.Y + (size.Y * 0.5f)));
        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, icon, iconCentre, ImGui.GetColorU32(iconColor));

        using (TypeScale.Body())
        {
            var textSize = ImGui.CalcTextSize(label);
            Chrome.Text(
                drawList,
                Chrome.CenterY(pos.X + Metrics.Xxxl + Metrics.Md, pos.Y, size.Y, textSize.Y),
                ImGui.GetColorU32(on ? Semantic.TextPrimary : Semantic.TextSecondary),
                label);
        }

        Tip.Hovered(label, tooltip, on ? Semantic.Primary : null);

        ImGui.Dummy(new Vector2(0f, Metrics.Sm));
        return clicked;
    }

    private void DrawContentColumn(Vector2 size)
    {
        var origin = ImGui.GetCursorScreenPos();

        DrawTopBar(origin, new Vector2(size.X, Metrics.TopBarHeight));

        var bodyOrigin = origin + new Vector2(0f, Metrics.TopBarHeight);
        var bodySize = new Vector2(size.X, size.Y - Metrics.TopBarHeight);

        ImGui.SetCursorScreenPos(bodyOrigin + new Vector2(Metrics.Xl, Metrics.Md));

        var inner = new Vector2(
            MathF.Max(1f, bodySize.X - (Metrics.Xl * 2f)),
            MathF.Max(1f, bodySize.Y - Metrics.Md - Metrics.Xl));

        using (AmbientStyle.Restore())
            ImGui.BeginChild("##shellBody", inner, false);

        using (Sty.New().Var(ImGuiStyleVar.Alpha, contentAlpha))
            DrawDestinationBody();

        ImGui.EndChild();
    }

    private void DrawTopBar(Vector2 origin, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddLine(
            new Vector2(origin.X, origin.Y + size.Y),
            new Vector2(origin.X + size.X, origin.Y + size.Y),
            ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);

        var centreY = origin.Y + (size.Y * 0.5f);
        var x = origin.X + Metrics.Xl;


        var title = DestinationTitle();
        using (TypeScale.Title())
        {
            var textSize = ImGui.CalcTextSize(title);
            Chrome.Text(drawList, Chrome.CenterY(x, centreY - (Metrics.TopBarHeight * 0.5f), Metrics.TopBarHeight, textSize.Y),
                ImGui.GetColorU32(Semantic.TextPrimary), title);
            x += textSize.X + Metrics.Lg;
        }

        DrawStatusCluster(drawList, origin, size, centreY);
    }

    private void DrawStatusCluster(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float centreY)
    {
        var status = plugin.AudioHostClient.LatestStatus.Broadcast;
        var right = origin.X + size.X - Metrics.Xl;

        var box = MathF.Round(Metrics.ControlMd);
        var boxTop = centreY - (box * 0.5f);

        right -= box;
        if (DrawTopBarIcon("##shellClose", FontAwesomeIcon.Times, new Vector2(right, boxTop),
                "Close", "Hides the EchoMix window. Playback and any live show keep running."))
            IsOpen = false;

        right -= box + Metrics.Sm;
        if (DrawTopBarIcon("##shellMinimize", FontAwesomeIcon.Compress, new Vector2(right, boxTop),
                Router.IsMinimized ? "Restore" : "Minimize",
                "Shrinks EchoMix to a small draggable visualizer you can leave on screen."))
            Router.IsMinimized = !Router.IsMinimized;

        right -= Metrics.Lg;

        var health = plugin.AudioHostClient.Health;
        TrackReconnect(health);

        if (health != AudioHostHealth.Connected)
        {
            DrawEngineOfflineBadge(drawList, ref right, centreY, health);
            return;
        }

        if (status.IsLive)
        {
            var listeners = status.ListenerCount;
            var text = listeners == 1 ? "1 listener" : $"{listeners} listeners";

            if (status.LiveSinceUtc is { } liveSince)
            {
                var elapsed = DateTime.UtcNow - liveSince;
                if (elapsed > TimeSpan.Zero)
                {
                    text = elapsed.TotalHours >= 1
                        ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m  ·  {text}"
                        : $"{elapsed.Minutes}m  ·  {text}";
                }
            }

            if (!string.IsNullOrEmpty(status.RoomCode))
                text = $"{status.RoomCode}  ·  {text}";

            using (TypeScale.Body())
            {
                var textSize = ImGui.CalcTextSize(text);
                right -= textSize.X;
                Chrome.Text(drawList, Chrome.Snap(new Vector2(right, centreY - (textSize.Y * 0.5f))),
                    ImGui.GetColorU32(Semantic.TextSecondary), text);
                right -= Metrics.Md;
            }

            var pulse = 0.55f + (Motion.Pulse(0.6f) * 0.45f);
            right -= Metrics.Md;
            drawList.AddCircleFilled(new Vector2(right, centreY), 4f * Metrics.Scale,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Live, pulse)));
            drawList.AddCircleFilled(new Vector2(right, centreY), 7f * Metrics.Scale,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Live, pulse * 0.18f)));

            using (TypeScale.Body())
            {
                const string live = "LIVE";
                var liveSize = ImGui.CalcTextSize(live);
                right -= liveSize.X + Metrics.Md;
                Chrome.Text(drawList, Chrome.Snap(new Vector2(right, centreY - (liveSize.Y * 0.5f))),
                    ImGui.GetColorU32(Semantic.Live), live);
            }
        }
        else if (status.IsListening)
        {
            var text = string.IsNullOrEmpty(status.HostDjName) ? "Listening" : $"Listening to {status.HostDjName}";
            using (TypeScale.Body())
            {
                var textSize = ImGui.CalcTextSize(text);
                right -= textSize.X;
                Chrome.Text(drawList, Chrome.Snap(new Vector2(right, centreY - (textSize.Y * 0.5f))),
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.DeckA, 0.9f)), text);
            }
        }

        DrawReconnectedBadge(drawList, ref right, centreY);
    }

    /// Notices the engine coming back and arms the green confirmation.
    private void TrackReconnect(AudioHostHealth health)
    {
        if (health == AudioHostHealth.Connected && lastEngineHealth != AudioHostHealth.Connected)
            reconnectedHold = ReconnectedHoldSeconds;

        lastEngineHealth = health;

        if (reconnectedHold > 0f)
            reconnectedHold = MathF.Max(0f, reconnectedHold - ImGui.GetIO().DeltaTime);
    }

    /// "AudioHost Reconnected", green, for a few seconds after the engine comes back.
    private void DrawReconnectedBadge(ImDrawListPtr drawList, ref float right, float centreY)
    {
        if (reconnectedHold <= 0f)
            return;

        var alpha = MathF.Min(1f, reconnectedHold);
        const string text = "AudioHost Reconnected";

        using (TypeScale.Body())
        {
            var textSize = ImGui.CalcTextSize(text);
            right -= textSize.X;
            Chrome.Text(drawList, Chrome.Snap(new Vector2(right, centreY - (textSize.Y * 0.5f))),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Success, alpha)), text);

            right -= Metrics.Md + (4f * Metrics.Scale);
            drawList.AddCircleFilled(new Vector2(right, centreY), 4f * Metrics.Scale,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Success, alpha)));

            right -= Metrics.Lg;
        }
    }

    /// "Audio engine offline", right-aligned, with the one thing a reader actually needs: that nothing they
    /// do will take effect until it reconnects, and that it is already trying.
    private void DrawEngineOfflineBadge(ImDrawListPtr drawList, ref float right, float centreY, AudioHostHealth health)
    {
        var notResponding = health == AudioHostHealth.NotResponding;
        var text = notResponding ? "AudioHost Not Responding" : "AudioHost Offline";

        using (TypeScale.Body())
        {
            var textSize = ImGui.CalcTextSize(text);
            right -= textSize.X;
            var textPos = Chrome.Snap(new Vector2(right, centreY - (textSize.Y * 0.5f)));
            Chrome.Text(drawList, textPos, ImGui.GetColorU32(Semantic.Danger), text);

            var pulse = 0.5f + (Motion.Pulse(0.6f) * 0.5f);
            right -= Metrics.Md + (4f * Metrics.Scale);
            drawList.AddCircleFilled(new Vector2(right, centreY), 4f * Metrics.Scale,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Danger, pulse)));

            var hitMin = new Vector2(right - (8f * Metrics.Scale), centreY - (Metrics.ControlMd * 0.5f));
            var hitMax = new Vector2(textPos.X + textSize.X, centreY + (Metrics.ControlMd * 0.5f));
            if (ImGui.IsMouseHoveringRect(hitMin, hitMax))
            {
                Tip.Show(text, notResponding
                    ? "The connection is open but AudioHost has stopped sending updates. Everything "
                      + "shown here is the last thing it reported. Restarting the game is the "
                      + "reliable fix."
                    : "EchoMix can't reach AudioHost, so nothing on these screens is live and no "
                      + "control will take effect. It retries automatically and normally recovers in "
                      + "a second or two. If it doesn't, end EchoMix.AudioHost.exe in Task Manager - "
                      + "a leftover one from an earlier session holds the connection open and blocks "
                      + "the new one.");
            }

            right -= Metrics.Md;
        }
    }

    /// An icon button in the topbar.
    private bool DrawTopBarIcon(string id, FontAwesomeIcon icon, Vector2 min, string tooltip, string? tooltipBody = null)
    {
        var box = MathF.Round(Metrics.ControlMd);
        min = Chrome.Snap(min);
        var max = min + new Vector2(box, box);
        var snappedCentre = Chrome.Snap(min + new Vector2(box * 0.5f, box * 0.5f));

        ImGui.SetCursorScreenPos(min);
        var clicked = ImGui.InvisibleButton(id, new Vector2(box, box));
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        if (hovered || held)
        {
            var fill = held
                ? Semantic.Alpha(Semantic.TextPrimary, 0.14f)
                : Semantic.Alpha(Semantic.TextPrimary, 0.08f);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), Metrics.RadiusSoft);
        }

        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, icon, snappedCentre,
                ImGui.GetColorU32(hovered ? Semantic.TextPrimary : Semantic.TextSecondary));

        Tip.Hovered(tooltip, tooltipBody);
        return clicked;
    }

    /// Follows the pending destination, not the committed one, so the title changes the instant the rail is
    /// clicked rather than a fade later.
    private string DestinationTitle()
    {
        foreach (var item in ShellRoutes.Rail)
        {
            if (item.Destination == SelectedDestination)
                return item.Label;
        }

        return "EchoMix";
    }

    private void DrawDestinationBody()
    {
        var contentOrigin = ImGui.GetCursorScreenPos();
        var contentSize = ImGui.GetContentRegionAvail();

        switch (Router.Destination)
        {
            case ShellDestination.Settings:
                settingsScreen.Draw();
                return;
            case ShellDestination.Mix:
                mixScreen.Draw();

                if (!plugin.Configuration.HasSeenTwoPointOhNote)
                    welcomeToTwo.Draw(contentOrigin, contentSize);

                return;
            case ShellDestination.Broadcast:
                broadcastScreen.Draw();
                return;
            case ShellDestination.Listen:
                listenScreen.Draw();
                return;
            case ShellDestination.Library:
                libraryScreen.Draw();
                return;
            case ShellDestination.Browse:
                browseScreen.Draw();
                return;
        }

        var isListening = plugin.AudioHostClient.LatestStatus.Broadcast.IsListening;
        var (view, settingsTab) = ShellRoutes.LegacyBodyFor(Router.Destination, isListening);
        plugin.DjDeckWindow.DrawLegacyBody(view, settingsTab);
    }

    /// Puts the window on the Mixer, for a guest DJ who has just taken a deck (see Plugin.OnFrameworkUpdate)
    /// and for /emix.
    public void ShowDecks() => ShowDestination(ShellDestination.Mix);

    /// /el's target.
    public void ShowListen() => ShowDestination(ShellDestination.Listen);

    private void ShowDestination(ShellDestination destination)
    {
        IsOpen = true;
        Router.IsMinimized = false;
        Navigate(destination);
    }

    /// Starts a transition toward `destination`.
    private void Navigate(ShellDestination destination)
    {
        if (destination == SelectedDestination)
            return;

        pendingDestination = destination;
    }

    /// Fade out, commit at zero, fade back in.
    private void UpdateDestinationTransition()
    {
        var dt = ImGui.GetIO().DeltaTime;

        if (pendingDestination is { } target)
        {
            contentAlpha -= dt / Motion.DurationFast;
            if (contentAlpha <= 0f)
            {
                contentAlpha = 0f;
                CommitDestination(target);
                pendingDestination = null;
            }
        }
        else if (contentAlpha < 1f)
        {
            contentAlpha = MathF.Min(1f, contentAlpha + (dt / Motion.DurationFast));
        }
    }

    private void CommitDestination(ShellDestination destination)
    {
        Router.Destination = destination;

        var isListening = plugin.AudioHostClient.LatestStatus.Broadcast.IsListening;
        var (view, _) = ShellRoutes.LegacyBodyFor(destination, isListening);
        Router.CurrentView = view;
        Router.PendingView = null;
        Router.ContentAlpha = 1f;

        lastSeenView = view;
    }

    /// Whether the nav rail is on screen and togglable right now.
    public bool CanToggleRail =>
        IsOpen && !Router.IsMinimized && !IsWelcome
        && !ShellRoutes.LegacyWantsFullWidth.Contains(Router.Destination);

    /// Collapse or expand the rail from outside the window - the keyboard shortcut's entry point.
    public void ToggleRail(bool expanded) => SetRailExpanded(expanded);

    /// Whether Up/Down should move a selection right now.
    public bool CanStepRail => CanToggleRail && (Router.RailExpanded || HasCategoryList);

    /// Whether the current destination has a category list for the arrows to walk.
    private bool HasCategoryList => Router.Destination switch
    {
        ShellDestination.Settings or ShellDestination.Broadcast or ShellDestination.Listen
            or ShellDestination.Library or ShellDestination.Browse => true,
        _ => false,
    };

    /// Moves whichever list the arrows currently own.
    public void StepRailSelection(int delta)
    {
        if (!Router.RailExpanded)
        {
            StepCategorySelection(delta);
            return;
        }

        StepRail(delta);
    }

    private void StepCategorySelection(int delta)
    {
        _ = Router.Destination switch
        {
            ShellDestination.Settings => settingsScreen.StepCategory(delta),
            ShellDestination.Broadcast => broadcastScreen.StepCategory(delta),
            ShellDestination.Listen => listenScreen.StepCategory(delta),
            ShellDestination.Library => libraryScreen.StepCategory(delta),
            ShellDestination.Browse => browseScreen.StepCategory(delta),
            _ => false,
        };
    }

    /// Moves the rail's selection one place up or down, skipping anything hidden for the current role and
    /// stopping at the ends rather than wrapping.
    private void StepRail(int delta)
    {
        var visible = new List<ShellDestination>(ShellRoutes.Rail.Length);
        foreach (var item in ShellRoutes.Rail)
        {
            if (item.DjOnly && IsListenerRole)
                continue;

            visible.Add(item.Destination);
        }

        if (visible.Count == 0)
            return;

        var current = visible.IndexOf(SelectedDestination);
        var next = current < 0
            ? 0
            : Math.Clamp(current + delta, 0, visible.Count - 1);

        if (next != current)
            Navigate(visible[next]);
    }

    private void SetRailExpanded(bool expanded)
    {
        Router.RailExpanded = expanded;
        plugin.Configuration.ShellRailExpanded = expanded;
        plugin.Configuration.Save();
    }
}
