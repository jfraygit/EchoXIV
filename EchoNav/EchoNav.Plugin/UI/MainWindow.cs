using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using EchoNav.Game;
using EchoNav.Nav;
using EchoNav.UI.Controls;

namespace EchoNav.UI;

/// What EchoNav is for: the things worth going to right now, and one click to go.
public sealed class MainWindow : Window
{
    private enum ViewMode
    {
        Main,
        Settings,
        NorthHorn,
        Changelog,
    }

    private readonly Plugin plugin;
    private readonly EncounterArt art;

    private ViewMode currentView = ViewMode.Main;
    private int themeColors;

    /// Which card is lit as the destination, by NavTarget.Key.
    private string? activeKey;

    /// Stand in for a target key on the two things that have no encounter behind them.
    private const string BaseCampKey = "basecamp";
    private const string PotKey = "pots";

    public MainWindow(Plugin plugin, EncounterArt art)
        : base("EchoNav###EchoNavMain", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin;
        this.art = art;
        SizeCondition = ImGuiCond.Always;

        minimised = plugin.Configuration.StartMinimised;
    }

    private Configuration Config => plugin.Configuration;

    /// Side length of the collapsed box.
    private const float MinimisedSize = 54f;

    /// How fast the box grows and shrinks.
    private const float ResizeLerpSpeed = 16f;

    private bool minimised;

    /// The size actually being rendered, eased toward the target rather than snapping.
    private Vector2 animatedSize;

    private Vector2 animatedPos;
    private Vector2 targetPos;

    /// True while the window is travelling between its two modes.
    private bool animatingPosition;

    private Vector2? minimisedPressAt;

    /// Full content only appears once the window is nearly the size it is heading for.
    private bool FullyExpanded => !minimised && animatedSize.X >= TargetSize.X * 0.92f;

    /// Room the settings need to stop reading as a column of cramped rows.
    private const float SettingsWidth = 520f;
    private const float SettingsHeight = 660f;

    /// The size the window is easing toward.
    private Vector2 TargetSize => minimised
        ? new Vector2(MinimisedSize, MinimisedSize)
        : currentView != ViewMode.Main
            ? new Vector2(MathF.Max(Config.WindowWidth, SettingsWidth), MathF.Max(Config.WindowHeight, SettingsHeight))
            : new Vector2(Config.WindowWidth, Config.WindowHeight);

    public override void PreDraw()
    {
        var target = TargetSize;

        if (animatedSize == Vector2.Zero)
            animatedSize = target;

        var dt = ImGui.GetIO().DeltaTime;
        animatedSize = new Vector2(
            UiHelpers.Lerp(animatedSize.X, target.X, ResizeLerpSpeed, dt),
            UiHelpers.Lerp(animatedSize.Y, target.Y, ResizeLerpSpeed, dt));

        if (Vector2.Distance(animatedSize, target) < 1f)
            animatedSize = target;

        Size = animatedSize;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar;

        if (Config.WindowLocked && !minimised)
            Flags |= ImGuiWindowFlags.NoMove;

        if (!FullyExpanded)
            Flags |= ImGuiWindowFlags.NoBackground;

        ApplySavedPosition();
        themeColors = Theme.Push();
    }

    /// Switches mode, starting an animation from wherever the window is now to wherever that mode was last
    /// left.
    private void SetMinimised(bool value)
    {
        if (minimised == value)
            return;

        RememberPosition();

        animatedPos = ImGui.GetWindowPos();
        minimised = value;
        Config.StartMinimised = value;

        var savedX = value ? Config.MinimisedX : Config.FullWindowX;
        var savedY = value ? Config.MinimisedY : Config.FullWindowY;

        targetPos = savedX > Configuration.UnsetPosition && savedY > Configuration.UnsetPosition
            ? new Vector2(savedX, savedY)
            : animatedPos;

        animatingPosition = true;
        Config.Save();
    }

    /// Eases the window toward the position belonging to the mode it's switching into.
    private void ApplySavedPosition()
    {
        if (!animatingPosition)
        {
            Position = null;
            return;
        }

        var dt = ImGui.GetIO().DeltaTime;
        animatedPos = new Vector2(
            UiHelpers.Lerp(animatedPos.X, targetPos.X, ResizeLerpSpeed, dt),
            UiHelpers.Lerp(animatedPos.Y, targetPos.Y, ResizeLerpSpeed, dt));

        Position = animatedPos;
        PositionCondition = ImGuiCond.Always;

        var settled = Vector2.Distance(animatedPos, targetPos) < 1f && animatedSize == TargetSize;

        if (settled)
        {
            animatedPos = targetPos;
            Position = animatedPos;
            animatingPosition = false;
        }
    }

