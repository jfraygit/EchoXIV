using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using EchoSim.Game;
using EchoSim.Sim;
using EchoSim.Sim.Engine;
using EchoSim.Shared;
using EchoSim.Sim.Analysis;
using EchoSim.Sim.Jobs;
using EchoSim.Sim.Jobs.Ninja;
using EchoSim.Sim.Validation;
using EchoSim.UI.Controls;

namespace EchoSim.UI;

public sealed partial class MainWindow : Window
{
    private enum ViewMode
    {
        Main,
        Settings,
    }

    private readonly Plugin plugin;

    private SimResult? result;
    private string statusMessage = string.Empty;
    private Vector4 statusColor = Theme.TextDim;
    private float[] buckets = [];

    private ViewMode currentView = ViewMode.Main;
    private ViewMode? pendingView;
    private float contentAlpha = 1f;

    /// How fast the cross-fade between views runs.
    private const float FadeSpeed = 14f;

    public MainWindow(Plugin plugin)
        : base("EchoSim###EchoSimMain", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin;
        SizeCondition = ImGuiCond.Always;

        minimised = plugin.Configuration.WindowMinimised;
    }

    private Configuration Config => plugin.Configuration;

    private int themeColors;

    /// Side length of the collapsed window.
    private const float MinimisedSize = 54f;

    private bool minimised;

    /// How fast the box grows and shrinks.
    private const float ResizeLerpSpeed = 16f;

    /// The size actually being rendered, eased toward the target rather than snapping.
    private Vector2 animatedSize;

    /// Full content only appears once the window is nearly full size.
    private bool FullyExpanded => !minimised && animatedSize.X >= Config.WindowWidth * 0.92f;

    public override void PreDraw()
    {
        UiHelpers.SampleScale();

        var target = minimised
            ? new Vector2(MinimisedSize, MinimisedSize)
            : new Vector2(Config.WindowWidth, Config.WindowHeight);

        if (animatedSize == Vector2.Zero)
            animatedSize = target;

        var dt = ImGui.GetIO().DeltaTime;
        animatedSize = new Vector2(
            UiHelpers.Lerp(animatedSize.X, target.X, ResizeLerpSpeed, dt),
            UiHelpers.Lerp(animatedSize.Y, target.Y, ResizeLerpSpeed, dt));

        if (Vector2.Distance(animatedSize, target) < 1f)
            animatedSize = target;

        Size = animatedSize * UiHelpers.Scale * UiHelpers.WindowSizeCompensation;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar;

        if (Config.WindowLocked && !minimised)
            Flags |= ImGuiWindowFlags.NoMove;

        if (!FullyExpanded)
            Flags |= ImGuiWindowFlags.NoBackground;

        ApplySavedPosition();
        themeColors = Theme.Push();
    }

    private Vector2 animatedPos;
    private Vector2 targetPos;

    /// True while the window is travelling between its two modes.
    private bool animatingPosition;

    /// Switches mode, starting an animation from wherever the window is now to wherever that mode was last
    /// left.
    private void SetMinimised(bool value)
    {
        if (minimised == value)
            return;

        RememberPosition();

        animatedPos = ImGui.GetWindowPos();
        minimised = value;
        Config.WindowMinimised = value;

        var savedX = value ? Config.MinimisedX : Config.FullWindowX;
        var savedY = value ? Config.MinimisedY : Config.FullWindowY;

        targetPos = savedX > Configuration.UnsetPosition && savedY > Configuration.UnsetPosition
            ? new Vector2(savedX, savedY)
            : animatedPos;

        animatingPosition = true;
        plugin.SaveConfig();
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

        var settled = Vector2.Distance(animatedPos, targetPos) < 1f
                      && animatedSize == (minimised
                          ? new Vector2(MinimisedSize, MinimisedSize)
                          : new Vector2(Config.WindowWidth, Config.WindowHeight));

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

        if (!FullyExpanded)
        {
            DrawMinimised();
            return;
        }

        DrawWindowBorder();
        DrawChrome();

        var target = pendingView ?? currentView;
        var dt = ImGui.GetIO().DeltaTime;

        contentAlpha = UiHelpers.Lerp(contentAlpha, pendingView is null ? 1f : 0f, FadeSpeed, dt);
        if (pendingView is not null && contentAlpha < 0.05f)
        {
            currentView = pendingView.Value;
            pendingView = null;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Clamp(contentAlpha, 0.02f, 1f));

        if (drawFailure is not null)
            DrawFailureView();
        else
            DrawGuarded();

        ImGui.PopStyleVar();
        _ = target;
    }

    /// Draws the current view, and if it throws, REMEMBERS THAT AND STOPS CALLING IT.
    private void DrawGuarded()
    {
        try
        {
            if (currentView == ViewMode.Settings)
                DrawSettingsView();
            else
                DrawMainView();
        }
        catch (Exception ex)
        {
            drawFailure = ex.Message;
            Plugin.Log.Error(ex, $"EchoSim: the {currentView} view threw while drawing and has been suspended.");
        }
    }

    /// What the window shows instead of a view that threw.
    private void DrawFailureView()
    {
        ImGui.Spacing();
        TextWrappedColored(Theme.Warning, "This panel hit an error and has been stopped.");
        ImGui.Spacing();

        TextWrappedColored(Theme.TextDim,
            "The rest of the plugin still works. Please send this with the bug button in the title " +
            "bar - the report includes the details automatically, so there is nothing to copy.");

        ImGui.Spacing();
        TextWrappedColored(Theme.TextDim, drawFailure ?? string.Empty);
        ImGui.Spacing();

        if (EchoButton.Draw("##retrydraw", "Try again", new Vector2(110f, 26f)))
            drawFailure = null;

        ImGui.SameLine(0f, 8f);
        if (EchoButton.Draw("##clearsetup", "Reset gear setup", new Vector2(150f, 26f)))
        {
            Config.GearSelection.Clear();
            Config.MateriaSelection.Clear();
            Config.InferredRelicStats.Clear();
            Config.BaselineRelicStats.Clear();
            Config.ModelError.Clear();
            Config.ModelCorrection.Clear();
            Config.Relic = new Sim.RelicAllocation();
            plugin.SaveConfig();
            drawFailure = null;
        }
    }

    private void SwitchTo(ViewMode view)
    {
        if (currentView != view)
            pendingView = view;
    }


