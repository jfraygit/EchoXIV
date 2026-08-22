using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using EchoGlam.Game;
using EchoGlam.UI.Controls;

namespace EchoGlam.UI;

/// EchoGlam's window: a wardrobe on one tab and a catalogue on the next.
public sealed class MainWindow : Window
{
    /// Tab order, and the index stored in Configuration.LastTab.
    private enum Tab
    {
        DressingRoom = 0,
        Gallery = 1,
        Profile = 2,
        Friends = 3,
    }

    private static readonly string[] TabLabels = ["Dressing Room", "Gallery", "Profile", "Friends"];

    /// The settings panel's own strip.
    private static readonly string[] SettingsTabLabels = ["General", "Changelog"];

    private const int ChangelogTab = 1;

    private readonly Plugin plugin;
    private readonly EchoTabs tabs = new();

    /// Its own instance, not the header's.
    private readonly EchoTabs settingsTabs = new();
    private readonly DressingRoomTab dressingRoom;
    private readonly GalleryTab gallery;
    private readonly ProfileTab profile;
    private readonly FriendsTab friends;
    private readonly SubmitPane submit;
    private int themeColors;

    /// Whether the settings panel is showing over the top of the tab content.
    private bool showSettings;

    /// Below this the gallery grid drops under two columns and the settings rows start wrapping every line.
    private static readonly Vector2 MinimumSize = new(460f, 380f);

    public MainWindow(Plugin plugin)
        : base("EchoGlam###EchoGlamMain", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar)
    {
        this.plugin = plugin;
        dressingRoom = new DressingRoomTab(plugin);
        gallery = new GalleryTab(plugin);
        profile = new ProfileTab(plugin, gallery.Images);
        friends = new FriendsTab(plugin);
        submit = new SubmitPane(plugin, plugin.Screenshots, gallery.Images);

        gallery.OpenProfile += id =>
        {
            profile.View(id, () => tabs.SetImmediate((int)Tab.Gallery));
            tabs.SetImmediate((int)Tab.Profile);
        };

        gallery.Publish += () => submit.OpenForCurrentLook();
        gallery.Edit += entry => submit.OpenForEdit(entry);

        profile.OpenGlamour += id =>
        {
            gallery.Show(id, () => tabs.SetImmediate((int)Tab.Profile));
            tabs.SetImmediate((int)Tab.Gallery);
        };

        submit.Published += _ =>
        {
            submit.Close();
            gallery.Reload();
        };

        submit.Removed += _ =>
        {
            submit.Close();
            gallery.Reload();
        };

        minimised = Config.StartMinimised;

        introRunning = !minimised;

        animatedSize = TargetSize;

        tabs.SetImmediate(Math.Clamp(Config.LastTab, 0, TabLabels.Length - 1));
    }

    private Configuration Config => plugin.Configuration;

    /// Side length of the square chrome buttons in the header row.
    private const float ChromeButtonDesign = 24f;

    private const string Wordmark = "ECHOGLAM";

    /// Side length of the collapsed box, in design units.
    private const float MinimisedSize = 54f;

    /// What the collapsed box shows.
    private const FontAwesomeIcon MinimisedIcon = FontAwesomeIcon.Tshirt;

    /// How fast the box grows and shrinks.
    private const float ResizeLerpSpeed = 16f;

    /// The first-run introduction, and whether it is currently running.
    private readonly IntroSequence intro = new();

    private bool introRunning;

    /// The size the introduction plays at, in design units.
    private static readonly Vector2 IntroSize = new(420f, 230f);

    private bool minimised;

    /// The size actually being rendered, eased toward the target rather than snapping.
    private Vector2 animatedSize;

    private Vector2 animatedPos;
    private Vector2 targetPos;

    /// True while the window is travelling between its two modes.
    private bool animatingPosition;

    private Vector2? minimisedPressAt;

    /// Assert the stored size for exactly one frame after the window opens.
    private bool assertSizeOnce = true;

    public override void OnOpen() => assertSizeOnce = true;

    /// Releases what the tabs hold.
    public void Dispose()
    {
        gallery.Dispose();
        submit.Dispose();
    }

    /// The size the window is easing toward, in design units.
    private Vector2 TargetSize => introRunning
        ? IntroSize
        : minimised
            ? new Vector2(MinimisedSize, MinimisedSize)
            : new Vector2(Config.WindowWidth, Config.WindowHeight);

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
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar;

        var target = TargetSize;
        var dt = ImGui.GetIO().DeltaTime;

        animatedSize = new Vector2(
            UiHelpers.Lerp(animatedSize.X, target.X, ResizeLerpSpeed, dt),
            UiHelpers.Lerp(animatedSize.Y, target.Y, ResizeLerpSpeed, dt));

        if (Vector2.Distance(animatedSize, target) < 1f)
            animatedSize = target;

        ApplyConstraints();

        if (assertSizeOnce)
        {
            Size = animatedSize;
            SizeCondition = ImGuiCond.Always;
            assertSizeOnce = false;
        }
        else if (Settled)
        {
            Size = null;
        }
        else
        {
            Size = animatedSize;
            SizeCondition = ImGuiCond.Always;
            Flags |= ImGuiWindowFlags.NoResize;
        }

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
        if (!plugin.Screenshots.ShouldDraw())
            return;