    /// Records where the window currently sits, against whichever mode is showing.
    private void RememberPosition()
    {
        if (animatingPosition)
            return;

        var pos = ImGui.GetWindowPos();

        if (minimised)
        {
            Config.MinimisedX = pos.X;
            Config.MinimisedY = pos.Y;
        }
        else
        {
            Config.FullWindowX = pos.X;
            Config.FullWindowY = pos.Y;
        }
    }

    public override void PostDraw() => Theme.Pop(themeColors);

    public override void Draw()
    {
        RememberPosition();

        if (!plugin.Journey.IsRunning && !plugin.MovementDriver.IsRunning)
            activeKey = null;

        if (!FullyExpanded)
        {
            DrawMinimised();
            return;
        }

        DrawWindowBorder();
        DrawChrome();

        if (currentView != ViewMode.Main)
        {
            DrawSettingsFrame();
            return;
        }

        var snapshot = plugin.Snapshot;

        if (!SupportedZones.Supports(snapshot.TerritoryType))
        {
            DrawUnsupportedZone();
            return;
        }

        var journeyHeight = plugin.Journey.Stage == JourneyStage.Idle && !plugin.MovementDriver.IsRunning
            ? 0f
            : 78f * UiHelpers.Scale;

        if (ImGui.BeginChild("##targets", new Vector2(0, -journeyHeight), false))
            DrawTargets(snapshot);

        ImGui.EndChild();

        if (journeyHeight > 0f)
            DrawJourney();
    }

    /// The collapsed box: a flag glyph in a rounded, accent-ringed square that can be parked anywhere and
    /// clicked to bring the window back.
    private void DrawMinimised()
    {
        var scale = UiHelpers.Scale;
        var inset = 1.5f * scale;

        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var min = origin + new Vector2(inset, inset);
        var max = origin + size - new Vector2(inset, inset);
        var drawList = ImGui.GetWindowDrawList();

        drawList.PushClipRectFullScreen();

        var hovered = ImGui.IsWindowHovered() && minimised;

        var body = Theme.Tinted(hovered ? 0.22f : 0.10f);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(body), 8f * scale);

        var span = MathF.Max(1f, (Config.WindowWidth - MinimisedSize) * scale);
        var collapsed = Math.Clamp(1f - ((size.X - (MinimisedSize * scale)) / span), 0f, 1f);