    /// An accent border around the whole window, matching EchoMix's framed look and the minimised box.
    private static void DrawWindowBorder()
    {
        const float inset = 1.5f;

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos() + new Vector2(inset, inset);
        var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(inset, inset);

        drawList.PushClipRectFullScreen();
        drawList.AddRect(min, max,
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.75f)),
            8f, ImDrawFlags.None, 2f);
        drawList.PopClipRect();
    }

    /// Custom title bar: centred title, with the settings and close controls paired at the right.
    private void DrawChrome()
    {
        var buttonSize = UiHelpers.S(24f);
        var buttonGap = UiHelpers.S(6f);

        var rowTop = ImGui.GetCursorPosY();
        var startX = ImGui.GetCursorPosX();
        var width = ImGui.GetContentRegionAvail().X;

        float titleHeight;
        using (plugin.Fonts.Header.PushSafe())
            titleHeight = DrawWordmark("ECHOSIM", startX, rowTop, width, (buttonSize * 2f) + UiHelpers.S(12f));

        var inSettings = currentView == ViewMode.Settings;

        const int buttonCount = 5;
        ImGui.SetCursorPosY(rowTop);
        ImGui.SetCursorPosX(startX + width - (buttonSize * buttonCount) - (buttonGap * (buttonCount - 1)));

        var lockIcon = Config.WindowLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
        if (EchoButton.BareIcon("##lock", plugin.Fonts.Icon, lockIcon, buttonSize,
                Config.WindowLocked ? "Unlock window position" : "Lock window position", Config.WindowLocked))
        {
            Config.WindowLocked = !Config.WindowLocked;
            plugin.SaveConfig();
        }

        ImGui.SameLine(0f, buttonGap);
        ImGui.SetCursorPosY(rowTop);

        if (EchoButton.BareIcon("##reportbug", plugin.Fonts.Icon, FontAwesomeIcon.Bug, buttonSize, "Report a bug"))
        {
            BugReporter.Reset();
            ImGui.OpenPopup(BugReportPopup);
        }

        DrawBugReportPopup();

        ImGui.SameLine(0f, buttonGap);
        ImGui.SetCursorPosY(rowTop);
        if (EchoButton.BareIcon("##settings", plugin.Fonts.Icon, FontAwesomeIcon.Cog, buttonSize,
                inSettings ? "Back" : "Settings", inSettings))
        {
            SwitchTo(inSettings ? ViewMode.Main : ViewMode.Settings);
        }

        if (HasUnseenChangelog)
            DrawUnseenBadge();

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

    /// The wordmark, in EchoMix's title style: letter-spaced caps with a drop shadow and an underline bar.
    private Vector2? minimisedPressAt;

    /// The collapsed window: a rising-chart glyph in a bordered box, which says "damage numbers" far better
    /// than a shrunk-down logo did at this size.
    private void DrawMinimised()
    {
        const float inset = 1.5f;

        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var min = origin + new Vector2(inset, inset);
        var max = origin + size - new Vector2(inset, inset);
        var drawList = ImGui.GetWindowDrawList();

        drawList.PushClipRectFullScreen();

        var hovered = ImGui.IsWindowHovered() && minimised;

        var body = Theme.Tinted(hovered ? 0.22f : 0.10f);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(body), 8f);

        var span = MathF.Max(1f, Config.WindowWidth - MinimisedSize);
        var collapsed = Math.Clamp(1f - ((size.X - MinimisedSize) / span), 0f, 1f);

        using (plugin.Fonts.Icon.PushSafe())
        {
            var glyph = FontAwesomeIcon.ChartLine.ToIconString();
            var font = ImGui.GetFont();
            const float glyphSize = 26f;

            var measured = ImGui.CalcTextSize(glyph) * (glyphSize / ImGui.GetFontSize());
            var centre = (min + max) / 2f;
            var colour = new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, collapsed);
            drawList.AddText(font, glyphSize, centre - (measured / 2f), ImGui.GetColorU32(colour), glyph, 0f);
        }

        drawList.AddRect(min, max,
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, (hovered ? 1f : 0.85f) * collapsed)),
            8f, ImDrawFlags.None, 2f);

        drawList.PopClipRect();

        if (hovered)
            ImGui.SetTooltip("EchoSim - click to expand, drag to move");

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            minimisedPressAt = ImGui.GetMousePos();

        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            return;

        if (minimisedPressAt is { } pressed && Vector2.Distance(pressed, ImGui.GetMousePos()) < 4f && hovered)
            SetMinimised(false);

        minimisedPressAt = null;
    }

    /// Returns:The vertical space the wordmark occupies, including its underline.
    private static float DrawWordmark(string text, float startX, float rowTop, float availableWidth, float reservedRight)
    {
        const float letterSpacing = 6f;

        var drawList = ImGui.GetWindowDrawList();
        var fontSize = ImGui.GetFontSize();

        Span<float> widths = stackalloc float[text.Length];
        var totalWidth = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            widths[i] = ImGui.CalcTextSize(text[i].ToString()).X;
            totalWidth += widths[i];
        }

        totalWidth += letterSpacing * (text.Length - 1);

        var ideal = MathF.Max(0f, (availableWidth - totalWidth) / 2f);
        var limit = MathF.Max(0f, availableWidth - reservedRight - totalWidth);
        ImGui.SetCursorPos(new Vector2(startX + MathF.Min(ideal, limit), rowTop));

        var origin = ImGui.GetCursorScreenPos();
        var x = origin.X;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i].ToString();
            drawList.AddText(new Vector2(x, origin.Y + 2f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)), ch);
            drawList.AddText(new Vector2(x, origin.Y), ImGui.GetColorU32(Theme.Accent), ch);
            x += widths[i] + letterSpacing;
        }

        var underlineY = origin.Y + fontSize + 3f;
        drawList.AddLine(
            new Vector2(origin.X, underlineY),
            new Vector2(origin.X + totalWidth, underlineY),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.7f)),
            2f);

        return fontSize + 7f;
    }

    /// Moves to another job, putting the current job's set away and taking out that job's own.
    private void SwitchJob(uint jobId)
    {
        var restored = Config.ApplySetup(jobId);
        plugin.SaveConfig();

        GearCatalog.SetJob(jobId);

        result = null;
        buckets = [];
        statusMessage = string.Empty;

        previousFigures = null;
        currentFigures = null;

        if (!restored)
            RebuildStatsFromGear();
    }

    /// A row of job icons.
    private void DrawJobRow()
    {
        GearCatalog.SetJob(Config.SelectedJobId);

        var gap = UiHelpers.S(3f);

        var jobs = JobList.All();
        if (jobs.Count == 0)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));

        var rowWidth = ImGui.GetContentRegionAvail().X;

        var maxIcon = UiHelpers.S(40f);
        var minIcon = UiHelpers.S(24f);

        var perRow = jobs.Count;
        var iconSize = MathF.Min(maxIcon, (rowWidth - (gap * (perRow - 1))) / perRow);

        if (iconSize < minIcon)
        {
            perRow = Math.Max(1, (int)((rowWidth + gap) / (minIcon + gap)));
            iconSize = (rowWidth - (gap * (perRow - 1))) / perRow;
        }

        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            var selected = job.Id == Config.SelectedJobId;

            if (i > 0 && i % perRow != 0)
                ImGui.SameLine();

            var clicked = ImGui.InvisibleButton($"##job{job.Id}", new Vector2(iconSize, iconSize));
            var hovered = ImGui.IsItemHovered();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var drawList = ImGui.GetWindowDrawList();

            if (selected || hovered)
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Tinted(selected ? 0.30f : 0.15f)), 5f);

            var icon = GameData.Icon(job.IconId);
            if (icon is not null)
            {
                var tint = job.Implemented ? Vector4.One : new Vector4(1f, 1f, 1f, 0.32f);
                drawList.AddImage(icon.Handle, min + new Vector2(1f, 1f), max - new Vector2(1f, 1f),
                    Vector2.Zero, Vector2.One, ImGui.GetColorU32(tint));
            }

            if (selected)
            {
                drawList.AddRect(min, max, ImGui.GetColorU32(Theme.Accent), 5f, ImDrawFlags.None, 1.6f);
            }

            if (hovered)
            {
                ImGui.SetTooltip(job.Implemented
                    ? $"{job.Name}"
                    : $"{job.Name}\nNot simulated yet.");
            }

            if (clicked && job.Id != Config.SelectedJobId)
                SwitchJob(job.Id);
        }

        ImGui.PopStyleVar();
    }

    /// True when the selected job actually has a simulation behind it.
    private bool JobImplemented => JobRegistry.IsImplemented(Config.SelectedJobId);


    /// A titled, tinted panel wrapping one group of settings.
    private Vector2 settingsPanelPos;

    private float settingsPanelWidth;

    private const float SettingsPanelPad = 14f;

    private const float SettingsColumnGap = 12f;

    private float BeginSettingsPanel(string title, float width)
    {
        settingsPanelPos = ImGui.GetCursorScreenPos();
        settingsPanelWidth = width;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(settingsPanelPos + new Vector2(SettingsPanelPad, SettingsPanelPad));
        ImGui.BeginGroup();

        Theme.SectionHeader(title, ruleWidth: width - (SettingsPanelPad * 2f));
        ImGui.Spacing();

        ImGui.PushTextWrapPos(settingsPanelPos.X - ImGui.GetWindowPos().X + width - SettingsPanelPad);

        return width - (SettingsPanelPad * 2f);
    }

    private void EndSettingsPanel()
    {
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();

        var height = ImGui.GetCursorScreenPos().Y - settingsPanelPos.Y + SettingsPanelPad;
        var size = new Vector2(settingsPanelWidth, height);
        var accent = Theme.Accent;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(settingsPanelPos, settingsPanelPos + size,
            ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.06f)), 10f);
        drawList.AddRect(settingsPanelPos, settingsPanelPos + size,
            ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.25f)), 10f, ImDrawFlags.None, 1f);
        drawList.ChannelsMerge();

        ImGui.SetCursorScreenPos(new Vector2(settingsPanelPos.X, settingsPanelPos.Y + height));
    }

    private void DrawSettingsView()
    {
        SectionHeader("SETTINGS");

        var tab = settingsTabs.Draw("##settings_tabs", SettingsTabLabels,
            badgeOn: ChangelogTabIndex, badgeText: HasUnseenChangelog ? string.Empty : null);
        ImGui.Spacing();

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * settingsTabs.ContentAlpha);

        switch (tab)
        {
            case 0:
                ImGui.BeginChild("##generalscroll", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
                DrawGeneralSettings();
                ImGui.EndChild();
                break;

            case 1:
                ImGui.BeginChild("##livescorescroll", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
                DrawLiveScoreTab();
                ImGui.EndChild();
                break;

            default:
                DrawChangelog();
                break;
        }

        ImGui.PopStyleVar();
    }

    /// BETA IS ON THE TAB RATHER THAN IN THE COPY, AND IT IS EARNED RATHER THAN CAUTIOUS.
    private static readonly string[] SettingsTabLabels = ["General", "Live Score (BETA)", "Changelog"];

    /// Where Changelog sits in SettingsTabLabels, for the unread badge.
    private const int ChangelogTabIndex = 2;

    /// True until the player has actually had the Changelog tab open on the newest entry.
    private bool HasUnseenChangelog
        => Config.LastSeenChangelogVersion != ChangelogData.LatestVersion;

    /// A dot in the corner of whatever was just drawn.
    private static void DrawUnseenBadge()
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        var radius = 4f;
        var dot = new Vector2(max.X - radius, min.Y + radius);
        ImGui.GetWindowDrawList().AddCircleFilled(dot, radius, ImGui.GetColorU32(Theme.Accent));
    }

    /// Release notes, newest first.
    private void DrawChangelog()
    {
        if (Config.LastSeenChangelogVersion != ChangelogData.LatestVersion)
        {
            Config.LastSeenChangelogVersion = ChangelogData.LatestVersion;
            plugin.SaveConfig();
        }

        ImGui.Spacing();

        if (ChangelogData.Entries.Length == 0)
        {
            const string line = "No releases yet.";
            UiHelpers.CenterCursorX(ImGui.CalcTextSize(line).X);
            ImGui.TextColored(Theme.TextDim, line);
            return;
        }

        ImGui.BeginChild("##changelogscroll", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);

        for (var i = 0; i < ChangelogData.Entries.Length; i++)
        {
            var entry = ChangelogData.Entries[i];

            using (plugin.Fonts.Header.PushSafe())
                ImGui.TextColored(Theme.Accent, $"v{entry.Version}");

            ImGui.Spacing();

            foreach (var highlight in entry.Highlights)
            {
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextWrapped(highlight);
            }

            if (i < ChangelogData.Entries.Length - 1)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }
        }

        ImGui.EndChild();
    }

    /// Two columns of panels across the full window width.
    private void DrawGeneralSettings()
    {
        var top = ImGui.GetCursorScreenPos();
        var columnWidth = (ImGui.GetContentRegionAvail().X - SettingsColumnGap) / 2f;

        var inner = BeginSettingsPanel("WINDOW", columnWidth);
        DrawWindowSettings(inner);
        EndSettingsPanel();

        NextPanelInColumn(top.X);

        inner = BeginSettingsPanel("HELP", columnWidth);
        DrawHelpSettings(inner);
        EndSettingsPanel();

        var leftBottom = ImGui.GetCursorScreenPos().Y;

        ImGui.SetCursorScreenPos(new Vector2(top.X + columnWidth + SettingsColumnGap, top.Y));

        inner = BeginSettingsPanel("THEME", columnWidth);
        DrawThemeSettings(inner);
        EndSettingsPanel();

        ImGui.SetCursorScreenPos(new Vector2(top.X, MathF.Max(leftBottom, ImGui.GetCursorScreenPos().Y)));
    }

    /// Opens the gap between one panel and the next in the same column.
    private static void NextPanelInColumn(float columnX)
        => ImGui.SetCursorScreenPos(
            new Vector2(columnX, ImGui.GetCursorScreenPos().Y + SettingsColumnGap));

    /// The live score's own settings tab.
    private void DrawLiveScoreTab()
    {
        var top = ImGui.GetCursorScreenPos();
        var columnWidth = (ImGui.GetContentRegionAvail().X - SettingsColumnGap) / 2f;

        var inner = BeginSettingsPanel("DISPLAY", columnWidth);
        DrawLiveScoreDisplaySettings(inner);
        EndSettingsPanel();

        NextPanelInColumn(top.X);

        inner = BeginSettingsPanel("LEADERBOARDS", columnWidth);
        DrawLeaderboardSettings(inner);
        EndSettingsPanel();

        var leftBottom = ImGui.GetCursorScreenPos().Y;
        var rightX = top.X + columnWidth + SettingsColumnGap;

        ImGui.SetCursorScreenPos(new Vector2(rightX, top.Y));

        var notice = BeginSettingsPanel("BETA", columnWidth);
        DrawBetaNotice(notice);
        EndSettingsPanel();

        NextPanelInColumn(rightX);

        inner = BeginSettingsPanel("WHAT IT MEASURES", columnWidth);
        DrawLiveScoreExplanation(inner);
        EndSettingsPanel();

        ImGui.SetCursorScreenPos(new Vector2(top.X, MathF.Max(leftBottom, ImGui.GetCursorScreenPos().Y)));
    }

    /// Says what BETA means here, in one line.
    private static void DrawBetaNotice(float width)
    {
        TextWrappedColored(Theme.Warning,
            "These numbers are still moving, and they will keep moving.");

        ImGui.Spacing();

        TextWrappedColored(Theme.TextDisabled,
            "Read a score as a reading, not a record. Where the model is wrong the score is wrong " +
            "with it, and if yours shifts after an update without your play changing, that's why.");
    }

    /// The in-combat readout, off unless asked for.
    private void DrawLiveScoreDisplaySettings(float width)
    {
        var show = Config.ShowLiveScore;
        if (EchoToggle.Draw("##livescore", "Show live score in combat", ref show))
        {
            Config.ShowLiveScore = show;
            plugin.SaveConfig();
        }

        ImGui.Spacing();

        if (!plugin.Combat.Available)
        {
            TextWrappedColored(Theme.Warning,
                "Unavailable - EchoSim couldn't attach to the game's action handling, so it can't see " +
                "what you press. Nothing else is affected.");
            return;
        }

        TextWrappedColored(Theme.TextDisabled,
            "A small panel that floats over the game showing your score and nothing else. It appears " +
            "when you start fighting, goes away when you stop, and pulses harder the better you're " +
            "doing.");

        ImGui.Spacing();
        ImGui.Spacing();

        var alwaysOn = Config.LiveScoreAlwaysOn;
        if (EchoToggle.Draw("##livealwayson", "Keep it up between fights", ref alwaysOn))
        {
            Config.LiveScoreAlwaysOn = alwaysOn;
            plugin.SaveConfig();
        }

        ImGui.Spacing();
        TextWrappedColored(Theme.TextDisabled,
            "Leaves the panel up after the fight, still showing how that pull went.");

        ImGui.Spacing();
        ImGui.Spacing();

        var placing = plugin.Live.Preview;
        if (EchoToggle.Draw("##liveplace", "Move it", ref placing))
            plugin.Live.Preview = placing;

        ImGui.Spacing();
        TextWrappedColored(Theme.TextDisabled,
            placing
                ? "Drag the panel where you want it, then switch this off. The score shown is a sample."
                : "Shows the panel so you can drag it into place. Otherwise it ignores the mouse " +
                  "entirely, so it can never swallow a click during a fight.");
    }

    /// Says what the number is not, as well as what it is.
    private bool ExplainingAHealer
    {
        get
        {
            var played = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
            var job = played != 0 ? played : Config.SelectedJobId;

            return Sim.Engine.CombatRoles.UsesPiety(JobRegistry.ForOrDefault(job).CreateSim().Role);
        }
    }

    /// Says out loud that a healer cannot reach 100, and why it is not their fault.
    private void DrawHealerCeilingNote()
    {
        if (!ExplainingAHealer)
            return;

        ImGui.Spacing();

        TextWrappedColored(Theme.Warning, "Healers can't reach 100, and shouldn't aim to.");

        ImGui.Spacing();

        TextWrappedColored(Theme.TextDisabled,
            "The ceiling never heals - every GCD and every resource goes to damage, which no real "
          + "healer can match. The gap is the healing your group needed, not a mistake. Rank yourself "
          + "against other healers.");
    }

    private void DrawLiveScoreExplanation(float width)
    {
        TextWrappedColored(Theme.TextDisabled,
            "How close your play is to this job's modelled ceiling, from 0 to 100. It watches only " +
            "your own actions.\n\n" +
            "Measured against a best-in-slot reference set rather than your own gear, so the number " +
            "holds still when you upgrade - it says how far off the best this job can do you are.\n\n" +
            "Scored on potency, not damage, so bad crits don't read as bad play. Gear reaches the " +
            "score in one place only: speed. Below the reference set's GCD you'll land short however " +
            "cleanly you play.\n\n" +
            "Not an FFLogs parse - that's rDPS ranked against everyone else, and it needs your " +
            "party's damage. EchoSim reads only your own hotbar. Time when nothing is targetable is " +
            "excluded, so a phase transition doesn't read as standing still.");

        DrawHealerCeilingNote();
    }

    /// The one setting in this plugin that sends anything anywhere.
    private void DrawLeaderboardSettings(float width)
    {
        var share = Config.ShareToLeaderboards;
        if (EchoToggle.Draw("##sharelb", "Post my kills to the leaderboards", ref share))
        {
            Config.ShareToLeaderboards = share;
            plugin.SaveConfig();
        }

        ImGui.Spacing();

        TextWrappedColored(Theme.TextDisabled,
            "Off by default, and nothing is sent while it is off.\n\n" +
            "Switched on, clearing a ranked fight sends your character name, world, job and the " +
            "actions you pressed. Wipes send nothing - the game's own duty completion is what " +
            "triggers it.\n\n" +
            "The score itself isn't sent. The server works it out from your actions with the same " +
            "simulator this plugin runs, so nobody can post a number they didn't earn. Only your " +
            "best on each fight is kept, ranked against others on your job.");

        ImGui.Spacing();

        TextWrappedColored(Theme.TextDisabled,
            "Your live score and your board score can differ slightly - one is read from what you " +
            "pressed, the other recomputed from your FFLogs report. The board's is the one that counts.");

        if (!plugin.Combat.Available)
        {
            ImGui.Spacing();
            TextWrappedColored(Theme.Warning,
                "Unavailable - EchoSim can't see what you press, so there'd be nothing to post.");
        }

    }

    private void DrawWindowSettings(float width)
    {
        var windowWidth = Config.WindowWidth;
        ImGui.TextColored(Theme.TextDim, "Width");
        if (EchoSlider.Draw("##winwidth", ref windowWidth, 620f, 1600f, width, "{0:F0} px", Configuration.DefaultWindowWidth))
        {
            Config.WindowWidth = windowWidth;
            plugin.SaveConfig();
        }

        ResetOnRightClick(() =>
        {
            Config.WindowWidth = Configuration.DefaultWindowWidth;
            plugin.SaveConfig();
        });

        ImGui.Spacing();

        var height = Config.WindowHeight;
        ImGui.TextColored(Theme.TextDim, "Height");
        if (EchoSlider.Draw("##winheight", ref height, 480f, 1400f, width, "{0:F0} px", Configuration.DefaultWindowHeight))
        {
            Config.WindowHeight = height;
            plugin.SaveConfig();
        }

        ResetOnRightClick(() =>
        {
            Config.WindowHeight = Configuration.DefaultWindowHeight;
            plugin.SaveConfig();
        });

        ImGui.Spacing();

        ImGui.TextColored(Theme.TextDisabled,
            "The window can't be dragged to resize - use these sliders. Right-click either one to reset it.");
    }

    /// The accent picker, laid out as a grid that fills its panel instead of a single run of buttons that
    /// wrapped wherever it happened to run out of room.
    private void DrawThemeSettings(float width)
    {
        ImGui.TextColored(Theme.TextDim, "Every colour in the plugin derives from this one.");
        ImGui.Spacing();

        const int columns = 3;
        const float gap = 6f;
        var buttonWidth = (width - (gap * (columns - 1))) / columns;

        for (var i = 0; i < Theme.Presets.Length; i++)
        {
            var (name, colour) = Theme.Presets[i];
            var selected = Math.Abs(colour.X - Config.AccentR) < 0.01f
                           && Math.Abs(colour.Y - Config.AccentG) < 0.01f
                           && Math.Abs(colour.Z - Config.AccentB) < 0.01f;

            if (i % columns != 0)
                ImGui.SameLine(0f, gap);

            if (EchoButton.Draw($"##preset{name}", name, new Vector2(buttonWidth, 26f), colour, selected: selected))
                ApplyAccent(colour);
        }

        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.TextColored(Theme.TextDim, "Custom");

        var accent = new Vector3(Config.AccentR, Config.AccentG, Config.AccentB);
        ImGui.SetNextItemWidth(width);
        if (ImGui.ColorEdit3("##customaccent", ref accent, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
            ApplyAccent(new Vector4(accent.X, accent.Y, accent.Z, 1f));

        ImGui.SameLine(0f, 8f);
        ImGui.TextColored(Theme.TextDisabled, "Click the swatch to pick");
    }

    private void ApplyAccent(Vector4 colour)
    {
        Config.AccentR = colour.X;
        Config.AccentG = colour.Y;
        Config.AccentB = colour.Z;
        plugin.SaveConfig();
        Theme.ApplyAccent(colour);
    }

    private string bugReport = string.Empty;

    /// The report box, as one number rather than two.
    private static readonly Vector2 BugReportBoxSize = new(360, 120);

    /// Optional, and free text - see BugReport.Name for why it exists.
    private string bugReportName = string.Empty;

    /// Which main tab was last drawn, so a report can say where the user was.
    private int lastTab;

    private const string BugReportPopup = "##bugreport";

    /// The Discord invite, as a button rather than an ImGui.Selectable.
    private void DrawHelpSettings(float width)
    {
        ImGui.TextColored(Theme.TextDim, "Found a bug, or want to ask something?");
        ImGui.Spacing();

        if (EchoButton.Draw("##discord", BugReporter.DiscordInvite, new Vector2(width, 28f),
                tooltip: "Click to copy the invite to your clipboard."))
        {
            ImGui.SetClipboardText(BugReporter.DiscordInvite);
        }

        ImGui.Spacing();
        ImGui.TextColored(Theme.TextDisabled,
            "The bug report form is on the title bar. It sends your plugin version, job, gear stats "
          + "and simulation settings with the report, and an optional name if you want a reply.");

        DrawRelaySwitch(width);
    }

    /// Points a development build at the live relay instead of the dev one.
    private void DrawRelaySwitch(float width)
    {
#if DEBUG
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(Theme.Warning, "Development build");
        ImGui.TextColored(Theme.TextDim, "Scores and bug reports go to the dev relay unless switched.");
        ImGui.Spacing();

        var live = RelayEndpoints.UsingLive;
        if (EchoButton.Draw("##relayswitch",
                live ? "Using LIVE relay - click for dev" : "Using dev relay - click for live",
                new Vector2(width, 28f),
                tooltip: "Only a Debug build can switch. Released builds are always live."))
        {
            RelayEndpoints.Choose(!live);
            Plugin.Log.Information($"EchoSim: relay switched to {RelayEndpoints.BaseUrl}");
        }
#else

        _ = width;
#endif
    }

    /// The bug report form.
    private void DrawBugReportPopup()
    {
        if (!ImGui.BeginPopup(BugReportPopup))
            return;

        ImGui.TextColored(Theme.Accent, "Report a bug");
        ImGui.TextColored(Theme.TextDim,
            "Sent to the EchoSim Discord with your plugin version, job, gear and settings.");
        ImGui.Spacing();

        WrappedInput.Multiline("##bugtext", ref bugReport, 1500, BugReportBoxSize);

        ImGui.Spacing();

        ImGui.TextColored(Theme.TextDim, "Name or Discord handle (optional)");
        ImGui.SetNextItemWidth(360);
        ImGui.InputTextWithHint("##bugname", "so we can reply - leave blank to stay anonymous",
            ref bugReportName, 64);

        ImGui.Spacing();

        var sending = BugReporter.State == BugReporter.SendState.Sending;
        if (EchoButton.Draw("##sendbug", sending ? "Sending..." : "Send", new Vector2(100, 26), enabled: !sending && bugReport.Trim().Length > 0))
        {
            BugReporter.Send(BuildBugReport());
        }

        ImGui.SameLine();

        switch (BugReporter.State)
        {
            case BugReporter.SendState.Sent:
                ImGui.TextColored(Theme.Accent, "Sent - thank you.");
                bugReport = string.Empty;

                break;
            case BugReporter.SendState.Failed:
                ImGui.TextColored(Theme.Warning, $"Failed: {BugReporter.LastError}");
                break;
        }

        ImGui.EndPopup();
    }

    /// Gathers the context a report is useless without.
    private BugReport BuildBugReport()
    {
        var stats = BuildStats();
        var selected = JobList.ById(Config.SelectedJobId)?.Name ?? "unknown";

        var currentId = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        var current = currentId == 0 ? "not logged in" : JobList.ById(currentId)?.Name ?? $"job {currentId}";

        var food = GameData.FoodById(Config.FoodItemId);
        var screen = lastTab >= 0 && lastTab < MainTabLabels.Length ? MainTabLabels[lastTab] : "unknown";

        return new BugReport
        {
            Description = WrappedInput.Unfold(bugReport, WrappedInput.WidthFor(BugReportBoxSize)).Trim(),
            Name = bugReportName.Trim(),
            PluginVersion = plugin.Version,
            DevBuild = RelayEndpoints.CanChoose,
            SelectedJob = selected,
            CurrentJob = current,

            Stats = $"WD {stats.WeaponDamage}  delay {stats.WeaponDelay:F2}  main {stats.MainStat}  "
                  + $"crit {stats.Crit}  det {stats.Determination}  dh {stats.DirectHit}  "
                  + $"sks/sps {stats.SkillSpeed}  ten {stats.Tenacity}  (GCD {stats.Gcd:F2}s)",

            Consumables = $"food {(food is null ? "none" : food.Name)}  "
                        + $"potion {Config.Potion.Name}  "
                        + $"party bonus {(Config.PartyBonus ? "on" : "off")}  "
                        + $"party buffs {(Config.UsePartyBuffs ? "on" : "off")}  "
                        + $"targets {Config.Targets}  duration {Config.FightDuration:F0}s",

            Screen = screen,

            GameVersion = $"{GameVersionString()} ({Plugin.ClientState.ClientLanguage})",
            DalamudVersion = DalamudVersionString(),
        };
    }

    /// The game's own build version, read from the data repository Lumina opened.
    private static string GameVersionString()
    {
        try
        {
            return Plugin.DataManager.GameData.Repositories.TryGetValue("ffxiv", out var repo)
                ? repo.Version
                : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// Dalamud's assembly version, which is also what the API level tracks.
    private static string DalamudVersionString()
    {
        try
        {
            return typeof(Dalamud.Plugin.IDalamudPluginInterface).Assembly.GetName().Version?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// Right-clicking the widget just drawn resets it.
    private static void ResetOnRightClick(Action reset)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Right-click to reset to default.");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                reset();
        }
    }


    private void DrawMainView()
    {
        DrawJobRow();
        ImGui.Spacing();

        if (!JobImplemented)
        {
            DrawUnimplementedJob();
            return;
        }

        DrawSummaryCards();
        ImGui.Spacing();

        var tab = mainTabs.Draw("##echosim_tabs", MainTabLabels, fill: true);
        ImGui.Spacing();

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * mainTabs.ContentAlpha);

        lastTab = tab;

        switch (tab)
        {
            case 0: DrawSetupTab(); break;
            case 1: DrawBreakdownTab(); break;
            case 2: DrawTimelineTab(); break;
            case 3: DrawBurstTab(); break;
            case 4: DrawOpenerTab(); break;
            case 5: DrawCompareTab(); break;
            case 6: DrawAnalysisTab(); break;
            case LeaderboardsTabIndex: DrawLeaderboardsTab(); break;
            case ProfileTabIndex: DrawProfileTab(); break;
        }

        ImGui.PopStyleVar();
    }

    private static readonly string[] MainTabLabels =
        ["Setup", "Breakdown", "Timeline", "Burst", "Opener", "Gear Compare", "Analysis", "Leaderboards", "Profile"];

    /// Named rather than numbered, because two of these are now jumped to from code.
    private const int LeaderboardsTabIndex = 7;

    private const int ProfileTabIndex = 8;

    private readonly EchoTabs mainTabs = new();
    private readonly EchoTabs settingsTabs = new();

    private void DrawUnimplementedJob()
    {
        var job = JobList.ById(Config.SelectedJobId);

        ImGui.Spacing();
        ImGui.Spacing();

        var title = job is null ? "This job" : job.Value.Name;
        var line = $"{title} isn't simulated yet.";
        UiHelpers.CenterCursorX(ImGui.CalcTextSize(line).X);
        ImGui.TextColored(Theme.Text, line);

        var ready = JobRegistry.Implemented
            .Select(id => JobList.ById(id)?.Name)
            .Where(name => name is not null)
            .Order()
            .ToList();

        var detail = ready.Count switch
        {
            0 => "No jobs are implemented yet.",
            1 => $"{ready[0]} is the only job implemented so far. Pick it to run a simulation.",
            _ => $"Implemented so far: {string.Join(", ", ready)}. Pick one to run a simulation.",
        };

        UiHelpers.CenterCursorX(ImGui.CalcTextSize(detail).X);
        ImGui.TextColored(Theme.TextDim, detail);

        ImGui.Spacing();

        var fallback = JobList.ById(JobRegistry.Default.ClassJobId);
        var label = $"Switch to {fallback?.Name ?? JobRegistry.Default.Name}";

        UiHelpers.CenterCursorX(140f);
        if (EchoButton.Draw("##switchjob", label, new Vector2(ImGui.CalcTextSize(label).X + 36f, 28)))
            SwitchJob(JobRegistry.Default.ClassJobId);
    }

    /// How long the summary figures take to count up to a finished result.
    private const float CountUpSeconds = 0.75f;

    /// Bumped by RunSimulation.
    private int resultGeneration;

    private int animatedGeneration = -1;
    private float countUpStartedAt;

    /// The figures the summary cards compare against - the run before the current one.
    private readonly record struct CardFigures(double Dps, double Damage, double Gcds, double Delay);

    private CardFigures? previousFigures;
    private CardFigures? currentFigures;

    /// Change since the previous run: how far, written out, and which way is an improvement.
    private readonly record struct StatDelta(double Amount, string Text, bool HigherIsBetter);

    /// Builds one card's delta, or null when there's nothing meaningful to say - no previous run, or a change
    /// too small to be worth an arrow.
    private StatDelta? Delta(
        Func<CardFigures, double> select,
        Func<double, string> format,
        double threshold,
        bool higherIsBetter = true)
    {
        if (previousFigures is not { } before || currentFigures is not { } after)
            return null;

        var change = select(after) - select(before);
        return Math.Abs(change) < threshold ? null : new StatDelta(change, format(change), higherIsBetter);
    }

    private void DrawSummaryCards()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var size = new Vector2((avail - (spacing * 3)) / 4f, UiHelpers.S(76f));

        var now = (float)ImGui.GetTime();
        if (animatedGeneration != resultGeneration)
        {
            animatedGeneration = resultGeneration;
            countUpStartedAt = now;
        }

        var progress = result is null
            ? 1f
            : UiHelpers.EaseOutCubic((now - countUpStartedAt) / CountUpSeconds);

        var counting = progress < 1f;

        var dpsAccent = counting ? Vector4.Lerp(Theme.Accent, Vector4.One, 0.35f * (1f - progress)) : Theme.Accent;

        var deltaAlpha = Math.Clamp((progress - 0.55f) / 0.45f, 0f, 1f);

        DrawStatCard("##card_dps", size, "DPS",
            result is null ? "-" : $"{result.Dps * progress:N0}", dpsAccent, tinted: true, accentOverride: dpsAccent,
            delta: Delta(f => f.Dps, v => $"{v:+#,##0;-#,##0}", 1.0), deltaAlpha: deltaAlpha);
        ImGui.SameLine();
        DrawStatCard("##card_total", size, "TOTAL DAMAGE",
            result is null ? "-" : $"{result.TotalDamage * progress:N0}", Theme.Text);
        ImGui.SameLine();
        var clipsByDesign = JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim().JobName == "Ninja";

        DrawStatCard("##card_gcds", size, "GCDS / UPTIME",
            result is null ? "-" : $"{result.GcdCount * progress:F0} / {result.GcdUptime * progress:P1}", Theme.Text);

        if (ImGui.IsItemHovered() && result is not null)
        {
            ImGui.SetTooltip(
                "Weaponskills landed, and the share of the fight the global cooldown spent rolling.\n\n" +
                (clipsByDesign
                    ? "Ninja runs below the others here and is meant to. Its ninjutsu need mudra presses\n" +
                      "inside the weave window beforehand, and when they don't fit the ninjutsu lands\n" +
                      "late - so the job loses GCD time no other melee does. The top reference parse\n" +
                      "manages only 91.7% despite 100% active time, and this simulation sits within a\n" +
                      "point of that. A Ninja at 100% would mean the mudra cost was not being modelled.\n\n"
                    : "100% means the rotation never waited: every weaponskill went out the moment its\n" +
                      "recast came up. That is the ceiling, and a real player will always sit below it.\n\n") +
                $"{result.GcdCount} GCDs over {result.Duration:F0}s.");
        }

        ImGui.SameLine();

        var delayFraction = result is null || result.Duration <= 0 ? 0 : result.TotalGcdDelay / result.Duration;

        var expectedDelay = clipsByDesign ? 0.10 : 0.02;

        var delayColor = delayFraction > expectedDelay ? Theme.Warning : Theme.Text;

        DrawStatCard("##card_delay", size, "GCD DELAY",
            result is null ? "-" : $"{result.TotalGcdDelay * progress:F1}s / {delayFraction * progress:P1}", delayColor);

        if (ImGui.IsItemHovered() && result is not null)
        {
            var why = clipsByDesign
                ? "\nOn Ninja most of this is unavoidable and is modelled on purpose. A ninjutsu's\n" +
                  "mudra presses have to happen inside the weave window before it, they can't overlap\n" +
                  "an ability's animation lock, and when they don't fit the ninjutsu lands late. The\n" +
                  "top reference parse reaches only 91.7% GCD uptime despite 100% active time, so a\n" +
                  "Ninja simulation showing no delay at all would be the suspicious one.\n"
                : string.Empty;

            ImGui.SetTooltip(
                "Total time the global cooldown spent waiting when it could have been rolling.\n" +
                why + "\n" +
                $"{result.TotalGcdDelay:F1}s here is {delayFraction:P1} of the fight, about " +
                $"{result.TotalGcdDelay / result.Stats.Gcd:F1} lost GCDs.\n" +
                $"Around {expectedDelay:P0} is expected on this job; well past that means it is clipping badly.");
        }
    }

    private void DrawStatCard(
        string id,
        Vector2 size,
        string label,
        string value,
        Vector4 valueColor,
        bool tinted = false,
        Vector4? accentOverride = null,
        StatDelta? delta = null,
        float deltaAlpha = 1f)
    {
        var accent = accentOverride ?? Theme.Accent;
        Theme.BeginCard(id, size, accent: tinted ? accent : null, gradientTint: tinted ? accent : null);

        var innerRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X - CardInnerPad;

        ImGui.TextColored(Theme.TextDim, label);

        if (delta is { } change && deltaAlpha > 0.01f)
            DrawDeltaAmount(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), change, deltaAlpha);

        using (plugin.Fonts.Numeric.PushSafe())
            ImGui.TextColored(valueColor, value);

        if (delta is { } arrowFor && deltaAlpha > 0.01f)
            DrawDeltaArrow(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), innerRight, arrowFor, deltaAlpha);

        Theme.EndCard();
    }

    /// Matches the left padding BeginCard applies, so both edges breathe equally.
    private static float CardInnerPad => UiHelpers.S(14f);

    /// Which way the number moved, and whether that was good.
    private static Vector4 DeltaColour(StatDelta delta, float alpha)
    {
        var improved = delta.Amount > 0 == delta.HigherIsBetter;
        var colour = improved ? Theme.Positive : Theme.Warning;
        colour.W = alpha;
        return colour;
    }

    /// The signed figure, immediately after the card's title.
    private static void DrawDeltaAmount(Vector2 labelMin, Vector2 labelMax, StatDelta delta, float alpha)
    {
        const float gap = 7f;

        ImGui.GetWindowDrawList().AddText(
            new Vector2(labelMax.X + gap, labelMin.Y),
            ImGui.GetColorU32(DeltaColour(delta, alpha)),
            delta.Text);
    }

    /// The arrow, sitting immediately after the value it describes.
    private static void DrawDeltaArrow(Vector2 valueMin, Vector2 valueMax, float innerRight, StatDelta delta, float alpha)
    {
        const float width = 12f;
        const float height = 13f;
        const float gap = 10f;

        var left = valueMax.X + gap;
        if (left + width > innerRight)
            return;

        var colour = DeltaColour(delta, 1f);

        UiHelpers.DrawTrendArrow(
            ImGui.GetWindowDrawList(),
            new Vector2(left + (width / 2f), (valueMin.Y + valueMax.Y) / 2f),
            width, height,
            delta.Amount > 0,
            colour,
            alpha);
    }


    private void DrawSetupTab()
    {
        ImGui.BeginChild("##setupScroll", new Vector2(0, ImGui.GetContentRegionAvail().Y - 46), false);

        SectionHeader("FIGHT");
        DrawDurationControl();
        DrawTargetControl();

        SectionHeader("PARTY");
        DrawPartySection();

        SectionHeader("CONSUMABLES");
        DrawConsumables();

        SectionHeader("GEAR");
        DrawGearSection();

        ImGui.EndChild();

        ImGui.Separator();
        ImGui.Spacing();

        const float runHeight = 34f;
        var runTop = ImGui.GetCursorPosY();

        if (EchoButton.Draw("##run", "Run Simulation", new Vector2(190, runHeight), icon: FontAwesomeIcon.Play, iconFont: plugin.Fonts.Icon))
            RunSimulation();

        if (!string.IsNullOrEmpty(statusMessage))
        {
            ImGui.SameLine();

            var available = ImGui.GetContentRegionAvail().X;
            var lines = Math.Max(1f, MathF.Ceiling(ImGui.CalcTextSize(statusMessage).X / Math.Max(1f, available)));
            var textHeight = lines * ImGui.GetTextLineHeight();

            ImGui.SetCursorPosY(runTop + ((runHeight - textHeight) / 2f));

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + available);
            ImGui.TextColored(statusColor, statusMessage);
            ImGui.PopTextWrapPos();
        }
    }

    /// Width reserved for the left-hand label column, so every control lines up.
    private const float LabelColumn = 108f;

    /// Standard width for a combo or text field in the setup form.
    private const float FieldWidth = 230f;

    /// A section heading: accent small-caps with a hairline rule running to the edge.
    private static void SectionHeader(string title)
    {
        ImGui.Dummy(new Vector2(0f, 6f));
        Theme.SectionHeader(title);
        ImGui.Dummy(new Vector2(0f, 2f));
    }

    /// Left-hand label for a form row.
    private static void FieldLabel(string label)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextDim, label);
        ImGui.SameLine(UiHelpers.S(LabelColumn));
    }

    /// A dim note under a field, indented to the field column.
    private static void FieldNote(string text, Vector4? color = null)
    {
        ImGui.Dummy(new Vector2(UiHelpers.S(LabelColumn) - ImGui.GetStyle().ItemSpacing.X, 0f));
        ImGui.SameLine();
        ImGui.TextColored(color ?? Theme.TextDim, text);
    }

    /// Fight length on one line: label, slider, typed seconds, and the mm:ss readout last.
    private static readonly float[] DurationTicks = [300f, 600f, 720f, 960f];

    /// The target-count range the model is asserted across.
    private const float MinTargets = 1f;

    private const float MaxTargets = 5f;

    /// A tick per whole enemy, so the track reads as counts rather than as a continuum.
    private static readonly float[] TargetTicks = [1f, 2f, 3f, 4f, 5f];

    private void DrawDurationControl()
    {
        FieldLabel("Length");

        var duration = (float)Config.FightDuration;

        if (EchoSlider.Draw("##duration", ref duration, 60f, 1200f, UiHelpers.S(FieldWidth - 90f),
                format: "{0:F0}s",
                resetTo: (float)Configuration.DefaultFightDuration,
                ticks: DurationTicks))
        {
            Config.FightDuration = Math.Round(duration);
            plugin.SaveConfig();
        }

        ResetOnRightClick(() =>
        {
            Config.FightDuration = Configuration.DefaultFightDuration;
            plugin.SaveConfig();
        });

        ImGui.SameLine();
        var seconds = (int)Config.FightDuration;
        ImGui.SetNextItemWidth(74);
        if (ImGui.InputInt("##durationsecs", ref seconds, 0, 0))
        {
            Config.FightDuration = Math.Clamp(seconds, 15, 3600);
            plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextDim, "s");

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Accent, PotionTimings.Format(Config.FightDuration));

        FieldLabel(string.Empty);
        foreach (var minutes in new[] { 5, 10, 12, 16 })
        {
            var presetLength = minutes * 60;
            var label = $"{minutes} min";
            var selected = Math.Abs(Config.FightDuration - presetLength) < 0.5;

            if (EchoButton.Draw($"##len{minutes}", label, new Vector2(ImGui.CalcTextSize(label).X + 20, 22), selected: selected))
            {
                Config.FightDuration = presetLength;
                plugin.SaveConfig();
            }

            ImGui.SameLine(0f, 4f);
        }

        ImGui.NewLine();
    }

    /// How many enemies the fight is against, and whether positionals are assumed to land.
    private void DrawTargetControl()
    {
        FieldLabel("Targets");

        var targets = (float)Math.Clamp(Config.Targets, MinTargets, MaxTargets);

        if (EchoSlider.Draw("##targets", ref targets, MinTargets, MaxTargets, UiHelpers.S(FieldWidth - 90f),
                format: "{0:F0}", resetTo: MinTargets, ticks: TargetTicks))
        {
            var rounded = (int)Math.Round(targets);

            if (rounded != Config.Targets)
            {
                Config.Targets = rounded;
                plugin.SaveConfig();
            }
        }

        ImGui.SameLine(0f, 10f);

        ImGui.TextUnformatted(Config.Targets >= MaxTargets
            ? $"{MaxTargets}+ targets"
            : Config.Targets == 1 ? "Single target" : $"{Config.Targets} targets");

        ImGui.NewLine();

        if (Config.Targets < ActionDef.DefaultPositionalsLostFrom)
            return;

        FieldLabel(string.Empty);

        var assume = Config.AssumePositionals;
        if (EchoToggle.Draw("##assumepositionals", "Assume positionals land", ref assume))
        {
            Config.AssumePositionals = assume;
            plugin.SaveConfig();
        }

        FieldNote(assume
            ? "Flank and rear bonuses are credited - right for stationary targets or dummies."
            : "Flank and rear bonuses are not credited at three or more enemies.");
    }

    /// Party composition and how its buffs are assumed to land.
    private void DrawPartySection()
    {
        FieldLabel(string.Empty);
        var partyBonus = Config.PartyBonus;
        if (EchoToggle.Draw("##partybonus", "Full party bonus (+5% main stat)", ref partyBonus))
        {
            Config.PartyBonus = partyBonus;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Off simulates a solo target dummy, which is what you'd actually parse against.\n" +
                "On matches the conditions published raid DPS figures are quoted under.");
        }

        FieldLabel(string.Empty);
        var usePartyBuffs = Config.UsePartyBuffs;
        if (EchoToggle.Draw("##partybuffs", "Simulate party raid buffs", ref usePartyBuffs))
        {
            Config.UsePartyBuffs = usePartyBuffs;
            plugin.SaveConfig();
        }

        if (!Config.UsePartyBuffs)
        {
            FieldNote("Off simulates a solo dummy - just your own damage.");
            return;
        }

        FieldLabel("Party");

        var layout = JobList.PartyLayoutFor(Config.SelectedJobId);
        var allJobs = JobList.All();

        for (var i = 0; i < Config.PartyJobs.Length && i < layout.Length; i++)
        {
            if (i > 0)
            {
                var newGroup = layout[i] != layout[i - 1];
                ImGui.SameLine(0f, newGroup ? 14f : 4f);
            }

            var slotRole = layout[i];
            var roleColour = RoleColour(slotRole);
            var current = Config.PartyJobs[i];
            var job = JobList.ById(current);

            if (job is { } j && j.Role != slotRole)
            {
                Config.PartyJobs[i] = 0;
                job = null;
                current = 0;
            }

            var clicked = ImGui.InvisibleButton($"##party{i}", new Vector2(UiHelpers.S(32f), UiHelpers.S(32f)));
            var hovered = ImGui.IsItemHovered();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var drawList = ImGui.GetWindowDrawList();

            if (job is not null)
            {
                drawList.AddRectFilled(min + new Vector2(0f, 2f), max + new Vector2(0f, 2f),
                    ImGui.GetColorU32(new Vector4(0f, 0f, 0f, hovered ? 0.32f : 0.22f)), 5f);
            }

            drawList.AddRectFilled(min, max,
                ImGui.GetColorU32(new Vector4(roleColour.X, roleColour.Y, roleColour.Z, hovered ? 0.28f : 0.12f)), 5f);

            var icon = GameData.Icon(job?.IconId ?? 0);
            if (icon is not null)
                drawList.AddImage(icon.Handle, min + new Vector2(1f, 1f), max - new Vector2(1f, 1f));

            if (job is null)
            {
                using (plugin.Fonts.Icon.PushSafe())
                {
                    UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.Plus, (min + max) / 2f,
                        ImGui.GetColorU32(new Vector4(roleColour.X, roleColour.Y, roleColour.Z, hovered ? 0.9f : 0.4f)));
                }
            }

            drawList.AddRect(min, max,
                ImGui.GetColorU32(new Vector4(roleColour.X, roleColour.Y, roleColour.Z, job is null ? 0.45f : 1f)),
                5f, ImDrawFlags.None, 1.6f);

            if (hovered)
            {
                if (job is { } filled)
                {
                    var buffs = PartyBuffs.ByJob.GetValueOrDefault(filled.Id, []);
                    var buffText = buffs.Length == 0
                        ? "Brings no raid buffs."
                        : string.Join("\n", buffs.Select(b => b.Name));
                    ImGui.SetTooltip($"{filled.Name}\n{buffText}\n\nRight-click to clear.");
                }
                else
                {
                    ImGui.SetTooltip($"Empty {RoleName(slotRole)} slot - click to pick a job.");
                }
            }

            if (clicked)
                ImGui.OpenPopup($"##partypick{i}");

            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                Config.PartyJobs[i] = 0;
                plugin.SaveConfig();
            }

            if (ImGui.BeginPopup($"##partypick{i}"))
            {
                ImGui.TextColored(roleColour, RoleName(slotRole));
                ImGui.Separator();

                foreach (var option in allJobs.Where(o => o.Role == slotRole))
                {
                    var buffs = PartyBuffs.ByJob.GetValueOrDefault(option.Id, []);
                    var label = buffs.Length == 0
                        ? $"{option.Name}  -  no raid buffs"
                        : $"{option.Name}  -  {string.Join(", ", buffs.Select(b => b.Name))}";

                    if (ImGui.Selectable(label, option.Id == current))
                    {
                        Config.PartyJobs[i] = option.Id;
                        plugin.SaveConfig();
                    }
                }

                ImGui.EndPopup();
            }
        }

        ImGui.NewLine();

        FieldLabel(string.Empty);
        var partner = Config.DancePartner;
        if (EchoToggle.Draw("##dancepartner", "Dance Partner", ref partner,
                "You're the Dancer's partner: Standard Finish plus Devilment on top of Technical Finish."))
        {
            Config.DancePartner = partner;
            plugin.SaveConfig();
        }

        ImGui.SameLine(0f, 18f);
        var card = Config.CardTarget;
        if (EchoToggle.Draw("##cardtarget", "Card Target", ref card,
                "You're the Astrologian's card target: 6% for 15s every 110s. The draw recasts in "
                + "55s, but the Balance goes to a melee and the Spear to a ranged, so one player "
                + "gets every other draw."))
        {
            Config.CardTarget = card;
            plugin.SaveConfig();
        }

        ImGui.SameLine(0f, 18f);
        var aligned = Config.AlignPartyBuffs;
        if (EchoToggle.Draw("##alignbuffs", "Align buffs to burst", ref aligned))
        {
            Config.AlignPartyBuffs = aligned;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "On assumes a coordinated group landing every buff on the two-minute windows.\n" +
                "Off lets each buff drift on its own cooldown - the gap between the two is\n" +
                "roughly what coordination is worth.");
        }

        var active = Config.PartyJobs.Count(j => j != 0);
        FieldNote(active == 0
            ? "No party members set - add jobs above to get their buffs."
            : $"{active} party member{(active == 1 ? string.Empty : "s")} contributing buffs.");
    }

    private void DrawConsumables()
    {
        FieldLabel("Potion");
        var grades = PotionDef.All;
        if (EchoCombo.Begin("##potiongrade", grades[Math.Clamp(Config.PotionGradeIndex, 0, grades.Length - 1)].Name, UiHelpers.S(FieldWidth)))
        {
            for (var i = 0; i < grades.Length; i++)
            {
                if (EchoCombo.Item(grades[i].Name, i == Config.PotionGradeIndex))
                {
                    Config.PotionGradeIndex = i;
                    plugin.SaveConfig();
                }
            }

            EchoCombo.End();
        }

        FieldLabel("Timings");
        var text = Config.PotionTimingsText;
        ImGui.SetNextItemWidth(UiHelpers.S(FieldWidth));
        if (ImGui.InputText("##potiontimings", ref text, 128))
        {
            Config.PotionTimingsText = text;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Minutes into the fight, separated by commas - \"0, 5, 10:30\".\n" +
                $"Gemdraughts share a {PlayerStats.PotionRecast:F0}s recast, so timings closer\n" +
                "together than that are rejected.");
        }

        FieldLabel(string.Empty);
        foreach (var plan in PotionPlan.All.Where(p => p.Times.Count > 0))
        {
            if (EchoButton.Draw($"##plan{plan.Name}", plan.Name, new Vector2(ImGui.CalcTextSize(plan.Name).X + 20, 22)))
            {
                Config.PotionTimingsText = string.Join(", ", plan.Times.Select(t => PotionTimings.Format(t)));
                plugin.SaveConfig();
            }

            ImGui.SameLine(0f, 4f);
        }

        ImGui.NewLine();

        var valid = PotionTimings.TryParse(Config.PotionTimingsText, Config.FightDuration, out var times, out var potionError);
        if (!valid)
        {
            FieldNote(potionError, Theme.Warning);
        }
        else
        {
            var usable = times.Count(t => t < Config.FightDuration);
            var note = string.IsNullOrEmpty(potionError) ? string.Empty : "  -  " + potionError;
            FieldNote(
                $"{usable} {(usable == 1 ? "potion" : "potions")} within the fight{note}",
                string.IsNullOrEmpty(potionError) ? Theme.TextDim : Theme.Warning);
        }

        FieldLabel("Food");
        var foods = GameData.Foods();
        var current = GameData.FoodById(Config.FoodItemId);

        if (EchoCombo.Begin("##food", current.Name, UiHelpers.S(FieldWidth + 90f)))
        {
            foreach (var food in foods)
            {
                if (EchoCombo.Item(food.Name, food.ItemId == Config.FoodItemId, food.IconId))
                {
                    Config.FoodItemId = food.ItemId;
                    plugin.SaveConfig();
                }
            }

            EchoCombo.End();
        }

        if (current.Params.Count > 0)
            FieldNote(current.Summary(Config.Stats));
    }

    private void DrawGearSection()
    {
        if (EchoButton.Draw("##mygear", "My Gear", new Vector2(130, 30), icon: FontAwesomeIcon.UserShield, iconFont: plugin.Fonts.Icon,
                tooltip: "Read the stats and gear you currently have equipped."))
        {
            if (GearReader.TryRead(out var read, out var error))
            {
                Config.Stats = read;
                Config.GearSelection = GearReader.ReadEquippedSelection();

                Config.MateriaSelection = GearReader.ReadEquippedMelds();

                Config.Relic.Clear();
                Config.StatsFromCharacterSheet = true;

                var directRelic = GearReader.ReadRelicSubstats();
                var inferredRelic = GearCatalog
                    .InferRelicSubstats(Config.GearSelection, Config.MateriaSelection, Config.Stats);

                Config.InferredRelicStats = (directRelic.Count > 0 ? directRelic : inferredRelic)
                    .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

                LogRelicSources(directRelic, inferredRelic);

                Config.BaselineStats = Config.Stats.Clone();
                Config.BaselineGear = new Dictionary<string, uint>(Config.GearSelection);
                Config.BaselineMateria = Config.MateriaSelection.ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value));
                Config.BaselineRelicStats = new Dictionary<string, int>(Config.InferredRelicStats);

                var relicForModel = SubStats.Parse(Config.InferredRelicStats);

                Config.ModelCorrection = GearCatalog.ModelCorrection(
                    Config.GearSelection, Config.MateriaSelection, Config.Stats,
                    JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim().MainStatModifier, relicForModel);

                Config.ModelError = GearCatalog
                    .ModelError(
                        Config.GearSelection, Config.MateriaSelection, Config.Stats,
                        JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim().MainStatModifier, relicForModel)
                    .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

                Plugin.Log.Information("EchoSim: model correction - " +
                    (Config.ModelCorrection.Count == 0
                        ? "none needed"
                        : string.Join(", ", Config.ModelCorrection.Select(kv => $"{kv.Key} {kv.Value:+#;-#}"))));

                GearCatalog.LogCaps(Config.GearSelection, Config.MateriaSelection);

                Plugin.Log.Information(Config.ModelError.Count == 0
                    ? "EchoSim: gear model reproduces the character sheet exactly."
                    : "EchoSim: gear model differs from the character sheet - " +
                      string.Join(", ", Config.ModelError.Select(kv => $"{kv.Key} {kv.Value:+#;-#}")));

                Plugin.Log.Information(Config.InferredRelicStats.Count == 0
                    ? "EchoSim: no relic substats - either no relic equipped, or everything was already accounted for."
                    : "EchoSim: relic substats in use - " +
                      string.Join(", ", Config.InferredRelicStats.Select(kv => $"{kv.Key} +{kv.Value}")));

                plugin.SaveConfig();
                statusMessage = string.IsNullOrEmpty(error) ? $"Loaded {read.Name}." : error;
                statusColor = string.IsNullOrEmpty(error) ? Theme.Accent : Theme.Warning;
            }
            else
            {
                statusMessage = error;
                statusColor = Theme.Warning;
            }
        }

        ImGui.SameLine();
        if (EchoButton.Draw("##bis", "Current BIS", new Vector2(140, 30), icon: FontAwesomeIcon.Star, iconFont: plugin.Fonts.Icon,
                tooltip: "Fill every slot with the best current-tier piece this job can equip,\n" +
                         "chosen by item level and then by stat priority, then meld it for\n" +
                         "maximum damage.\n" +
                         "It won't reproduce the off-piece choices a hand-tuned set sometimes\n" +
                         "makes to hit a speed tier."))
        {
            Config.GearSelection = GearCatalog.BestAvailable(
                JobRegistry.ForOrDefault(Config.SelectedJobId).TargetGcd is not null);

            Config.MateriaSelection.Clear();
            Config.Relic.Clear();
            Config.InferredRelicStats.Clear();

            var filled = Config.GearSelection.Count;
            if (filled == 0)
            {
                RebuildStatsFromGear();
                statusMessage = "No current-tier gear found - is the game data loaded?";
                statusColor = Theme.Warning;
            }
            else
            {
                RebuildStatsFromGear();
                OptimiseMelds(silent: true);

                var melded = Config.MateriaSelection.Sum(kv => kv.Value.Count);
                statusMessage = $"Filled {filled} slots and melded {melded} materia.";
                statusColor = Theme.Accent;
            }
        }

        ImGui.SameLine();
        if (EchoButton.Draw("##bestmelds", "Find Best Materia", new Vector2(175, 30),
                icon: FontAwesomeIcon.Magic, iconFont: plugin.Fonts.Icon,
                tooltip: "Re-meld the current set for maximum damage.\n" +
                         "Accounts for each item's stat caps and substat tier boundaries.\n" +
                         "Reverts itself if it can't beat what you already have."))
        {
            OptimiseMelds();
        }

        ImGui.Spacing();

        RefreshGearFindings();

        DrawGearFindings();

        var statColumnWidth = UiHelpers.S(260f);
        ImGui.BeginGroup();
        ImGui.BeginChild("##statcol", new Vector2(statColumnWidth, GearColumnHeight), false);

        var s = Config.Stats;
        ImGui.TextColored(Theme.TextDim, s.Name);
        ImGui.Spacing();

        var changed = false;
        changed |= StatInput("Weapon Damage", ref s.WeaponDamage);
        changed |= StatInput(GearCatalog.MainStatName, ref s.MainStat);
        changed |= StatInput("Critical Hit", ref s.Crit);
        changed |= StatInput("Determination", ref s.Determination);
        changed |= StatInput("Direct Hit", ref s.DirectHit);
        changed |= StatInput(GearCatalog.SpeedStatName, ref s.SkillSpeed);

        if (Sim.Engine.CombatRoles.UsesTenacity(
                JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim().Role))
        {
            changed |= StatInput("Tenacity", ref s.Tenacity);
        }

        if (Sim.Engine.CombatRoles.UsesPiety(
                JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim().Role))
        {
            changed |= StatInput("Piety", ref s.Piety);
        }

        var delay = (float)s.WeaponDelay;
        ImGui.SetNextItemWidth(UiHelpers.S(StatInputWidth));
        if (ImGui.InputFloat("Weapon Delay", ref delay, 0.01f, 0.1f, "%.2f"))
        {
            s.WeaponDelay = Math.Clamp(delay, 0.5, 5.0);
            changed = true;
        }

        if (changed)
            plugin.SaveConfig();

        ImGui.EndChild();
        ImGui.EndGroup();

        ColumnDivider();
        DrawDerivedStats();

        ColumnDivider();
        DrawGearColumn();

        ImGui.Spacing();
    }

    /// What the gear linter made of the current set, as a banner sitting directly above the gear itself
    /// rather than a collapsed panel at the bottom of a scroll region.
    private void DrawGearFindings()
    {
        var notable = gearFindings.Where(f => f.Severity != Severity.Info).ToList();
        if (notable.Count == 0)
            return;

        var errors = notable.Count(f => f.Severity == Severity.Error);
        var accent = errors > 0 ? Theme.Warning : Theme.Accent;

        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var width = ImGui.GetContentRegionAvail().X;

        var textWidth = Math.Max(80f, width - (14f * 2f));
        var lines = 0f;
        foreach (var finding in notable)
            lines += Math.Max(1f, MathF.Round(ImGui.CalcTextSize(finding.Message, false, textWidth).Y / lineHeight));

        var height = ((lines + 1) * lineHeight) + 20f;

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(origin, origin + new Vector2(width, height),
            ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.10f)), 6f);
        drawList.AddRectFilled(origin, origin + new Vector2(3f, height), ImGui.GetColorU32(accent), 2f);

        ImGui.BeginChild("##gearfindings", new Vector2(width, height), false, ImGuiWindowFlags.NoBackground);
        ImGui.SetCursorPos(new Vector2(14f, 8f));

        ImGui.BeginGroup();
        using (plugin.Fonts.Icon.PushSafe())
            ImGui.TextColored(accent, FontAwesomeIcon.ExclamationTriangle.ToIconString());

        ImGui.SameLine();
        ImGui.TextColored(accent, errors > 0
            ? $"{errors} problem{(errors == 1 ? string.Empty : "s")} with this gear set"
            : $"{notable.Count} thing{(notable.Count == 1 ? string.Empty : "s")} worth fixing");

        foreach (var finding in notable)
        {
            ImGui.SetCursorPosX(14f);

            ImGui.PushTextWrapPos(14f + textWidth);
            ImGui.TextColored(finding.Severity == Severity.Error ? Theme.Warning : Theme.Text, finding.Message);
            ImGui.PopTextWrapPos();
        }

        ImGui.EndGroup();
        ImGui.EndChild();

        ImGui.Spacing();
    }

    /// Slots the gear linter has something to say about, for marking their squares.
    private bool SlotHasFinding(string slotLabel, out bool isError)
    {
        isError = false;
        var any = false;

        foreach (var finding in gearFindings)
        {
            if (finding.Severity == Severity.Info || finding.Subject != slotLabel)
                continue;

            any = true;
            isError |= finding.Severity == Severity.Error;
        }

        return any;
    }

    /// What the raw stats actually turn into.
    private void DrawDerivedStats()
    {
        var width = UiHelpers.S(196f);
        ImGui.BeginChild("##derivedcol", new Vector2(width, GearColumnHeight), false);

        var stats = BuildStats();

        ImGui.TextColored(Theme.TextDim, "DERIVED");
        ImGui.Spacing();

        Row("GCD", $"{stats.Gcd:F2}s");
        Row("Auto Every", $"{stats.AutoAttackInterval:F2}s");
        Row("Crit Rate", $"{stats.CritChance:P1}");

        Row("Crit Damage", $"{stats.CritMulti:F3}x");
        Row("Direct Hit", $"{stats.DirectHitChance:P1}");
        Row("Determination", $"{stats.DetMulti:F3}");

        Row("Main Stat", $"{stats.MainStatMulti:F3}");
        Row("Weapon Damage", $"{stats.WeaponDamageMulti:F2}");
        Row("Potion", stats.PotionMainStatBonus > 0
            ? $"+{stats.PotionMainStatBonus} {GearCatalog.MainStatLabel}"
            : "none");

        ImGui.EndChild();
        return;

        static void Row(string label, string value)
        {
            ImGui.TextColored(Theme.TextDim, label);
            ImGui.SameLine();

            var valueWidth = ImGui.CalcTextSize(value).X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - valueWidth);
            ImGui.TextColored(Theme.Text, value);
        }
    }

    /// A hairline rule between two side-by-side columns, with equal padding either side.
    private static void ColumnDivider()
    {
        var gap = UiHelpers.S(13f);

        ImGui.SameLine(0f, 0f);

        var top = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(gap * 2f, GearColumnHeight));

        ImGui.GetWindowDrawList().AddLine(
            new Vector2(top.X + gap, top.Y + UiHelpers.S(6f)),
            new Vector2(top.X + gap, top.Y + GearColumnHeight - UiHelpers.S(6f)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.30f)),
            1.5f);

        ImGui.SameLine(0f, 0f);
    }

    /// Weapon and armour, in the order the character sheet lists them.
    private static readonly GearSlot[] ArmourSlots =
        [GearSlot.Weapon, GearSlot.Head, GearSlot.Body, GearSlot.Hands, GearSlot.Legs, GearSlot.Feet];

    /// The accessories, with the shield above them.
    private static GearSlot[] AccessorySlots => GearCatalog.For(GearSlot.OffHand).Count > 0
        ?
        [
            GearSlot.OffHand, GearSlot.Ears, GearSlot.Neck,
            GearSlot.Wrists, GearSlot.RingL, GearSlot.RingR,
        ]
        : [GearSlot.Ears, GearSlot.Neck, GearSlot.Wrists, GearSlot.RingL, GearSlot.RingR];

    private const float GearSquare = 42f;
    private const float GearGap = 6f;

    /// Gear laid out like the in-game character sheet: weapon and armour in one column, accessories in the
    /// other.
    private void DrawGearColumn()
    {
        ImGui.BeginChild("##gearcol", new Vector2(0, GearColumnHeight), false);

        ImGui.TextColored(Theme.TextDim, "GEAR");
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDisabled, "- click a slot to change it");
        ImGui.Spacing();

        var top = ImGui.GetCursorPos();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, UiHelpers.S(GearGap)));

        ImGui.BeginGroup();
        foreach (var slot in ArmourSlots)
            DrawGearSquare(slot);
        ImGui.EndGroup();

        ImGui.SetCursorPos(new Vector2(top.X + UiHelpers.S(GearSquare + (GearGap * 3f)), top.Y));

        ImGui.BeginGroup();
        foreach (var slot in AccessorySlots)
            DrawGearSquare(slot);
        ImGui.EndGroup();

        ImGui.PopStyleVar();
        ImGui.EndChild();
    }

    private static float GearColumnHeight => UiHelpers.S((ArmourSlots.Length * (GearSquare + GearGap)) + 40f);

    /// One gear slot as a clickable square: the item's icon, or an empty styled well.
    private void DrawGearSquare(GearSlot slot)
    {
        var piece = GearCatalog.ById(Config.GearSelection.GetValueOrDefault(GearCatalog.Label(slot)));

        var clicked = ImGui.InvisibleButton($"##slot{slot}", new Vector2(UiHelpers.S(GearSquare), UiHelpers.S(GearSquare)));

        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();

        if (piece is not null)
        {
            drawList.AddRectFilled(
                min + new Vector2(0f, 2f), max + new Vector2(0f, 2f),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, hovered ? 0.30f : 0.20f)), 6f);
        }

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Tinted(hovered ? 0.18f : 0.06f)), 6f);

        var icon = GameData.Icon(piece?.IconId ?? 0);
        if (icon is not null)
        {
            drawList.AddImage(icon.Handle, min + new Vector2(2f, 2f), max - new Vector2(2f, 2f));

            drawList.AddRectFilledMultiColor(
                new Vector2(min.X + 2f, min.Y + ((max.Y - min.Y) * 0.45f)),
                max - new Vector2(2f, 2f),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f)),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)));
        }
        else
        {
            using (plugin.Fonts.Icon.PushSafe())
            {
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.Plus, (min + max) / 2f,
                    ImGui.GetColorU32(hovered ? Theme.AccentHover : Theme.TextDisabled));
            }
        }

        var flagged = SlotHasFinding(GearCatalog.Label(slot), out var isError);
        var borderColor = flagged
            ? isError ? Theme.Warning : Theme.Accent
            : Theme.Accent;

        var borderAlpha = flagged ? 1f : hovered ? 0.95f : piece is null ? 0.22f : 0.55f;
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(borderColor.X, borderColor.Y, borderColor.Z, borderAlpha)),
            6f, ImDrawFlags.None, flagged ? 2f : 1.5f);

        if (flagged)
        {
            var dot = new Vector2(max.X - 4f, min.Y + 4f);
            drawList.AddCircleFilled(dot, 4f, ImGui.GetColorU32(isError ? Theme.Warning : Theme.Accent));
            drawList.AddCircleFilled(dot, 1.6f, ImGui.GetColorU32(Theme.Background));
        }

        if (piece is not null)
            DrawMateriaPips(drawList, min, max, slot, piece);

        if (hovered)
        {
            if (piece is not null)
            {
                DrawItemTooltip(slot, piece, Config.MateriaSelection.GetValueOrDefault(GearCatalog.Label(slot)), RelicStatsForDisplay());
            }
            else
            {
                ImGui.SetTooltip($"{GearCatalog.Label(slot)}\nEmpty - click to choose a piece.");
            }
        }

        if (clicked)
            ImGui.OpenPopup($"##pick{slot}");

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right) && piece is not null)
        {
            var label = GearCatalog.Label(slot);
            Config.GearSelection.Remove(label);
            Config.MateriaSelection.Remove(label);

            if (piece.IsCustomisableRelic)
                Config.Relic.Clear();

            RebuildStatsFromGear();
        }

        DrawGearPicker(slot);
    }

    /// A row of pips along the bottom of a slot: one per materia socket, lit when melded.
    private void DrawMateriaPips(ImDrawListPtr drawList, Vector2 min, Vector2 max, GearSlot slot, GearPiece piece)
    {
        if (piece.IsCustomisableRelic || piece.MateriaSlots <= 0)
            return;

        var melds = Config.MateriaSelection.GetValueOrDefault(GearCatalog.Label(slot));

        const float radius = 2.6f;
        const float gap = 3.4f;
        var span = (piece.MateriaSlots * radius * 2f) + ((piece.MateriaSlots - 1) * gap);

        var centre = (min.X + max.X) / 2f;
        var y = max.Y - radius - 3.5f;
        var x = centre - (span / 2f) + radius;

        drawList.AddRectFilled(
            new Vector2(centre - (span / 2f) - 4f, y - radius - 2f),
            new Vector2(centre + (span / 2f) + 4f, y + radius + 2f),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)),
            radius + 2f);

        for (var i = 0; i < piece.MateriaSlots; i++)
        {
            var filled = melds is not null && i < melds.Count && melds[i] != 0;

            if (filled)
            {
                drawList.AddCircleFilled(new Vector2(x, y), radius, ImGui.GetColorU32(Theme.Accent));
            }
            else
            {
                drawList.AddCircleFilled(new Vector2(x, y), radius, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.5f)));
                drawList.AddCircle(new Vector2(x, y), radius,
                    ImGui.GetColorU32(Theme.TextDisabled with { W = 0.85f }), 0, 1f);
            }

            x += (radius * 2f) + gap;
        }
    }

    private static void DrawItemTooltip(
        GearSlot slot,
        GearPiece piece,
        IReadOnlyList<int>? melds = null,
        IReadOnlyDictionary<string, int>? relicStats = null)
    {
        ImGui.BeginTooltip();
        ImGui.TextColored(Theme.Accent, piece.Name);
        ImGui.TextColored(Theme.TextDim, $"{GearCatalog.Label(slot)}   -   Item Level {piece.ItemLevel}");
        ImGui.Separator();

        if (piece.WeaponDamage > 0)
        {
            ImGui.TextUnformatted($"Physical Damage   {piece.WeaponDamage}");
            ImGui.TextUnformatted($"Delay             {piece.WeaponDelay:F2}s");
        }

        if (piece.MainStat > 0)
            ImGui.TextUnformatted($"{GearCatalog.MainStatLabel,-18}+{piece.MainStat}");
        if (piece.Crit > 0)
            ImGui.TextUnformatted($"Critical Hit      +{piece.Crit}");
        if (piece.Determination > 0)
            ImGui.TextUnformatted($"Determination     +{piece.Determination}");
        if (piece.DirectHit > 0)
            ImGui.TextUnformatted($"Direct Hit        +{piece.DirectHit}");
        if (piece.SkillSpeed > 0)
            ImGui.TextUnformatted($"{GearCatalog.SpeedStatName,-18}+{piece.SkillSpeed}");
        if (piece.Tenacity > 0)
            ImGui.TextUnformatted($"Tenacity          +{piece.Tenacity}");

        if (piece.Piety > 0)
            ImGui.TextUnformatted($"Piety             +{piece.Piety}");

        var isRelic = piece.IsCustomisableRelic;

        if (isRelic || piece.MateriaSlots > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Theme.TextDim, isRelic ? "Relic substats" : $"{piece.MateriaSlots} materia slot(s)");

            if (isRelic && relicStats is { Count: > 0 })
            {
                foreach (var (stat, value) in relicStats.OrderByDescending(kv => kv.Value))
                    ImGui.TextColored(Theme.Accent, $"  {stat,-4} +{value}");
            }
            else if (melds is { Count: > 0 })
            {
                foreach (var encoded in melds)
                {
                    var option = MateriaCatalog.Decode(encoded)
                                 ?? MateriaCatalog.FromEquipped(
                                     (ushort)MateriaOption.Decode(encoded).RowId,
                                     (byte)MateriaOption.Decode(encoded).Grade);

                    if (option is not { } o)
                        continue;

                    ImGui.TextColored(Theme.Accent, isRelic
                        ? $"  {GearCatalog.StatLabel(o.Stat),-4} +{o.Value}"
                        : $"  {o.Name}  +{o.Value} {GearCatalog.StatLabel(o.Stat)}");
                }
            }
            else if (isRelic)
            {
                ImGui.TextColored(Theme.TextDim, "  none chosen yet");
            }
        }

        ImGui.EndTooltip();
    }

    /// Which role stat's relic allocation was cleared for this job, so the note explaining it can stay on
    /// screen until a replacement is chosen.
    private string? relicStatCleared;

    /// The message from an exception thrown while drawing a view, or null while everything is fine.
    private string? drawFailure;

    private string gearFilter = string.Empty;

    private void DrawGearPicker(GearSlot slot)
    {
        if (!ImGui.BeginPopup($"##pick{slot}"))
            return;

        var label = GearCatalog.Label(slot);
        ImGui.TextColored(Theme.Accent, label);

        var current = GearCatalog.ById(Config.GearSelection.GetValueOrDefault(label));

        if (current is { IsCustomisableRelic: true })
        {
            ImGui.Separator();
            DrawRelicEditor();
        }

        if (current is not null && current.MateriaSlots > 0)
        {
            ImGui.Separator();
            DrawMeldEditor(label, current);
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(300);
        ImGui.InputTextWithHint("##gearfilter", "Search...", ref gearFilter, 64);
        ImGui.Separator();

        ImGui.BeginChild("##gearlist", new Vector2(360, 320), false);

        var options = GearCatalog.For(slot);
        if (options.Count == 0)
            ImGui.TextColored(Theme.TextDim, "No gear found for this slot.");

        foreach (var option in options)
        {
            if (gearFilter.Length > 0 && !option.Name.Contains(gearFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var icon = GameData.Icon(option.IconId);
            if (icon is not null)
            {
                ImGui.Image(icon.Handle, new Vector2(22, 22));
                ImGui.SameLine();
            }

            if (ImGui.Selectable($"i{option.ItemLevel}  {option.Name}##{option.ItemId}"))
            {
                var slotLabel = GearCatalog.Label(slot);
                var replacing = Config.GearSelection.GetValueOrDefault(slotLabel);

                Config.GearSelection[slotLabel] = option.ItemId;

                if (replacing != option.ItemId)
                {
                    AdoptOwnedMelds(slotLabel, option);

                    if (!option.IsCustomisableRelic)
                        Config.Relic.Clear();
                }

                RebuildStatsFromGear();
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.IsItemHovered())
                DrawItemTooltip(slot, option);
        }

        ImGui.EndChild();

        if (EchoButton.Draw($"##clear{slot}", "Clear slot", new Vector2(110, 24)))
        {
            Config.GearSelection.Remove(GearCatalog.Label(slot));
            RebuildStatsFromGear();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// Substat allocation for a customisable relic: two majors and a minor, all distinct.
    private void DrawRelicEditor()
    {
        var weapon = GearCatalog.ById(Config.GearSelection.GetValueOrDefault(GearCatalog.Label(GearSlot.Weapon)));
        var shield = GearCatalog.ById(Config.GearSelection.GetValueOrDefault(GearCatalog.Label(GearSlot.OffHand)));

        var scale = weapon?.RelicScale ?? RelicSlot.TwoHand;
        var split = shield is { IsCustomisableRelic: true };

        string Major() => split
            ? $"{RelicAllocation.MajorValue(scale)}+{RelicAllocation.MajorValue(RelicSlot.OffHand)}"
            : $"{RelicAllocation.MajorValue(scale)}";

        string Minor() => split
            ? $"{RelicAllocation.MinorValue(scale)}+{RelicAllocation.MinorValue(RelicSlot.OffHand)}"
            : $"{RelicAllocation.MinorValue(scale)}";

        ImGui.TextColored(Theme.TextDim, $"RELIC SUBSTATS  (2 x +{Major()}, 1 x +{Minor()})");

        if (split)
            ImGui.TextColored(Theme.TextDim, "  Split across the sword and shield; the total is the same.");

        var relic = Config.Relic;
        var changed = false;

        var role = JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim().Role;

        var useless = Enum.GetValues<SubStat>()
            .Where(stat => !Sim.Engine.CombatRoles.GearCarries(role, stat))
            .ToList();

        foreach (var dead in useless)
        {
            var cleared = false;

            if (relic.MajorA == (int)dead) { relic.MajorA = -1; cleared = true; }
            if (relic.MajorB == (int)dead) { relic.MajorB = -1; cleared = true; }
            if (relic.Minor == (int)dead) { relic.Minor = -1; cleared = true; }

            if (cleared)
            {
                changed = true;
                relicStatCleared = GearCatalog.StatLabel(dead);
                Plugin.Log.Information(
                    $"EchoSim: cleared a {dead} relic allocation - this job's relic cannot be given it.");
            }
        }

        changed |= RelicStatCombo("Major A", $"+{Major()}", useless, () => relic.MajorA, v => relic.MajorA = v);
        changed |= RelicStatCombo("Major B", $"+{Major()}", useless, () => relic.MajorB, v => relic.MajorB = v);
        changed |= RelicStatCombo("Minor", $"+{Minor()}", useless, () => relic.Minor, v => relic.Minor = v);

        if (relicStatCleared is not null)
        {
            if (relic.IsComplete)
                relicStatCleared = null;
            else
                ImGui.TextColored(Theme.Warning,
                    $"  This job's relic can't be given {relicStatCleared}, so it was cleared - pick another stat.");
        }

        if (!relic.IsDistinct)
            ImGui.TextColored(Theme.Warning, "  Each stat can only be chosen once.");
        else if (!relic.IsComplete && relic.AnySet)
            ImGui.TextColored(Theme.TextDim, "  Some allocations are unset.");

        if (changed)
        {
            plugin.SaveConfig();
            RebuildStatsFromGear();
        }
    }

    /// One relic allocation slot.
    private static bool RelicStatCombo(
        string label, string suffix, IReadOnlyList<SubStat> useless, Func<int> get, Action<int> set)
    {
        var currentValue = get();
        var preview = currentValue < 0 ? "Unset" : $"{GearCatalog.StatLabel((SubStat)currentValue)}  {suffix}";

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextDim, label);
        ImGui.SameLine(UiHelpers.S(80f));

        if (!EchoCombo.Begin($"##relic{label}", preview, UiHelpers.S(200f)))
            return false;

        var changed = false;

        if (EchoCombo.Item("Unset", currentValue < 0))
        {
            set(-1);
            changed = true;
        }

        foreach (var stat in Enum.GetValues<SubStat>())
        {
            if (useless.Contains(stat))
                continue;

            if (EchoCombo.Item($"{GearCatalog.StatLabel(stat)}  {suffix}", currentValue == (int)stat))
            {
                set((int)stat);
                changed = true;
            }
        }

        EchoCombo.End();
        return changed;
    }

    /// The meld editor for one slot: one dropdown per materia slot the item has.
    private void DrawMeldEditor(string slotLabel, GearPiece piece)
    {
        ImGui.TextColored(Theme.TextDim, $"MATERIA  ({piece.MateriaSlots} slot(s))");

        var melds = Config.MateriaSelection.TryGetValue(slotLabel, out var existing)
            ? new List<int>(existing)
            : [];

        var capacity = Math.Max(piece.MateriaSlots, melds.Count > piece.MateriaSlots ? piece.MaxMelds : piece.MateriaSlots);

        while (melds.Count < capacity)
            melds.Add(0);
        if (melds.Count > capacity)
            melds.RemoveRange(capacity, melds.Count - capacity);

        var changed = false;

        for (var i = 0; i < capacity; i++)
        {
            var chosen = melds[i] == 0 ? null : MateriaCatalog.Decode(melds[i]);
            var preview = chosen is { } c ? $"{GearCatalog.StatLabel(c.Stat)} +{c.Value}" : "Empty";

            if (!EchoCombo.Begin($"##meld{slotLabel}{i}", preview, UiHelpers.S(220f)))
                continue;

            if (EchoCombo.Item("Empty", melds[i] == 0))
            {
                melds[i] = 0;
                changed = true;
            }

            foreach (var option in MateriaCatalog.All())
            {
                var others = melds.Where((_, idx) => idx != i)
                    .Select(MateriaCatalog.Decode)
                    .Where(o => o is not null && o.Value.Stat == option.Stat)
                    .Sum(o => o!.Value.Value);

                var headroom = Math.Max(0, piece.CapFor(option.Stat) - piece.StatFor(option.Stat) - others);
                var effective = Math.Min(option.Value, headroom);

                var text = effective == option.Value
                    ? $"{option.Name}  (+{option.Value} {GearCatalog.StatLabel(option.Stat)})"
                    : $"{option.Name}  (+{effective} of {option.Value} - capped)";

                if (EchoCombo.Item(text, melds[i] == option.Encoded, option.IconId))
                {
                    melds[i] = option.Encoded;
                    changed = true;
                }
            }

            EchoCombo.End();
        }

        if (!changed)
            return;

        Config.MateriaSelection[slotLabel] = melds;
        RebuildStatsFromGear();
    }

    /// Recomputes the stat line from the selected gear and its melds, then runs the gear linter over the
    /// result so a bad set is caught here rather than showing up as a quietly wrong DPS number.
    private void RebuildStatsFromGear()
    {
        var job = JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim();
        var inferred = Config.InferredRelicStats.Count == 0
            ? null
            : SubStats.Parse(Config.InferredRelicStats);

        Config.Stats = GearCatalog.ToStatPreset(
            Config.GearSelection, job.MainStatModifier, "Selected gear", Config.MateriaSelection, Config.Relic, inferred);

        GearCatalog.ApplyCorrection(Config.Stats, Config.ModelCorrection);

        Config.StatsFromCharacterSheet = false;
        plugin.SaveConfig();

        RefreshGearFindings();

        foreach (var finding in gearFindings.Where(f => f.Severity != Severity.Info))
            Plugin.Log.Information($"EchoSim gear check [{finding.Severity}] {finding.Message}");

        var errors = gearFindings.ErrorCount();
        var warnings = gearFindings.WarningCount();

        statusMessage = errors + warnings == 0
            ? "Stats rebuilt from your gear and melds."
            : "Stats rebuilt.";
        statusColor = Theme.Accent;
    }

    /// Copies the melds off the player's own copy of an item, if they have one.
    private void AdoptOwnedMelds(string slotLabel, GearPiece piece)
    {
        var owned = GearReader.FindMeldsForItem(piece.ItemId);

        if (owned is { Count: > 0 })
            Config.MateriaSelection[slotLabel] = owned;
        else
            Config.MateriaSelection.Remove(slotLabel);
    }

    /// The relic's substats for display, from whichever source actually knows them.
    private Dictionary<string, int> RelicStatsForDisplay()
    {
        var result = new Dictionary<string, int>();

        if (Config.InferredRelicStats.Count > 0)
        {
            foreach (var (key, value) in Config.InferredRelicStats)
            {
                if (SubStats.TryParse(key, out var stat))
                    result[GearCatalog.StatLabel(stat)] = value;
            }

            return result;
        }

        foreach (var (stat, value) in Config.Relic.Entries())
            result[GearCatalog.StatLabel(stat)] = result.GetValueOrDefault(GearCatalog.StatLabel(stat)) + value;

        return result;
    }

    private List<Finding> gearFindings = [];

    /// Re-runs the gear check against the current selection.
    private void RefreshGearFindings()
    {
        var setup = GearSetupBuilder
            .Build(JobRegistry.ForOrDefault(Config.SelectedJobId).Name,
                Config.GearSelection, Config.MateriaSelection, Config.Relic, Config.InferredRelicStats)
            with { StatsFromCharacterSheet = Config.StatsFromCharacterSheet };

        gearFindings = GearValidator.Validate(setup);
    }

    /// Re-melds the current set for maximum damage.
    private void OptimiseMelds(bool silent = false)
    {
        var job = JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim();

        PlayerStats ToStats(StatPreset preset) => preset.ToPlayerStats(
            job,
            Config.PartyBonus, GameData.FoodById(Config.FoodItemId), Config.Potion);

        var profile = JobRegistry.AutoCritProfileFor(Config.SelectedJobId);
        var before = MeldOptimiser.DamageIndex(BuildStats(), profile);
        var previousStats = Config.Stats.Clone();
        var previousMelds = Config.MateriaSelection.ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value));
        var previousRelic = Config.Relic.Clone();
        var previousFromSheet = Config.StatsFromCharacterSheet;

        var inferred = Config.InferredRelicStats.Count == 0
            ? null
            : SubStats.Parse(Config.InferredRelicStats);

        var weapon = GearCatalog.ById(Config.GearSelection.GetValueOrDefault(GearCatalog.Label(GearSlot.Weapon)));
        var relicIsOurs = weapon is { IsCustomisableRelic: true } && Config.InferredRelicStats.Count == 0;

        if (relicIsOurs)
            Config.Relic = MeldOptimiser.OptimiseRelic(
                Config.GearSelection, Config.MateriaSelection, ToStats,
                JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim().Role, profile);

        Config.MateriaSelection = MeldOptimiser.Optimise(
            Config.GearSelection, Config.Relic, ToStats, inferred,
            JobRegistry.ForOrDefault(Config.SelectedJobId).TargetGcd, profile);

        if (relicIsOurs)
            Config.Relic = MeldOptimiser.OptimiseRelic(
                Config.GearSelection, Config.MateriaSelection, ToStats,
                JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim().Role, profile);

        RebuildStatsFromGear();

        var after = MeldOptimiser.DamageIndex(BuildStats(), profile);

        if (after < before - 1e-9)
        {
            Config.Stats = previousStats;
            Config.MateriaSelection = previousMelds;
            Config.Relic = previousRelic;
            Config.StatsFromCharacterSheet = previousFromSheet;
            plugin.SaveConfig();
            RefreshGearFindings();

            statusMessage = "Left your melds alone - nothing it tried beat what you already have.";
            statusColor = Theme.TextDim;
            return;
        }

        if (silent)
            return;

        var gain = before > 0 ? (after / before) - 1 : 0;
        var melded = Config.MateriaSelection.Sum(kv => kv.Value.Count);
        statusMessage = gain > 0
            ? $"Melded {melded} materia. Damage up {gain:0.00%}."
            : $"Melded {melded} materia. Already optimal.";
        statusColor = Theme.Accent;
    }

    /// Shared width for every stat field, so their labels form a straight column.
    private const float StatInputWidth = 140f;

    private static bool StatInput(string label, ref int value)
    {
        ImGui.SetNextItemWidth(UiHelpers.S(StatInputWidth));
        return ImGui.InputInt(label, ref value, 1, 10);
    }

    private static Vector4 RoleColour(JobRole role) => role switch
    {
        JobRole.Tank => Theme.RoleTank,
        JobRole.Healer => Theme.RoleHealer,
        _ => Theme.RoleDps,
    };

    private static string RoleName(JobRole role) => role switch
    {
        JobRole.Tank => "Tank",
        JobRole.Healer => "Healer",
        _ => "DPS",
    };


    private void DrawBreakdownTab()
    {
        if (result is null)
        {
            ImGui.TextColored(Theme.TextDim, "Run a simulation to see where the damage went.");
            return;
        }

        ImGui.Spacing();

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;

        var tableStyle = Theme.PushTableStyle();

        if (!ImGui.BeginTable("##breakdown", 5, flags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
        {
            Theme.PopTableStyle(tableStyle);
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch, 2.6f);
        ImGui.TableSetupColumn("Hits", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Damage", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableSetupColumn("DPS", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableHeadersRow();

        const float iconSize = 26f;

        foreach (var row in result.Breakdown())
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var icon = GameData.Icon(GameData.ActionIcon(row.Name));
            if (icon is not null)
            {
                ImGui.Image(icon.Handle, new Vector2(iconSize, iconSize));
                ImGui.SameLine();
            }
            else
            {
                ImGui.Dummy(new Vector2(iconSize, iconSize));
                ImGui.SameLine();
            }

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((iconSize - ImGui.GetTextLineHeight()) / 2f));
            ImGui.TextUnformatted(row.Name);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Casts.ToString());

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{row.TotalDamage:N0}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{row.TotalDamage / result.Duration:N0}");

            ImGui.TableNextColumn();
            Theme.ShareBar((float)row.DamageShare, ImGui.GetContentRegionAvail().X, $"{row.DamageShare:P1}");
        }

        ImGui.EndTable();
        Theme.PopTableStyle(tableStyle);
    }

    private void DrawTimelineTab()
    {
        if (result is null)
        {
            ImGui.TextColored(Theme.TextDim, "Run a simulation to see the rotation it played.");
            return;
        }

        ImGui.Spacing();

        var prePullSteps = JobRegistry.ForOrDefault(Config.SelectedJobId).PrePullSteps;
        if (prePullSteps.Length > 0)
        {
            var names = string.Join("  ->  ", prePullSteps.Select(s => $"{s.Timing} {s.Action}"));

            ImGui.TextColored(Theme.Accent, "Before the pull:");
            ImGui.SameLine();
            ImGui.TextColored(Theme.Text, names);

            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X - 12);
            ImGui.TextColored(Theme.TextDim,
                "Cast before the timer, so it costs no time in the fight and is not simulated below. "
                + "Anything it leaves behind - gauge, buffs, an element - the simulation starts with. "
                + "See the Opener tab for why each one is there.");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;

        var tableStyle = Theme.PushTableStyle();

        if (!ImGui.BeginTable("##timeline", 4, flags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
        {
            Theme.PopTableStyle(tableStyle);
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch, 2.4f);
        ImGui.TableSetupColumn("Damage", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Delay", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableHeadersRow();

        var clipper = new ImGuiListClipper();
        clipper.Begin(result.Timeline.Count);

        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var entry = result.Timeline[i];
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextColored(Theme.TextDim, $"{entry.Time:F2}s");

                ImGui.TableNextColumn();

                var offGcd = entry.Kind == ActionKind.OffGcd;
                if (offGcd)
                {
                    ImGui.Dummy(new Vector2(14f, 0f));
                    ImGui.SameLine(0f, 0f);
                }

                const float rowIcon = 20f;
                var actionIcon = GameData.Icon(GameData.ActionIcon(entry.Action));
                if (actionIcon is not null)
                    ImGui.Image(actionIcon.Handle, new Vector2(rowIcon, rowIcon));
                else
                    ImGui.Dummy(new Vector2(rowIcon, rowIcon));

                ImGui.SameLine();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((rowIcon - ImGui.GetTextLineHeight()) / 2f));
                ImGui.TextColored(offGcd ? Theme.TextDim : Theme.Text, entry.Action);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Damage > 0 ? $"{entry.Damage:N0}" : "-");

                ImGui.TableNextColumn();
                if (entry.GcdDelaySeconds > 0.001)
                    ImGui.TextColored(Theme.Warning, $"+{entry.GcdDelaySeconds:F2}s");
                else
                    ImGui.TextColored(Theme.TextDim, "-");
            }
        }

        clipper.End();
        ImGui.EndTable();
        Theme.PopTableStyle(tableStyle);
    }

    private void DrawBurstTab()
    {
        if (result is null || buckets.Length == 0)
        {
            ImGui.TextColored(Theme.TextDim, "Run a simulation to see the burst cycle.");
            return;
        }

        ImGui.Spacing();
        ImGui.TextColored(Theme.TextDim,
            "DPS in each 10-second slice of the fight. The regular spikes are the two-minute burst\n" +
            "windows - even, repeating peaks mean the cycle is holding together. A peak that shrinks\n" +
            "or drifts later each time means cooldowns are slipping out of alignment.");
        ImGui.Spacing();

        var peak = buckets.Max();
        ImGui.TextColored(Theme.Text, $"peak {peak:N0}");
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, $"   average {result.Dps:N0} DPS   -   {BucketSeconds:F0}s slices");
        ImGui.Spacing();

        DrawBurstChart(peak);
    }

    /// The burst histogram, drawn directly rather than through ImGui.PlotHistogram.
    private void DrawBurstChart(float peak)
    {
        if (result is null || peak <= 0)
            return;

        var size = new Vector2(ImGui.GetContentRegionAvail().X, MathF.Max(120f, ImGui.GetContentRegionAvail().Y - 12f));
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(Theme.Tinted(0.03f)), 6f);

        var top = peak * 1.1f;
        var barWidth = size.X / buckets.Length;

        var perMinute = 60.0 / BucketSeconds;
        for (var minute = 1; minute * perMinute < buckets.Length; minute++)
        {
            var x = origin.X + ((float)(minute * perMinute) * barWidth);
            drawList.AddLine(new Vector2(x, origin.Y), new Vector2(x, origin.Y + size.Y),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)));
        }

        for (var i = 0; i < buckets.Length; i++)
        {
            var height = size.Y * (buckets[i] / top);
            var min = new Vector2(origin.X + (i * barWidth), origin.Y + size.Y - height);
            var max = new Vector2(min.X + MathF.Max(1f, barWidth - 1f), origin.Y + size.Y);

            var aboveAverage = buckets[i] > result.Dps;
            var color = aboveAverage ? Theme.Accent : new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.45f);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(color), 1.5f);
        }

        var averageY = origin.Y + size.Y - (size.Y * ((float)result.Dps / top));
        drawList.AddLine(new Vector2(origin.X, averageY), new Vector2(origin.X + size.X, averageY),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)));

        ImGui.InvisibleButton("##burstchart", size);

        if (ImGui.IsItemHovered())
        {
            var index = (int)((ImGui.GetIO().MousePos.X - origin.X) / barWidth);
            if (index >= 0 && index < buckets.Length)
            {
                var from = index * BucketSeconds;
                ImGui.SetTooltip($"{PotionTimings.Format(from)} - {PotionTimings.Format(from + BucketSeconds)}\n{buckets[index]:N0} DPS");
            }
        }
    }

    /// What changed between the gear you're wearing and the set currently loaded.
    private void DrawCompareTab()
    {
        if (!Config.HasBaseline || Config.BaselineStats is not { } baseline)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.TextDim, "Press \"My Gear\" first - that becomes the baseline everything is compared against.");
            return;
        }

        ImGui.Spacing();

        var job = JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim();
        var food = GameData.FoodById(Config.FoodItemId);

        PlayerStats ToStats(StatPreset preset) => preset.ToPlayerStats(
            job, Config.PartyBonus, food, Config.Potion);

        var fromStats = ToStats(baseline);
        var toStats = ToStats(Config.Stats);

        var compareProfile = JobRegistry.AutoCritProfileFor(Config.SelectedJobId);
        var fromIndex = MeldOptimiser.DamageIndex(fromStats, compareProfile);
        var toIndex = MeldOptimiser.DamageIndex(toStats, compareProfile);
        var change = fromIndex > 0 ? (toIndex / fromIndex) - 1 : 0;

        var colour = change > 0.0001 ? Theme.Accent : change < -0.0001 ? Theme.Warning : Theme.TextDim;
        var verdict = change > 0.0001 ? "better than your gear"
            : change < -0.0001 ? "WORSE than your gear"
            : "the same as your gear";

        using (plugin.Fonts.Numeric.PushSafe())
            ImGui.TextColored(colour, $"{change:+0.00%;-0.00%;0.00%}");

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextDim, $"expected damage, {verdict}");

        ImGui.Spacing();
        ImGui.TextColored(Theme.TextDim, $"Baseline: {baseline.Name}    Current: {Config.Stats.Name}");

        if (Config.ModelError.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.Warning,
                "Model check failed - rebuilding your own gear doesn't match your character sheet:");
            ImGui.TextColored(Theme.Warning,
                "   " + string.Join(", ", Config.ModelError.Select(kv => $"{kv.Key} {kv.Value:+#;-#}")));
            ImGui.TextColored(Theme.TextDim,
                "   Substat caps are the usual cause. Until this reads zero, treat built sets as approximate.");
        }

        ImGui.Spacing();

        DrawStatComparison(baseline);
        ImGui.Spacing();
        DrawSlotComparison();
    }

    private void DrawStatComparison(StatPreset baseline)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;

        var tableStyle = Theme.PushTableStyle();

        if (!ImGui.BeginTable("##statdiff", 4, flags))
        {
            Theme.PopTableStyle(tableStyle);
            return;
        }

        ImGui.TableSetupColumn("Stat", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("Your Gear", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("This Set", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Change", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableHeadersRow();

        foreach (var stat in GearDiff.CompareStats(baseline, Config.Stats))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(stat.Name);

            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.TextDim, $"{stat.From:N0}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{stat.To:N0}");

            ImGui.TableNextColumn();
            if (stat.Delta == 0)
                ImGui.TextColored(Theme.TextDisabled, "-");
            else
                ImGui.TextColored(stat.Delta > 0 ? Theme.Accent : Theme.Warning, $"{stat.Delta:+#,##0;-#,##0}");
        }

        ImGui.EndTable();
        Theme.PopTableStyle(tableStyle);
    }

    private void DrawSlotComparison()
    {
        var diffs = GearDiff.Compare(
            Config.BaselineGear, Config.BaselineMateria,
            Config.GearSelection, Config.MateriaSelection,
            RelicSubstats(Config.BaselineRelicStats), CurrentRelicSubstats());
        var changed = diffs.Where(d => d.AnyChange).ToList();

        if (changed.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "No gear or materia differences.");
            return;
        }

        ImGui.TextColored(Theme.TextDim, $"{changed.Count} slot(s) to change:");
        ImGui.Spacing();

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;

        var tableStyle = Theme.PushTableStyle();

        var height = Math.Max(ImGui.GetContentRegionAvail().Y - 8f, ImGui.GetFrameHeight() * 3f);

        if (!ImGui.BeginTable("##slotdiff", 3, flags, new Vector2(0, height)))
        {
            Theme.PopTableStyle(tableStyle);
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Your Gear", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("Change To", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableHeadersRow();

        foreach (var diff in changed)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.TextDim, diff.Slot);

            ImGui.TableNextColumn();
            DrawDiffSide(diff.From, diff.FromMelds, diff.FromRelic, Theme.TextDim);

            ImGui.TableNextColumn();
            DrawDiffSide(diff.To, diff.ToMelds, diff.ToRelic, diff.ItemChanged ? Theme.Accent : Theme.Text);
        }

        ImGui.EndTable();
        Theme.PopTableStyle(tableStyle);
    }

    private static void DrawDiffSide(GearPiece? piece, string melds, string relic, Vector4 colour)
    {
        const float iconSize = 22f;

        var icon = GameData.Icon(piece?.IconId ?? 0);
        if (icon is not null)
            ImGui.Image(icon.Handle, new Vector2(iconSize, iconSize));
        else
            ImGui.Dummy(new Vector2(iconSize, iconSize));

        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextColored(colour, piece?.Name ?? "-");
        ImGui.TextColored(Theme.TextDisabled, string.IsNullOrEmpty(melds) ? "no materia" : melds);

        if (!string.IsNullOrEmpty(relic))
            ImGui.TextColored(Theme.TextDisabled, relic);

        ImGui.EndGroup();
    }

    /// Logs both relic readings and flags any disagreement.
    private static void LogRelicSources(
        IReadOnlyDictionary<SubStat, int> direct,
        IReadOnlyDictionary<SubStat, int> inferred)
    {
        static string Describe(IReadOnlyDictionary<SubStat, int> values)
            => values.Count == 0
                ? "nothing"
                : string.Join(", ", values.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} +{kv.Value}"));

        if (direct.Count == 0)
        {
            Plugin.Log.Information(
                $"EchoSim: relic read directly reported nothing; using inference - {Describe(inferred)}.");
            return;
        }

        var agrees = direct.Count == inferred.Count
                     && direct.All(kv => inferred.TryGetValue(kv.Key, out var v) && v == kv.Value);

        if (agrees)
        {
            Plugin.Log.Information($"EchoSim: relic read directly - {Describe(direct)} (inference agrees).");
            return;
        }

        Plugin.Log.Warning(
            $"EchoSim: relic readings disagree. Direct: {Describe(direct)}. Inferred: {Describe(inferred)}. " +
            "Using the direct read - check the gear model line below, which compares the total against " +
            "the character sheet.");
    }

    /// Parses a stored relic-substat map, which is keyed by SubStat name.
    private static Dictionary<SubStat, int>? RelicSubstats(IReadOnlyDictionary<string, int> stored)
    {
        if (stored.Count == 0)
            return null;

        var result = new Dictionary<SubStat, int>();
        foreach (var (key, value) in stored)
        {
            if (SubStats.TryParse(key, out var stat))
                result[stat] = value;
        }

        return result;
    }

    /// The selected set's relic substats, from whichever of the two sources knows them - inferred values
    /// describe a weapon read off the character, an allocation is what was chosen for a set built from the
    /// catalogue.
    private Dictionary<SubStat, int>? CurrentRelicSubstats()
    {
        if (Config.InferredRelicStats.Count > 0)
            return RelicSubstats(Config.InferredRelicStats);

        if (!Config.Relic.AnySet)
            return null;

        var result = new Dictionary<SubStat, int>();
        foreach (var (stat, value) in Config.Relic.Entries())
            result[stat] = result.GetValueOrDefault(stat) + value;

        return result;
    }

    /// The opener, as a list to follow.
    private void DrawOpenerTab()
    {
        ImGui.Spacing();
        ImGui.TextColored(Theme.TextDim,
            "The standard 7.55 opener. Off-GCDs are indented under the GCD they weave into.");
        ImGui.Spacing();

        ImGui.BeginChild("##openerscroll", new Vector2(0, ImGui.GetContentRegionAvail().Y - 4), false);

        var prePull = JobRegistry.ForOrDefault(Config.SelectedJobId).PrePullSteps;
        if (prePull.Length > 0)
            SectionHeader("PRE-PULL");

        foreach (var (timing, action, why) in prePull)
        {
            ImGui.TextColored(Theme.Accent, $"{timing,5}");
            ImGui.SameLine(64);
            ImGui.TextColored(Theme.Text, action);

            ImGui.Dummy(new Vector2(64, 0));
            ImGui.SameLine();
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X - 12);
            ImGui.TextColored(Theme.TextDim, why);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        SectionHeader("OPENER");

        var timeline = result?.Timeline;
        var steps = JobRegistry.ForOrDefault(Config.SelectedJobId).OpenerSteps;

        const float iconSize = 26f;
        var gcdNumber = 0;

        for (var i = 0; i < steps.Count; i++)
        {
            var name = steps[i];
            var table = JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim().Actions;
            var action = table.TryGetValue(name, out var def) ? def : null;
            var isGcd = action?.IsGcd ?? false;

            if (isGcd)
                gcdNumber++;

            if (!isGcd)
            {
                ImGui.Dummy(new Vector2(30f, 0));
                ImGui.SameLine(0f, 0f);
            }

            ImGui.TextColored(Theme.TextDim, isGcd ? $"{gcdNumber,2}." : "  ");
            ImGui.SameLine();

            var icon = GameData.Icon(GameData.ActionIcon(name));
            if (icon is not null)
                ImGui.Image(icon.Handle, new Vector2(iconSize, iconSize));
            else
                ImGui.Dummy(new Vector2(iconSize, iconSize));

            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((iconSize - ImGui.GetTextLineHeight()) / 2f));
            ImGui.TextColored(isGcd ? Theme.Text : Theme.TextDim, name);

            if (timeline is not null && i < timeline.Count)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.TextDisabled, $"   {timeline[i].Time:F2}s");
            }
        }

        if (timeline is null)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.TextDisabled, "Run a simulation to see the timings for your own GCD.");
        }

        ImGui.EndChild();
    }


    private string logInput = string.Empty;
    private string logStatus = string.Empty;
    private Vector4 logStatusColor = Theme.TextDim;
    private bool logBusy;

    private LogReportInfo? loadedReport;
    private int selectedFightId = -1;
    private int selectedPlayerId = -1;

    /// How each fight in the loaded report reads in the picker, keyed by fight id.
    private Dictionary<int, FightLabel> fightLabels = [];

    /// A fight's name in the picker.
    private readonly record struct FightLabel(string Short, string Full);

    /// Numbers every pull in the report the way the raid night actually ran.
    private static Dictionary<int, FightLabel> BuildFightLabels(LogReportInfo report)
    {
        var labels = new Dictionary<int, FightLabel>();

        foreach (var encounter in report.Fights.GroupBy(f => (f.Name, f.Difficulty)))
        {
            var pulls = encounter.OrderBy(f => f.StartTime).ToList();
            var kills = pulls.Count(f => f.Kill);
            var killIndex = 0;

            for (var i = 0; i < pulls.Count; i++)
            {
                var fight = pulls[i];

                if (fight.Kill)
                    killIndex++;

                var outcome = fight.Kill
                    ? kills > 1 ? $"kill {killIndex} of {kills}" : "kill"
                    : fight.BossPercentage is { } percent ? $"wipe at {percent:F1}%" : "wipe";

                var prefix = pulls.Count > 1 ? $"Pull {i + 1}  -  " : string.Empty;
                var name = $"{prefix}{fight.Name} ({fight.Difficulty}) - {outcome}";

                labels[fight.Id] = new FightLabel(
                    name,
                    $"{name}  [{Clock(fight.Duration)}]");
            }
        }

        return labels;
    }

    private FightLabel LabelFor(LogFightSummary fight)
        => fightLabels.TryGetValue(fight.Id, out var label)
            ? label
            : new FightLabel(fight.Name, fight.Name);

    /// Seconds as m:ss.
    private static string Clock(double seconds)
    {
        var whole = (int)Math.Max(0, seconds);
        return $"{whole / 60}:{whole % 60:00}";
    }

    /// A job name as the game writes it, given whatever a log called it.
    private static string JobDisplayName(string logJobName)
        => JobRegistry.ByName(logJobName)?.Name ?? logJobName;

    /// How far a finding card's text sits inside its own width, and how much room it leaves top and bottom.
    private const float CardTextInset = 38f;

    private const float CardPaddingY = 12f;

    private CombatTimeline? analysedFight;
    private List<AnalysisFinding> analysisFindings = [];

    /// The clean run the findings were paced against, kept so the potency-to-damage rate comes from the same
    /// job and fight length rather than from whatever is on the Breakdown tab.
    private SimResult? analysisReference;

    /// Whether the findings were paced against reference gear rather than the player's own.
    private bool analysisOnReferenceGear;

    /// The job the analysed player was, or null when EchoSim has no simulation for it.
    private JobDefinition? analysedJob;

    /// The player's FFLogs percentile, null when the fight isn't ranked.
    private double? analysedParse;

    private double? analysedRankedDps;

    /// When the parse first appeared, so its entrance animation runs once rather than looping.
    private float parseShownAt;
    private List<string> analysisUnmapped = [];

    /// Reads an FFLogs parse and says where damage was lost.
    private void DrawAnalysisTab()
    {
        ImGui.Spacing();

        ImGui.TextColored(Theme.TextDim, "Paste an FFLogs report link or code. Public reports only.");
        ImGui.Spacing();

        var loadLabel = logBusy ? "Loading..." : "Load Report";
        var loadSize = EchoButton.Measure(loadLabel, FontAwesomeIcon.CloudDownloadAlt, new Vector2(140, 26));

        ImGui.SetNextItemWidth(-(loadSize.X + ImGui.GetStyle().ItemSpacing.X));
        var pasted = logInput;
        if (ImGui.InputTextWithHint("##logurl", "https://www.fflogs.com/reports/...", ref pasted, 200))
            logInput = pasted;

        ImGui.SameLine();
        if (EchoButton.Draw("##fetchlog", loadLabel, loadSize,
                icon: FontAwesomeIcon.CloudDownloadAlt, iconFont: plugin.Fonts.Icon, enabled: !logBusy))
        {
            LoadReport();
        }

        if (!string.IsNullOrEmpty(logStatus))
        {
            ImGui.Spacing();
            ImGui.TextColored(logStatusColor, logStatus);
        }

        if (loadedReport is not null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawFightPicker();
        }

        if (analysedFight is null)
            return;

        ImGui.Spacing();
        ImGui.Separator();
        DrawFindings();
    }

    private void DrawFightPicker()
    {
        var report = loadedReport!;

        FieldLabel("Fight");
        var fight = report.Fights.FirstOrDefault(f => f.Id == selectedFightId);
        var fightLabel = fight is null ? "Choose a fight" : LabelFor(fight).Short;

        if (EchoCombo.Begin("##fightpick", fightLabel, UiHelpers.S(380f)))
        {
            foreach (var option in report.Fights.OrderByDescending(f => f.StartTime))
            {
                if (!EchoCombo.Item(LabelFor(option).Full, option.Id == selectedFightId))
                    continue;

                selectedFightId = option.Id;
                selectedPlayerId = GuessPlayer(option);
                analysedFight = null;
            }

            EchoCombo.End();
        }

        if (fight is null)
            return;

        FieldLabel("Player");
        var player = fight.Players.FirstOrDefault(p => p.Id == selectedPlayerId);

        if (EchoCombo.Begin("##playerpick", player is null ? "Choose a player" : $"{player.Name} ({JobDisplayName(player.Job)})", UiHelpers.S(380f)))
        {
            foreach (var option in fight.Players.OrderBy(p => JobDisplayName(p.Job)).ThenBy(p => p.Name))
            {
                if (!EchoCombo.Item($"{option.Name} ({JobDisplayName(option.Job)})", option.Id == selectedPlayerId))
                    continue;

                selectedPlayerId = option.Id;
                analysedFight = null;
            }

            EchoCombo.End();
        }

        ImGui.Spacing();
        FieldLabel(string.Empty);

        if (EchoButton.Draw("##analyse", logBusy ? "Working..." : "Analyse Fight", new Vector2(160, 28),
                icon: FontAwesomeIcon.Search, iconFont: plugin.Fonts.Icon,
                enabled: !logBusy && selectedPlayerId >= 0))
        {
            AnalyseFight();
        }
    }

    /// Defaults to the logged-in character if they're in the fight - usually who you want.
    private int GuessPlayer(LogFightSummary fight)
    {
        var me = Plugin.ObjectTable.LocalPlayer?.Name.TextValue;
        if (!string.IsNullOrEmpty(me))
        {
            var mine = fight.Players.FirstOrDefault(p => string.Equals(p.Name, me, StringComparison.OrdinalIgnoreCase));
            if (mine is not null)
                return mine.Id;
        }

        var selected = JobRegistry.For(Config.SelectedJobId);
        if (selected is not null)
        {
            var match = fight.Players.FirstOrDefault(p => JobRegistry.ByName(p.Job)?.ClassJobId == selected.ClassJobId);
            if (match is not null)
                return match.Id;
        }

        return fight.Players.FirstOrDefault(p => JobRegistry.ByName(p.Job) is not null)?.Id ?? -1;
    }

    /// Coloured text that wraps at the window edge.
    private static void TextWrappedColored(Vector4 colour, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// The player's parse, in FFLogs' own colours.
    private void DrawParse()
    {
        if (analysedParse is not { } percent)
            return;

        var band = ParseBands.For(percent);
        var baseColour = new Vector4(band.R / 255f, band.G / 255f, band.B / 255f, 1f);

        var time = (float)ImGui.GetTime();
        var age = time - parseShownAt;

        const float countUpSeconds = 0.6f;
        var shown = age < countUpSeconds ? percent * (age / countUpSeconds) : percent;

        var colour = baseColour;
        var scale = 1f;

        if (percent >= 95)
        {
            var speed = percent >= 100 ? 3.4f : percent >= 99 ? 2.8f : 2.2f;
            var pulse = (MathF.Sin(time * speed) + 1f) * 0.5f;
            var lift = percent >= 100 ? 0.35f : percent >= 99 ? 0.25f : 0.18f;

            colour = new Vector4(
                Math.Min(1f, baseColour.X + (pulse * lift)),
                Math.Min(1f, baseColour.Y + (pulse * lift)),
                Math.Min(1f, baseColour.Z + (pulse * lift)),
                1f);

            if (percent >= 100)
                scale = 1f + (pulse * 0.04f);
        }

        var label = age < countUpSeconds ? $"{shown:F0}" : Ordinal((int)percent);
        var fontSize = RestingParseSize * scale;

        Vector2 numberSize, restingSize;
        var font = ImGui.GetFont();

        using (plugin.Fonts.Display.PushSafe())
        {
            font = ImGui.GetFont();
            var natural = ImGui.CalcTextSize(label);
            numberSize = natural * (fontSize / Fonts.DisplayBakedSize);
            restingSize = natural * (RestingParseSize / Fonts.DisplayBakedSize);
        }

        var captionHeight = (ImGui.GetTextLineHeight() * 2) + ImGui.GetStyle().ItemSpacing.Y;
        var rowHeight = MathF.Max(restingSize.Y, captionHeight);

        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        ImGui.BeginGroup();

        var pos = new Vector2(
            origin.X + ((restingSize.X - numberSize.X) * 0.5f),
            origin.Y + ((rowHeight - numberSize.Y) * 0.5f));

        if (percent >= 95)
        {
            var glow = colour with { W = 0.25f };
            foreach (var offset in GlowOffsets)
                draw.AddText(font, fontSize, pos + offset, ImGui.ColorConvertFloat4ToU32(glow), label);
        }

        draw.AddText(font, fontSize, pos, ImGui.ColorConvertFloat4ToU32(colour), label);

        ImGui.Dummy(new Vector2(restingSize.X, rowHeight));
        ImGui.SameLine(0f, 12f);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((rowHeight - captionHeight) * 0.5f));

        ImGui.BeginGroup();
        ImGui.TextColored(colour, $"{band.Name} parse");
        ImGui.TextColored(Theme.TextDisabled,
            analysedRankedDps is { } rdps ? $"{rdps:N0} rDPS" : "ranked damage");
        ImGui.EndGroup();

        ImGui.EndGroup();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Your FFLogs percentile for this fight - the share of logged parses on this job and\n" +
                "encounter that you beat. It's based on rDPS, which credits the damage your buffs\n" +
                "gave the party and removes what theirs gave you.");
        }

        ImGui.Spacing();
    }

    /// The parse's size at rest.
    private const float RestingParseSize = 42f;

    /// Offsets for the cheap four-way glow behind a high parse.
    private static readonly Vector2[] GlowOffsets =
    [
        new(-2f, 0f), new(2f, 0f), new(0f, -2f), new(0f, 2f),
    ];

    /// "95th", "72nd" - the way a parse is actually said out loud.
    private static string Ordinal(int value)
    {
        var suffix = (value % 100) is >= 11 and <= 13
            ? "th"
            : (value % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

        return $"{value}{suffix}";
    }

    private void DrawFindings()
    {
        var fight = analysedFight!;

        ImGui.Spacing();
        DrawParse();

        ImGui.TextColored(Theme.TextDim,
            $"{fight.Source}   -   {Clock(fight.Duration)} " +
            $"({fight.UptimeSeconds:F0}s on target, {fight.Downtime.Count} downtime window(s))");

        if (fight.Downtime.Count > 0)
        {
            TextWrappedColored(Theme.TextDisabled,
                "Downtime is when nothing at all could be damaged, so it's an estimate. Fighting adds " +
                "isn't downtime - that damage counts toward your parse. Anything it covers isn't counted against you.");
        }

        if (analysisUnmapped.Count > 0)
        {
            TextWrappedColored(Theme.TextDisabled,
                $"Skipped {analysisUnmapped.Count} unrecognised action(s), usually items: " +
                string.Join(", ", analysisUnmapped.Take(6)) +
                (analysisUnmapped.Count > 6 ? ", ..." : string.Empty));
        }

        ImGui.Spacing();

        if (analysedJob is null)
        {
            ImGui.Spacing();
            TextWrappedColored(Theme.Warning,
                $"That player was {JobDisplayName(fight.JobName)}, which EchoSim has no simulation for yet. The " +
                "parse above is still theirs - it comes from FFLogs rather than from here - but there's no " +
                "modelled rotation to measure their cooldowns against.");
            return;
        }

        if (analysedJob.ClassJobId != Config.SelectedJobId)
        {
            TextWrappedColored(Theme.TextDisabled,
                $"Paced against {analysedJob.Name}'s rotation on reference gear, since that's the job they " +
                "played rather than the one EchoSim is set to. Switch to it on the job bar to use your own gear instead.");
        }
        else if (analysisOnReferenceGear)
        {
            TextWrappedColored(Theme.TextDisabled,
                "Paced against reference gear, because there's no gear set up on the Setup tab yet. Fill that " +
                "in to have these measured against your own stats - it changes both the GCD the pacing " +
                "assumes and what a point of potency is worth in damage.");
        }

        if (analysisFindings.Count == 0)
        {
            ImGui.TextColored(Theme.Accent, "Nothing worth flagging - that's a clean parse.");
            return;
        }

        var damagePerPotency = EstimateDamagePerPotency(analysisReference ?? result);

        var total = analysisFindings.Sum(f => f.PotencyLost);
        ImGui.TextColored(Theme.Text,
            damagePerPotency > 0
                ? $"About {total:N0} potency lost, roughly {total * damagePerPotency / fight.Duration:N0} DPS."
                : $"About {total:N0} potency lost.");

        if (analysisFindings.Any(f => !f.IsPriced))
        {
            TextWrappedColored(Theme.TextDisabled,
                "Findings shown without a damage figure are cooldowns that do no damage themselves - buffs and " +
                "resource tools. They're real losses, but what they're worth lands on other actions or on the " +
                "rest of the party, so they aren't counted in the total above.");
        }

        ImGui.Spacing();

        ImGui.BeginChild("##findings", new Vector2(0, ImGui.GetContentRegionAvail().Y - 4), false);

        var cardWidth = ImGui.GetContentRegionAvail().X;
        var wrapAt = cardWidth - CardTextInset;

        foreach (var finding in analysisFindings)
        {
            var cost = !finding.IsPriced
                ? $"{finding.UsesMissed} missed"
                : damagePerPotency > 0
                    ? $"~{finding.DpsCost(damagePerPotency, fight.Duration):N0} DPS"
                    : $"~{finding.PotencyLost:N0} potency";

            var body = ImGui.CalcTextSize(finding.Explanation, false, wrapAt).Y;
            var height = CardPaddingY + ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y
                         + body + CardPaddingY;

            Theme.BeginCard($"##finding{finding.Title}", new Vector2(-1, height), accent: Theme.Warning);

            ImGui.TextColored(Theme.Warning, finding.Title);
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, $"   {cost}");

            ImGui.PushTextWrapPos(wrapAt);
            ImGui.TextColored(Theme.Text, finding.Explanation);
            ImGui.PopTextWrapPos();

            Theme.EndCard();
            ImGui.Spacing();
        }

        ImGui.EndChild();
    }

    /// Damage per point of potency, taken from a simulation.
    private static double EstimateDamagePerPotency(SimResult? run)
    {
        if (run is null || run.Timeline.Count == 0)
            return 0;

        var totalPotency = run.Damage.Sum(d => d.Potency);
        return totalPotency > 0 ? run.TotalDamage / totalPotency : 0;
    }

    private void LoadReport()
    {
        var code = LogClient.ParseReportCode(logInput);
        if (code is null)
        {
            logStatus = "That doesn't look like an FFLogs report link or code.";
            logStatusColor = Theme.Warning;
            return;
        }

        logBusy = true;
        logStatus = "Fetching report...";
        logStatusColor = Theme.TextDim;

        _ = Task.Run(async () =>
        {
            try
            {
                var report = await LogClient.GetReportAsync(code, CancellationToken.None).ConfigureAwait(false);

                loadedReport = report;
                fightLabels = BuildFightLabels(report);
                selectedFightId = -1;
                selectedPlayerId = -1;
                analysedFight = null;

                logStatus = $"{report.Title} - {report.Fights.Count} fight(s).";
                logStatusColor = Theme.Accent;
            }
            catch (Exception ex)
            {
                loadedReport = null;
                logStatus = ex.Message;
                logStatusColor = Theme.Warning;
            }
            finally
            {
                logBusy = false;
            }
        });
    }

    /// The stats the analysed player should be judged on.
    private PlayerStats AnalysisStats(JobDefinition definition)
    {
        if (definition.ClassJobId == Config.SelectedJobId)
        {
            var own = BuildStats();

            if (own.WeaponDamage > 0)
            {
                analysisOnReferenceGear = false;
                return own;
            }
        }

        analysisOnReferenceGear = true;

        return definition.ReferenceGear().ToPlayerStats(
            definition.CreateSim(), partyBonus: false, food: FoodDef.None, potion: PotionDef.Grade4);
    }

    /// A clean run of the analysed job at that fight's own length, for the cooldown audit to count against.
    private static SimResult? ReferenceRun(JobDefinition definition, PlayerStats stats, double duration)
    {
        try
        {
            return new Simulator().Run(
                definition.CreateSim(),
                definition.CreateRotation([]),
                stats,
                duration);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Reference run for the log analysis failed");
            return null;
        }
    }

    private void AnalyseFight()
    {
        var code = loadedReport?.Code;
        if (code is null)
            return;

        var fightId = selectedFightId;
        var sourceId = selectedPlayerId;

        logBusy = true;
        logStatus = "Fetching fight...";
        logStatusColor = Theme.TextDim;

        _ = Task.Run(async () =>
        {
            try
            {
                var detail = await LogClient.GetFightAsync(code, fightId, sourceId, CancellationToken.None)
                    .ConfigureAwait(false);

                await Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    var timeline = LogTimelineBuilder.Build(detail, out var unmapped);

                    var definition = JobRegistry.ByName(timeline.JobName);

                    analysedFight = timeline;
                    analysisUnmapped = unmapped;
                    analysedJob = definition;

                    var stats = definition is null ? null : AnalysisStats(definition);
                    analysisReference = definition is null || stats is null
                        ? null
                        : ReferenceRun(definition, stats, timeline.Duration);

                    analysisFindings = definition is null || stats is null
                        ? []
                        : RotationAnalysis.Analyse(
                            timeline, stats.Gcd, definition.CreateSim(), analysisReference, stats);

                    var rateRun = analysisReference ?? result;
                    Plugin.Log.Information(
                        $"EchoSim: analysis rate - source {(analysisReference is not null ? "reference" : rateRun is not null ? "breakdown" : "none")}, " +
                        $"{EstimateDamagePerPotency(rateRun):F3} dmg/potency over {timeline.Duration:F0}s, " +
                        $"{rateRun?.Damage.Count ?? 0} damage events, " +
                        $"{rateRun?.Damage.Sum(x => x.Potency) ?? 0:N0} potency, " +
                        $"{rateRun?.TotalDamage ?? 0:N0} damage.");

                    analysedParse = detail.ParsePercent;
                    analysedRankedDps = detail.RankedDps;
                    parseShownAt = (float)ImGui.GetTime();
                }).ConfigureAwait(false);

                logStatus = $"Analysed {detail.FightName}.";
                logStatusColor = Theme.Accent;
            }
            catch (Exception ex)
            {
                analysedFight = null;
                logStatus = ex.Message;
                logStatusColor = Theme.Warning;
            }
            finally
            {
                logBusy = false;
            }
        });
    }


    /// Width of one slice in the burst chart.
    private const double BucketSeconds = 10.0;

    private PlayerStats BuildStats()
    {
        var job = JobRegistry.ForOrDefault(Config.SelectedJobId).CreateSim();
        return Config.Stats.ToPlayerStats(
            job,
            Config.PartyBonus,
            GameData.FoodById(Config.FoodItemId),
            Config.Potion);
    }

    private void RunSimulation()
    {
        try
        {
            var definition = JobRegistry.ForOrDefault(Config.SelectedJobId);
            var job = definition.CreateSim();
            var stats = BuildStats();

            var partyBuffs = Config.UsePartyBuffs
                ? PartyBuffs.Schedule(Config.PartyJobs, Config.DancePartner, Config.AlignPartyBuffs,
                    Config.FightDuration, Config.CardTarget)
                : null;

            result = new Simulator().RunBest(
                job, () => definition.CreateRotation(Config.PotionTimes), stats,
                Config.FightDuration, partyBuffs,
                Config.Targets,
                Config.AssumePositionals ? int.MaxValue : ActionDef.DefaultPositionalsLostFrom);
            buckets = result.DpsBuckets(BucketSeconds);

            previousFigures = currentFigures;
            currentFigures = new CardFigures(
                result.Dps, result.TotalDamage, result.GcdCount, result.TotalGcdDelay);

            resultGeneration++;

            GameData.LogMissingIcons(result.Breakdown().Select(b => b.Name));

            statusMessage = $"Simulated {Config.FightDuration:F0}s, {result.Timeline.Count} actions.";
            statusColor = Theme.Accent;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Simulation failed");
            statusMessage = $"Simulation failed: {ex.Message}";
            statusColor = Theme.Warning;
            result = null;
        }
    }
}
