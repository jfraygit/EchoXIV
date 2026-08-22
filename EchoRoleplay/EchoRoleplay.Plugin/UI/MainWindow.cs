using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using EchoRoleplay.Game;
using EchoRoleplay.Shared;
using EchoRoleplay.UI.Controls;

namespace EchoRoleplay.UI;

/// EchoRoleplay's window.
public sealed class MainWindow : Window
{
    /// Tab order, and the index stored in Configuration.LastTab.
    private enum Tab
    {
        Profile = 0,
        Statuses = 1,
        Contacts = 2,
    }

    /// STATUS RATHER THAN STATUSES.
    private static readonly string[] TabLabels = ["Profile", "Status", "Contacts"];

    /// The settings panel's own strip.
    private static readonly string[] SettingsTabLabels = Build.Diagnostics
        ? ["Appearance", "Profiles", "About", "Changelog", "Development"]
        : ["Appearance", "Profiles", "About", "Changelog"];

    private const int AppearanceTab = 0;
    private const int ProfilesTab = 1;
    private const int AboutTab = 2;
    private const int ChangelogTab = 3;
    private const int DevelopmentTab = 4;

    /// The two places statuses can live.
    private static readonly string[] StatusPlacementLabels = ["Under Character", "In Tooltip"];

    /// The segmented control's sliding highlight.
    private Vector2 statusPlacementSlide;

    private readonly Plugin plugin;
    private readonly EchoTabs tabs = new();
    private readonly StatusesTab statuses;
    private readonly ProfileTab profile;
    private readonly ContactsTab contacts;

    /// Its own instance, not the header's.
    private readonly EchoTabs settingsTabs = new();

    private int themeColors;

    /// Whether the settings panel is showing over the top of the tab content.
    private bool showSettings;

    /// Below this the profile form's label and field stop fitting on one line and every row starts wrapping.
    private static readonly Vector2 MinimumSize = new(480f, 400f);

    public MainWindow(Plugin plugin)
        : base("EchoRoleplay###EchoRoleplayMain", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar)
    {
        this.plugin = plugin;
        statuses = new StatusesTab(plugin);
        profile = new ProfileTab(plugin);
        contacts = new ContactsTab(plugin);
        rules = new RulesScreen(plugin);

        minimised = Config.StartMinimised;

        introRunning = !minimised;

        animatedSize = TargetSize;

        tabs.SetImmediate(Math.Clamp(Config.LastTab, 0, TabLabels.Length - 1));
    }

    private Configuration Config => plugin.Configuration;

    /// Side length of the square chrome buttons in the header row.
    private const float ChromeButtonDesign = 24f;

    private const string Wordmark = "ECHOROLEPLAY";

    /// Side length of the collapsed box, in design units.
    private const float MinimisedSize = 54f;

    /// What the collapsed box shows.
    private const FontAwesomeIcon MinimisedIcon = FontAwesomeIcon.TheaterMasks;

    /// How fast the box grows and shrinks.
    private const float ResizeLerpSpeed = 16f;

    /// The introduction, and whether it is currently running.
    private readonly IntroSequence intro = new();

    private bool introRunning;

    /// The rules, and whether this build's have been accepted.
    private readonly RulesScreen rules;

    /// The size the introduction plays at, in design units.
    private static readonly Vector2 IntroSize = new(480f, 240f);

    private bool minimised;

    /// The size actually being rendered, eased toward the target rather than snapping.
    private Vector2 animatedSize;

    private Vector2 animatedPos;
    private Vector2 targetPos;

    /// True while the window is travelling between its two modes.
    private bool animatingPosition;

    private Vector2? minimisedPressAt;


    /// The size the window is easing toward, in design units.
    private Vector2 TargetSize => introRunning
        ? IntroSize
        : minimised
            ? new Vector2(MinimisedSize, MinimisedSize)
            : BaseSize * Math.Clamp(
                Config.WindowScale, Configuration.MinimumWindowScale, Configuration.MaximumWindowScale);

    /// The window at a scale of one.
    private static readonly Vector2 BaseSize =
        new(Configuration.DefaultWindowWidth, Configuration.DefaultWindowHeight);

    /// Whether the resize has finished and the window is at its full size.
    private bool FullyExpanded => !minimised && !introRunning && animatedSize.X >= TargetSize.X * 0.92f;

    /// True once the window is expanded and nothing is moving - the only state in which the user is in charge
    /// of the size and the position.
    private bool Settled => !minimised && !introRunning && !animatingPosition && animatedSize == TargetSize;

    /// Whether the changelog holds something this player has not opened.
    private bool ChangelogUnread =>
        !string.IsNullOrEmpty(ChangelogData.LatestVersion)
        && Config.LastSeenChangelogVersion != ChangelogData.LatestVersion;

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize;

        var target = TargetSize;
        var dt = ImGui.GetIO().DeltaTime;