        using (plugin.Fonts.Icon.PushSafe())
        {
            var glyph = FontAwesomeIcon.Flag.ToIconString();
            var font = ImGui.GetFont();
            var glyphSize = 26f * scale;

            var measured = ImGui.CalcTextSize(glyph) * (glyphSize / ImGui.GetFontSize());
            var centre = (min + max) / 2f;
            var colour = new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, collapsed);
            drawList.AddText(font, glyphSize, centre - (measured / 2f), ImGui.GetColorU32(colour), glyph, 0f);
        }

        drawList.AddRect(min, max,
            ImGui.GetColorU32(new Vector4(
                Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, (hovered ? 1f : 0.85f) * collapsed)),
            8f * scale, ImDrawFlags.None, 2f * scale);

        drawList.PopClipRect();

        if (hovered)
            ImGui.SetTooltip("EchoNav - click to expand, drag to move");

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            minimisedPressAt = ImGui.GetMousePos();

        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            return;

        if (minimisedPressAt is { } pressed && Vector2.Distance(pressed, ImGui.GetMousePos()) < 4f && hovered)
            SetMinimised(false);

        minimisedPressAt = null;
    }

    /// An accent border around the whole window, matching the collapsed box and the rest of the suite.
    private static void DrawWindowBorder()
    {
        var scale = UiHelpers.Scale;
        var inset = 1.5f * scale;

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos() + new Vector2(inset, inset);
        var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(inset, inset);

        drawList.PushClipRectFullScreen();
        drawList.AddRect(min, max,
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.75f)),
            8f * scale, ImDrawFlags.None, 2f * scale);
        drawList.PopClipRect();
    }

    /// Custom title bar: the wordmark, with the chrome controls paired at the right.
    private void DrawChrome()
    {
        var scale = UiHelpers.Scale;
        var buttonSize = 24f * scale;
        var buttonGap = 6f * scale;

        const int buttonCount = 4;

        var rowTop = ImGui.GetCursorPosY();
        var startX = ImGui.GetCursorPosX();
        var width = ImGui.GetContentRegionAvail().X;

        var chromeWidth = (buttonSize * buttonCount) + (buttonGap * (buttonCount - 1)) + (12f * scale);

        float titleHeight;
        float wordmarkLeft;
        using (plugin.Fonts.Header.PushSafe())
            titleHeight = UiHelpers.DrawWordmark("ECHONAV", startX, rowTop, width, chromeWidth, out wordmarkLeft);

        DrawPotTimer(startX, rowTop, titleHeight, wordmarkLeft);

        var inSettings = currentView != ViewMode.Main;

        ImGui.SetCursorPosY(rowTop);
        ImGui.SetCursorPosX(startX + width - (buttonSize * buttonCount) - (buttonGap * (buttonCount - 1)));

        var lockIcon = Config.WindowLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
        if (EchoButton.BareIcon("##lock", plugin.Fonts.Icon, lockIcon, buttonSize,
                Config.WindowLocked ? "Unlock window position" : "Lock window position", Config.WindowLocked))
        {
            Config.WindowLocked = !Config.WindowLocked;
            Config.Save();
        }

        ImGui.SameLine(0f, buttonGap);
        ImGui.SetCursorPosY(rowTop);
        if (EchoButton.BareIcon("##settings", plugin.Fonts.Icon, FontAwesomeIcon.Cog, buttonSize,
                inSettings ? "Back" : "Settings", inSettings))
            currentView = inSettings ? ViewMode.Main : ViewMode.Settings;

        if (HasUnreadChangelog)
            DrawUnreadDot();

        ImGui.SameLine(0f, buttonGap);
        ImGui.SetCursorPosY(rowTop);
        if (EchoButton.BareIcon("##minimise", plugin.Fonts.Icon, FontAwesomeIcon.Compress, buttonSize, "Minimise"))
            SetMinimised(true);

        ImGui.SameLine(0f, buttonGap);
        ImGui.SetCursorPosY(rowTop);
        if (EchoButton.BareIcon("##close", plugin.Fonts.Icon, FontAwesomeIcon.Times, buttonSize, "Close"))
            IsOpen = false;

        ImGui.SetCursorPosY(rowTop + MathF.Max(buttonSize, titleHeight));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    /// The pot cycle, in the corner the wordmark leaves free.
    private void DrawPotTimer(float startX, float rowTop, float titleHeight, float wordmarkLeft)
    {
        if (!SupportedZones.Supports(plugin.Snapshot.TerritoryType))
            return;

        var pots = plugin.Snapshot.Pots;

        ImGui.SetCursorPos(new Vector2(startX, rowTop));
        var origin = ImGui.GetCursorScreenPos();

        var scale = UiHelpers.Scale;
        var available = wordmarkLeft - origin.X - (10f * scale);
        if (available < 40f * scale)
            return;

        string label;
        if (!pots.Known)
            label = "Pots: not seen yet";
        else if (string.IsNullOrEmpty(pots.NextSide))
            label = $"Pots: next in {Clock(pots.SecondsUntilNext)}";
        else
            label = $"Pots: {pots.NextSide} in {Clock(pots.SecondsUntilNext)}";

        var colour = !pots.Known ? Theme.TextDisabled : pots.IsUp ? Theme.Accent : Theme.TextDim;

        var lineHeight = ImGui.GetTextLineHeight();
        var y = origin.Y + ((titleHeight - lineHeight) / 2f);
        var text = UiHelpers.Truncate(label, available);

        var travellable = pots.NextPosition is not null;

        if (travellable && activeKey == PotKey)
            colour = Theme.Accent;

        ImGui.GetWindowDrawList().AddText(new Vector2(origin.X, y), ImGui.GetColorU32(colour), text);

        var min = new Vector2(origin.X, y);
        var max = min + new Vector2(ImGui.CalcTextSize(text).X, lineHeight);
        var hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max);

        if (hovered)
        {
            UiHelpers.WrappedTooltip(pots.Detail);

            if (travellable)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                ImGui.GetWindowDrawList().AddLine(
                    new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), ImGui.GetColorU32(colour), 1f * scale);
            }
        }

        if (!travellable)
            return;

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            potPressAt = ImGui.GetMousePos();

        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            return;

        if (potPressAt is { } pressed && hovered && Vector2.Distance(pressed, ImGui.GetMousePos()) < 4f)
            GoToNextPot(pots);

        potPressAt = null;
    }

    private Vector2? potPressAt;

    /// Heads for where the next pot will be, before it is there.
    private void GoToNextPot(PotForecast pots)
    {
        if (pots.NextPosition is not { } destination)
            return;

        activeKey = PotKey;
        var plan = plugin.PlanTravel(destination);
        plugin.Journey.Start(
            plan, 20f,
            string.IsNullOrEmpty(pots.NextSide) ? "the next pots" : $"{pots.NextSide} pots",
            plugin.PlanRoute);
    }

    private static string Clock(long seconds) => $"{seconds / 60}:{seconds % 60:D2}";

    /// Says plainly that nothing is going to happen here, rather than showing an empty list that reads as
    /// "nothing is running" and leaves someone waiting for a card to appear.
    private void DrawUnsupportedZone()
    {
        VSpace(12f);
        Theme.SectionHeader("not here");
        ImGui.Spacing();

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(Theme.TextDim,
            $"EchoNav works in {SupportedZones.SupportedName}. Everything it does - the shipped map, " +
            "the shard network, Occult Return - is built and tested for that zone, so outside it the " +
            "plugin stays out of the way entirely.");
        ImGui.PopTextWrapPos();
    }

    private void DrawTargets(NavSnapshot snapshot)
    {
        var live = snapshot.AllTargets.Where(target => !target.IsDormant).ToList();

        var tower = live.FirstOrDefault(target => target.IsForkedTower);

        var ordinary = live
            .Where(target => !target.IsForkedTower)
            .OrderBy(target => plugin.TargetOrder.RankOf(target))
            .ToList();

        if (tower != null)
            DrawTargetCard(tower, snapshot);

        foreach (var target in ordinary)
            DrawTargetCard(target, snapshot);

        if (ordinary.Count == 0)
        {
            VSpace(4f);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Theme.TextDim, snapshot.MeshState != NavMeshState.Ready
                ? "Getting the map ready..."
                : tower != null
                    ? "No other CE/Fates available currently - the tower is entered from its pad."
                    : "No CE/Fates available currently.");
            ImGui.PopTextWrapPos();
            VSpace(6f);
        }

        if (ordinary.Any(target => target.Kind == NavTargetKind.Fate))
            return;

        if (ordinary.Count > 0)
            VSpace(2f);

        DrawBaseCampCard(snapshot);
    }

    private void DrawTargetCard(NavTarget target, NavSnapshot snapshot)
    {
        if (TargetCard.Draw(
                $"##card{target.Key}", target, Distance(snapshot, target), art, activeKey == target.Key))
            Go(target);

        VSpace(5f);
    }

    /// Offered when there is nothing to go to, because that is exactly when you want to be somewhere else.
    private void DrawBaseCampCard(NavSnapshot snapshot)
    {
        if (snapshot.MeshState != NavMeshState.Ready)
            return;

        if (snapshot.BaseCamp is not { } baseCamp)
            return;

        var distance = snapshot.HasPlayer ? Vector3.Distance(snapshot.PlayerPosition, baseCamp) : 0f;
        var detail = snapshot.ReturnAvailable ? "Occult Return is up" : "On foot - Return is on cooldown";

        if (!TargetCard.DrawAction(
                $"##{BaseCampKey}", "Return to Base Camp", detail, $"{distance:F0}y",
                FontAwesomeIcon.Campground, plugin.Fonts.Icon,
                activeKey == BaseCampKey ? Theme.Accent : Theme.UtilityHue,
                activeKey == BaseCampKey))
            return;

        activeKey = BaseCampKey;
        var plan = plugin.PlanTravel(baseCamp);
        plugin.Journey.Start(
            plan, 20f,
            string.IsNullOrEmpty(snapshot.BaseCampName) ? "base camp" : snapshot.BaseCampName,
            plugin.PlanRoute, announceArrival: false);
    }

    /// The trip in progress, and the one control that matters while one is.
    private void DrawJourney()
    {
        var journey = plugin.Journey;
        var driver = plugin.MovementDriver;

        ImGui.Separator();
        ImGui.Spacing();

        var stageColour = journey.Stage switch
        {
            JourneyStage.Failed => Theme.Bad,
            JourneyStage.Done => Theme.Accent,
            JourneyStage.Idle => Theme.TextDim,
            _ => Theme.Warning,
        };

        var headline = !string.IsNullOrEmpty(journey.Detail) ? journey.Detail : driver.StatusDetail;
        if (string.IsNullOrEmpty(headline))
            headline = driver.Status.ToString();

        var scale = UiHelpers.Scale;
        var width = ImGui.GetContentRegionAvail().X;
        var distanceText = driver.IsRunning ? $"{driver.LastDistance:F0}y" : string.Empty;

        var distanceWidth = distanceText.Length > 0 ? ImGui.CalcTextSize(distanceText).X + (8f * scale) : 0f;

        var left = ImGui.GetCursorPosX();
        ImGui.TextColored(stageColour, UiHelpers.Truncate(headline, width - distanceWidth));

        if (distanceText.Length > 0)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(left + width - distanceWidth + (8f * scale));
            ImGui.TextColored(Theme.TextDim, distanceText);
        }

        if (!string.IsNullOrEmpty(journey.TargetLabel))
            ImGui.TextColored(Theme.TextDim, UiHelpers.Truncate(journey.TargetLabel, width));

        var buttonSize = new Vector2(ImGui.GetContentRegionAvail().X, 26f * scale);

        if (journey.IsRunning)
        {
            if (EchoButton.Draw("##stop", "Stop", buttonSize, Theme.Warning))
                plugin.Journey.Stop();
        }
        else if (journey.Stage == JourneyStage.Failed)
        {
            if (EchoButton.Draw("##dismiss", "Dismiss", buttonSize, Theme.Bad))
                plugin.Journey.Stop();
        }
    }

    private void Go(NavTarget target)
    {
        activeKey = target.Key;
        var plan = plugin.PlanTravel(target.Position);
        plugin.Journey.Start(plan, target.Radius > 1f ? target.Radius : 15f, target.Name, plugin.PlanRoute);
    }

    private static float Distance(NavSnapshot snapshot, NavTarget target) =>
        snapshot.HasPlayer ? Vector3.Distance(snapshot.PlayerPosition, target.Position) : 0f;


    /// True until the changelog has actually been open on the newest entry.
    private bool HasUnreadChangelog =>
        ChangelogData.Entries.Length > 0 && Config.LastSeenChangelogVersion != ChangelogData.LatestVersion;

    /// A dot in the corner of whatever was just drawn.
    private static void DrawUnreadDot()
    {
        var radius = 3.5f * UiHelpers.Scale;
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        ImGui.GetWindowDrawList().AddCircleFilled(
            new Vector2(max.X - radius, min.Y + radius), radius, ImGui.GetColorU32(Theme.Accent));
    }

    private readonly EchoTabs settingsTabs = new();

    private static readonly string[] SettingsTabLabels = ["Settings", "North Horn", "Changelog"];

    private const int ChangelogTab = 2;

    /// The two panels behind the cog, and the strip that chooses between them.
    private void DrawSettingsFrame()
    {
        var tab = settingsTabs.Draw("##settingstabs", SettingsTabLabels, dotOn: HasUnreadChangelog ? ChangelogTab : -1);

        currentView = tab switch
        {
            ChangelogTab => ViewMode.Changelog,
            1 => ViewMode.NorthHorn,
            _ => ViewMode.Settings,
        };

        VSpace(6f);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, settingsTabs.ContentAlpha);

        switch (tab)
        {
            case ChangelogTab:
                DrawChangelog();
                break;
            case 1:
                DrawNorthHorn();
                break;
            default:
                DrawSettings();
                break;
        }

        ImGui.PopStyleVar();
    }

    /// Release notes, newest first.
    private void DrawChangelog()
    {
        if (ChangelogData.Entries.Length > 0 && Config.LastSeenChangelogVersion != ChangelogData.LatestVersion)
        {
            Config.LastSeenChangelogVersion = ChangelogData.LatestVersion;
            Config.Save();
        }

        if (!ImGui.BeginChild("##changelog", Vector2.Zero, false))
        {
            ImGui.EndChild();
            return;
        }

        if (ChangelogData.Entries.Length == 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Theme.TextDim, "Nothing yet - what changes in each update will show up here.");
            ImGui.PopTextWrapPos();
            ImGui.EndChild();
            return;
        }

        for (var i = 0; i < ChangelogData.Entries.Length; i++)
        {
            var entry = ChangelogData.Entries[i];

            Theme.SectionHeader($"v{entry.Version}");
            ImGui.Spacing();

            foreach (var highlight in entry.Highlights)
            {
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.PushTextWrapPos(0f);
                ImGui.TextUnformatted(highlight);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
            }

            if (i < ChangelogData.Entries.Length - 1)
            {
                VSpace(6f);
                ImGui.Separator();
                VSpace(6f);
            }
        }

        ImGui.EndChild();
    }

    /// Draws a settings section as a rounded panel with its heading inside it.
    private Vector2 panelPos;
    private float panelWidth;

    /// Inner padding.
    private float panelPad;

    private const float PanelPadDesign = 14f;

    private float BeginPanel(string title, float width)
    {
        panelPos = ImGui.GetCursorScreenPos();
        panelWidth = width;
        panelPad = PanelPadDesign * UiHelpers.Scale;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(panelPos + new Vector2(panelPad, panelPad));
        ImGui.BeginGroup();

        Theme.SectionHeader(title, ruleWidth: width - (panelPad * 2f));
        ImGui.Spacing();

        ImGui.PushTextWrapPos(panelPos.X - ImGui.GetWindowPos().X + width - panelPad);

        return width - (panelPad * 2f);
    }

    private void EndPanel()
    {
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();

        var scale = UiHelpers.Scale;
        var height = ImGui.GetCursorScreenPos().Y - panelPos.Y + panelPad;
        var size = new Vector2(panelWidth, height);
        var accent = Theme.Accent;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(panelPos, panelPos + size,
            ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.06f)), 10f * scale);
        drawList.AddRect(panelPos, panelPos + size,
            ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.25f)), 10f * scale, ImDrawFlags.None,
            1f * scale);
        drawList.ChannelsMerge();

        ImGui.SetCursorScreenPos(new Vector2(panelPos.X, panelPos.Y + height));
        VSpace(10f);
    }

    /// Everything that only means anything inside the zone.
    private void DrawNorthHorn()
    {
        if (!ImGui.BeginChild("##northhorn", Vector2.Zero, false))
        {
            ImGui.EndChild();
            return;
        }

        var panel = ImGui.GetContentRegionAvail().X;
        var width = BeginPanel("travelling", panel);

        var avoidMonsters = Config.AvoidMonsters;
        if (EchoToggle.Draw("##avoidmonsters", "Avoid Monsters", ref avoidMonsters))
        {
            Config.AvoidMonsters = avoidMonsters;
            Config.Save();
        }

        Note("Only where there is room - a narrow pass is walked straight through, since a detour " +
             "into a wall to dodge something is worse than the fight.");

        if (Config.AvoidMonsters)
        {
            ImGui.Spacing();
            var floorNow = Config.IgnoreMonstersBelowLevel;
            ImGui.TextColored(Theme.TextDim,
                floorNow == 0 ? "Ignore monsters below level - avoiding everything" : $"Ignore monsters below level {floorNow}");

            var floor = Config.IgnoreMonstersBelowLevel;
            if (EchoSlider.DrawInt("##monsterfloor", ref floor, 0, 60, width, "level {0:F0}", 0))
            {
                Config.IgnoreMonstersBelowLevel = floor;
                Config.Save();
            }


            Note("Anything below this is left alone, since it will not start anything. Set by hand " +
                 "because your knowledge level is not something the plugin can read - four places in " +
                 "the game's own memory were checked and none of them hold it.");
        }

        ImGui.Spacing();

        var collectCoffers = Config.CollectCoffers;
        if (EchoToggle.Draw("##coffers", "Collect Chests", ref collectCoffers))
        {
            Config.CollectCoffers = collectCoffers;
            Config.Save();
        }

        Note("Steps aside for anything within about twenty-five yalms of where it already is - it " +
             "never goes looking, so the route decides what is worth a look.");

        ImGui.Spacing();
        var arrivalSound = Config.ArrivalSound;
        if (EchoToggle.Draw("##chime", "Arrival Chime", ref arrivalSound))
        {
            Config.ArrivalSound = arrivalSound;
            Config.Save();

            if (arrivalSound)
                SoundEffects.Play(SoundEffects.Arrival);
        }

        Note("Only when a trip finishes on its own - stopping it, or taking the controls back, stays " +
             "silent.");

        EndPanel();
        width = BeginPanel("phantom buffs", panel);
        DrawPhantomSettings(width);
        EndPanel();

        ImGui.EndChild();
    }

    private void DrawSettings()
    {
        if (!ImGui.BeginChild("##settings", Vector2.Zero, false))
        {
            ImGui.EndChild();
            return;
        }

        var scale = UiHelpers.Scale;
        var panel = ImGui.GetContentRegionAvail().X;
        var width = BeginPanel("window", panel);

        var windowWidth = Config.WindowWidth;
        if (SizeSlider("Width", "##winwidth", ref windowWidth, 320f, 900f, Configuration.DefaultWindowWidth, width))
        {
            Config.WindowWidth = windowWidth;
            Config.Save();
        }

        var windowHeight = Config.WindowHeight;
        if (SizeSlider("Height", "##winheight", ref windowHeight, 260f, 1200f, Configuration.DefaultWindowHeight, width))
        {
            Config.WindowHeight = windowHeight;
            Config.Save();
        }

        ImGui.Spacing();
        Note("The window can't be dragged to resize - use these sliders, or right-click one to put it " +
             "back. The padlock in the title bar pins it in place.");

        EndPanel();
        width = BeginPanel("behaviour", panel);

        var autoMount = Config.AutoMount;
        if (EchoToggle.Draw("##automount", "Use Mount", ref autoMount))
        {
            Config.AutoMount = autoMount;
            Config.Save();
        }

        Note("Mount speed is roughly triple running speed, so this is the difference between a trip " +
             "and an expedition.");

        ImGui.Spacing();
        DrawMountPicker(width);

        EndPanel();
        width = BeginPanel("colour", panel);
        Note("Every colour in the plugin derives from this one.", Theme.TextDim);
        ImGui.Spacing();

        var swatchGap = 6f * scale;
        var swatch = MathF.Max(28f * scale, (width - (swatchGap * (Theme.Presets.Length - 1))) / Theme.Presets.Length);

        for (var i = 0; i < Theme.Presets.Length; i++)
        {
            var (name, colour) = Theme.Presets[i];

            if (i > 0)
                ImGui.SameLine(0f, swatchGap);

            if (EchoButton.Draw($"##accent{i}", null, new Vector2(swatch, 26f * scale), colour,
                    selected: ColourMatches(Theme.Accent, colour), tooltip: name))
            {
                Theme.ApplyAccent(colour);
                Config.AccentColour = colour;
                Config.Save();
            }
        }

        ImGui.Spacing();

        var custom = new Vector3(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z);
        if (ImGui.ColorEdit3("##accentcustom", ref custom, ImGuiColorEditFlags.NoInputs))
        {
            var chosen = new Vector4(custom.X, custom.Y, custom.Z, 1f);
            Theme.ApplyAccent(chosen);
            Config.AccentColour = chosen;
            Config.Save();
        }

        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, "Anything else - click the swatch");

        EndPanel();
        width = BeginPanel("help", panel);

        if (EchoButton.Draw("##reportbug", "Report a Bug", new Vector2(width, 28f * scale), Theme.Warning,
                icon: FontAwesomeIcon.Bug, iconFont: plugin.Fonts.Icon))
        {
            BugReporter.Reset();
            ImGui.OpenPopup(BugReportPopup);
        }

        DrawBugReportPopup();

        ImGui.Spacing();
        if (EchoButton.Draw("##discord", BugReporter.DiscordInvite, new Vector2(width, 28f * scale),
                icon: FontAwesomeIcon.Comments, iconFont: plugin.Fonts.Icon,
                tooltip: "Click to copy the invite"))
            ImGui.SetClipboardText(BugReporter.DiscordInvite);

        if (Build.Diagnostics)
        {
            ImGui.Spacing();
            if (EchoButton.Draw("##diagnostics", "Open diagnostics", new Vector2(width, 26f * scale),
                    tooltip: "Everything the plugin knows - also /echonav debug"))
                plugin.ToggleDebugWindow();
        }

        EndPanel();
        ImGui.EndChild();
    }

    /// A labelled size slider that right-clicks back to its default.
    private bool SizeSlider(string label, string id, ref float value, float min, float max, float fallback, float width)
    {
        ImGui.TextColored(Theme.TextDim, label);

        var changed = EchoSlider.Draw(id, ref value, min, max, width, "{0:F0} px", fallback);

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            value = fallback;
            changed = true;
        }

        return changed;
    }

    /// Explanatory text under a setting, wrapped to the panel.
    private static void VSpace(float pixels) => ImGui.Dummy(new Vector2(0f, pixels * UiHelpers.Scale));

    private static void Note(string text, Vector4? colour = null)
    {
        ImGui.TextColored(colour ?? Theme.TextDisabled, text);
    }

    private string mountFilter = string.Empty;

    /// Picks which mount to summon, out of the ones this character has.
    private void DrawMountPicker(float width)
    {
        const string roulette = "Mount Roulette";

        var owned = plugin.MountRoster.Owned;
        var chosen = Config.MountId;

        var current = chosen == 0
            ? roulette
            : plugin.MountRoster.NameOf(chosen) ?? $"Not on this character (#{chosen})";

        ImGui.TextColored(Theme.TextDim, "Mount");
        ImGui.SetNextItemWidth(width);

        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Tinted(0.05f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Tinted(0.14f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Tinted(0.22f));
        var comboOpen = ImGui.BeginCombo("##mount", current);
        ImGui.PopStyleColor(3);

        if (comboOpen)
        {
            if (ImGui.IsWindowAppearing())
            {
                mountFilter = string.Empty;
                ImGui.SetKeyboardFocusHere();
            }

            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##mountfilter", "Search", ref mountFilter, 64);
            ImGui.Separator();

            if (ImGui.Selectable(roulette, chosen == 0))
            {
                Config.MountId = 0;
                Config.Save();
            }

            foreach (var mount in owned)
            {
                if (mountFilter.Length > 0
                    && !mount.Name.Contains(mountFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ImGui.Selectable(mount.Name, mount.Id == chosen))
                    continue;

                Config.MountId = mount.Id;
                Config.Save();
            }

            if (owned.Count == 0)
                ImGui.TextColored(Theme.TextDisabled, "Reading your collection...");

            ImGui.EndCombo();
        }

        Note("Falls back to the roulette wherever this one can't be summoned, so a restricted duty " +
             "still gets you a mount rather than nothing.");
    }

    /// The knowledge-crystal buffs, and which job to be left on afterwards.
    private void DrawPhantomSettings(float width)
    {
        var autoBuff = Config.PhantomBuffAtCrystals;
        if (EchoToggle.Draw("##phantombuff", "Keep buffs up on trips", ref autoBuff))
        {
            Config.PhantomBuffAtCrystals = autoBuff;
            Config.Save();
        }

        Note("Topped up during a trip, at the crystal you set off from or teleport to - anything " +
             "missing or under five minutes, then it puts back whichever phantom job you were on. " +
             "Never starts on its own while you are just standing about.");

        var state = plugin.PhantomState;

        ImGui.Spacing();

        if (!state.Available)
        {
            Note("Phantom levels show up once you're in the zone.");
            return;
        }

        var freelancer = state.LevelOf(PhantomJobs.Freelancer);
        if (freelancer >= PhantomJobs.InquiringMindLevel)
        {
            ImGui.TextColored(Theme.Accent,
                $"Inquiring Mind (Freelancer {freelancer}) - all of them in one cast.");
        }
        else
        {
            Note($"Freelancer {freelancer}/{PhantomJobs.InquiringMindLevel}: at 15, Inquiring Mind " +
                 "grants all of these at once instead of one job change each.", Theme.TextDim);
        }

        ImGui.Spacing();

        foreach (var buff in PhantomJobs.Buffs)
        {
            var level = state.LevelOf(buff.JobId);
            var can = level >= buff.JobLevel;

            ImGui.TextColored(can ? Theme.Accent : Theme.TextDisabled, can ? "+" : "-");
            ImGui.SameLine();
            ImGui.TextColored(can ? Theme.Text : Theme.TextDisabled, buff.Name);
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDisabled,
                $"- {buff.ActionName}, Ph. {buff.JobName} {level}/{buff.JobLevel}");
        }
    }

    private static bool ColourMatches(Vector4 a, Vector4 b) =>
        MathF.Abs(a.X - b.X) < 0.01f && MathF.Abs(a.Y - b.Y) < 0.01f && MathF.Abs(a.Z - b.Z) < 0.01f;


    private const string BugReportPopup = "##bugreport";
    private string bugDescription = string.Empty;
    private string bugName = string.Empty;

    private void DrawBugReportPopup()
    {
        if (!ImGui.BeginPopup(BugReportPopup))
            return;

        var scale = UiHelpers.Scale;
        var fieldWidth = 360f * scale;

        ImGui.PushTextWrapPos(fieldWidth);
        ImGui.TextColored(Theme.TextDim,
            "Sent to the EchoNav Discord along with your plugin version, zone and navigation state.");
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.SetNextItemWidth(fieldWidth);
        UiHelpers.WrappingInput("##bugtext", ref bugDescription, 2000, new Vector2(fieldWidth, 110f * scale));

        ImGui.Spacing();
        ImGui.TextColored(Theme.TextDim, "Name or Discord handle (optional)");
        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputText("##bugname", ref bugName, 64);

        ImGui.Spacing();

        var sending = BugReporter.State == BugReporter.SendState.Sending;
        if (EchoButton.Draw("##sendbug", sending ? "Sending..." : "Send", new Vector2(fieldWidth, 26f * scale),
                enabled: !sending && !string.IsNullOrWhiteSpace(bugDescription)))
            BugReporter.Send(BuildBugReport());

        switch (BugReporter.State)
        {
            case BugReporter.SendState.Sent:
                ImGui.TextColored(Theme.Accent, "Sent - thank you.");
                break;
            case BugReporter.SendState.Failed:
                ImGui.PushTextWrapPos(fieldWidth);
                ImGui.TextColored(Theme.Bad, $"Couldn't send: {BugReporter.LastError}");
                ImGui.PopTextWrapPos();
                break;
        }

        ImGui.EndPopup();
    }

    /// The state worth having alongside the words.
    private BugReport BuildBugReport() => new()
    {
        Name = bugName,

        Description = UiHelpers.Unwrapped("##bugtext", bugDescription),
        Version = plugin.Version,
        Territory = plugin.Snapshot.TerritoryType,
        MeshState = plugin.NavMesh.State.ToString(),
        JourneyStage = plugin.Journey.Stage.ToString(),
        MovementStatus = plugin.MovementDriver.Status.ToString(),
        UsingHook = plugin.MovementDriver.UsingHook,
        UsingLiveCamera = plugin.MovementDriver.UsingLiveCamera,
        Calibrated = plugin.Configuration.HasFullCalibration,
    };
}