        if (introRunning)
        {
            intro.Draw(plugin.Fonts);

            if (intro.Finished)
                FinishIntro();

            return;
        }

        RememberPosition();
        RememberSize();

        if (!FullyExpanded)
        {
            DrawMinimised();
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

        if (ImGui.BeginChild("##echoglamcontent", ImGui.GetContentRegionAvail(), false))
        {
            if (showSettings)
            {
                DrawSettings();
            }
            else if (submit.IsOpen)
            {
                submit.Draw();
            }
            else
            {
                switch (selected)
                {
                    case Tab.DressingRoom:
                        dressingRoom.Draw();
                        break;

                    case Tab.Gallery:
                        gallery.Draw();
                        break;

                    case Tab.Friends:
                        friends.Draw();
                        break;

                    case Tab.Profile:
                        profile.Draw();
                        break;
                }
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    /// The collapsed box: a shirt glyph in a rounded, accent-ringed square that can be parked anywhere and
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

        var span = MathF.Max(1f, (Config.WindowWidth - MinimisedSize) * ImGuiHelpers.GlobalScale);
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
            ImGui.SetTooltip("EchoGlam - click to expand, drag to move");

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

    /// The minimum size, but only while the window is a window.
    private void ApplyConstraints()
    {
        if (minimised || !FullyExpanded)
        {
            SizeConstraints = null;
            return;
        }

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = MinimumSize,
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    /// Ends the introduction and opens into the plugin.
    private void FinishIntro()
    {
        introRunning = false;
        animatedSize = IntroSize;
        animatedPos = ImGui.GetWindowPos();

        targetPos = Config.FullWindowX > Configuration.UnsetPosition && Config.FullWindowY > Configuration.UnsetPosition
            ? new Vector2(Config.FullWindowX, Config.FullWindowY)
            : animatedPos - ((new Vector2(Config.WindowWidth, Config.WindowHeight) - IntroSize) * ImGuiHelpers.GlobalScale / 2f);

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

        var selected = tabs.Draw("##echoglamtabs", TabLabels, stripOrigin, stripWidth);

        var headerBottom = MathF.Max(rowOrigin.Y + topRowHeight, stripOrigin.Y + stripHeight);
        ImGui.SetCursorScreenPos(new Vector2(rowOrigin.X, headerBottom + (10f * scale)));

        return selected;
    }

    /// Writes the dragged size back to the configuration.
    private void RememberSize()
    {
        if (!Settled)
            return;

        var live = ImGui.GetWindowSize() / MathF.Max(0.01f, ImGuiHelpers.GlobalScale);

        if (MathF.Abs(live.X - Config.WindowWidth) < 1f && MathF.Abs(live.Y - Config.WindowHeight) < 1f)
            return;

        Config.WindowWidth = live.X;
        Config.WindowHeight = live.Y;

        animatedSize = new Vector2(Config.WindowWidth, Config.WindowHeight);

        Config.Save();
    }

    /// Lock, settings, minimise and close, at a screen position the caller has worked out.
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

    /// A tab that exists in the strip but not yet in the plugin.
    private static void DrawPlaceholder(string title, string what, string status)
    {
        var scale = UiHelpers.Scale;

        ImGui.Dummy(new Vector2(0f, 40f * scale));
        Theme.SectionHeader(title);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        ImGui.TextUnformatted(what);
        ImGui.Dummy(new Vector2(0f, 8f * scale));
        ImGui.TextColored(Theme.TextDim, status);
        ImGui.PopTextWrapPos();
    }

    /// The settings panel: a strip of its own, and whichever half it is showing.
    private void DrawSettings()
    {
        var scale = UiHelpers.Scale;

        var origin = ImGui.GetCursorScreenPos();
        var selected = settingsTabs.Draw(
            "##echoglamsettingstabs", SettingsTabLabels, origin, ImGui.GetContentRegionAvail().X,
            ChangelogUnread ? ChangelogTab : -1);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + EchoTabs.MeasureHeight() + (10f * scale)));

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * settingsTabs.ContentAlpha);

        if (selected == ChangelogTab)
            DrawChangelog();
        else
            DrawGeneralSettings();

        ImGui.PopStyleVar();
    }

    private void DrawGeneralSettings()
    {
        var scale = UiHelpers.Scale;

        Theme.SectionHeader("Appearance");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        ImGui.TextColored(Theme.TextDim, "Accent colour");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        var available = ImGui.GetContentRegionAvail().X;
        var swatch = 26f * scale;
        var swatchGap = 8f * scale;
        var x = 0f;

        foreach (var (name, colour) in Theme.Presets)
        {
            var width = EchoButton.ContentSize(name).X;

            if (x > 0f && x + width > available)
            {
                x = 0f;
            }
            else if (x > 0f)
            {
                ImGui.SameLine(0f, swatchGap);
            }

            var isCurrent = ColoursMatch(Theme.Accent, colour);
            if (EchoButton.Draw($"##accent{name}", name, new Vector2(0f, swatch), colour, selected: isCurrent))
            {
                Theme.ApplyAccent(colour);
                Config.AccentColour = colour;
                Config.Save();
            }

            x += width + swatchGap;
        }

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        var custom = new Vector3(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z);
        if (ImGui.ColorEdit3("Custom", ref custom, ImGuiColorEditFlags.NoInputs))
        {
            var chosen = new Vector4(custom.X, custom.Y, custom.Z, 1f);
            Theme.ApplyAccent(chosen);
            Config.AccentColour = chosen;
            Config.Save();
        }


        ImGui.Dummy(new Vector2(0f, 14f * scale));
        Theme.SectionHeader("Community");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        ImGui.TextColored(Theme.TextDim, "Ask a question, show off a glamour, or hear about updates.");
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawClickableLink("Join our Discord", DiscordBlurple, DiscordInvite);


        if (Build.Diagnostics)
        {
            ImGui.Dummy(new Vector2(0f, 14f * scale));
            Theme.SectionHeader("Development");
            ImGui.Dummy(new Vector2(0f, 4f * scale));
            ImGui.TextColored(Theme.TextDim, $"EchoGlam {plugin.Version} - development build");
            ImGui.TextColored(Theme.TextDim, $"UI scale: {UiHelpers.Scale:0.00}x");
            ImGui.TextColored(Theme.TextDim, $"Window: {Config.WindowWidth:0} x {Config.WindowHeight:0} design units");
        }
    }

    private const string BugReportPopup = "##bugreport";

    private string bugReport = string.Empty;

    /// Kept across sends on purpose - see the note where it is cleared.
    private string bugReportName = string.Empty;

    /// The width the report box was last laid out to, so the wrapper's own breaks can be told from the ones
    /// somebody typed when the report is sent.
    private float bugWrap;

    /// The bug report form.
    private void DrawBugReportPopup()
    {
        if (!ImGui.BeginPopup(BugReportPopup))
            return;

        var scale = UiHelpers.Scale;
        var width = 280f * scale;

        ImGui.TextColored(Theme.Accent, "Report a Bug");

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(Theme.TextDim, "Goes to the EchoGlam Discord with your version attached.");
        ImGui.PopTextWrapPos();

        ImGui.Spacing();

        bugWrap = width - (ImGui.GetStyle().FramePadding.X * 2f) - (18f * scale);

        UiHelpers.WrappingInputTextMultiline(
            "##bugtext", ref bugReport, BugReporter.MaximumDescription,
            new Vector2(width, 72f * scale), bugWrap);

        ImGui.Spacing();

        ImGui.TextColored(Theme.TextDim, "Name or Discord handle (optional)");
        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##bugname", "leave blank to stay anonymous",
            ref bugReportName, BugReporter.MaximumName);

        ImGui.Spacing();

        var sending = BugReporter.State == BugReporter.SendState.Sending;
        if (EchoButton.Draw("##sendbug", sending ? "Sending..." : "Send", new Vector2(100f * scale, 26f * scale),
                enabled: !sending && bugReport.Trim().Length > 0))
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
    private BugReport BuildBugReport() => new()
    {
        Description = UiHelpers.Unwrap(bugReport, bugWrap).Trim(),
        Name = bugReportName.Trim(),
        Version = plugin.Version,
        Relay = RelayEndpoints.UsingLive ? "live" : "dev",
        Tab = TabLabels[Math.Clamp(tabs.Selected, 0, TabLabels.Length - 1)],
        Scale = UiHelpers.Scale,
        AppearanceAvailable = Appearance.LayoutTrusted,
        SlotsOverridden = plugin.Wardrobe.Current.Count,
        CatalogueSize = plugin.Items.Count,
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
            Plugin.Log.Error(ex, "[EchoGlam] Failed to open the Discord invite.");
        }
    }