        animatedSize = new Vector2(
            UiHelpers.Lerp(animatedSize.X, target.X, ResizeLerpSpeed, dt),
            UiHelpers.Lerp(animatedSize.Y, target.Y, ResizeLerpSpeed, dt));

        if (Vector2.Distance(animatedSize, target) < 1f)
            animatedSize = target;

        Size = animatedSize;
        SizeCondition = ImGuiCond.Always;

        if (!FullyExpanded)
            Flags |= ImGuiWindowFlags.NoBackground;

        if (Config.WindowLocked && !minimised)
            Flags |= ImGuiWindowFlags.NoMove;

        if (introRunning)
        {
            Flags |= ImGuiWindowFlags.NoMove;

            var viewport = ImGuiHelpers.MainViewport;

            Position = viewport.Pos + (viewport.Size / 2f) - (IntroSize * ImGuiHelpers.GlobalScale / 2f);
            PositionCondition = ImGuiCond.Always;

            themeColors = Theme.Push();
            return;
        }

        ApplySavedPosition();
        themeColors = Theme.Push();
    }

    public override void PostDraw() => Theme.Pop(themeColors);

    public override void Draw()
    {
        if (introRunning)
        {
            intro.Draw(plugin.Fonts);

            if (intro.Finished)
                FinishIntro();

            return;
        }

        RememberPosition();

        if (!FullyExpanded)
        {
            DrawMinimised();
            return;
        }

        if (rules.Pending)
        {
            DrawRules();
            return;
        }

        DrawWindowBorder();

        var selected = (Tab)DrawHeader();

        if ((int)selected != Config.LastTab)
        {
            Config.LastTab = (int)selected;
            Config.Save();
        }

        if (tabs.ClickedThisFrame)
            showSettings = false;

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * tabs.ContentAlpha);

        if (ImGui.BeginChild("##echorpcontent", ImGui.GetContentRegionAvail(), false))
        {
            if (showSettings)
            {
                DrawSettings();
            }
            else
            {
                switch (selected)
                {
                    case Tab.Profile:
                        profile.Draw();
                        break;

                    case Tab.Statuses:
                        statuses.Draw();
                        break;

                    case Tab.Contacts:
                        contacts.Draw();
                        break;
                }
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    /// The collapsed box: a masks glyph in a rounded, accent-ringed square that can be parked anywhere and
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

        var span = MathF.Max(1f, (TargetSize.X - MinimisedSize) * ImGuiHelpers.GlobalScale);
        var collapsed = Math.Clamp(1f - ((size.X - (MinimisedSize * ImGuiHelpers.GlobalScale)) / span), 0f, 1f);

        using (plugin.Fonts.Icon.PushSafe())
        {
            var glyph = MinimisedIcon.ToIconString();
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
            ImGui.SetTooltip("EchoRoleplay - click to expand, drag to move");

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


    /// Ends the introduction and opens into the plugin.
    private void FinishIntro()
    {
        introRunning = false;
        animatedSize = IntroSize;
        animatedPos = ImGui.GetWindowPos();

        targetPos = Config.FullWindowX > Configuration.UnsetPosition && Config.FullWindowY > Configuration.UnsetPosition
            ? new Vector2(Config.FullWindowX, Config.FullWindowY)
            : animatedPos - ((TargetSize - IntroSize) * ImGuiHelpers.GlobalScale / 2f);

        animatingPosition = true;
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

        if (Vector2.Distance(animatedPos, targetPos) < 1f && animatedSize == TargetSize)
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

    /// The wordmark, the tab strip and the chrome buttons, and whatever the strip returns.
    private int DrawHeader()
    {
        var scale = UiHelpers.Scale;
        var chrome = ChromeButtonDesign * scale;
        var chromeGap = 6f * scale;
        var gapAfterWordmark = 20f * scale;

        var rowOrigin = ImGui.GetCursorScreenPos();
        var contentWidth = ImGui.GetContentRegionAvail().X;

        Vector2 wordmarkSize;
        using (plugin.Fonts.Header.PushSafe())
            wordmarkSize = UiHelpers.MeasureWordmark(Wordmark);

        var stripHeight = EchoTabs.MeasureHeight();
        var stripNaturalWidth = EchoTabs.MeasureWidth(TabLabels);

        const int chromeCount = 5;
        var chromeWidth = (chrome * chromeCount) + (chromeGap * (chromeCount - 1));
        var chromeX = rowOrigin.X + contentWidth - chromeWidth;

        var inlineStart = rowOrigin.X + wordmarkSize.X + gapAfterWordmark;
        var inline = inlineStart + stripNaturalWidth <= chromeX - gapAfterWordmark;

        var topRowHeight = MathF.Max(MathF.Max(wordmarkSize.Y, chrome), inline ? stripHeight : 0f);

        using (plugin.Fonts.Header.PushSafe())
        {
            UiHelpers.DrawWordmark(
                Wordmark, new Vector2(rowOrigin.X, rowOrigin.Y + ((topRowHeight - wordmarkSize.Y) / 2f)));
        }

        DrawChromeButtons(new Vector2(chromeX, rowOrigin.Y + ((topRowHeight - chrome) / 2f)), chrome, chromeGap);

        var stripOrigin = inline
            ? new Vector2(inlineStart, rowOrigin.Y + ((topRowHeight - stripHeight) / 2f))
            : new Vector2(rowOrigin.X, rowOrigin.Y + topRowHeight + (4f * scale));

        var stripWidth = inline
            ? MathF.Max(0f, chromeX - gapAfterWordmark - stripOrigin.X)
            : contentWidth;

        var selected = tabs.Draw("##echorptabs", TabLabels, stripOrigin, stripWidth);

        var headerBottom = MathF.Max(rowOrigin.Y + topRowHeight, stripOrigin.Y + stripHeight);
        ImGui.SetCursorScreenPos(new Vector2(rowOrigin.X, headerBottom + (10f * scale)));

        return selected;
    }


    /// Lock, bug, settings, minimise and close, at a screen position the caller has worked out.
    private void DrawChromeButtons(Vector2 screenPos, float buttonSize, float gap)
    {
        var cursor = ImGui.GetCursorScreenPos();
        var x = screenPos.X;

        ImGui.SetCursorScreenPos(new Vector2(x, screenPos.Y));
        var locked = Config.WindowLocked;
        if (EchoButton.BareIcon(
                "##lock", plugin.Fonts.Icon,
                locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen, buttonSize,
                locked ? "Window is pinned. Click to let it move again." : "Pin the window in place.",
                selected: locked))
        {
            Config.WindowLocked = !locked;
            Config.Save();
        }

        x += buttonSize + gap;
        ImGui.SetCursorScreenPos(new Vector2(x, screenPos.Y));
        if (EchoButton.BareIcon("##reportbug", plugin.Fonts.Icon, FontAwesomeIcon.Bug, buttonSize, "Report a bug"))
        {
            BugReporter.Reset();
            ImGui.OpenPopup(BugReportPopup);
        }

        DrawBugReportPopup();

        x += buttonSize + gap;
        ImGui.SetCursorScreenPos(new Vector2(x, screenPos.Y));
        if (EchoButton.BareIcon(
                "##settings", plugin.Fonts.Icon, FontAwesomeIcon.Cog, buttonSize,
                showSettings ? "Back" : "Settings", selected: showSettings))
        {
            showSettings = !showSettings;

            if (showSettings && ChangelogUnread)
                settingsTabs.SetImmediate(ChangelogTab);
        }

        if (ChangelogUnread)
            DrawUnreadDot(new Vector2(x + buttonSize, screenPos.Y));

        x += buttonSize + gap;
        ImGui.SetCursorScreenPos(new Vector2(x, screenPos.Y));
        if (EchoButton.BareIcon("##minimise", plugin.Fonts.Icon, FontAwesomeIcon.Compress, buttonSize, "Minimise"))
            SetMinimised(true);

        x += buttonSize + gap;
        ImGui.SetCursorScreenPos(new Vector2(x, screenPos.Y));
        if (EchoButton.BareIcon("##close", plugin.Fonts.Icon, FontAwesomeIcon.Times, buttonSize, "Close"))
            IsOpen = false;

        ImGui.SetCursorScreenPos(cursor);
    }

    /// A small accent pip on the top-right corner of the cog.
    private static void DrawUnreadDot(Vector2 corner)
    {
        var scale = UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddCircleFilled(
            corner - new Vector2(3f * scale, -3f * scale), 3.5f * scale, ImGui.GetColorU32(Theme.Accent));
    }


    /// The settings panel: a strip of its own, and whichever half it is showing.
    private void DrawSettings()
    {
        var scale = UiHelpers.Scale;

        var origin = ImGui.GetCursorScreenPos();
        var selected = settingsTabs.Draw(
            "##echorpsettingstabs", SettingsTabLabels, origin, ImGui.GetContentRegionAvail().X,
            ChangelogUnread ? ChangelogTab : -1);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + EchoTabs.MeasureHeight() + (10f * scale)));

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * settingsTabs.ContentAlpha);

        if (ImGui.BeginChild("##settingsbody", ImGui.GetContentRegionAvail(), false))
        {
            switch (selected)
            {
                case ChangelogTab:
                    DrawChangelog();
                    break;

                case ProfilesTab:
                    DrawProfileSettings();
                    break;

                case AboutTab:
                    DrawAboutSettings();
                    break;

                case DevelopmentTab:
                    DrawDevelopmentSettings();
                    break;

                default:
                    DrawAppearanceSettings();
                    break;
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    /// Air between one card and the next.
    private static void CardGap() => ImGui.Dummy(new Vector2(0f, 10f * UiHelpers.Scale));

    private const string AccentPickerPopup = "##accentpicker";

    /// How the plugin looks: its colour and how much of it there is.
    private void DrawAppearanceSettings()
    {
        var scale = UiHelpers.Scale;

        Theme.BeginPanel("##setaccent");
        Theme.PanelHeader("Accent Colour");

        var available = Theme.ContentWidth;
        var swatch = 26f * scale;
        var swatchGap = 8f * scale;
        var x = 0f;

        foreach (var (name, colour) in Theme.Presets)
        {
            var width = EchoButton.ContentSize(name).X;

            if (x > 0f && x + width > available)
                x = 0f;
            else if (x > 0f)
                ImGui.SameLine(0f, swatchGap);

            if (EchoButton.Draw($"##accent{name}", name, new Vector2(0f, swatch), colour,
                    selected: ColoursMatch(Theme.Accent, colour)))
            {
                Theme.ApplyAccent(colour);
                Config.AccentColour = colour;
                Config.Save();
            }

            x += width + swatchGap;
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        SettingsRow.Divider();

        var custom = new Vector3(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z);

        var swatchWidth = 74f * scale;
        var swatchHeight = ImGui.GetFrameHeight();

        var row = SettingsRow.Begin(
            "Custom Colour", "Anything the presets do not cover.", swatchWidth, swatchHeight);

        if (ImGui.ColorButton("##customaccent", new Vector4(custom.X, custom.Y, custom.Z, 1f),
                ImGuiColorEditFlags.NoTooltip, new Vector2(swatchWidth, swatchHeight)))
        {
            ImGui.OpenPopup(AccentPickerPopup);
        }

        if (ImGui.BeginPopup(AccentPickerPopup))
        {
            if (ImGui.ColorPicker3("##accentwheel", ref custom,
                    ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoSmallPreview))
            {
                var chosen = new Vector4(custom.X, custom.Y, custom.Z, 1f);
                Theme.ApplyAccent(chosen);
                Config.AccentColour = chosen;
                Config.Save();
            }

            ImGui.EndPopup();
        }

        SettingsRow.End(row);
        Theme.EndPanel();

        CardGap();

        Theme.BeginPanel("##setwindow");
        Theme.PanelHeader("Window");

        var sliderWidth = MathF.Min(200f * scale, Theme.ContentWidth * 0.5f);
        var windowScale = Config.WindowScale;

        var sizeRow = SettingsRow.Begin(
            "Window Size",
            "How much fits on screen at once. Dalamud's own font scale changes how big things are; "
            + "the two work together.",
            sliderWidth, EchoSlider.Height);

        if (EchoSlider.Draw("##windowscale", ref windowScale,
                Configuration.MinimumWindowScale, Configuration.MaximumWindowScale,
                sliderWidth, "{0:0.00}x", resetTo: 1f))
        {
            Config.WindowScale = windowScale;
            Config.Save();
        }

        SettingsRow.End(sizeRow);

        Theme.EndPanel();
    }

    /// What the plugin shows you of other people, and what it shows them of you.
    private void DrawProfileSettings()
    {
        var scale = UiHelpers.Scale;

        Theme.BeginPanel("##setwhoyousee");
        Theme.PanelHeader("Who You See");

        var friendsOnly = Config.OnlyShowFriends;

        if (SettingsRow.Toggle("##onlyfriends", "Only Show Friends",
                "Hides everybody who is not on your friends list - their profile, their statuses and "
                + "their roleplay name. Turning it off brings everyone back.", ref friendsOnly))
        {
            Config.OnlyShowFriends = friendsOnly;
            Config.Save();

            plugin.PlateNames.Refresh();
        }

        SettingsRow.Divider();

        var hover = Config.ShowHoverTooltip;

        if (SettingsRow.Toggle("##hovertooltip", "Hover Profiles",
                "Rest the pointer on somebody to read their profile. Waits a moment before it "
                + "appears, and never takes a click.", ref hover))
        {
            Config.ShowHoverTooltip = hover;
            Config.Save();
        }

        SettingsRow.Divider();

        var hideInDuty = Config.HideInDuty;

        if (SettingsRow.Toggle("##hideinduty", "Hide In Duties",
                "Hides statuses and hover profiles while you are in a duty. Roleplay names and your "
                + "own profile are not affected.", ref hideInDuty))
        {
            Config.HideInDuty = hideInDuty;
            Config.Save();
        }

        Theme.EndPanel();

        CardGap();

        Theme.BeginPanel("##setstatuses");
        Theme.PanelHeader("Statuses");

        var segmentSize = ProfileFields.SegmentedSize(StatusPlacementLabels);

        var placementRow = SettingsRow.Begin(
            "Where They Show",
            "The small things people are showing about themselves right now.",
            segmentSize.X, segmentSize.Y);

        var placement = ProfileFields.Segmented(
            "##statusplacement", StatusPlacementLabels, (int)Config.Statuses, ref statusPlacementSlide);

        SettingsRow.End(placementRow);

        if (placement != (int)Config.Statuses)
        {
            Config.Statuses = (StatusPlacement)placement;
            Config.Save();
        }

        var blind = Config.Statuses == StatusPlacement.InTooltip && !Config.ShowHoverTooltip;

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
        ImGui.TextColored(
            blind ? Theme.Warning : Theme.TextDim,
            Config.Statuses == StatusPlacement.UnderCharacter
                ? "An icon row under everyone who has set any."
                : blind
                    ? "Hover Profiles is off, so nothing will show them."
                    : "In the hover card, with the words that go with them.");
        ImGui.PopTextWrapPos();

        Theme.EndPanel();

        CardGap();

        Theme.BeginPanel("##setworld");
        Theme.PanelHeader("In The World");

        var plateNames = Config.ReplaceNamePlateNames;

        if (SettingsRow.Toggle("##platenames", "Roleplay Names",
                "Shows the name from a profile over the character instead of their own. Only on your "
                + "screen, only for people running EchoRoleplay, and only where a profile has a name "
                + "written.", ref plateNames))
        {
            Config.ReplaceNamePlateNames = plateNames;
            Config.Save();

            plugin.PlateNames.Refresh();
        }

        SettingsRow.Divider();

        var themes = Config.PlayThemeSongs;

        if (SettingsRow.Toggle("##themesongs", "Theme Songs",
                "Opening somebody's profile plays their theme, using the game's own music. The zone's "
                + "music comes back when you close it.", ref themes))
        {
            Config.PlayThemeSongs = themes;
            Config.Save();

            if (!themes)
                plugin.GameMusic.Stop();
        }

        Theme.EndPanel();

        _ = scale;
    }

    /// What this is, and where to go with it.
    private void DrawAboutSettings()
    {
        var scale = UiHelpers.Scale;

        Theme.BeginPanel("##setabout", Theme.Accent, gradient: true);

        UiHelpers.DrawWordmark("ECHOROLEPLAY", ImGui.GetCursorScreenPos());
        ImGui.Dummy(UiHelpers.MeasureWordmark("ECHOROLEPLAY") + new Vector2(0f, 6f * scale));

        ImGui.TextColored(Theme.TextDim, $"Version {plugin.Version}");

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
        ImGui.TextColored(Theme.TextDim,
            "A character sheet for your character, and a way to read everybody else's.");
        ImGui.PopTextWrapPos();

        Theme.EndPanel();

        CardGap();

        Theme.BeginPanel("##setcommunity");
        Theme.PanelHeader("Community");

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
        ImGui.TextColored(Theme.TextDim, "Ask a question, suggest a field, or hear about updates.");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        DrawClickableLink("Join our Discord", DiscordBlurple, DiscordInvite);

        Theme.EndPanel();
    }

    /// Everything that exists to build this plugin rather than to use it.
    private void DrawDevelopmentSettings()
    {
        if (Build.Diagnostics)
        {
            var scale = UiHelpers.Scale;

            Theme.SectionHeader("Build");
            ImGui.Dummy(new Vector2(0f, 4f * scale));

            ImGui.TextColored(Theme.TextDim, $"EchoRoleplay {plugin.Version} - development build");
            ImGui.TextColored(Theme.TextDim, $"UI scale: {UiHelpers.Scale:0.00}x");
            ImGui.TextColored(Theme.TextDim,
                $"Window: {TargetSize.X:0} x {TargetSize.Y:0} design units at {Config.WindowScale:0.00}x");

            ImGui.Dummy(new Vector2(0f, 14f * scale));
            Theme.SectionHeader("Fake Peer");
            ImGui.Dummy(new Vector2(0f, 4f * scale));

            var mirror = plugin.Directory.MirrorLocalProfile;

            if (EchoToggle.Draw("##mirror", "Show My Statuses on Everyone", ref mirror,
                    "Answers every passer-by with your own profile, so the row, the hover card and the "
                    + "sheet can be tested against somebody who is not you. It changes only what this "
                    + "client draws - nobody is being told anything."))
            {
                plugin.Directory.MirrorLocalProfile = mirror;

                plugin.PlateNames.Refresh();
            }

            if (plugin.Icons.Built)
            {
                ImGui.Dummy(new Vector2(0f, 6f * scale));
                ImGui.TextColored(Theme.TextDim,
                    $"Icons: {plugin.Icons.All.Count} usable, {plugin.Icons.Missing} named but missing");
            }

            ImGui.Dummy(new Vector2(0f, 14f * scale));
            Theme.SectionHeader("Relay");
            ImGui.Dummy(new Vector2(0f, 4f * scale));

            DrawRelayDiagnostics(scale);

            ImGui.Dummy(new Vector2(0f, 14f * scale));
            Theme.SectionHeader("World Overlay");
            ImGui.Dummy(new Vector2(0f, 4f * scale));

            DrawNamePlateDiagnostics(scale);
        }
    }

    /// The rules, in the window's own frame, scrolling.
    private void DrawRules()
    {
        var scale = UiHelpers.Scale;

        DrawWindowBorder();
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (ImGui.BeginChild("##rulesbody", ImGui.GetContentRegionAvail(), false))
            rules.Draw(scale);

        ImGui.EndChild();
    }

    /// What the relay is doing.
    private void DrawRelayDiagnostics(float scale)
    {
        if (Build.Diagnostics)
        {
            var relay = plugin.Relay;

            ImGui.TextColored(Theme.TextDim, $"Relay: {RelayEndpoints.BaseUrl}");

            ImGui.TextColored(
                relay.Reached ? Theme.TextDim : Theme.Warning,
                relay.Reached
                    ? $"Index: {relay.KnownProfiles} published nearby, {relay.CardsHeld} card(s) held"
                    : "Index: no answer from the relay yet");

            ImGui.Dummy(new Vector2(0f, 6f * scale));

            ImGui.TextColored(Theme.TextDim, $"Owner key: {Config.OwnerKey}");

            ImGui.Dummy(new Vector2(0f, 4f * scale));

            if (EchoButton.Draw("##newownerkey", "New Owner Key", new Vector2(170f * scale, 26f * scale),
                    tooltip: "Pretend to be a second installation, so friendships and claims can be tested "
                             + "from one PC. Anything published from here is withdrawn first."))
            {
                RotateOwnerKey();
            }

            ImGui.Dummy(new Vector2(0f, 6f * scale));

            if (EchoButton.Draw("##showrules", "Show Rules Again", new Vector2(170f * scale, 26f * scale),
                    tooltip: "Forgets that these rules were accepted, so they show on the next open."))
            {
                Config.AcceptedRulesVersion = null;
                Config.Save();
            }
        }
    }

    /// Becomes a different installation, as far as the relay is concerned.
    private void RotateOwnerKey()
    {
        foreach (var id in Config.PublishedIds.Values.ToList())
        {
            if (id.Length > 0)
                _ = plugin.RelayClient.WithdrawAsync(id, System.Threading.CancellationToken.None);
        }

        Config.PublishedIds.Clear();
        Config.OwnerKey = Guid.NewGuid().ToString("N");
        Config.Save();

        plugin.Friends.Sync();
    }

    /// What the phase 2 binding currently thinks it is looking at.
    private void DrawNamePlateDiagnostics(float scale)
    {
        if (Build.Diagnostics)
        {
            var plates = plugin.NamePlates;

            ImGui.Dummy(new Vector2(0f, 8f * scale));

            var show = Config.ShowStatusRowSpike;
            if (EchoToggle.Draw("##statusrowspike", "Status Row Spike", ref show))
            {
                Config.ShowStatusRowSpike = show;
                Config.Save();
            }

            ImGui.Dummy(new Vector2(0f, 4f * scale));

            var mark = Config.MarkStatusRowAnchor;
            if (EchoToggle.Draw("##markanchor", "Mark Projected Anchor", ref mark,
                    "Draws a red cross on the exact projected point, with no offset. If the cross sits on "
                    + "the character's feet at every zoom, the projection is right and anything that looks "
                    + "wrong is in what is drawn around it."))
            {
                Config.MarkStatusRowAnchor = mark;
                Config.Save();
            }

            ImGui.Dummy(new Vector2(0f, 4f * scale));

            ImGui.Dummy(new Vector2(0f, 4f * scale));

            var avoidUi = Config.AvoidGameUi;
            if (EchoToggle.Draw("##avoidui", "Avoid Game UI", ref avoidUi,
                    "Withholds a row that would land on the game's own interface. The character really "
                    + "is behind the hotbar, so not drawing their row is the honest depiction of it."))
            {
                Config.AvoidGameUi = avoidUi;
                Config.Save();
            }

            ImGui.Dummy(new Vector2(0f, 4f * scale));

            var snap = Config.SnapStatusRowToPixels;
            if (EchoToggle.Draw("##snappixels", "Snap To Pixels", ref snap,
                    "Rounds the row to whole pixels. Try turning this OFF if there is still a small "
                    + "wobble - rounding a smoothed position can make it flip between two pixels, which "
                    + "no amount of extra smoothing will fix because the smoothing is not what is "
                    + "stepping."))
            {
                Config.SnapStatusRowToPixels = snap;
                Config.Save();
            }

            ImGui.Dummy(new Vector2(0f, 6f * scale));

            ImGui.TextColored(Theme.TextDim, "Position Smoothing (0 = Off)");

            var smooth = Config.StatusRowSmoothing;
            if (EchoSlider.Draw(
                    "##smoothrow", ref smooth, 0f, StatusRowOverlay.MaximumSmoothing,
                    MathF.Min(220f * scale, ImGui.GetContentRegionAvail().X),
                    "{0:0.00}", resetTo: 0.5f))
            {
                Config.StatusRowSmoothing = smooth;
                Config.Save();
            }

            ImGui.Dummy(new Vector2(0f, 6f * scale));

            ImGui.TextColored(Theme.TextDim, "Icon Size");

            var iconSize = Config.StatusRowIconSize;
            if (EchoSlider.Draw(
                    "##statusrowiconsize", ref iconSize, 12f, 56f,
                    MathF.Min(220f * scale, ImGui.GetContentRegionAvail().X),
                    "{0:0} px", resetTo: Configuration.DefaultIconSize))
            {
                Config.StatusRowIconSize = iconSize;
                Config.Save();
            }

            ImGui.Dummy(new Vector2(0f, 6f * scale));

            ImGui.TextColored(Theme.TextDim, "Draw Distance (Yalms)");

            var distance = Config.StatusRowMaxDistance;
            if (EchoSlider.Draw(
                    "##statusrowdistance", ref distance, 5f, 100f,
                    MathF.Min(220f * scale, ImGui.GetContentRegionAvail().X),
                    "{0:0} y", resetTo: 40f))
            {
                Config.StatusRowMaxDistance = distance;
                Config.Save();
            }

            ImGui.Dummy(new Vector2(0f, 6f * scale));

            ImGui.TextColored(Theme.TextDim, "Row Drop");

            var offset = Config.StatusRowYOffset;
            if (EchoSlider.Draw(
                    "##statusrowoffset", ref offset, -30f, 60f,
                    MathF.Min(220f * scale, ImGui.GetContentRegionAvail().X),
                    "{0:0} px", resetTo: Configuration.DefaultRowDrop))
            {
                Config.StatusRowYOffset = offset;
                Config.Save();
            }

            ImGui.Dummy(new Vector2(0f, 6f * scale));

            ImGui.TextColored(Theme.TextDim, $"Handlers offered: {plates.LastHandlerCount}");
            ImGui.TextColored(Theme.TextDim, $"Player plates: {plates.LastPlayerPlateCount}");
            ImGui.TextColored(Theme.TextDim, $"Players nearby: {plugin.StatusRow.LastNearbyPlayers}");
            ImGui.TextColored(Theme.TextDim, $"Hidden by line of sight: {plugin.StatusRow.LastOccluded}");
            ImGui.TextColored(Theme.TextDim,
                $"Hidden behind game UI: {plugin.StatusRow.LastBehindUi} ({plugin.StatusRow.UiRegionCount} regions, {plugin.StatusRow.UiNodesVisited} nodes)");

            ImGui.TextColored(Theme.TextDim, $"Rows drawn last frame: {plugin.StatusRow.LastDrawnRows}");
            ImGui.TextColored(Theme.TextDim, $"Data updates seen: {plates.UpdatesSeen}");

            ImGui.TextColored(Theme.TextDim,
                $"Plate names written: {plugin.PlateNames.LastNamesWritten}");

            var track = plugin.GameMusic.NowPlaying;

            ImGui.TextColored(
                track != 0 ? Theme.Good : Theme.TextDim,
                track != 0
                    ? $"Theme song: playing {track} ({plugin.Music.NameOf(track)})"
                    : "Theme song: silent");

            ImGui.TextColored(
                plugin.Tooltip.Showing ? Theme.Good : Theme.TextDim,
                plugin.Tooltip.Showing ? "Hover card: showing" : "Hover card: nothing under the pointer");
            ImGui.TextColored(Theme.TextDim, $"Plate updates seen: {plugin.PlateNames.UpdatesSeen}");

            if (EchoButton.Draw("##redrawplates", "Redraw Nameplates", new Vector2(0f, 24f * scale),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Sync,
                    tooltip: "Forces every plate to rebuild. If something appears only after pressing "
                        + "this, the write is fine and something is not asking for a redraw when it "
                        + "should."))
            {
                plugin.PlateNames.Refresh();
            }

            ImGui.TextColored(
                plugin.Sight.CollisionAvailable ? Theme.Good : Theme.Warning,
                plugin.Sight.CollisionAvailable
                    ? $"World collision: reachable ({plugin.Sight.LastRaysCast} rays last frame)"
                    : "World collision: unreachable - everyone counts as visible");


            DrawUiScanReport(scale);
        }
    }

    /// Why each addon was accepted or rejected by the interface scan.
    private void DrawUiScanReport(float scale)
    {
        if (Build.Diagnostics)
        {
            ImGui.Dummy(new Vector2(0f, 8f * scale));

            var explain = plugin.StatusRow.ExplainUiScan;
            if (EchoToggle.Draw("##explainscan", "Explain UI Scan", ref explain,
                    "Lists every addon the interface scan looked at and what it decided, so a missing "
                    + "region can be traced to the check that dropped it."))
            {
                plugin.StatusRow.ExplainUiScan = explain;
            }

            if (!explain)
                return;

            ImGui.Dummy(new Vector2(0f, 4f * scale));

            if (EchoButton.Draw("##copyscan", "Copy Scan Report", new Vector2(0f, 26f * scale),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Copy))
            {
                var report = plugin.StatusRow.UiScanReport;
                var text = new System.Text.StringBuilder();

                text.AppendLine(
                    $"regions={plugin.StatusRow.UiRegionCount} nodes={plugin.StatusRow.UiNodesVisited} entries={report.Count}");

                foreach (var line in report)
                    text.AppendLine(line);

                ImGui.SetClipboardText(text.ToString());
            }

            ImGui.Dummy(new Vector2(0f, 4f * scale));

            if (ImGui.BeginChild("##uiscanreport", new Vector2(0f, 200f * scale), true))
            {
                foreach (var line in plugin.StatusRow.UiScanReport)
                {
                    ImGui.TextColored(line.Contains(": claimed") ? Theme.Good : Theme.TextDim, line);
                }
            }

            ImGui.EndChild();
        }
    }

    private const string BugReportPopup = "##bugreport";

    private string bugReport = string.Empty;

    /// Kept across sends on purpose - see the note where it is cleared.
    private string bugReportName = string.Empty;

    /// The bug report form.
    private void DrawBugReportPopup()
    {
        if (!ImGui.BeginPopup(BugReportPopup))
            return;

        var scale = UiHelpers.Scale;
        var width = 280f * scale;

        ImGui.TextColored(Theme.Accent, "Report a Bug");

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(Theme.TextDim, "Goes to the EchoRoleplay Discord with your version attached.");
        ImGui.PopTextWrapPos();

        ImGui.Spacing();

        UiHelpers.WrappingMultiline(
            "##bugtext", ref bugReport, BugReporter.MaximumDescription, new Vector2(width, 72f * scale));

        ImGui.Spacing();

        ImGui.TextColored(Theme.TextDim, "Name or Discord Handle");
        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##bugname", "Optional",
            ref bugReportName, BugReporter.MaximumName);

        ImGui.Spacing();

        var sending = BugReporter.State == BugReporter.SendState.Sending;
        if (EchoButton.Draw("##sendbug", sending ? "Sending..." : "Send", new Vector2(100f * scale, 26f * scale),
                enabled: !sending && bugReport.Trim().Length > 0))
        {
            BugReporter.Send(BuildBugReport(), Plugin.PluginInterface.GetPluginConfigDirectory());
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
    private BugReport BuildBugReport() => new()
    {
        Description = UiHelpers.Unwrap(bugReport, 280f * UiHelpers.Scale).Trim(),
        Name = bugReportName.Trim(),
        Version = plugin.Version,
        Relay = RelayEndpoints.UsingLive ? "live" : "dev",
        Tab = TabLabels[Math.Clamp(tabs.Selected, 0, TabLabels.Length - 1)],
        Scale = UiHelpers.Scale,
    };

    /// Discord's own brand colour, so the link reads as a Discord link before it is read as words.
    private static readonly Vector4 DiscordBlurple = new(0.345f, 0.396f, 0.949f, 1f);

    private const string DiscordInvite = "https://discord.gg/nJauXrNWx3";

    /// Coloured text that opens a URL, underlined under the pointer.
    private static void DrawClickableLink(string label, Vector4 colour, string url)
    {
        var start = ImGui.GetCursorScreenPos();
        var hovered = ImGui.IsMouseHoveringRect(start, start + ImGui.CalcTextSize(label));

        ImGui.TextColored(colour, label);

        if (!hovered)
            return;

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), ImGui.GetColorU32(colour));

        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoRoleplay] Failed to open the Discord invite.");
        }
    }

    /// The release notes, newest first.
    private void DrawChangelog()
    {
        var scale = UiHelpers.Scale;

        if (ChangelogData.Entries.Length == 0)
        {
            ImGui.TextColored(Theme.TextDim, "Nothing here yet - this is where update notes will appear.");
            return;
        }

        if (ChangelogUnread)
        {
            Config.LastSeenChangelogVersion = ChangelogData.LatestVersion;
            Config.Save();
        }

        foreach (var entry in ChangelogData.Entries)
        {
            ImGui.TextColored(Theme.Accent, entry.Version);
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            foreach (var line in entry.Highlights)
            {
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextUnformatted(line);
            }

            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, 8f * scale));
        }
    }

    /// Whether two accents are the same to the eye.
    private static bool ColoursMatch(Vector4 a, Vector4 b) =>
        MathF.Abs(a.X - b.X) < 0.01f && MathF.Abs(a.Y - b.Y) < 0.01f && MathF.Abs(a.Z - b.Z) < 0.01f;
}
