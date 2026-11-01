using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using EchoGlam.Game;
using EchoGlam.UI.Controls;
using EchoGlam.UI.Design;
using EchoGlam.UI.Foundation;
using EchoGlam.UI.Shell;

namespace EchoGlam.UI;

/// EchoGlam's window: a wardrobe behind one icon and a catalogue behind the next.
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

    /// The index, in Tab order.
    private static readonly Destination[] Destinations =
    [
        new("Dressing Room"),
        new("Gallery"),
        new("Profile"),
        new("Friends"),
    ];


    /// The settings panel's own strip.
    private const string AppearanceCategory = "Appearance";

    private const string ChangelogCategory = "Changelog";

    private const string AboutCategory = "About";

    private const string DevelopmentCategory = "Development";

    /// Built per draw rather than held as a static, because the last entry is only there in a development
    /// build and a static array would need a second one for the other case.
    private static CategoryList.Item[] SettingsCategories() =>
        Build.Diagnostics
            ?
            [
                new(AppearanceCategory, "Appearance", FontAwesomeIcon.Palette, "Theme, colours and the window"),
                new(ChangelogCategory, "Changelog", FontAwesomeIcon.History, "What's new"),
                new(AboutCategory, "About", FontAwesomeIcon.InfoCircle, "Version and links"),
                new(DevelopmentCategory, "Development", FontAwesomeIcon.Flask, "Probes and diagnostics"),
            ]
            :
            [
                new(AppearanceCategory, "Appearance", FontAwesomeIcon.Palette, "Theme, colours and the window"),
                new(ChangelogCategory, "Changelog", FontAwesomeIcon.History, "What's new"),
                new(AboutCategory, "About", FontAwesomeIcon.InfoCircle, "Version and links"),
            ];

    private readonly Plugin plugin;

    /// The masthead: wordmark, numbered index, lock.
    private readonly Masthead masthead = new();

    private readonly CategoryList settingsCategories = new();

    private string settingsCategory = AppearanceCategory;

    /// The three things that can occupy the content region.
    private const string PagePane = "page";

    private const string SettingsPane = "settings";

    private const string SubmitPaneKey = "submit";

    private readonly Crossfade pane = new(PagePane);
    private readonly DressingRoomTab dressingRoom;
    private readonly GalleryTab gallery;
    private readonly ProfileTab profile;

    /// Somebody else's profile, drawn in the Gallery's place.
    private readonly ProfileTab guest;

    private readonly FriendsTab friends;
    private readonly SubmitPane submit;
    private Theme.ThemePush themeColors;

    /// Whether the settings panel is showing over the top of the tab content.
    private bool showSettings;

    public MainWindow(Plugin plugin)
        : base("EchoGlam###EchoGlamMain", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar)
    {
        this.plugin = plugin;
        dressingRoom = new DressingRoomTab(plugin);
        gallery = new GalleryTab(plugin);
        profile = new ProfileTab(plugin, gallery.Images);
        guest = new ProfileTab(plugin, gallery.Images);
        friends = new FriendsTab(plugin);
        submit = new SubmitPane(plugin, plugin.Screenshots, gallery.Images);

        gallery.OpenProfile += id => guest.View(id, guest.Stop);

        friends.OpenProfile += id =>
        {
            guest.View(id, guest.Stop);
            masthead.SetImmediate((int)Tab.Gallery);
        };

        guest.WasMine += () =>
        {
            guest.Stop();
            masthead.SetImmediate((int)Tab.Profile);
        };

        gallery.Publish += () => submit.OpenForCurrentLook();
        gallery.Edit += entry => submit.OpenForEdit(entry);

        profile.OpenGlamour += id =>
        {
            gallery.Show(id, () => masthead.SetImmediate((int)Tab.Profile));
            masthead.SetImmediate((int)Tab.Gallery);
        };

        guest.OpenGlamour += id =>
        {
            if (guest.ViewingId is not { } author)
                return;

            gallery.Show(id, () => guest.View(author, guest.Stop));
            guest.Stop();
        };

        submit.Published += _ => gallery.Reload();
        submit.Removed += _ => gallery.Reload();

        minimised = Config.StartMinimised;

        introRunning = true;

        animatedSize = TargetSize;

        masthead.SetImmediate(Math.Clamp(Config.LastTab, 0, TabLabels.Length - 1));
    }

    private Configuration Config => plugin.Configuration;

    /// Side length of the collapsed box, in design units.
    private const float MinimisedSize = 54f;

    /// What the collapsed box shows.
    private const FontAwesomeIcon MinimisedIcon = FontAwesomeIcon.Tshirt;

    /// How fast the box grows and shrinks.
    private const float ResizeLerpSpeed = Anim.ResizeSpeed;

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

    /// Whether the pointer has travelled since the collapsed box was pressed.
    private bool minimisedDragged;

    /// Where the window's own top-left corner was this frame.
    private Vector2 windowOrigin;

    /// How far the pointer may travel during a press and still count as a click.
    private static float MinimisedClickSlop => 2f * UiHelpers.Scale;

    public override void OnOpen() => animatedSize = TargetSize;

    /// Releases what the tabs hold.
    public void Dispose()
    {
        gallery.Dispose();
        submit.Dispose();

        profile.Dispose();
        guest.Dispose();
    }

    /// The window's one size, in design units.
    public static readonly Vector2 DesignSize = new(1240f, 780f);

    /// DesignSize, less the figure column, while the Dressing Room is collapsed.
    private Vector2 DesignForPage =>
        pane.Showing != PagePane
            ? DesignSize
            : DesignSize with { X = DesignSize.X - FoldedAway };

    /// How much width the page on screen has folded away, in design units.
    private float FoldedAway => (Tab)Config.LastTab switch
    {
        Tab.DressingRoom when Config.DressingRoomCompact => DressingRoomTab.CollapseSaving,
        Tab.Gallery when gallery.PictureFolded => GalleryTab.CollapseSaving,
        _ => 0f,
    };

    /// The size the window is easing toward, in design units.
    private Vector2 TargetSize => introRunning
        ? IntroSize
        : minimised
            ? new Vector2(MinimisedSize, MinimisedSize)
            : FittedSize;

    /// DesignSize, shrunk if it would not fit on the monitor.
    private Vector2 FittedSize
    {
        get
        {
            var design = DesignForPage;
            var scale = ImGuiHelpers.GlobalScale;

            if (scale <= 0f)
                return design;

            var work = ImGui.GetMainViewport().WorkSize / scale;

            var room = work - new Vector2(24f, 24f);

            return new Vector2(
                MathF.Min(design.X, MathF.Max(room.X, 640f)),
                MathF.Min(design.Y, MathF.Max(room.Y, 480f)));
        }
    }

    /// Whether the full layout is up, rather than the collapsed box or the intro card.
    private bool FullyExpanded => !minimised && !introRunning && contentUp;

    private bool contentUp;

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

        if (minimised || introRunning)
            contentUp = false;
        else if (animatedSize.X >= target.X * 0.92f)
            contentUp = true;

        ApplyConstraints();

        Size = animatedSize;
        SizeCondition = ImGuiCond.Always;
        Flags |= ImGuiWindowFlags.NoResize;

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

        windowOrigin = ImGui.GetWindowPos();

        RememberPosition();

        Modal.Frame(windowOrigin, ImGui.GetWindowSize());



        gallery.Images.Tick();

        if (!FullyExpanded)
        {
            DrawMinimised();
            return;
        }

        DrawWindowFrame();

        var region = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();

        var windowTop = ImGui.GetWindowPos().Y;

        var selected = (Tab)masthead.Draw(origin, region.Y, Destinations);

        if ((int)selected != Config.LastTab)
        {
            Config.LastTab = (int)selected;
            Config.Save();
        }

        if (masthead.ClickedThisFrame)
            showSettings = false;

        var contentLeft = origin.X + Masthead.Width + Sp.Xxl;
        var contentWidth = MathF.Max(0f, region.X - Masthead.Width - Sp.Xxl);

        ImGui.SetCursorScreenPos(new Vector2(contentLeft, origin.Y));

        if (ImGui.BeginChild("##echoglamcontent", new Vector2(contentWidth, region.Y), false))
        {
            var inner = ImGui.GetCursorScreenPos();
            var innerWidth = ImGui.GetContentRegionAvail().X;

            var ruleY = inner.Y + PageHeader.RuleOffset(bare: true);

            var chromeY = MathF.Max(
                origin.Y + Stroke.Hair,
                ((windowTop + ruleY) / 2f) - (WindowChrome.Height / 2f));

            switch (WindowChrome.Draw(
                        new Vector2(inner.X + innerWidth, chromeY),
                        showSettings,
                        Config.WindowLocked,
                        ChangelogUnread))
            {
                case ChromeAction.Lock:
                    Config.WindowLocked = !Config.WindowLocked;
                    Config.Save();
                    break;

                case ChromeAction.Settings:
                    showSettings = !showSettings;

                    if (showSettings && ChangelogUnread)
                        settingsCategory = ChangelogCategory;

                    break;

                case ChromeAction.Minimise:
                    SetMinimised(true);
                    break;

                case ChromeAction.Close:
                    IsOpen = false;
                    break;
            }

            ImGui.SetCursorScreenPos(inner);

            var wanted = showSettings
                ? SettingsPane
                : submit.IsOpen
                    ? SubmitPaneKey
                    : PagePane;

            var showing = pane.Show(wanted);

            ImGui.PushStyleVar(
                ImGuiStyleVar.Alpha,
                ImGui.GetStyle().Alpha * masthead.ContentAlpha * pane.Alpha);

            PageHeader.Draw(string.Empty, ImGui.GetContentRegionAvail().X);

            if (showing == SettingsPane)
            {
                DrawSettings();
            }
            else if (showing == SubmitPaneKey)
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
                        if (guest.IsViewing)
                            guest.Draw();
                        else
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

            ImGui.PopStyleVar();
        }

        ImGui.EndChild();
    }

    /// Whether the band drawn last frame was the bare one.

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

        var span = MathF.Max(1f, (DesignSize.X - MinimisedSize) * ImGuiHelpers.GlobalScale);
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
        {
            minimisedPressAt = ImGui.GetMousePos();
            minimisedDragged = false;
        }

        if (minimisedPressAt is { } pressedAt
            && Vector2.Distance(pressedAt, ImGui.GetMousePos()) >= MinimisedClickSlop)
            minimisedDragged = true;

        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            return;

        if (minimisedPressAt is not null && !minimisedDragged && hovered)
            SetMinimised(false);

        minimisedPressAt = null;
        minimisedDragged = false;
    }

    /// An accent border around the whole window, matching the collapsed box and the rest of the suite.
    private static void DrawWindowFrame()
    {
        var scale = UiHelpers.Scale;
        var inset = 1.5f * scale;
        var rounding = 8f * scale;

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos() + new Vector2(inset, inset);
        var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(inset, inset);

        Backdrop.Draw(min, max, rounding);

        drawList.PushClipRectFullScreen();
        drawList.AddRect(
            min, max,
            Col.U32(Theme.Accent with { W = 0.34f }),
            rounding, ImDrawFlags.None, Stroke.Edge);
        drawList.PopClipRect();
    }

    /// The minimum size, but only while the window is a window.
    private void ApplyConstraints()
    {
        SizeConstraints = null;
    }

    /// Ends the introduction and opens into the plugin.
    private void FinishIntro()
    {
        introRunning = false;
        animatedSize = IntroSize;
        animatedPos = ImGui.GetWindowPos();

        var savedX = minimised ? Config.MinimisedX : Config.FullWindowX;
        var savedY = minimised ? Config.MinimisedY : Config.FullWindowY;

        targetPos = savedX > Configuration.UnsetPosition && savedY > Configuration.UnsetPosition
            ? new Vector2(savedX, savedY)
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

        animatedPos = windowOrigin;
        minimised = value;
        Config.StartMinimised = value;

        var savedX = value ? Config.MinimisedX : Config.FullWindowX;
        var savedY = value ? Config.MinimisedY : Config.FullWindowY;

        targetPos = savedX > Configuration.UnsetPosition && savedY > Configuration.UnsetPosition
            ? new Vector2(savedX, savedY)
            : animatedPos;

        animatingPosition = true;
        Config.Save();

        if (Build.Diagnostics)
        {
            Plugin.Log.Information(
                $"[EchoGlam] minimise={value} from={animatedPos} to={targetPos} "
                + $"stored.full=({Config.FullWindowX},{Config.FullWindowY}) "
                + $"stored.min=({Config.MinimisedX},{Config.MinimisedY})");
        }
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

            if (Build.Diagnostics)
                Plugin.Log.Information($"[EchoGlam] settled minimised={minimised} at={animatedPos}");
        }
    }

    /// Records where the window currently sits, against whichever mode is showing.
    private void RememberPosition()
    {
        if (animatingPosition)
            return;

        var pos = windowOrigin;

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


    /// Lock, settings, minimise and close, at a screen position the caller has worked out.


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
        var categories = SettingsCategories();
        var origin = ImGui.GetCursorScreenPos();
        var region = ImGui.GetContentRegionAvail();

        settingsCategory = settingsCategories.Draw(
            origin, categories, settingsCategory, plugin.Fonts.Icon,
            key => key == ChangelogCategory && ChangelogUnread);

        var rule = origin.X + CategoryList.Width + Sp.Xl;

        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(rule, origin.Y),
            new Vector2(rule + Stroke.Hair, origin.Y + region.Y),
            Col.U32(Col.EdgeRest with { W = 0.5f }));

        var detailLeft = rule + Stroke.Hair + Sp.Xl;
        var detailWidth = MathF.Max(0f, origin.X + region.X - detailLeft);

        ImGui.SetCursorScreenPos(new Vector2(detailLeft, origin.Y));

        if (ImGui.BeginChild("##settingsdetail", new Vector2(detailWidth, region.Y), false))
        {
            switch (settingsCategory)
            {
                case ChangelogCategory:
                    DrawChangelog();
                    break;

                case AboutCategory:
                    DrawAbout();
                    break;

                case DevelopmentCategory:
                    DrawDevelopment();
                    break;

                default:
                    DrawAppearanceSettings();
                    break;
            }
        }

        ImGui.EndChild();
    }

    private void DrawAppearanceSettings()
    {
        var width = ImGui.GetContentRegionAvail().X;

        Theme.SectionHeader("Theme", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        var match = ColourThemes.Match(Theme.Accent);

        ImGui.TextColored(
            Theme.TextDim,
            match is { } named
                ? $"{named.Name} - {named.Group}"
                : "Custom - picked off the wheel rather than from the list.");

        ImGui.Dummy(new Vector2(0f, Sp.Md));

        DrawThemePicker(width);

        ImGui.Dummy(new Vector2(0f, Sp.Xl));
        Theme.SectionHeader("Your own colour", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        var custom = new Vector3(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z);

        if (ImGui.ColorEdit3("Pick a colour", ref custom, ImGuiColorEditFlags.NoInputs))
        {
            var chosen = new Vector4(custom.X, custom.Y, custom.Z, 1f);
            Theme.ApplyAccent(chosen);
            Config.AccentColour = chosen;
            Config.Save();
        }

        ImGui.Dummy(new Vector2(0f, Sp.Xl));
        Theme.SectionHeader("Window", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        var startMinimised = Config.StartMinimised;

        if (EchoToggle.Draw(
                "##startminimised", "Open minimised", ref startMinimised,
                "EchoGlam starts as the small box rather than the full window. Click the box to "
                + "bring it back."))
        {
            Config.StartMinimised = startMinimised;
            Config.Save();
        }

    }

    /// The theme list, grouped the way EchoMix groups it.
    private void DrawThemePicker(float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var swatch = 30f * UiHelpers.Scale;
        var gap = Sp.Sm;
        var perRow = Math.Max(1, (int)((width + gap) / (swatch + gap)));

        foreach (var (group, themes) in ColourThemes.Grouped)
        {
            using (Text.PushCaption())
            {
                var at = ImGui.GetCursorScreenPos();

                Text.Tracked(
                    drawList, at, group.ToUpperInvariant(), Col.U32(Col.TextMuted), 1.6f, width);

                ImGui.Dummy(new Vector2(width, Text.CaptionSize * 1.6f));
            }

            for (var i = 0; i < themes.Count; i++)
            {
                if (i % perRow != 0)
                    ImGui.SameLine(0f, gap);

                DrawThemeSwatch(drawList, themes[i], swatch);
            }

            ImGui.Dummy(new Vector2(0f, Sp.Lg));
        }
    }

    private void DrawThemeSwatch(ImDrawListPtr drawList, ColourThemes.Theme theme, float size)
    {
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(size, size);
        var selected = ColoursMatch(Theme.Accent, theme.Accent);

        if (ImGui.InvisibleButton(
                $"##theme{theme.Name}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft))
        {
            Theme.ApplyAccent(theme.Accent);
            Config.AccentColour = theme.Accent;
            Config.Save();
        }

        var hovered = ImGui.IsItemHovered();

        if (hovered)
            UiHelpers.WrappedTooltip($"{theme.Name}\n{theme.Group}");

        if (selected)
            Elev.Glow(drawList, min, max, theme.Accent, 0.30f, Rad.Ctl, 7.8f);

        drawList.AddRectFilled(min, max, Col.U32(Col.Sunken), Rad.Ctl);

        var inset = Sp.Xs;

        drawList.AddRectFilled(
            min + new Vector2(inset, inset), max - new Vector2(inset, inset),
            Col.U32(theme.Accent), Rad.Chip);

        drawList.AddRect(
            min, max,
            Col.U32(selected
                ? theme.Accent
                : hovered
                    ? new Vector4(1f, 1f, 1f, 0.45f)
                    : Col.EdgeRest with { W = 0.5f }),
            Rad.Ctl, ImDrawFlags.None, selected ? Stroke.Focus : Stroke.Hair);
    }

    /// What this is, where to ask about it, and what to do when it breaks.
    private void DrawAbout()
    {
        var width = MathF.Min(ImGui.GetContentRegionAvail().X, Modal.Width(ModalSize.Large));

        Theme.SectionHeader("EchoGlam", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, Sp.Md));

        using (Surface.Begin(width, SurfaceKind.Raised))
        {
            ImGui.TextColored(Theme.Text, $"Version {plugin.Version}");
            ImGui.TextColored(Theme.TextDim, "A glamour board and dressing room for Final Fantasy XIV.");
        }

        ImGui.Dummy(new Vector2(0f, Sp.Xl));
        Theme.SectionHeader("Community", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, Sp.Md));

        using (Surface.Begin(width, SurfaceKind.Raised))
        {
            if (EchoButton.Draw(
                    "##discord", "Join Our Discord", new Vector2(0f, Metrics.RowTall),
                    DiscordBlurple, plugin.Fonts.Icon, FontAwesomeIcon.Comments))
                OpenUrl(DiscordInvite);

            ImGui.Dummy(new Vector2(0f, Sp.Sm));
            ImGui.TextColored(Theme.TextDim, "Questions, help and release announcements.");
        }

        ImGui.Dummy(new Vector2(0f, Sp.Xl));
        Theme.SectionHeader("Something Wrong?", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, Sp.Md));

        using (var card = Surface.Begin(width, SurfaceKind.Raised))
            DrawBugReport(card.Box.Inner);
    }

    /// NEVER REACHABLE IN A RELEASE BUILD, because the category that selects it is not in the list outside a
    /// development one - see SettingsCategories.
    private void DrawDevelopment()
    {
        var width = ImGui.GetContentRegionAvail().X;

        Theme.SectionHeader("Build", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        ImGui.TextColored(Theme.TextDim, $"EchoGlam {plugin.Version} - development build");
        ImGui.TextColored(Theme.TextDim, $"UI scale: {UiHelpers.Scale:0.00}x");

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        if (EchoButton.Draw(
                "##diagnostics",
                plugin.Debug?.IsOpen == true ? "Close Diagnostics" : "Open Diagnostics",
                new Vector2(0f, Metrics.Row),
                tooltip: "Layout and foundation probes. Dev builds only."))
        {
            if (plugin.Debug is { } debug)
                debug.IsOpen = !debug.IsOpen;
        }
    }


    private string bugReport = string.Empty;

    /// Kept across sends on purpose - see the note where it is cleared.
    private string bugReportName = string.Empty;

    /// The width the report box was last laid out to, so the wrapper's own breaks can be told from the ones
    /// somebody typed when the report is sent.
    private float bugWrap;

    /// The bug report form.
    private void DrawBugReport(float width)
    {
        var scale = UiHelpers.Scale;

        ImGui.TextColored(
            Theme.TextDim,
            "Sends what you write below with your plugin version, build and UI scale attached -");

        ImGui.TextColored(Theme.TextDim, "nothing else from this PC. It goes to the EchoGlam Discord.");

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
                ImGui.TextColored(Col.Danger, $"Failed: {BugReporter.LastError}");
                break;
        }
    }

    /// Gathers the context a report is useless without.
    private BugReport BuildBugReport() => new()
    {
        Description = UiHelpers.Unwrap(bugReport, bugWrap).Trim(),
        Name = bugReportName.Trim(),
        Version = plugin.Version,
        Relay = RelayEndpoints.UsingLive ? "live" : "dev",
        Tab = TabLabels[Math.Clamp(masthead.Selected, 0, TabLabels.Length - 1)],
        Scale = UiHelpers.Scale,
        AppearanceAvailable = Appearance.LayoutTrusted,
        SlotsOverridden = plugin.Wardrobe.Current.Count,
        CatalogueSize = plugin.Items.Count,
    };

    /// Discord's own brand colour, so the link reads as a Discord link before it is read as words.
    private static readonly Vector4 DiscordBlurple = new(0.345f, 0.396f, 0.949f, 1f);

    private const string DiscordInvite = "https://discord.gg/nJauXrNWx3";

    /// Hands a URL to the shell.
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Failed to open {0}.", url);
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

            foreach (var group in entry.Groups)
            {
                using (Text.PushCaption())
                {
                    Text.Tracked(
                        ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(),
                        group.Heading.ToUpperInvariant(), Col.U32(Col.TextMuted), 1.8f,
                        ImGui.GetContentRegionAvail().X);

                    ImGui.Dummy(new Vector2(0f, Text.CaptionSize * 1.5f));
                }

                foreach (var line in group.Lines)
                {
                    ImGui.Bullet();
                    ImGui.SameLine();

                    ImGui.PushTextWrapPos(0f);
                    ImGui.TextUnformatted(line);
                    ImGui.PopTextWrapPos();

                    ImGui.Spacing();
                }

                ImGui.Dummy(new Vector2(0f, Sp.Sm));
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