    /// The release notes, newest first.
    private void DrawChangelog()
    {
        var scale = UiHelpers.Scale;

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

        if (ChangelogUnread)
        {
            Config.LastSeenChangelogVersion = ChangelogData.LatestVersion;
            Config.Save();
        }

        for (var i = 0; i < ChangelogData.Entries.Length; i++)
        {
            var entry = ChangelogData.Entries[i];

            Theme.SectionHeader($"v{entry.Version}", ruleWidth: 0f);
            ImGui.Spacing();

            foreach (var line in entry.Highlights)
            {
                ImGui.Bullet();
                ImGui.SameLine();

                ImGui.PushTextWrapPos(0f);
                ImGui.TextUnformatted(line);
                ImGui.PopTextWrapPos();

                ImGui.Spacing();
            }

            if (i < ChangelogData.Entries.Length - 1)
            {
                ImGui.Dummy(new Vector2(0f, 6f * scale));
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0f, 6f * scale));
            }
        }

        ImGui.EndChild();
    }

    /// Whether two accents are the same to the eye.
    private static bool ColoursMatch(Vector4 a, Vector4 b) =>
        MathF.Abs(a.X - b.X) < 0.01f && MathF.Abs(a.Y - b.Y) < 0.01f && MathF.Abs(a.Z - b.Z) < 0.01f;
}
