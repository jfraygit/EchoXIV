using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using EchoMix.Plugin.Integrations;
using EchoMix.Plugin.Ipc;
using EchoMix.Plugin.UI.Controls;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI;

public sealed class DjDeckWindow : Window, IDisposable
{
    private enum ViewMode { Deck, Settings, Listener, Welcome, JoinShow, BrowseShows, DjList, DjProfile, DjProfileEdit }

    private readonly Plugin plugin;
    private readonly PlaylistPanel playlistPanel;

    private readonly TabStrip settingsTabStrip = new();
    private readonly TabStrip liveShowsTabStrip = new(selfContained: false);
    private readonly FileDialogManager soundPadFileDialogManager = new();
    private readonly FileDialogManager showImageFileDialogManager = new();
    private readonly ImageCropDialog imageCropDialog = new();

    private float scrollToTopVisibility;
    private bool scrollToTopAnimating;
    private readonly string[] padLabelEditBuffers = Enumerable.Repeat(string.Empty, 8).ToArray();
    private readonly float[] padLoopIntervalBuffers = new float[8];
    private readonly float[] padVolumeBuffers = new float[8];
    private readonly Dictionary<string, float> trackGainEditBuffers = new();
    private readonly Dictionary<string, float> trackBpmEditBuffers = new();
    private string? selectedPlaylistName;

    private static readonly string[] ListenerVisualizerStyleNames =
    {
        "Bars", "Smooth Line", "Filled Area", "Mirrored Bars", "Mirrored Filled Area", "Dots", "Blocks",
        "Waveform", "Radial", "Rings", "Polygon", "Peak Bars", "Waterfall", "Embers", "Heat Strip", "Orbit Dots", "Skyline",
        "Pulse Line", "Starfield", "Matrix Rain", "Kaleidoscope", "Comet Ride", "Mesh", "Pyramid Bars", "Ripple Field", "Bounce Balls", "Aurora",
    };

    private float MinimizedMirrorCenterOffset => 10f * Scale;

    private const float ViewFadeSeconds = 0.18f;
    private ViewMode currentView = ViewMode.Deck;
    private ViewMode? pendingView;
    private float contentAlpha = 1f;

    private ViewMode lastNonSettingsView = ViewMode.Deck;

    private ViewMode viewBeforeBrowseShows = ViewMode.Deck;

    private ViewMode viewBeforeDjProfileEdit = ViewMode.DjList;

    private bool wasListening;

    private float welcomeViewSeconds;

    private bool hasClearedWelcomeThisSession;

    private string broadcastDjNameBuffer = string.Empty;
    private string broadcastPasswordBuffer = string.Empty;
    private string broadcastHostPasswordBuffer = string.Empty;
    private string broadcastRoomCodeBuffer = string.Empty;
    private string connectRoomCodeBuffer = string.Empty;
    private string connectPasswordBuffer = string.Empty;
    private bool joinShowBlankRoomCodeHint;
    private string publicShowNameBuffer = string.Empty;
    private string venueNameBuffer = string.Empty;
    private string venueDataCenterBuffer = string.Empty;
    private string venueWorldBuffer = string.Empty;
    private string venueHousingAreaBuffer = string.Empty;
    private string venueWardBuffer = string.Empty;
    private string venuePlotBuffer = string.Empty;
    private bool venueIsApartmentBuffer;
    private bool venueSubdivisionBuffer;

    private List<SavedVenueDto>? ownSavedVenuesCache;
    private bool ownSavedVenuesRequestSent;
    private bool ownSavedVenuesDetailRequestSent;

    private IDalamudTextureWrap? showImagePreview;
    private string? showImageError;

    private string? stagedShowImagePath;
    private bool wasBroadcastLiveLastFrame;

    /// Where the DJ's last-picked show image is kept durably (unlike stagedShowImagePath, which used to point
    /// at a throwaway OS temp file) so it's automatically reused for every future show instead of needing to
    /// be re-picked each time - see FinishShowImageUpload and the IsLive edge-trigger check in DrawHeader's
    /// per-frame poll block, both of which read/ write this same path.
    private static string LastShowImagePath => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "last-show-image.jpg");

    private string? visitError;

    private float browseShowCardHeight = 340f;
    private float measuredBrowseShowCardHeight;

    private string reportShowReasonBuffer = string.Empty;
    private bool reportShowSending;
    private ReportShowResultMessage? reportShowSendResult;

    private string joinPasswordBuffer = string.Empty;
    private string? lastAttemptedJoinRoomCode;

    private readonly object publicShowImageGate = new();
    private readonly Dictionary<string, IDalamudTextureWrap?> publicShowImageCache = new();
    private readonly HashSet<string> publicShowImageLoading = new();

    private float djListCardHeight = 220f;
    private float measuredDjListCardHeight;
    private readonly object djProfileImageGate = new();
    private readonly Dictionary<string, IDalamudTextureWrap?> djProfileAvatarCache = new();
    private readonly HashSet<string> djProfileAvatarLoading = new();
    private IDalamudTextureWrap? djProfileBannerTexture;
    private string? djProfileBannerTextureForId;
    private bool djProfileBannerLoading;

    private string djProfileReportReasonBuffer = string.Empty;
    private bool djProfileReportSending;
    private DjProfileReportAckMessage? djProfileReportSendResult;

    private bool djProfileDeleteSending;
    private DjProfileDeleteResultMessage? djProfileDeleteResult;

    private double? djProfileNumberCopiedAt;

    private readonly HashSet<string> djProfileLikePending = new();
    private readonly HashSet<string> djProfileFollowPending = new();

    private readonly Dictionary<string, float> djProfileCardGlow = new();

    private readonly Dictionary<string, float> publicShowCardGlow = new();

    private string djListGenreFilter = string.Empty;
    private string liveShowsGenreFilter = string.Empty;

    private object? liveShowsGenreFilterOptionsSource;
    private List<string> liveShowsGenreFilterOptions = new();
    private object? djListGenreFilterOptionsSource;
    private List<string> djListGenreFilterOptions = new();
    private string djListNameSearchBuffer = string.Empty;

    /// The avatar frame style options a DJ can pick in the edit form - see DrawDjProfileAvatar for how each
    /// one actually renders.
    private static readonly string[] DjFrameStyleOptions =
    {
        "Solid", "Dashed", "Dotted", "Double", "Corners", "Gradient", "Glow", "Ticks", "Chain", "Brackets", "Stitch", "Blocks",        "Pulse", "Chase", "Spin", "Rainbow", "Sparkle", "Sentry", "Anchor", "Pendulum", "Glitch", "Confetti",    };

    /// The DJ name's own text-effect options on the profile detail page - see DrawDjName.
    private static readonly string[] DjNameEffectOptions =
    {
        "None", "Gradient", "Glow", "Outline", "Underline", "Embossed", "Split",        "Pulse", "Rainbow", "Wave", "Shimmer", "Chase", "Flicker", "Typewriter", "Marquee", "Glitch", "Cascade", "Heatwave", "Blink",    };

    private string? editingDjProfileId;

    private string? pendingEditProfileIdOnDetailLoad;

    private string djEditDjNameBuffer = string.Empty;
    private string djEditBioBuffer = string.Empty;
    private bool djEditBioWrapPending;
    private float djEditBioWrapWidth;
    private readonly List<SavedVenueDto> djEditSavedVenues = new();
    private string djEditVenueNameBuffer = string.Empty;
    private string djEditVenueDataCenterBuffer = string.Empty;
    private string djEditVenueWorldBuffer = string.Empty;
    private string djEditVenueHousingAreaBuffer = string.Empty;
    private string djEditVenueWardBuffer = string.Empty;
    private string djEditVenuePlotBuffer = string.Empty;
    private bool djEditVenueIsApartment;
    private bool djEditVenueSubdivision;
    private readonly List<string> djEditGenres = new();
    private string djEditGenreEntryBuffer = string.Empty;
    private readonly List<DjAvailabilityDayDto> djEditAvailability = Enumerable.Range(0, 7).Select(_ => new DjAvailabilityDayDto()).ToList();
    private Vector3 djEditFrameColor = new(0.25f, 0.85f, 0.95f);
    private string djEditFrameStyle = "Solid";
    private string djEditNameEffect = "None";
    private Vector3 djEditNameColor = new(0.25f, 0.85f, 0.95f);
    private string djEditAetherphoneNumber = string.Empty;

    private readonly List<string> djEditLinkedCharacterNames = new();
    private bool djEditShowLinkedCharacters;
    private string? djLinkCodeGenerated;
    private float djLinkCodeExpiresInSeconds;
    private bool djLinkCodeGenerating;
    private string? djLinkCodeError;
    private double? djLinkCodeCopiedAt;
    private readonly HashSet<string> djUnlinkPending = new(StringComparer.OrdinalIgnoreCase);

    private string djRedeemCodeBuffer = string.Empty;
    private bool djRedeemSending;
    private ProfileLinkRedeemResultMessage? djRedeemResult;

    private readonly FileDialogManager djProfileImageFileDialogManager = new();
    private IDalamudTextureWrap? djEditAvatarPreview;
    private string? djEditAvatarUploadPath;
    private IDalamudTextureWrap? djEditBannerPreview;
    private string? djEditBannerUploadPath;
    private string? djEditImageError;
    private bool djProfileSaveSending;
    private DjProfileSaveResultMessage? djProfileSaveResult;

    private ViewMode? pendingDjProfileEditExit;

    private float djEditPreviewCardHeight = 210f;

    private Vector2 settingsPanelPos;
    private float settingsPanelWidth;
    private const float SettingsPanelPad = 14f;

    private const float SettingsPanelWidth = 480f;

    private float djProfileSaveElapsed;
    private const float DjProfileSaveTimeoutSeconds = 20f;

    private string songRequestNameBuffer = string.Empty;

    private const string ReportBugPopupId = "##reportBugPopup";
    private string reportBugDescriptionBuffer = string.Empty;
    private string reportBugDiscordNameBuffer = string.Empty;
    private bool reportBugSending;
    private BugReportResultMessage? reportBugSendResult;

    private const string ListenerVolumePopupId = "##listenerVolumePopup";

    private bool showingSongRequests;
    private bool? pendingShowingSongRequests;
    private float playlistSectionAlpha = 1f;
    private const float PlaylistSectionFadeSeconds = 0.15f;

    private bool joinAsDjMode;
    private string connectHostPasswordBuffer = string.Empty;
    private string connectDjNameBuffer = string.Empty;

    private bool isMinimized;

    private bool miniBoxDraggedThisPress;

    private Vector2? expandedPosition;
    private Vector2? minimizedPosition;
    private bool wasMinimizedLastFrame;

    private Vector2? positionAnimTarget;

    private bool awaitingToggleClickRelease;

    private float glowA;
    private float glowB;

    private float crossfaderCurveGlowC;
    private float crossfaderCurveGlowP;
    private float crossfaderCurveGlowL;

    private float? pendingSeekA;
    private float? pendingSeekB;

    private float knobUnitHeightCache = 64f;

    private float displayTopYOffsetCache = 48f;

    private float tempoBarScreenY;

    private DeckId? mirrorGainDragDeck;
    private float mirrorGainDragSelfBaseline;
    private float mirrorGainDragOtherBaseline;

    private float deckColumnHeightCache = 480f;

    private static readonly Vector2 BaseSize = new(1072, 880);

    private static readonly Vector2 ListenerSize = new(340, 460);

    private static readonly Vector2 WelcomeSize = new(460, 380);

    private static readonly Vector2 MinimizedDeckSize = new(220, 190);
    private static readonly Vector2 MinimizedListenerSize = new(220, 190);

    private Vector2 currentSize = BaseSize;
    private const float ResizeLerpSpeed = 10f;

    private int themeColorCount;

    private float Scale => Math.Clamp(plugin.Configuration.UiScale, 0.75f, 1.5f);

    public Vector2 WindowScreenPosition { get; private set; }
    public Vector2 CurrentWindowSize => currentSize;

    public DjDeckWindow(Plugin plugin) : base("EchoMix###echomix-main")
    {
        this.plugin = plugin;
        playlistPanel = new PlaylistPanel(plugin);
        broadcastDjNameBuffer = plugin.Configuration.HostDisplayName ?? string.Empty;
        broadcastRoomCodeBuffer = plugin.Configuration.LastVanityRoomCode ?? string.Empty;
        connectRoomCodeBuffer = plugin.Configuration.LastRoomCode ?? string.Empty;
        publicShowNameBuffer = plugin.Configuration.LastShowName ?? string.Empty;
        venueNameBuffer = plugin.Configuration.LastVenueName ?? string.Empty;
        venueDataCenterBuffer = plugin.Configuration.LastVenueDataCenter ?? string.Empty;
        venueWorldBuffer = plugin.Configuration.LastVenueWorld ?? string.Empty;
        venueHousingAreaBuffer = plugin.Configuration.LastVenueHousingArea ?? string.Empty;
        venueWardBuffer = plugin.Configuration.LastVenueWard ?? string.Empty;
        venuePlotBuffer = plugin.Configuration.LastVenuePlot ?? string.Empty;
        venueIsApartmentBuffer = plugin.Configuration.LastVenueIsApartment;
        venueSubdivisionBuffer = plugin.Configuration.LastVenueSubdivision;

        if (!plugin.Configuration.ShowWelcomeOnEnable && plugin.Configuration.LastChosenRole.HasValue)
        {
            hasClearedWelcomeThisSession = true;
            currentView = plugin.Configuration.LastChosenRole.Value == UserRole.Listener ? ViewMode.JoinShow : ViewMode.Deck;
        }
        else
            currentView = ViewMode.Welcome;
        currentSize = TargetSizeFor(currentView);

        LoadLastShowImagePreview();
    }

    /// IDalamudTextureWrap owns a real GPU texture, not just managed memory - Dalamud can't reclaim that on
    /// its own once this plugin unloads, so every one this window ever created (the DJ's own upload preview,
    /// and every listener-side cache entry from browsing live shows) needs an explicit Dispose here, not just
    /// letting the GC eventually get to it.
    public void Dispose()
    {
        showImagePreview?.Dispose();

        lock (publicShowImageGate)
        {
            foreach (var texture in publicShowImageCache.Values)
                texture?.Dispose();
            publicShowImageCache.Clear();
            publicShowImageLoading.Clear();
        }

        djProfileBannerTexture?.Dispose();
        djEditAvatarPreview?.Dispose();
        djEditBannerPreview?.Dispose();

        lock (djProfileImageGate)
        {
            foreach (var texture in djProfileAvatarCache.Values)
                texture?.Dispose();
            djProfileAvatarCache.Clear();
            djProfileAvatarLoading.Clear();
        }
    }

    /// Settings always uses BaseSize (nothing to minimize there) - Deck and Listener both respect
    /// isMinimized, each with their own minimized size (see MinimizedDeckSize/MinimizedListenerSize).
    private Vector2 TargetSizeFor(ViewMode view)
    {
        if (view == ViewMode.Settings || view == ViewMode.BrowseShows || view == ViewMode.DjList || view == ViewMode.DjProfile || view == ViewMode.DjProfileEdit)
            return BaseSize * Scale;
        if (view == ViewMode.Welcome || view == ViewMode.JoinShow)
            return WelcomeSize * Scale;
        if (isMinimized)
            return (view == ViewMode.Listener ? MinimizedListenerSize : MinimizedDeckSize) * Scale;
        return (view == ViewMode.Listener ? ListenerSize : BaseSize) * Scale;
    }

    /// Sets Size (and Flags' NoMove bit) fresh every frame - rather than only once via
    /// SizeCondition.FirstUseEver/at construction time - so dragging the UI Scale slider resizes the window
    /// immediately and toggling the lock button takes effect immediately, both without needing to reopen the
    /// window.
    public override void PreDraw()
    {
        themeColorCount = Theme.Push();

        var targetSize = TargetSizeFor(pendingView ?? currentView);
        var dt = ImGui.GetIO().DeltaTime;
        currentSize = new Vector2(
            UiHelpers.Lerp(currentSize.X, targetSize.X, ResizeLerpSpeed, dt),
            UiHelpers.Lerp(currentSize.Y, targetSize.Y, ResizeLerpSpeed, dt));

        Size = currentSize * UiHelpers.WindowSizeCompensation;
        SizeCondition = ImGuiCond.Always;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize;
        if (plugin.Configuration.IsWindowLocked)
            Flags |= ImGuiWindowFlags.NoMove;

        var isGrowingIntoView = currentSize.Y < targetSize.Y - 1f;
        if (currentView == ViewMode.Listener || pendingView == ViewMode.Listener || isMinimized || isGrowingIntoView)
            Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    }

    public override void PostDraw() => Theme.Pop(themeColorCount);

    /// Snaps straight to a view with no fade (and no resize animation) - used when the window is being opened
    /// fresh from closed, since there's no previously-visible content to transition away from.
    public void ShowDeckImmediately()
    {
        currentView = ViewMode.Deck;
        pendingView = null;
        contentAlpha = 1f;
        currentSize = TargetSizeFor(ViewMode.Deck);
    }

    public void ShowSettingsImmediately()
    {
        currentView = ViewMode.Settings;
        pendingView = null;
        contentAlpha = 1f;
        currentSize = TargetSizeFor(ViewMode.Settings);
    }

    public void ShowWelcomeImmediately()
    {
        currentView = ViewMode.Welcome;
        pendingView = null;
        contentAlpha = 1f;
        welcomeViewSeconds = 0f;
        currentSize = TargetSizeFor(ViewMode.Welcome);
    }

    public void ShowListenerImmediately()
    {
        currentView = ViewMode.Listener;
        pendingView = null;
        contentAlpha = 1f;
        currentSize = TargetSizeFor(ViewMode.Listener);
    }

    public void ShowJoinShowImmediately()
    {
        currentView = ViewMode.JoinShow;
        pendingView = null;
        contentAlpha = 1f;
        currentSize = TargetSizeFor(ViewMode.JoinShow);
    }

    /// The /el command's target - jumps straight to the Listener view (or Join a Show if not currently
    /// listening) from wherever the window currently is, for a DJ who's parked in Deck view and wants to
    /// check the Listener side without disabling/re-enabling the plugin just to force Welcome back up and
    /// re-pick a role.
    public void ShowListenerView()
    {
        if (plugin.AudioHostClient.LatestStatus.Broadcast.IsListening)
            ShowListenerImmediately();
        else
            ShowJoinShowImmediately();
    }

    /// What Plugin.ToggleDjDeckWindow shows when opening the window from closed - Welcome until the user's
    /// ever actually picked a role (see Configuration.LastChosenRole), then routes to that role's own context
    /// rather than always landing on the DJ-facing Deck view: a Listener who's currently connected goes
    /// straight back into their live show, one who isn't goes back to the Join a Show screen (whether they
    /// closed mid-show or just sitting on that screen), and a DJ goes to Deck.
    public void ShowInitialView()
    {
        if (!hasClearedWelcomeThisSession || !plugin.Configuration.LastChosenRole.HasValue)
        {
            ShowWelcomeImmediately();
        }
        else if (plugin.Configuration.LastChosenRole.Value == UserRole.Listener)
        {
            if (plugin.AudioHostClient.LatestStatus.Broadcast.IsListening)
                ShowListenerImmediately();
            else
                ShowJoinShowImmediately();
        }
        else
        {
            ShowDeckImmediately();
        }
    }

    /// Starts the fade-out/swap/fade-in transition toward Settings, or back to whichever non-Settings view
    /// was active before (Deck, or Listener while listening to a show) - safe to call mid-transition, it just
    /// retargets where the fade-out is headed.
    public void ToggleSettingsView()
    {
        if (currentView == ViewMode.Settings || pendingView == ViewMode.Settings)
        {
            pendingView = lastNonSettingsView;
        }
        else
        {
            lastNonSettingsView = currentView;
            pendingView = ViewMode.Settings;
        }
    }

    public override void Draw()
    {
        WindowScreenPosition = ImGui.GetWindowPos();
        UpdateMinimizePosition();
        DrawWindowChrome();
        soundPadFileDialogManager.Draw();
        showImageFileDialogManager.Draw();
        djProfileImageFileDialogManager.Draw();
        imageCropDialog.Draw(Scale);

        ImGui.SetWindowFontScale(Scale);

        var dt = ImGui.GetIO().DeltaTime;
        var status = plugin.AudioHostClient.LatestStatus;
        glowA = UiHelpers.Lerp(glowA, status.DeckA.IsPlaying ? 1f : 0f, 6f, dt);
        glowB = UiHelpers.Lerp(glowB, status.DeckB.IsPlaying ? 1f : 0f, 6f, dt);

        UpdateViewTransition(dt);
        UpdateListenerViewTransition(status.Broadcast);

        if (isMinimized && (currentView == ViewMode.Deck || currentView == ViewMode.Listener))
        {
            if (currentView == ViewMode.Listener)
                DrawMinimizedListenerBody(status.Broadcast);
            else
                DrawMinimizedDeckBody(status);
            return;
        }

        DrawHeader();
        ImGui.Spacing();

        if (currentView == ViewMode.BrowseShows)
            DrawLiveShowsHeaderRow(isDjList: false);
        else if (currentView == ViewMode.DjList)
            DrawLiveShowsHeaderRow(isDjList: true);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, contentAlpha);
        if (currentView == ViewMode.Deck)
            DrawDeckBody(status);
        else if (currentView == ViewMode.Settings)
            DrawSettingsBody();
        else if (currentView == ViewMode.Welcome)
            DrawWelcomeBody();
        else if (currentView == ViewMode.JoinShow)
            DrawJoinShowBody(status.Broadcast);
        else if (currentView == ViewMode.BrowseShows)
            DrawBrowseShowsBody();
        else if (currentView == ViewMode.DjList)
            DrawDjListBody();
        else if (currentView == ViewMode.DjProfile)
            DrawDjProfileBody();
        else if (currentView == ViewMode.DjProfileEdit)
            DrawDjProfileEditBody();
        else
            DrawListenerBody(status.Broadcast);
        ImGui.PopStyleVar();
    }

    /// Eases toward whichever position this state (expanded vs.
    private void UpdateMinimizePosition()
    {
        if (isMinimized != wasMinimizedLastFrame)
        {
            positionAnimTarget = isMinimized ? minimizedPosition : expandedPosition;
            wasMinimizedLastFrame = isMinimized;
            awaitingToggleClickRelease = true;
        }

        if (awaitingToggleClickRelease && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            awaitingToggleClickRelease = false;

        if (positionAnimTarget.HasValue && !awaitingToggleClickRelease && !plugin.Configuration.IsWindowLocked
            && ImGui.IsWindowFocused() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            positionAnimTarget = null;
        }

        if (positionAnimTarget.HasValue)
        {
            var dt = ImGui.GetIO().DeltaTime;
            var current = ImGui.GetWindowPos();
            var target = positionAnimTarget.Value;
            var next = new Vector2(
                UiHelpers.Lerp(current.X, target.X, ResizeLerpSpeed, dt),
                UiHelpers.Lerp(current.Y, target.Y, ResizeLerpSpeed, dt));
            ImGui.SetWindowPos(next);

            if (Vector2.Distance(next, target) < 0.5f)
                positionAnimTarget = null;
        }

        if (!positionAnimTarget.HasValue)
        {
            if (isMinimized)
                minimizedPosition = ImGui.GetWindowPos();
            else
                expandedPosition = ImGui.GetWindowPos();
        }
    }

    /// Fades contentAlpha down to 0 while a view switch is pending, swaps the actual view the instant it gets
    /// there, then fades back up to 1 - a simple two-phase timer rather than an easing curve, since a clean
    /// "fully gone, then swap, then fully back" read is the point (an asymptotic ease never really reaches 0,
    /// which would leave a ghost of the old view's colors bleeding through right at the swap).
    private void UpdateViewTransition(float dt)
    {
        var fadeStep = dt / ViewFadeSeconds;

        if (pendingView.HasValue)
        {
            contentAlpha = MathF.Max(0f, contentAlpha - fadeStep);
            if (contentAlpha <= 0f)
            {
                currentView = pendingView.Value;
                pendingView = null;
                if (currentView == ViewMode.Welcome)
                    welcomeViewSeconds = 0f;
            }
        }
        else if (contentAlpha < 1f)
        {
            contentAlpha = MathF.Min(1f, contentAlpha + fadeStep);
        }

        if (currentView == ViewMode.Welcome)
            welcomeViewSeconds += dt;
    }

    /// Switches the window into (or out of) the Listener view the moment AudioHost's listen connection
    /// actually comes up or drops - joining/leaving a show is what drives this view, not manual navigation,
    /// so there's no button that goes "to" it directly.
    private void UpdateListenerViewTransition(BroadcastStatusMessage broadcast)
    {
        if (broadcast.IsListening && !wasListening && currentView != ViewMode.Listener)
        {
            isMinimized = false;
            pendingView = ViewMode.Listener;

        }
        else if (!broadcast.IsListening && !broadcast.IsListenerReconnecting && wasListening
            && (currentView == ViewMode.Listener || pendingView == ViewMode.Listener))
        {
            pendingView = ViewMode.JoinShow;
        }

        wasListening = broadcast.IsListening;
    }

    /// The Host/Listener choice - see ShowInitialView.
    private void DrawWelcomeBody()
    {
        var cardWidth = 150f * Scale;
        var cardHeight = 130f * Scale;
        var cardGap = 40f * Scale;

        var avail = ImGui.GetContentRegionAvail();
        var groupWidth = (cardWidth * 2f) + cardGap;
        var offset = new Vector2(
            MathF.Max(0f, (avail.X - groupWidth) / 2f),
            MathF.Max(0f, (avail.Y - cardHeight) / 2f));
        ImGui.SetCursorPos(ImGui.GetCursorPos() + offset);

        if (DrawWelcomeChoiceCard("##welcomeDj", FontAwesomeIcon.RecordVinyl, "DJ", Theme.CyanAccent, cardWidth, cardHeight))
        {
            plugin.Configuration.LastChosenRole = UserRole.Dj;
            plugin.Configuration.Save();
            hasClearedWelcomeThisSession = true;
            pendingView = ViewMode.Deck;
        }

        ImGui.SameLine(0, cardGap);

        if (DrawWelcomeChoiceCard("##welcomeListener", FontAwesomeIcon.Headphones, "Listener", Theme.OrangeAccent, cardWidth, cardHeight))
        {
            plugin.Configuration.LastChosenRole = UserRole.Listener;
            plugin.Configuration.Save();
            hasClearedWelcomeThisSession = true;
            pendingView = ViewMode.JoinShow;
        }
    }

    /// One clickable card: a dark instrument-panel face (same family as PanelButton/
    /// TransportButton/SoundPadButton - drop shadow, accent ring, press-in offset) with a big FontAwesome
    /// glyph and an animated label underneath, so the whole card itself reads as a single button rather than
    /// a caption under a picture.
    private bool DrawWelcomeChoiceCard(string id, FontAwesomeIcon icon, string label, Vector4 accent, float cardWidth, float cardHeight)
    {
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(cardWidth, cardHeight);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var pressOffset = active ? 2f * Scale : 0f;

        var drawList = ImGui.GetWindowDrawList();
        var faceMin = pos + new Vector2(0f, pressOffset);
        var faceMax = faceMin + size;
        var rounding = 10f * Scale;

        var shadowScale = active ? 0.4f : 1f;
        for (var i = 3; i >= 1; i--)
        {
            var shadowOffset = new Vector2(0f, i * 1.5f * shadowScale);
            var shadowAlpha = 0.05f * i;
            drawList.AddRectFilled(pos + shadowOffset, pos + size + shadowOffset, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, shadowAlpha)), rounding);
        }

        drawList.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(Theme.Panel), rounding);

        var ringColor = hovered || active ? accent : new Vector4(accent.X, accent.Y, accent.Z, 0.4f);
        drawList.AddRect(faceMin, faceMax, ImGui.GetColorU32(ringColor), rounding, ImDrawFlags.None, hovered ? 2.5f : 1.5f);

        var glyphColor = hovered || active ? Vector4.Lerp(accent, Vector4.One, 0.2f) : accent;
        using (plugin.Fonts.IconLarge.PushSafe())
        {
            var glyph = icon.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);
            var glyphPos = new Vector2(faceMin.X + ((cardWidth - glyphSize.X) / 2f), faceMin.Y + (16f * Scale));
            drawList.AddText(glyphPos, ImGui.GetColorU32(glyphColor), glyph);
        }

        using (plugin.Fonts.Header.PushSafe())
        {
            var labelY = faceMax.Y - (14f * Scale) - ImGui.GetFontSize();
            DrawWelcomeCardLabel(label, faceMin.X, cardWidth, labelY, accent, hovered || active);
        }

        return clicked;
    }

    /// "DJ"/"Listener" in the same per-letter fade/rise entrance technique the old "Welcome to EchoMix"
    /// headline used before it was cut (redundant with the header's own wordmark) - just smaller, and tinted
    /// the card's own accent instead of spanning a cyan-to-orange gradient across a longer phrase.
    private void DrawWelcomeCardLabel(string text, float cardLeftX, float cardWidth, float y, Vector4 accent, bool lit)
    {
        const float staggerPerLetter = 0.035f;
        const float letterFadeInSeconds = 0.28f;
        const float riseDistance = 10f;

        var drawList = ImGui.GetWindowDrawList();
        Span<float> widths = stackalloc float[text.Length];
        var totalWidth = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            widths[i] = ImGui.CalcTextSize(text[i].ToString()).X;
            totalWidth += widths[i];
        }

        var baseColor = lit ? Vector4.Lerp(accent, Vector4.One, 0.25f) : accent;
        var x = cardLeftX + ((cardWidth - totalWidth) / 2f);

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i].ToString();
            var letterStart = i * staggerPerLetter;
            var letterT = Math.Clamp((welcomeViewSeconds - letterStart) / letterFadeInSeconds, 0f, 1f);

            if (letterT > 0f)
            {
                var color = baseColor;
                color.W *= letterT;
                var yOffset = (1f - letterT) * riseDistance;
                var letterPos = new Vector2(x, y + yOffset);

                var shadow = new Vector4(0f, 0f, 0f, 0.4f * letterT);
                drawList.AddText(letterPos + new Vector2(0f, 1.5f), ImGui.GetColorU32(shadow), ch);
                drawList.AddText(letterPos, ImGui.GetColorU32(color), ch);
            }

            x += widths[i];
        }
    }

    /// Centers whatever's drawn next (an input, a button, a line of text) within a region availWidth wide
    /// starting at the current cursor X - the same "measure then SetCursorPosX" idiom HostLobbyWindow already
    /// uses for its own centered Monitor Volume fader/label.
    private static void CenterNextItem(float itemWidth, float availWidth)
    {
        var offset = (availWidth - itemWidth) / 2f;
        if (offset > 0f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
    }

    /// The streamlined Listener join screen: a small centered, styled card (Theme's own BeginCard/EndCard -
    /// the same panel look the Deck view's peak meter and digital displays use) rather than a plain
    /// left-aligned copy of the Settings Listen tab.
    private void DrawJoinShowBody(BroadcastStatusMessage broadcast)
    {
        var cardWidth = 300f * Scale;
        var fieldWidth = 220f * Scale;
        if (!string.IsNullOrWhiteSpace(connectRoomCodeBuffer))
            joinShowBlankRoomCodeHint = false;

        var autoJoin = plugin.Configuration.ListenerAutoJoinNearbyShows;
        var hasError = !autoJoin && (!string.IsNullOrEmpty(broadcast.ListenError) || joinShowBlankRoomCodeHint);
        var cardHeight = (autoJoin ? 268f : (hasError ? 356f : 320f)) * Scale;
        var innerWidth = cardWidth - (28f * Scale);

        var avail = ImGui.GetContentRegionAvail();
        var offset = new Vector2(
            MathF.Max(0f, (avail.X - cardWidth) / 2f),
            MathF.Max(0f, (avail.Y - cardHeight) / 2f));
        ImGui.SetCursorPos(ImGui.GetCursorPos() + offset);

        Theme.BeginCard("##joinShowCard", new Vector2(cardWidth, cardHeight), fontScale: Scale);

        using (plugin.Fonts.Header.PushSafe())
        {
            CenterNextItem(ImGui.CalcTextSize("Join a Show").X, innerWidth);
            ImGui.TextColored(Theme.NeutralAccent, "Join a Show");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();

        if (autoJoin)
        {
            DrawAutoJoinStatusLine(innerWidth);
            ImGui.Spacing();
            ImGui.Spacing();
        }
        else
        {
            const string hint = "Enter the room code the DJ gave you.";
            CenterNextItem(ImGui.CalcTextSize(hint).X, innerWidth);
            ImGui.TextDisabled(hint);
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Background);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.16f, 0.16f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.25f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.6f));

            CenterNextItem(fieldWidth, innerWidth);
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.InputTextWithHint("##joinShowRoomCode", "Room code", ref connectRoomCodeBuffer, 16);

            ImGui.Spacing();
            CenterNextItem(fieldWidth, innerWidth);
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.InputTextWithHint("##joinShowPassword", "Password (if set)", ref connectPasswordBuffer, 32, ImGuiInputTextFlags.Password);

            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar();

            ImGui.Spacing();
            ImGui.Spacing();
            var connectButtonSize = new Vector2(160, 32) * Scale;
            CenterNextItem(connectButtonSize.X, innerWidth);
            if (PanelButton.Draw("##joinShowConnect", plugin.Fonts.Icon, FontAwesomeIcon.SignInAlt, "Connect", connectButtonSize, Theme.NeutralAccent))
            {
                if (string.IsNullOrWhiteSpace(connectRoomCodeBuffer))
                {
                    joinShowBlankRoomCodeHint = true;
                }
                else
                {
                    joinShowBlankRoomCodeHint = false;
                    ConnectToRoom(connectRoomCodeBuffer.Trim(), connectPasswordBuffer);
                }
            }
            ImGui.Spacing();
        }

        var buttonSize = new Vector2(160, 32) * Scale;
        CenterNextItem(buttonSize.X, innerWidth);
        if (PanelButton.Draw("##viewLiveShows", plugin.Fonts.Icon, FontAwesomeIcon.Globe, "View Live Shows", buttonSize, Theme.CyanAccent))
        {
            viewBeforeBrowseShows = currentView;
            pendingView = ViewMode.BrowseShows;
            plugin.AudioHostClient.Send(MessageType.RequestPublicShows, new object());
        }

        ImGui.Spacing();
        ImGui.Spacing();
        if (SettingsToggle.Draw("##joinShowAutoJoin", "Auto-Join Nearby Shows (Beta)", ref autoJoin,
                "Joins a live, public, Proximity-mode show automatically once you're in range, and leaves again once you walk out - one show at a time. While this is on, it overrides manual joining entirely; switch it off to go back to joining shows yourself."))
        {
            plugin.Configuration.ListenerAutoJoinNearbyShows = autoJoin;
            plugin.Configuration.Save();
        }

        if (hasError)
        {
            var errorText = joinShowBlankRoomCodeHint ? "Enter a room code first." : broadcast.ListenError!;
            ImGui.Spacing();
            var errorWidth = ImGui.CalcTextSize(errorText).X;
            if (errorWidth <= innerWidth)
            {
                CenterNextItem(errorWidth, innerWidth);
                ImGui.TextColored(Theme.OrangeAccent, errorText);
            }
            else
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerWidth);
                ImGui.TextColored(Theme.OrangeAccent, errorText);
                ImGui.PopTextWrapPos();
            }
        }

        Theme.EndCard();
    }

    /// Shared by the manual room-code Connect button above and a clicked card in DrawBrowseShowsBody - a
    /// public show's card supplies its RoomCode with an empty password (see Room.IsPubliclyListed's own doc
    /// comment for why that's accepted), everything else is identical to typing the code in by hand.
    private void ConnectToRoom(string roomCode, string password)
    {
        if (plugin.Configuration.ListenerAutoJoinNearbyShows)
            return;

        plugin.Configuration.LastRoomCode = roomCode;
        plugin.Configuration.Save();
        plugin.AudioHostClient.Send(MessageType.ConnectToRemote, new ConnectToRemoteCommand
        {
            RoomCode = roomCode,
            Password = password,
            CharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
        });
    }

    /// The "View Live Shows" grid - a one-shot snapshot (see AudioHostClient.
    private static readonly string[] LiveShowsTabLabels = { "Live Shows", "DJ List" };

    /// Shared "Live Shows"/"DJ List" tab strip at the top of both browse screens - same TabStrip control (see
    /// Controls\TabStrip.cs) Settings' own tab bar uses, in place of the old bare DrawSectionToggleWord pair.
    private void DrawLiveShowsNavToggle(bool isDjList)
    {
        if (pendingView == null)
            liveShowsTabStrip.Sync(isDjList ? 1 : 0);

        using (plugin.Fonts.Header.PushSafe())
            liveShowsTabStrip.Draw("##liveShowsTabs", LiveShowsTabLabels);

        if (liveShowsTabStrip.JustClicked == 0 && isDjList)
        {
            pendingView = ViewMode.BrowseShows;
            plugin.AudioHostClient.Send(MessageType.RequestPublicShows, new object());
        }
        else if (liveShowsTabStrip.JustClicked == 1 && !isDjList)
        {
            pendingView = ViewMode.DjList;
            plugin.AudioHostClient.Send(MessageType.RequestDjProfiles, new RequestDjProfilesMessage
            {
                RequesterCharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
            });
        }
    }

    /// The whole Live Shows/DJ List nav row - the tab strip plus whichever trailing action button belongs to
    /// that side (Refresh, or Add Listing when the player has no listing yet) - drawn from PreDraw's dispatch
    /// BEFORE the contentAlpha push wraps the actual grid body, not from inside
    /// DrawBrowseShowsBody/DrawDjListBody themselves.
    private void DrawLiveShowsHeaderRow(bool isDjList)
    {
        DrawLiveShowsNavToggle(isDjList);

        if (!isDjList)
        {
            var refreshSize = new Vector2(110, 26) * Scale;
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - refreshSize.X + ImGui.GetCursorPosX());
            if (PanelButton.Draw("##refreshLiveShows", plugin.Fonts.Icon, FontAwesomeIcon.SyncAlt, "Refresh", refreshSize, Theme.NeutralAccent))
            {
                lock (publicShowImageGate)
                {
                    foreach (var texture in publicShowImageCache.Values)
                        texture?.Dispose();
                    publicShowImageCache.Clear();
                    publicShowImageLoading.Clear();
                }

                plugin.AudioHostClient.Send(MessageType.RequestPublicShows, new object());
            }
        }
        else
        {
            var snapshot = plugin.AudioHostClient.LatestDjProfiles;
            var hasOwnProfile = snapshot?.Profiles.Any(p => p.IsOwnProfile) ?? false;
            if (!hasOwnProfile)
            {
                var addSize = new Vector2(130, 26) * Scale;
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - addSize.X + ImGui.GetCursorPosX());
                if (PanelButton.Draw("##addDjListing", plugin.Fonts.Icon, FontAwesomeIcon.Plus, "Add Listing", addSize, Theme.NeutralAccent))
                {
                    ResetDjEditBuffersForNewProfile();
                    viewBeforeDjProfileEdit = ViewMode.DjList;
                    pendingView = ViewMode.DjProfileEdit;
                }

                var linkSize = new Vector2(150, 26) * Scale;
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - addSize.X - linkSize.X - (8f * Scale) + ImGui.GetCursorPosX());
                if (PanelButton.Draw("##linkDjCharacter", plugin.Fonts.Icon, FontAwesomeIcon.Link, "Link a Character", linkSize, Theme.NeutralAccent))
                {
                    djRedeemCodeBuffer = string.Empty;
                    djRedeemResult = null;
                    ImGui.OpenPopup("##redeemLinkCodePopup");
                }

                DrawRedeemLinkCodePopup();
            }
        }

        ImGui.Spacing();
        ImGui.Spacing();
    }

    /// The redeem side of Linked Characters (see the edit form's own LINKED CHARACTERS panel for the generate
    /// side) - reachable from a character with no listing of its own yet, via the DJ List header's "Link a
    /// Character" button.
    private void DrawRedeemLinkCodePopup()
    {
        if (!ImGui.BeginPopup("##redeemLinkCodePopup"))
            return;

        ImGui.SetWindowFontScale(Scale);
        ImGui.TextColored(Theme.CyanAccent, "Link a Character");
        ImGui.TextDisabled("Enter a code generated from another character's DJ Profile (Edit Listing >");
        ImGui.TextDisabled("Linked Characters) to share that listing's likes, follows, and live status");
        ImGui.TextDisabled("with this character too.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(200f * Scale);
        var enterPressed = ImGui.InputTextWithHint("##redeemCode", "e.g. AB12CD", ref djRedeemCodeBuffer, 6,
            ImGuiInputTextFlags.CharsUppercase | ImGuiInputTextFlags.EnterReturnsTrue);

        if (djRedeemSending)
        {
            ImGui.TextDisabled("Linking...");
        }
        else if ((PanelButton.Draw("##redeemCodeSend", null, null, "Link", new Vector2(120, 26) * Scale, Theme.NeutralAccent) || enterPressed)
                 && !string.IsNullOrWhiteSpace(djRedeemCodeBuffer))
        {
            djRedeemSending = true;
            djRedeemResult = null;
            plugin.AudioHostClient.Send(MessageType.RedeemProfileLinkCode, new RedeemProfileLinkCodeMessage
            {
                Code = djRedeemCodeBuffer.Trim(),
                RequesterCharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
            });
        }

        if (djRedeemResult != null)
        {
            ImGui.Spacing();
            if (djRedeemResult.Success)
            {
                ImGui.TextColored(Theme.CyanAccent, $"Linked to \"{djRedeemResult.DjName}\".");
                ImGui.CloseCurrentPopup();
            }
            else
            {
                ImGui.TextColored(Theme.OrangeAccent, djRedeemResult.Error ?? "Couldn't link that code.");
            }
        }

        ImGui.EndPopup();
    }

    private const int LiveShowsColumns = 3;
    private const int DjListColumns = 4;

    /// A small icon glyph prefixing something ELSE drawn via normal ImGui widgets (a combo box, an input
    /// field - not plain text, see DrawIconMetaRow for that case) in normal layout flow - advances the cursor
    /// like a Dummy the same size as the glyph, caller follows with ImGui.SameLine() to continue the row.
    private void DrawInlineIcon(FontAwesomeIcon icon, Vector4 color)
    {
        string glyph;
        Vector2 glyphSize;
        using (plugin.Fonts.Icon.PushSafe())
        {
            glyph = icon.ToIconString();
            glyphSize = ImGui.CalcTextSize(glyph);
        }

        var rowHeight = MathF.Max(ImGui.GetTextLineHeight(), glyphSize.Y);
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0f, (rowHeight - glyphSize.Y) / 2f);
        using (plugin.Fonts.Icon.PushSafe())
            ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(color), glyph);

        ImGui.Dummy(new Vector2(glyphSize.X, rowHeight));
    }

    /// A single "icon + text" metadata row - both the glyph and the text are drawn directly via the draw list
    /// and manually centered within the SAME shared row height, instead of pairing a manually-positioned icon
    /// (DrawInlineIcon) with a plain ImGui.TextDisabled item via SameLine.
    private void DrawIconMetaRow(FontAwesomeIcon icon, Vector4 iconColor, string text, Vector4 textColor, float maxTextWidth, float? columnWidth = null)
    {
        string glyph;
        Vector2 glyphSize;
        using (plugin.Fonts.Icon.PushSafe())
        {
            glyph = icon.ToIconString();
            glyphSize = ImGui.CalcTextSize(glyph);
        }

        var gap = 6f * Scale;
        var iconColumnWidth = columnWidth ?? glyphSize.X;
        var truncatedText = TruncateToWidth(text, MathF.Max(0f, maxTextWidth - iconColumnWidth - gap));
        var textSize = ImGui.CalcTextSize(truncatedText);
        var rowHeight = MathF.Max(textSize.Y, glyphSize.Y);

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var iconPos = origin + new Vector2((iconColumnWidth - glyphSize.X) / 2f, (rowHeight - glyphSize.Y) / 2f);
        using (plugin.Fonts.Icon.PushSafe())
            drawList.AddText(iconPos, ImGui.GetColorU32(iconColor), glyph);

        var textPos = origin + new Vector2(iconColumnWidth + gap, (rowHeight - textSize.Y) / 2f);
        drawList.AddText(textPos, ImGui.GetColorU32(textColor), truncatedText);

        ImGui.Dummy(new Vector2(iconColumnWidth + gap + textSize.X, rowHeight));
    }

    /// A centered "nothing here" placeholder - a large low-alpha icon above a message, filling whatever's
    /// left of the current content region - used by every empty/loading/ error branch across the Live Shows
    /// and DJ List grids instead of a single line of disabled text pinned to the top-left corner.
    private void DrawEmptyState(FontAwesomeIcon icon, string message)
    {
        var avail = ImGui.GetContentRegionAvail();
        var areaHeight = MathF.Max(160f * Scale, avail.Y);

        string glyph;
        Vector2 glyphSize;
        using (plugin.Fonts.IconLarge.PushSafe())
        {
            glyph = icon.ToIconString();
            glyphSize = ImGui.CalcTextSize(glyph);
        }

        var messageSize = ImGui.CalcTextSize(message);
        var gap = 12f * Scale;
        var blockTop = MathF.Max(0f, (areaHeight - glyphSize.Y - gap - messageSize.Y) / 2f);

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var iconColor = ImGui.GetColorU32(new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.28f));
        var textColor = ImGui.GetColorU32(new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.55f));

        using (plugin.Fonts.IconLarge.PushSafe())
            drawList.AddText(origin + new Vector2((avail.X - glyphSize.X) / 2f, blockTop), iconColor, glyph);

        drawList.AddText(origin + new Vector2((avail.X - messageSize.X) / 2f, blockTop + glyphSize.Y + gap), textColor, message);

        ImGui.Dummy(new Vector2(avail.X, areaHeight));
    }

    private void DrawBrowseShowsBody()
    {
        var snapshot = plugin.AudioHostClient.LatestPublicShows;

        if (!string.IsNullOrEmpty(visitError))
            ImGui.TextColored(Theme.OrangeAccent, visitError);

        if (snapshot == null)
        {
            DrawEmptyState(FontAwesomeIcon.Compass, "Looking for live shows...");
            return;
        }

        if (!string.IsNullOrEmpty(snapshot.Error))
        {
            DrawEmptyState(FontAwesomeIcon.ExclamationTriangle, $"Couldn't reach the relay: {snapshot.Error}");
            return;
        }

        if (snapshot.Shows.Count == 0)
        {
            DrawEmptyState(FontAwesomeIcon.Music, "No shows are currently live - check back later, or hit Refresh.");
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0f, 0f, 0f, 0f));
        ImGui.BeginChild("##browseShowsScroll", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false, ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(Scale);
        var scrollbarInset = ImGui.GetStyle().ScrollbarSize;
        ImGui.Indent(scrollbarInset);

        DrawInlineIcon(FontAwesomeIcon.Filter, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.7f));
        ImGui.SameLine(0, 6f * Scale);
        if (!ReferenceEquals(liveShowsGenreFilterOptionsSource, snapshot))
        {
            liveShowsGenreFilterOptionsSource = snapshot;
            liveShowsGenreFilterOptions = ComputeGenreFilterOptions(snapshot.Shows.Select(s => s.Genres));
        }
        liveShowsGenreFilter = DrawGenreFilter("##liveShowsGenreFilter", liveShowsGenreFilterOptions, liveShowsGenreFilter, 180f * Scale);
        ImGui.Spacing();

        var shows = string.IsNullOrEmpty(liveShowsGenreFilter)
            ? snapshot.Shows
            : snapshot.Shows.Where(s => s.Genres.Any(g => string.Equals(g, liveShowsGenreFilter, StringComparison.OrdinalIgnoreCase))).ToList();

        if (shows.Count == 0)
        {
            DrawEmptyState(FontAwesomeIcon.Filter, $"No live shows under \"{liveShowsGenreFilter}\" right now.");
        }
        else
        {
            var gap = 12f * Scale;
            var avail = ImGui.GetContentRegionAvail().X;
            var cardWidth = (avail - (gap * (LiveShowsColumns - 1))) / LiveShowsColumns;
            var cardSize = new Vector2(cardWidth, browseShowCardHeight * Scale);

            measuredBrowseShowCardHeight = 0f;
            for (var i = 0; i < shows.Count; i++)
            {
                if (i % LiveShowsColumns != 0)
                    ImGui.SameLine(0, gap);
                else if (i > 0)
                    ImGui.Spacing();

                DrawPublicShowCard(shows[i], cardSize);
            }

            if (measuredBrowseShowCardHeight > 0f)
                browseShowCardHeight = measuredBrowseShowCardHeight;
        }

        DrawScrollToTopButton();

        ImGui.Unindent(scrollbarInset);
        ImGui.EndChild();
        ImGui.PopStyleColor(4);
    }

    /// One card in the live-shows grid - PushID'd by RoomCode (same idiom PlaylistPanel.DrawTrackList already
    /// uses per-track) so every id inside, including Theme.BeginCard's own child window, is unique across
    /// however many cards are on screen at once.
    private void DrawPublicShowCard(PublicShowEntryDto show, Vector2 cardSize)
    {
        ImGui.PushID(show.RoomCode);
        var innerWidth = cardSize.X - (28f * Scale);

        var accentColor = show.IsVenueShow ? Theme.FixedOrange : Theme.FixedCyan;
        var currentGlow = publicShowCardGlow.TryGetValue(show.RoomCode, out var existingGlow) ? existingGlow : 0f;
        currentGlow = UiHelpers.Lerp(currentGlow, 1f, 10f, ImGui.GetIO().DeltaTime);
        publicShowCardGlow[show.RoomCode] = currentGlow;

        Theme.BeginCard("##publicShowCard", cardSize, glow: currentGlow, glowColor: accentColor, fontScale: Scale, gradientTint: accentColor);
        var contentStartY = ImGui.GetCursorPosY();
        var cardContentPos = ImGui.GetCursorPos();

        DrawPublicShowImage(show, innerWidth);
        ImGui.Spacing();

        var displayName = string.IsNullOrWhiteSpace(show.ShowName) ? $"{show.DjName}'s Show" : show.ShowName;
        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextColored(Theme.CyanAccent, TruncateToWidth(displayName, innerWidth));

        var mutedMeta = new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.5f);

        float metaIconColumnWidth;
        using (plugin.Fonts.Icon.PushSafe())
        {
            metaIconColumnWidth = MathF.Max(ImGui.CalcTextSize(FontAwesomeIcon.Clock.ToIconString()).X,
                MathF.Max(ImGui.CalcTextSize(FontAwesomeIcon.MapMarkerAlt.ToIconString()).X, ImGui.CalcTextSize(FontAwesomeIcon.Headphones.ToIconString()).X));
        }

        DrawIconMetaRow(FontAwesomeIcon.Clock, mutedMeta, $"{show.DjName} - {FormatElapsed(DateTime.UtcNow - show.LiveSinceUtc)}", mutedMeta, innerWidth, metaIconColumnWidth);

        var (addressLine1, addressLine2) = FormatVenueAddressLines(show);
        if (addressLine1 != null)
        {
            DrawIconMetaRow(FontAwesomeIcon.MapMarkerAlt, mutedMeta, addressLine1, mutedMeta, innerWidth, metaIconColumnWidth);
            if (addressLine2 != null)
            {
                var indent = metaIconColumnWidth + (6f * Scale);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
                ImGui.TextDisabled(TruncateToWidth(addressLine2, innerWidth - indent));
            }
        }

        DrawIconMetaRow(FontAwesomeIcon.Headphones, mutedMeta, $"{show.ListenerCount} listening", mutedMeta, innerWidth, metaIconColumnWidth);

        void TryJoinShow()
        {
            if (show.HasPassword)
            {
                joinPasswordBuffer = string.Empty;
                ImGui.OpenPopup("##joinPasswordPopup");
            }
            else
            {
                lastAttemptedJoinRoomCode = show.RoomCode;
                ConnectToRoom(show.RoomCode, string.Empty);
            }
        }

        var joinIcon = show.HasPassword ? FontAwesomeIcon.Lock : FontAwesomeIcon.SignInAlt;

        ImGui.Spacing();
        var canVisit = show.IsVenueShow && LifestreamIntegration.HasVisitableLocation(show.VenueWorld, show.VenueHousingArea, show.VenueWard, show.VenuePlot);
        if (canVisit)
        {
            var buttonGap = 8f * Scale;
            var halfSize = new Vector2((innerWidth - buttonGap) / 2f, 26f * Scale);
            if (PanelButton.Draw("##joinPublicShow", plugin.Fonts.Icon, joinIcon, "Join", halfSize, Theme.NeutralAccent))
                TryJoinShow();
            ImGui.SameLine(0, buttonGap);
            if (PanelButton.Draw("##visitPublicShow", plugin.Fonts.Icon, FontAwesomeIcon.MapMarkerAlt, "Visit", halfSize, Theme.OrangeAccent))
                visitError = LifestreamIntegration.TryVisit(show.VenueWorld!, show.VenueHousingArea!, show.VenueWard!, show.VenuePlot!, show.VenueIsApartment, show.VenueSubdivision);
        }
        else
        {
            var joinSize = new Vector2(innerWidth, 26f * Scale);
            if (PanelButton.Draw("##joinPublicShow", plugin.Fonts.Icon, joinIcon, "Join", joinSize, Theme.NeutralAccent))
                TryJoinShow();
        }

        var listenError = plugin.AudioHostClient.LatestStatus.Broadcast.ListenError;
        if (!show.HasPassword && lastAttemptedJoinRoomCode == show.RoomCode && !string.IsNullOrEmpty(listenError))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerWidth);
            ImGui.TextColored(Theme.OrangeAccent, listenError);
            ImGui.PopTextWrapPos();
        }

        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1.5f);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(Theme.CyanAccent.X, Theme.CyanAccent.Y, Theme.CyanAccent.Z, 0.7f));
        if (ImGui.BeginPopup("##joinPasswordPopup"))
        {
            ImGui.SetWindowFontScale(Scale);
            var popupContentWidth = 240f * Scale;
            ImGui.TextColored(Theme.CyanAccent, "Password Required");
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + popupContentWidth);
            ImGui.TextDisabled($"{displayName} needs a password to join.");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            ImGui.SetNextItemWidth(popupContentWidth);
            var enterPressed = ImGui.InputTextWithHint("##joinPasswordInput", "Password", ref joinPasswordBuffer, 32,
                ImGuiInputTextFlags.Password | ImGuiInputTextFlags.EnterReturnsTrue);

            if (lastAttemptedJoinRoomCode == show.RoomCode && !string.IsNullOrEmpty(listenError))
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + popupContentWidth);
                ImGui.TextColored(Theme.OrangeAccent, listenError);
                ImGui.PopTextWrapPos();
            }

            ImGui.Spacing();
            if ((PanelButton.Draw("##joinPasswordConfirm", plugin.Fonts.Icon, FontAwesomeIcon.SignInAlt, "Connect", new Vector2(popupContentWidth, 28f * Scale), Theme.NeutralAccent) || enterPressed))
            {
                lastAttemptedJoinRoomCode = show.RoomCode;
                ConnectToRoom(show.RoomCode, joinPasswordBuffer);
            }

            ImGui.EndPopup();
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        var afterContentPos = ImGui.GetCursorPos();
        var overlayMargin = 6f * Scale;

        var venueLabel = show.IsVenueShow ? (string.IsNullOrWhiteSpace(show.VenueName) ? "Venue" : show.VenueName) : "Global";
        var venueBadgeText = TruncateToWidth(venueLabel, innerWidth * 0.55f);
        ImGui.SetCursorPos(cardContentPos + new Vector2(overlayMargin, overlayMargin));
        DrawSingleChip(venueBadgeText, show.IsVenueShow ? Theme.FixedOrange : Theme.FixedCyan, 8f * Scale, 3f * Scale);


        var reportIconSize = 20f * Scale;
        ImGui.SetCursorPos(cardContentPos + new Vector2(innerWidth - reportIconSize - (4f * Scale), 4f * Scale));
        var reportIconScreenPos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddCircleFilled(reportIconScreenPos + new Vector2(reportIconSize / 2f, reportIconSize / 2f),
            (reportIconSize / 2f) + (3f * Scale), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)));
        if (DrawIconOnlyButton("##reportShow", plugin.Fonts.Icon, FontAwesomeIcon.Flag, "Report this show", reportIconSize, Theme.OrangeAccent, false))
        {
            reportShowReasonBuffer = string.Empty;
            reportShowSendResult = null;
            ImGui.OpenPopup("##reportShowPopup");
        }
        ImGui.SetCursorPos(afterContentPos);

        if (ImGui.BeginPopup("##reportShowPopup"))
        {
            ImGui.SetWindowFontScale(Scale);
            ImGui.TextColored(Theme.OrangeAccent, "Report This Show");
            ImGui.TextDisabled("Sends the room code and your character name to the developer -");
            ImGui.TextDisabled("only use this for actual abuse, not to grief another DJ.");
            ImGui.Spacing();

            ImGui.TextDisabled("What's wrong? (required)");
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Background);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.16f, 0.16f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.25f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.6f));
            ImGui.SetNextItemWidth(280f * Scale);
            WrappedInput.Multiline("##reportShowReason", ref reportShowReasonBuffer, 500, new Vector2(280f, 60f) * Scale);
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar();
            ImGui.Spacing();

            if (reportShowSending)
            {
                ImGui.TextDisabled("Sending...");
            }
            else if (PanelButton.Draw("##reportShowSend", null, null, "Send Report", new Vector2(160, 28) * Scale, Theme.OrangeAccent))
            {
                if (string.IsNullOrWhiteSpace(reportShowReasonBuffer))
                {
                    reportShowSendResult = new ReportShowResultMessage { Success = false, Error = "Please describe what's wrong first." };
                }
                else
                {
                    reportShowSending = true;
                    reportShowSendResult = null;
                    plugin.AudioHostClient.Send(MessageType.ReportShow, new ReportShowCommand
                    {
                        RoomCode = show.RoomCode,
                        ShowName = show.ShowName,
                        DjName = show.DjName,
                        Reason = WrappedInput.Unfold(reportShowReasonBuffer, WrappedInput.WidthFor(new Vector2(280f, 60f) * Scale)).Trim(),
                        ReporterCharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
                    });
                }
            }

            if (reportShowSendResult != null)
            {
                ImGui.Spacing();
                if (reportShowSendResult.Success)
                    ImGui.TextColored(Theme.CyanAccent, "Reported - thank you.");
                else
                    ImGui.TextColored(Theme.OrangeAccent, $"Couldn't send: {reportShowSendResult.Error ?? "unknown error"}");
            }

            ImGui.EndPopup();
        }

        var contentHeight = ImGui.GetCursorPosY() - contentStartY + 20f;
        measuredBrowseShowCardHeight = Math.Max(measuredBrowseShowCardHeight, contentHeight / Math.Max(Scale, 0.01f));

        Theme.EndCard();
        ImGui.PopID();
    }

    private const float ShowImageRounding = 12f;

    /// Draws this show's image if it's already cached, kicks off decoding it if it isn't (and nothing's
    /// already in flight for this RoomCode), or falls back to a plain placeholder icon - while loading, once
    /// decoding failed, or if this show simply never uploaded one.
    private void DrawPublicShowImage(PublicShowEntryDto show, float innerWidth)
    {
        var imageHeight = innerWidth * (ShowImageProcessor.TargetHeight / (float)ShowImageProcessor.TargetWidth);
        var imageSize = new Vector2(innerWidth, imageHeight);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (!string.IsNullOrEmpty(show.ImageBase64))
        {
            bool hasEntry;
            IDalamudTextureWrap? texture;
            var shouldStartLoad = false;
            lock (publicShowImageGate)
            {
                hasEntry = publicShowImageCache.TryGetValue(show.RoomCode, out texture);
                if (!hasEntry)
                    shouldStartLoad = publicShowImageLoading.Add(show.RoomCode);
            }

            if (shouldStartLoad)
                _ = LoadPublicShowImageAsync(show.RoomCode, show.ImageBase64);

            if (hasEntry && texture != null)
            {
                drawList.AddImageRounded(texture.Handle, origin, origin + imageSize, Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(Vector4.One), ShowImageRounding, ImDrawFlags.RoundCornersTop);
                DrawShowImageDepth(drawList, origin, imageSize);
                ImGui.Dummy(imageSize);
                return;
            }
        }

        drawList.AddRectFilled(origin, origin + imageSize, ImGui.GetColorU32(Theme.Background), ShowImageRounding, ImDrawFlags.RoundCornersTop);
        using (plugin.Fonts.IconLarge.PushSafe())
        {
            var glyph = FontAwesomeIcon.Image.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(origin + ((imageSize - glyphSize) / 2f), ImGui.GetColorU32(Theme.NeutralAccent * new Vector4(1f, 1f, 1f, 0.4f)), glyph);
        }
        ImGui.Dummy(imageSize);
    }

    /// A top-down scrim plus a soft bottom vignette over the show image - not just decorative, the top one is
    /// what keeps the Venue/Global and report-flag badges overlaid on it (see DrawPublicShowCard) legible
    /// regardless of how bright a DJ's own uploaded image is, and the bottom one softens what would otherwise
    /// be a hard flat seam where the photo ends and the card's own text content begins right below it.
    private static void DrawShowImageDepth(ImDrawListPtr drawList, Vector2 origin, Vector2 imageSize)
    {
        var topFadeBottom = origin.Y + (imageSize.Y * 0.35f);
        drawList.AddRectFilledMultiColor(origin, new Vector2(origin.X + imageSize.X, topFadeBottom),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.5f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.5f)),
            0x00000000u, 0x00000000u);

        var bottomFadeTop = origin.Y + (imageSize.Y * 0.72f);
        drawList.AddRectFilledMultiColor(new Vector2(origin.X, bottomFadeTop), origin + imageSize,
            0x00000000u, 0x00000000u,
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.4f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.4f)));
    }

    private async Task LoadPublicShowImageAsync(string roomCode, string imageBase64)
    {
        IDalamudTextureWrap? wrap = null;
        try
        {
            var bytes = Convert.FromBase64String(imageBase64);
            wrap = await Plugin.TextureProvider.CreateFromImageAsync(bytes);
        }
        catch
        {
        }

        lock (publicShowImageGate)
        {
            publicShowImageCache[roomCode] = wrap;
            publicShowImageLoading.Remove(roomCode);
        }
    }

    /// Composes whichever of the structured venue-address fields a DJ actually filled in into exactly two
    /// fixed display lines - "Data Center - World, Housing Area" and "Ward X, Plot X" - rather than one
    /// flowing string, so every card can reserve the same two-line-tall address block regardless of which
    /// fields are set (a null line still gets drawn as a blank placeholder by the caller, purely to keep
    /// every card's Join button at the same height).
    private static (string? Line1, string? Line2) FormatVenueAddressLines(PublicShowEntryDto show)
    {
        if (!show.IsVenueShow)
            return (null, null);

        var line1Parts = new List<string>();
        var server = string.Join(" - ", new[] { show.VenueDataCenter, show.VenueWorld }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(server))
            line1Parts.Add(server);
        if (!string.IsNullOrWhiteSpace(show.VenueHousingArea))
            line1Parts.Add(show.VenueHousingArea!);
        var line1 = line1Parts.Count > 0 ? string.Join(", ", line1Parts) : null;

        var line2Parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(show.VenueWard))
            line2Parts.Add($"Ward {show.VenueWard}{(show.VenueIsApartment && show.VenueSubdivision ? " (Subdivision)" : "")}");
        if (!string.IsNullOrWhiteSpace(show.VenuePlot))
            line2Parts.Add(show.VenueIsApartment ? $"Apartment {show.VenuePlot}" : $"Plot {show.VenuePlot}");
        var line2 = line2Parts.Count > 0 ? string.Join(", ", line2Parts) : null;

        return (line1, line2);
    }

    private static readonly string[] DjAvailabilityDayLabels = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    /// The "DJ List" grid - a persistent, always-browsable directory of DJ profiles, independent of who's
    /// currently live (contrast DrawBrowseShowsBody, which only shows what's live right now).
    private void DrawDjListBody()
    {
        var snapshot = plugin.AudioHostClient.LatestDjProfiles;
        var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

        if (snapshot == null)
        {
            DrawEmptyState(FontAwesomeIcon.Compass, "Looking for DJ profiles...");
            return;
        }

        if (!string.IsNullOrEmpty(snapshot.Error))
        {
            DrawEmptyState(FontAwesomeIcon.ExclamationTriangle, $"Couldn't reach the relay: {snapshot.Error}");
            return;
        }

        if (snapshot.Profiles.Count == 0)
        {
            DrawEmptyState(FontAwesomeIcon.Users, "No DJ profiles yet - be the first to add one!");
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0f, 0f, 0f, 0f));
        ImGui.BeginChild("##djListScroll", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false, ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(Scale);
        var scrollbarInset = ImGui.GetStyle().ScrollbarSize;
        ImGui.Indent(scrollbarInset);

        DrawTopLiveDjsShowcase(snapshot.Profiles, characterName);

        var filterRowStartX = ImGui.GetCursorPosX();
        var filterRowAvail = ImGui.GetContentRegionAvail().X;
        var ownProfile = snapshot.Profiles.FirstOrDefault(p => p.IsOwnProfile);

        DrawInlineIcon(FontAwesomeIcon.Filter, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.7f));
        ImGui.SameLine(0, 6f * Scale);
        if (!ReferenceEquals(djListGenreFilterOptionsSource, snapshot))
        {
            djListGenreFilterOptionsSource = snapshot;
            djListGenreFilterOptions = ComputeGenreFilterOptions(snapshot.Profiles.Select(p => p.Genres));
        }
        djListGenreFilter = DrawGenreFilter("##djListGenreFilter", djListGenreFilterOptions, djListGenreFilter, 180f * Scale);

        ImGui.SameLine(0, 12f * Scale);
        DrawInlineIcon(FontAwesomeIcon.Search, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.7f));
        ImGui.SameLine(0, 6f * Scale);
        ImGui.SetNextItemWidth(180f * Scale);
        ImGui.InputTextWithHint("##djListNameSearch", "Search DJ name...", ref djListNameSearchBuffer, 32);

        if (ownProfile != null)
        {
            var editButtonSize = new Vector2(100, 24) * Scale;
            ImGui.SameLine(filterRowStartX + filterRowAvail - editButtonSize.X);
            if (PanelButton.Draw("##editOwnDjProfileFromList", plugin.Fonts.Icon, FontAwesomeIcon.Edit, "Edit", editButtonSize, Theme.NeutralAccent))
            {
                pendingEditProfileIdOnDetailLoad = ownProfile.Id;
                plugin.AudioHostClient.Send(MessageType.GetDjProfileDetail, new GetDjProfileDetailMessage { Id = ownProfile.Id, RequesterCharacterName = characterName });
            }
        }

        ImGui.Spacing();

        var profiles = snapshot.Profiles
            .Where(p => string.IsNullOrEmpty(djListGenreFilter) || p.Genres.Any(g => string.Equals(g, djListGenreFilter, StringComparison.OrdinalIgnoreCase)))
            .Where(p => string.IsNullOrWhiteSpace(djListNameSearchBuffer) || p.DjName.Contains(djListNameSearchBuffer.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.IsLiveNow)
            .ToList();

        if (profiles.Count == 0)
        {
            var noMatchLabel = !string.IsNullOrEmpty(djListGenreFilter) && !string.IsNullOrWhiteSpace(djListNameSearchBuffer)
                ? $"No DJ profiles named \"{djListNameSearchBuffer.Trim()}\" under \"{djListGenreFilter}\"."
                : !string.IsNullOrEmpty(djListGenreFilter)
                    ? $"No DJ profiles listed under \"{djListGenreFilter}\"."
                    : $"No DJ profiles named \"{djListNameSearchBuffer.Trim()}\".";
            DrawEmptyState(FontAwesomeIcon.Filter, noMatchLabel);
        }
        else
        {
            var gap = 12f * Scale;
            var avail = ImGui.GetContentRegionAvail().X;
            var cardWidth = (avail - (gap * (DjListColumns - 1))) / DjListColumns;
            var cardSize = new Vector2(cardWidth, djListCardHeight * Scale);

            measuredDjListCardHeight = 0f;
            for (var i = 0; i < profiles.Count; i++)
            {
                if (i % DjListColumns != 0)
                    ImGui.SameLine(0, gap);
                else if (i > 0)
                    ImGui.Spacing();

                DrawDjProfileCard(profiles[i], cardSize, characterName);
            }

            if (measuredDjListCardHeight > 0f)
                djListCardHeight = measuredDjListCardHeight;
        }

        DrawScrollToTopButton();

        ImGui.Unindent(scrollbarInset);
        ImGui.EndChild();
        ImGui.PopStyleColor(4);
    }

    private const float ScrollToTopShowAfterY = 40f;
    private const float ScrollToTopFadeSpeed = 10f;
    private const float ScrollToTopAnimSpeed = 9f;
    private const float ScrollToTopDiameter = 40f;

    /// A small floating "back to top" button, centered at the top of whichever AlwaysVerticalScrollbar child
    /// called this (DrawDjListBody/DrawBrowseShowsBody) - only once scrolled down a bit, since it'd just be
    /// clutter sitting over content that's already at the top.
    private void DrawScrollToTopButton()
    {
        var scrollY = ImGui.GetScrollY();

        if (scrollToTopAnimating)
        {
            scrollY = UiHelpers.Lerp(scrollY, 0f, ScrollToTopAnimSpeed, ImGui.GetIO().DeltaTime);
            if (scrollY < 0.5f)
            {
                scrollY = 0f;
                scrollToTopAnimating = false;
            }
            ImGui.SetScrollY(scrollY);
        }

        var targetVisibility = scrollY > ScrollToTopShowAfterY * Scale || scrollToTopAnimating ? 1f : 0f;
        scrollToTopVisibility = UiHelpers.Lerp(scrollToTopVisibility, targetVisibility, ScrollToTopFadeSpeed, ImGui.GetIO().DeltaTime);

        if (scrollToTopVisibility < 0.01f)
            return;

        var windowPos = ImGui.GetWindowPos();
        var windowWidth = ImGui.GetWindowSize().X;
        var diameter = ScrollToTopDiameter * Scale;

        var slideOffset = (1f - scrollToTopVisibility) * 10f * Scale;
        var center = new Vector2(windowPos.X + (windowWidth / 2f), windowPos.Y + (10f * Scale) - slideOffset + (diameter / 2f));

        var mousePos = ImGui.GetIO().MousePos;
        var hovered = Vector2.Distance(mousePos, center) <= diameter / 2f;
        var active = hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var clicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        var drawList = ImGui.GetForegroundDrawList();
        var a = scrollToTopVisibility;

        drawList.AddCircleFilled(center + new Vector2(0f, 2f * Scale), diameter / 2f, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f * a)));

        var glowAlpha = (hovered ? 0.4f : 0.24f) * a;
        drawList.AddCircleFilled(center, (diameter / 2f) + (7f * Scale), ImGui.GetColorU32(new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, glowAlpha)));

        var bodyColor = active ? Theme.NeutralAccentActive : hovered ? Theme.NeutralAccentHover : Theme.NeutralAccent;
        drawList.AddCircleFilled(center, diameter / 2f, ImGui.GetColorU32(new Vector4(bodyColor.X, bodyColor.Y, bodyColor.Z, a)));
        drawList.AddCircle(center, diameter / 2f, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.3f * a)), 0, 1.5f * Scale);

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.ArrowUp, center + new Vector2(0f, active ? 1.5f * Scale : 0f),
                ImGui.GetColorU32(new Vector4(Theme.Background.X, Theme.Background.Y, Theme.Background.Z, a)));
        }

        if (hovered)
            ImGui.SetTooltip("Back to top");

        if (clicked)
            scrollToTopAnimating = true;
    }

    private const float ShowcaseHeight = 235f;
    private const float ShowcaseCenterOffsetY = 108f;
    private const float ShowcaseOrbitRadiusX = 150f;
    private const float ShowcaseOrbitRadiusY = 22f;
    private const float ShowcaseAngularSpeed = 0.22f;    private const float ShowcaseBobAmplitude = 6f;
    private const float ShowcaseBobSpeed = 1.1f;
    private const int ShowcaseTrailDots = 4;
    private const float ShowcaseTrailStep = 0.11f;
    /// A spotlight above the DJ List grid for whichever live DJs currently have the most listeners (up to 3)
    /// - the grid below already tells you WHO'S live, this is for "who's actually drawing a crowd right now."
    /// The three (or however many are live, if fewer) orbit a shared center together at the same slow angular
    /// speed so they read as one rotating group rather than three independent things that happen to share a
    /// panel, with a small per-DJ vertical bob layered on top (different phase per slot) so it reads as
    /// floating rather than a rigid carousel, plus a fading comet trail behind each one (same visual language
    /// as Theme.BeginCard's own chasing highlight) so the motion itself reads as intentional rather than
    /// jittery.
    private void DrawTopLiveDjsShowcase(List<DjProfileSummaryDto> allProfiles, string requesterCharacterName)
    {
        var topLive = allProfiles.Where(p => p.IsLiveNow).OrderByDescending(p => p.ListenerCount).Take(3).ToList();
        if (topLive.Count == 0)
            return;

        var avail = ImGui.GetContentRegionAvail().X;
        var height = ShowcaseHeight * Scale;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var accent = Theme.FixedOrange;
        drawList.AddRectFilled(origin, origin + new Vector2(avail, height), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.07f)), 10f * Scale);
        drawList.AddRect(origin, origin + new Vector2(avail, height), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.25f)), 10f * Scale, ImDrawFlags.None, 1f);

        ImGui.SetCursorScreenPos(origin + new Vector2(16f * Scale, 12f * Scale));
        DrawDjProfileColumnLabel("LIVE NOW - TOP BY LISTENERS", accent);

        var center = origin + new Vector2(avail / 2f, ShowcaseCenterOffsetY * Scale);
        var radiusX = ShowcaseOrbitRadiusX * Scale;
        var radiusY = ShowcaseOrbitRadiusY * Scale;

        const int guideSegments = 48;
        for (var s = 0; s < guideSegments; s += 2)
        {
            var a0 = s / (float)guideSegments * MathF.PI * 2f;
            var a1 = (s + 1) / (float)guideSegments * MathF.PI * 2f;
            var p0 = center + new Vector2(MathF.Cos(a0) * radiusX, MathF.Sin(a0) * radiusY);
            var p1 = center + new Vector2(MathF.Cos(a1) * radiusX, MathF.Sin(a1) * radiusY);
            drawList.AddLine(p0, p1, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.12f)), 1f * Scale);
        }

        var time = (float)ImGui.GetTime();
        var count = topLive.Count;
        var labelBaselineY = center.Y + radiusY + (ShowcaseBobAmplitude * Scale) + (46f * Scale);

        var orbs = new List<(DjProfileSummaryDto Profile, Vector2 Pos, float AvatarSize, float GlowAlpha, float Angle, float Bob)>();
        for (var i = 0; i < count; i++)
        {
            var profile = topLive[i];

            var slotAngle = i * (MathF.PI * 2f / count);
            var angle = (time * ShowcaseAngularSpeed) + slotAngle;
            var bob = MathF.Sin((time * ShowcaseBobSpeed) + (i * 2.1f)) * ShowcaseBobAmplitude * Scale;
            var pos = center + new Vector2(MathF.Cos(angle) * radiusX, (MathF.Sin(angle) * radiusY) + bob);
            var avatarSize = (i == 0 ? 78f : 60f) * Scale;

            var glowStrength = topLive[0].ListenerCount > 0 ? profile.ListenerCount / (float)topLive[0].ListenerCount : 1f;
            orbs.Add((profile, pos, avatarSize, 0.12f + (0.18f * glowStrength), angle, bob));
        }

        foreach (var orb in orbs.OrderBy(o => o.Pos.Y))
        {
            var (profile, pos, avatarSize, glowAlpha, angle, bob) = orb;

            for (var t = ShowcaseTrailDots; t >= 1; t--)
            {
                var trailAngle = angle - (t * ShowcaseTrailStep);
                var trailPos = center + new Vector2(MathF.Cos(trailAngle) * radiusX, (MathF.Sin(trailAngle) * radiusY) + bob);
                var trailFade = (1f - (t / (float)(ShowcaseTrailDots + 1))) * glowAlpha;
                var trailRadius = MathF.Max(1.5f, (avatarSize / 2f) * 0.3f * (1f - (t * 0.15f)));
                drawList.AddCircleFilled(trailPos, trailRadius, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, trailFade)));
            }

            drawList.AddCircleFilled(pos, (avatarSize / 2f) + (10f * Scale), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, glowAlpha)));

            var avatarTopLeft = pos - new Vector2(avatarSize / 2f, avatarSize / 2f);
            var frameColor = new Vector4(profile.FrameColorR, profile.FrameColorG, profile.FrameColorB, 1f);

            ImGui.SetCursorScreenPos(avatarTopLeft);
            DrawDjProfileAvatar(profile.Id, profile.AvatarBase64, avatarSize, frameColor, profile.FrameStyle);

            ImGui.SetCursorScreenPos(avatarTopLeft);
            ImGui.PushID(profile.Id);
            var clicked = ImGui.InvisibleButton("##showcaseAvatar", new Vector2(avatarSize, avatarSize));
            var hovered = ImGui.IsItemHovered();
            ImGui.PopID();

            if (clicked)
            {
                plugin.AudioHostClient.Send(MessageType.GetDjProfileDetail, new GetDjProfileDetailMessage { Id = profile.Id, RequesterCharacterName = requesterCharacterName });
                viewBeforeBrowseShows = currentView;
                pendingView = ViewMode.DjProfile;
            }

            if (hovered)
                ImGui.SetTooltip($"{profile.DjName} - {profile.ListenerCount} listener{(profile.ListenerCount == 1 ? "" : "s")} (click to view)");

            var nameText = TruncateToWidth(profile.DjName, 110f * Scale);
            var nameSize = ImGui.CalcTextSize(nameText);
            var countText = $"{profile.ListenerCount} listening";
            var countSize = ImGui.CalcTextSize(countText);

            var blockWidth = MathF.Max(nameSize.X, countSize.X) + (16f * Scale);
            var blockHeight = nameSize.Y + countSize.Y + (6f * Scale);
            var blockMin = new Vector2(pos.X - (blockWidth / 2f), labelBaselineY - (4f * Scale));
            drawList.AddRectFilled(blockMin, blockMin + new Vector2(blockWidth, blockHeight),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.28f)), 6f * Scale);

            var nameColor = hovered ? Theme.Text : new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.85f);
            drawList.AddText(new Vector2(pos.X - (nameSize.X / 2f), labelBaselineY), ImGui.GetColorU32(nameColor), nameText);
            drawList.AddText(new Vector2(pos.X - (countSize.X / 2f), labelBaselineY + nameSize.Y + (2f * Scale)),
                ImGui.GetColorU32(new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.5f)), countText);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0f, height + (10f * Scale)));
    }

    /// One card in the DJ List grid - same self-measuring card-height technique as DrawPublicShowCard (see
    /// browseShowCardHeight's own doc comment), applied independently here since a profile card's content
    /// shape (no address lines, an avatar instead of a wide thumbnail) is different from a show card's.
    private void DrawDjProfileCard(DjProfileSummaryDto profile, Vector2 cardSize, string requesterCharacterName)
    {
        ImGui.PushID(profile.Id);
        var innerWidth = cardSize.X - (28f * Scale);
        var frameColor = new Vector4(profile.FrameColorR, profile.FrameColorG, profile.FrameColorB, 1f);

        var cardScreenPos = ImGui.GetCursorScreenPos();
        var isHovered = ImGui.IsMouseHoveringRect(cardScreenPos, cardScreenPos + cardSize);
        var targetGlow = profile.IsLiveNow ? 1f : isHovered ? 0.55f : 0f;
        var currentGlow = djProfileCardGlow.TryGetValue(profile.Id, out var existingGlow) ? existingGlow : 0f;
        currentGlow = UiHelpers.Lerp(currentGlow, targetGlow, 10f, ImGui.GetIO().DeltaTime);
        djProfileCardGlow[profile.Id] = currentGlow;
        var glowColor = profile.IsLiveNow ? Theme.FixedOrange : frameColor;

        Theme.BeginCard("##djProfileCard", cardSize, glow: currentGlow, glowColor: glowColor, fontScale: Scale, gradientTint: frameColor);
        var contentStartY = ImGui.GetCursorPosY();

        ImGui.Spacing();
        var avatarSize = 130f * Scale;
        CenterNextItem(avatarSize, innerWidth);
        DrawDjProfileAvatar(profile.Id, profile.AvatarBase64, avatarSize, frameColor, profile.FrameStyle);
        ImGui.Spacing();

        string nameText;
        using (plugin.Fonts.Header.PushSafe())
        {
            nameText = TruncateToWidth(profile.DjName, innerWidth);
            CenterNextItem(ImGui.CalcTextSize(nameText).X, innerWidth);
        }
        var nameColor = new Vector4(profile.NameColorR, profile.NameColorG, profile.NameColorB, 1f);
        DrawDjName(nameText, nameColor, profile.NameEffect, 1f);

        if (profile.IsLiveNow)
            DrawLiveBadge(innerWidth);
        else
            ImGui.Dummy(new Vector2(innerWidth, MeasureLiveBadgeHeight()));

        var genreAreaStartY = ImGui.GetCursorPosY();
        if (profile.Genres.Count > 0)
        {
            var displayGenres = profile.Genres.Select(CapitalizeWords).ToList();
            var totalChipWidth = MeasureChipRowWidth(displayGenres);
            if (totalChipWidth <= innerWidth)
                CenterNextItem(totalChipWidth, innerWidth);
            DrawCappedChipRow(displayGenres, innerWidth, Theme.FixedCyan);
        }

        var genreAreaHeight = MeasureCappedChipRowHeight();
        var genreAreaUsed = ImGui.GetCursorPosY() - genreAreaStartY;
        if (genreAreaUsed < genreAreaHeight)
            ImGui.Dummy(new Vector2(innerWidth, genreAreaHeight - genreAreaUsed));

        ImGui.Spacing();
        var toggleButtonSize = 28f * Scale;
        var toggleGap = 8f * Scale;
        var toggleRowWidth = (toggleButtonSize * 2f) + toggleGap;
        CenterNextItem(toggleRowWidth, innerWidth);
        DrawDjProfileLikeAndFollowToggles(profile.Id, profile.IsLikedByRequester, profile.IsFollowedByRequester, requesterCharacterName);

        CenterNextItem(toggleRowWidth, innerWidth);
        var countRowX = ImGui.GetCursorPosX();

        var likeCountText = profile.LikeCount.ToString();
        ImGui.SetCursorPosX(countRowX + ((toggleButtonSize - ImGui.CalcTextSize(likeCountText).X) / 2f));
        ImGui.TextDisabled(likeCountText);

        var followerCountText = profile.FollowerCount.ToString();
        ImGui.SameLine();
        ImGui.SetCursorPosX(countRowX + toggleButtonSize + toggleGap + ((toggleButtonSize - ImGui.CalcTextSize(followerCountText).X) / 2f));
        ImGui.TextDisabled(followerCountText);

        ImGui.Spacing();
        var viewSize = new Vector2(innerWidth, 26f * Scale);
        if (PanelButton.Draw("##viewDjProfile", plugin.Fonts.Icon, FontAwesomeIcon.User, profile.IsOwnProfile ? "View (You)" : "View Profile", viewSize, Theme.NeutralAccent))
        {
            plugin.AudioHostClient.Send(MessageType.GetDjProfileDetail, new GetDjProfileDetailMessage { Id = profile.Id, RequesterCharacterName = requesterCharacterName });
            pendingView = ViewMode.DjProfile;
        }

        var contentHeight = ImGui.GetCursorPosY() - contentStartY + 20f;
        measuredDjListCardHeight = Math.Max(measuredDjListCardHeight, contentHeight / Math.Max(Scale, 0.01f));

        Theme.EndCard();
        ImGui.PopID();
    }

    private Vector2 MeasureLivePillSize()
    {
        var padX = 10f * Scale;
        var padY = 3f * Scale;
        var dotRadius = 3f * Scale;
        var dotGap = 5f * Scale;
        var textSize = ImGui.CalcTextSize("LIVE");
        return new Vector2((dotRadius * 2f) + dotGap + textSize.X + (padX * 2f), textSize.Y + (padY * 2f));
    }

    private float MeasureLiveBadgeHeight() => MeasureLivePillSize().Y;

    /// The pill itself, drawn at whatever the current cursor is - no centering baked in, so both the
    /// (centered) grid card badge and the (left-aligned, inline-with-Join) profile page badge can share one
    /// implementation.
    private void DrawLivePill()
    {
        const string text = "LIVE";
        var padX = 10f * Scale;
        var padY = 3f * Scale;
        var dotRadius = 3f * Scale;
        var dotGap = 5f * Scale;
        var textSize = ImGui.CalcTextSize(text);
        var badgeSize = new Vector2((dotRadius * 2f) + dotGap + textSize.X + (padX * 2f), textSize.Y + (padY * 2f));

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var accent = Theme.FixedOrange;

        drawList.AddRectFilled(pos, pos + badgeSize, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.2f)), badgeSize.Y / 2f);
        drawList.AddRect(pos, pos + badgeSize, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.65f)), badgeSize.Y / 2f, ImDrawFlags.None, 1.2f);

        var dotCenter = pos + new Vector2(padX + dotRadius, badgeSize.Y / 2f);
        var pulse = 0.6f + (0.4f * MathF.Sin((float)ImGui.GetTime() * 3f));
        drawList.AddCircleFilled(dotCenter, dotRadius * 1.8f, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, pulse * 0.3f)));
        drawList.AddCircleFilled(dotCenter, dotRadius, ImGui.GetColorU32(accent));

        drawList.AddText(pos + new Vector2(padX + (dotRadius * 2f) + dotGap, padY), ImGui.GetColorU32(accent), text);
        ImGui.Dummy(badgeSize);
    }

    private void DrawLiveBadge(float innerWidth)
    {
        CenterNextItem(MeasureLivePillSize().X, innerWidth);
        DrawLivePill();
    }

    private float MeasureChipRowWidth(IReadOnlyList<string> items)
    {
        var padX = 8f * Scale;
        var gap = 6f * Scale;
        var total = 0f;
        for (var i = 0; i < items.Count; i++)
        {
            total += ImGui.CalcTextSize(items[i]).X + (padX * 2f);
            if (i > 0)
                total += gap;
        }

        return total;
    }

    /// Heart (Like) and bell (Follow) toggle buttons side by side - shared by the DJ List grid card and the
    /// profile detail page (see DrawDjProfileBody), so both work off the primitive fields DjProfileSummaryDto
    /// and DjProfileDetailDto both happen to carry (same names, unrelated types) rather than a single
    /// concrete DTO.
    private void DrawDjProfileLikeAndFollowToggles(string profileId, bool isLikedByRequester, bool isFollowedByRequester, string requesterCharacterName)
    {
        var buttonSize = new Vector2(28, 28) * Scale;
        var gap = 8f * Scale;

        var isLikePending = djProfileLikePending.Contains(profileId);
        var heartColor = isLikedByRequester ? Theme.OrangeAccent : Theme.NeutralAccent;
        if (isLikePending)
        {
            ImGui.Dummy(buttonSize);
        }
        else if (PanelButton.Draw("##likeDjProfile", plugin.Fonts.Icon, FontAwesomeIcon.Heart, null, buttonSize, heartColor))
        {
            djProfileLikePending.Add(profileId);
            plugin.AudioHostClient.Send(MessageType.ToggleDjProfileLike, new ToggleDjProfileLikeMessage
            {
                ProfileId = profileId,
                CharacterName = requesterCharacterName,
            });
        }

        ImGui.SameLine(0, gap);

        var isFollowPending = djProfileFollowPending.Contains(profileId);
        var bellColor = isFollowedByRequester ? Theme.OrangeAccent : Theme.NeutralAccent;
        if (isFollowPending)
        {
            ImGui.Dummy(buttonSize);
        }
        else
        {
            if (PanelButton.Draw("##followDjProfile", plugin.Fonts.Icon, FontAwesomeIcon.Bell, null, buttonSize, bellColor))
            {
                djProfileFollowPending.Add(profileId);
                plugin.AudioHostClient.Send(MessageType.ToggleDjProfileFollow, new ToggleDjProfileFollowMessage
                {
                    ProfileId = profileId,
                    CharacterName = requesterCharacterName,
                });
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(isFollowedByRequester ? "Following - notified when they go live" : "Follow - get notified when they go live");
        }
    }

    /// Shared by the grid card, the profile detail view, and the edit form's own live preview - draws a
    /// square avatar at `size` inside a DJ-customizable framed border (color + style, see DrawAvatarFrame),
    /// kicking off an async decode+cache the first time a given cacheKey is seen (mirrors
    /// DrawPublicShowImage's exact pattern), or a placeholder person icon while loading/absent - the frame
    /// draws either way.
    private void DrawDjProfileAvatar(string cacheKey, string? avatarBase64, float size, Vector4 frameColor, string frameStyle, IDalamudTextureWrap? overrideTexture = null)
    {
        const float frameRounding = 8f;
        var avatarSize = new Vector2(size, size);
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var imageRounding = IsSquareCorneredFrameStyle(frameStyle) ? 0f : frameRounding;

        for (var i = 3; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 1.5f * Scale);
            drawList.AddRectFilled(origin + offset, origin + avatarSize + offset, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.06f * i)), imageRounding);
        }

        var hasImage = false;
        if (overrideTexture != null)
        {
            drawList.AddImageRounded(overrideTexture.Handle, origin, origin + avatarSize, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), imageRounding);
            ImGui.Dummy(avatarSize);
            hasImage = true;
        }
        else if (!string.IsNullOrEmpty(avatarBase64))
        {
            bool hasEntry;
            IDalamudTextureWrap? texture;
            var shouldStartLoad = false;
            lock (djProfileImageGate)
            {
                hasEntry = djProfileAvatarCache.TryGetValue(cacheKey, out texture);
                if (!hasEntry)
                    shouldStartLoad = djProfileAvatarLoading.Add(cacheKey);
            }

            if (shouldStartLoad)
                _ = LoadDjProfileAvatarAsync(cacheKey, avatarBase64);
            if (hasEntry && texture != null)
            {
                drawList.AddImageRounded(texture.Handle, origin, origin + avatarSize, Vector2.Zero, Vector2.One, ImGui.GetColorU32(Vector4.One), imageRounding);
                ImGui.Dummy(avatarSize);
                hasImage = true;
            }
        }

        if (!hasImage)
        {
            drawList.AddRectFilled(origin, origin + avatarSize, ImGui.GetColorU32(Theme.Background), imageRounding);

            using (plugin.Fonts.IconLarge.PushSafe())
            {
                var placeholderSize = MathF.Min(ImGui.GetFontSize(), size * 0.55f);
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.User, origin + (avatarSize / 2f),
                    ImGui.GetColorU32(Theme.NeutralAccent * new Vector4(1f, 1f, 1f, 0.4f)), placeholderSize);
            }
            ImGui.Dummy(avatarSize);
        }

        DrawAvatarFrame(drawList, origin, avatarSize, frameRounding, frameColor, frameStyle);
    }

    /// The two frame styles whose stroke geometry is fundamentally sharp-cornered by design (see
    /// DrawAvatarFrame) rather than just happening to look that way - everything else (including an
    /// unrecognized/blank style, which falls back to Solid) is rounded.
    private static bool IsSquareCorneredFrameStyle(string style) => style is "Corners" or "Gradient" or "Brackets" or "Sentry";

    /// Renders whichever frame style the profile's owner picked - see the individual case blocks below for
    /// each one's mechanics.
    private void DrawAvatarFrame(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float rounding, Vector4 color, string style)
    {
        var thickness = 3f * Scale;
        var outset = thickness / 2f;
        var outerMin = origin - new Vector2(outset);
        var outerMax = origin + size + new Vector2(outset);
        var outerSize = outerMax - outerMin;
        var r = MathF.Min(rounding + outset, MathF.Min(outerSize.X, outerSize.Y) / 2f);
        var col = ImGui.GetColorU32(color);

        switch (style)
        {
            case "Dashed":
            {
                const int segments = 20;
                for (var i = 0; i < segments; i += 2)
                {
                    var p0 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, i / (float)segments);
                    var p1 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, (i + 0.6f) / segments);
                    drawList.AddLine(p0, p1, col, thickness);
                }
                break;
            }

            case "Dotted":
            {
                const int dots = 28;
                for (var i = 0; i < dots; i++)
                {
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, i / (float)dots);
                    drawList.AddCircleFilled(pos, thickness * 0.6f, col);
                }
                break;
            }

            case "Double":
            {
                var farGap = 4f * Scale;
                drawList.AddRect(outerMin, outerMax, col, r, ImDrawFlags.None, 1.5f * Scale);
                drawList.AddRect(outerMin - new Vector2(farGap), outerMax + new Vector2(farGap), col, r + farGap, ImDrawFlags.None, 1.5f * Scale);
                break;
            }

            case "Corners":
            {
                var armLength = MathF.Min(outerSize.X, outerSize.Y) * 0.22f;
                void DrawCorner(Vector2 corner, Vector2 dirX, Vector2 dirY)
                {
                    drawList.AddLine(corner, corner + (dirX * armLength), col, thickness);
                    drawList.AddLine(corner, corner + (dirY * armLength), col, thickness);
                }

                DrawCorner(outerMin, new Vector2(1f, 0f), new Vector2(0f, 1f));
                DrawCorner(new Vector2(outerMax.X, outerMin.Y), new Vector2(-1f, 0f), new Vector2(0f, 1f));
                DrawCorner(outerMax, new Vector2(-1f, 0f), new Vector2(0f, -1f));
                DrawCorner(new Vector2(outerMin.X, outerMax.Y), new Vector2(1f, 0f), new Vector2(0f, -1f));
                break;
            }

            case "Gradient":
            {
                var tint = new Vector4(MathF.Min(1f, color.X + 0.35f), MathF.Min(1f, color.Y + 0.35f), MathF.Min(1f, color.Z + 0.35f), 1f);
                var c1 = ImGui.GetColorU32(tint);
                var topLeft = outerMin;
                var topRight = new Vector2(outerMax.X, outerMin.Y);
                var bottomRight = outerMax;
                var bottomLeft = new Vector2(outerMin.X, outerMax.Y);
                drawList.AddLine(topLeft, topRight, c1, thickness);
                drawList.AddLine(topRight, bottomRight, col, thickness);
                drawList.AddLine(bottomRight, bottomLeft, c1, thickness);
                drawList.AddLine(bottomLeft, topLeft, col, thickness);
                break;
            }

            case "Glow":
            {
                for (var i = 4; i >= 1; i--)
                {
                    var haloOutset = i * 2.5f * Scale;
                    var alpha = 0.16f / i;
                    drawList.AddRect(outerMin - new Vector2(haloOutset), outerMax + new Vector2(haloOutset),
                        ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha)), r + haloOutset, ImDrawFlags.None, thickness);
                }
                drawList.AddRect(outerMin, outerMax, col, r, ImDrawFlags.None, thickness * 0.7f);
                break;
            }

            case "Pulse":
            {
                var pulse = 0.55f + (0.45f * MathF.Sin((float)ImGui.GetTime() * 2.2f));
                var pulseColor = new Vector4(color.X, color.Y, color.Z, MathF.Max(0.25f, pulse));
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(pulseColor), r, ImDrawFlags.None, (thickness * 0.7f) + (pulse * thickness * 0.6f));
                break;
            }

            case "Chase":
            {
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.3f)), r, ImDrawFlags.None, thickness * 0.6f);

                const int trailDots = 6;
                const float trailSpacing = 0.02f;
                const float loopsPerSecond = 0.3f;
                var headT = (float)(ImGui.GetTime() * loopsPerSecond % 1.0);
                for (var i = 0; i < trailDots; i++)
                {
                    var t = headT - (i * trailSpacing);
                    var alpha = MathF.Pow(1f - (i / (float)trailDots), 1.5f);
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var dotRadius = MathF.Max(1.5f, 4f - (i * 0.4f)) * Scale;
                    drawList.AddCircleFilled(pos, dotRadius, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha)));
                }
                break;
            }

            case "Spin":
            {
                const int segments = 40;
                var timeOffset = (float)(ImGui.GetTime() * 0.25 % 1.0);
                var tint = new Vector4(MathF.Min(1f, color.X + 0.4f), MathF.Min(1f, color.Y + 0.4f), MathF.Min(1f, color.Z + 0.4f), 1f);
                for (var i = 0; i < segments; i++)
                {
                    var t0 = i / (float)segments;
                    var t1 = (i + 1f) / segments;
                    var blend = (MathF.Sin((t0 + timeOffset) * MathF.PI * 2f) + 1f) / 2f;
                    var segColor = Vector4.Lerp(color, tint, blend);
                    var p0 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t0);
                    var p1 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t1);
                    drawList.AddLine(p0, p1, ImGui.GetColorU32(segColor), thickness);
                }
                break;
            }

            case "Rainbow":
            {
                var hue = (float)(ImGui.GetTime() * 0.15 % 1.0);
                var rainbow = HsvToRgb(hue, 0.75f, 1f);
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(rainbow.X, rainbow.Y, rainbow.Z, 1f)), r, ImDrawFlags.None, thickness);
                break;
            }

            case "Sparkle":
            {
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.35f)), r, ImDrawFlags.None, thickness * 0.6f);

                const int sparkleCount = 8;
                var time = (float)ImGui.GetTime();
                for (var i = 0; i < sparkleCount; i++)
                {
                    var t = i / (float)sparkleCount;
                    var phase = (time * 1.3f) + (i * 1.7f);
                    var twinkle = MathF.Max(0f, MathF.Sin(phase));
                    if (twinkle <= 0.05f)
                        continue;

                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var radius = (1.5f + (twinkle * 2.5f)) * Scale;
                    drawList.AddCircleFilled(pos, radius, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, twinkle * 0.9f)));
                    drawList.AddCircleFilled(pos, radius * 0.5f, col);
                }
                break;
            }

            case "Ticks":
            {
                var center = origin + (size / 2f);
                const int tickCount = 16;
                var tickLength = thickness * 1.8f;
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.35f)), r, ImDrawFlags.None, thickness * 0.5f);
                for (var i = 0; i < tickCount; i++)
                {
                    var t = i / (float)tickCount;
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var dir = pos - center;
                    if (dir.LengthSquared() > 0.0001f)
                        dir = Vector2.Normalize(dir);
                    drawList.AddLine(pos, pos + (dir * tickLength), col, thickness * 0.8f);
                }
                break;
            }

            case "Chain":
            {
                const int links = 18;
                for (var i = 0; i < links; i++)
                {
                    var t = i / (float)links;
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var radius = (i % 2 == 0 ? thickness * 0.85f : thickness * 0.45f);
                    drawList.AddCircleFilled(pos, radius, col);
                }
                break;
            }

            case "Brackets":
            {
                var insetFromCorner = outerSize.X * 0.16f;
                var tickDrop = thickness * 1.6f;
                void DrawBracket(float y, float dropDir)
                {
                    var lineStart = new Vector2(outerMin.X + insetFromCorner, y);
                    var lineEnd = new Vector2(outerMax.X - insetFromCorner, y);
                    drawList.AddLine(lineStart, lineEnd, col, thickness);
                    drawList.AddLine(lineStart, lineStart + new Vector2(0f, tickDrop * dropDir), col, thickness);
                    drawList.AddLine(lineEnd, lineEnd + new Vector2(0f, tickDrop * dropDir), col, thickness);
                }
                DrawBracket(outerMin.Y, 1f);
                DrawBracket(outerMax.Y, -1f);
                break;
            }

            case "Stitch":
            {
                const int stitches = 24;
                var stitchLength = thickness * 2.2f;
                for (var i = 0; i < stitches; i++)
                {
                    var t0 = i / (float)stitches;
                    var t1 = (i + 0.15f) / stitches;
                    var p0 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t0);
                    var p1 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t1);
                    var tangent = p1 - p0;
                    if (tangent.LengthSquared() < 0.0001f)
                        tangent = new Vector2(1f, 0f);
                    tangent = Vector2.Normalize(tangent);
                    var normal = new Vector2(-tangent.Y, tangent.X) * (i % 2 == 0 ? 1f : -1f);
                    var diagonal = Vector2.Normalize(tangent + normal) * stitchLength;
                    var mid = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, (t0 + t1) / 2f);
                    drawList.AddLine(mid - (diagonal / 2f), mid + (diagonal / 2f), col, thickness * 0.6f);
                }
                break;
            }

            case "Blocks":
            {
                const int blockCount = 20;
                var blockSize = thickness * 1.6f;
                for (var i = 0; i < blockCount; i++)
                {
                    var t = i / (float)blockCount;
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var half = new Vector2(blockSize / 2f);
                    var alpha = i % 2 == 0 ? 1f : 0.3f;
                    drawList.AddRectFilled(pos - half, pos + half, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha)));
                }
                break;
            }

            case "Sentry":
            {
                Span<Vector2> corners = [outerMin, new Vector2(outerMax.X, outerMin.Y), outerMax, new Vector2(outerMin.X, outerMax.Y)];
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.3f)), r, ImDrawFlags.None, thickness * 0.6f);

                const float secondsPerCorner = 0.6f;
                var cycle = (float)ImGui.GetTime() / secondsPerCorner;
                var index = (int)cycle % corners.Length;
                var localT = cycle - MathF.Floor(cycle);
                var from = corners[index];
                var to = corners[(index + 1) % corners.Length];
                drawList.AddCircleFilled(Vector2.Lerp(from, to, localT), thickness, col);
                break;
            }

            case "Anchor":
            {
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.3f)), r, ImDrawFlags.None, thickness * 0.6f);
                Span<float> anchorT = [0f, 0.25f, 0.5f, 0.75f];
                var time = (float)ImGui.GetTime();
                for (var i = 0; i < anchorT.Length; i++)
                {
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, anchorT[i]);
                    var breathe = (MathF.Sin((time * 1.8f) + (i * MathF.PI / 2f)) + 1f) / 2f;
                    drawList.AddCircleFilled(pos, (thickness * 0.6f) + (breathe * thickness * 0.9f), col);
                }
                break;
            }

            case "Pendulum":
            {
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.3f)), r, ImDrawFlags.None, thickness * 0.6f);
                var swing = (MathF.Sin((float)ImGui.GetTime() * 1.4f) + 1f) / 2f;
                var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, swing * 0.5f);
                drawList.AddCircleFilled(pos, thickness * 0.9f, col);
                break;
            }

            case "Glitch":
            {
                var bucket = (int)((float)ImGui.GetTime() * 6f);
                var rng = new Random(bucket);
                var jitter = new Vector2((rng.NextSingle() - 0.5f) * thickness, (rng.NextSingle() - 0.5f) * thickness);

                const int segments = 16;
                for (var i = 0; i < segments; i++)
                {
                    if (rng.NextDouble() < 0.25)
                        continue;

                    var t0 = i / (float)segments;
                    var t1 = (i + 0.8f) / segments;
                    var p0 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t0) + jitter;
                    var p1 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t1) + jitter;
                    drawList.AddLine(p0, p1, col, thickness);
                }
                break;
            }

            case "Confetti":
            {
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.25f)), r, ImDrawFlags.None, thickness * 0.5f);

                const int particleCount = 10;
                const float cycleSeconds = 2.2f;
                var time = (float)ImGui.GetTime();
                for (var i = 0; i < particleCount; i++)
                {
                    var slotRng = new Random(i * 7919);
                    var t = (float)slotRng.NextDouble();
                    var hue = (float)slotRng.NextDouble();
                    var phase = ((time / cycleSeconds) + (i / (float)particleCount)) % 1f;
                    var life = MathF.Sin(phase * MathF.PI);
                    if (life <= 0.02f)
                        continue;

                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var rgb = HsvToRgb(hue, 0.7f, 1f);
                    drawList.AddCircleFilled(pos, (1.5f + (life * 2f)) * Scale, ImGui.GetColorU32(new Vector4(rgb.X, rgb.Y, rgb.Z, life)));
                }
                break;
            }

            default:                drawList.AddRect(outerMin, outerMax, col, r, ImDrawFlags.None, thickness);
                break;
        }
    }

    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        var i = (int)(h * 6f);
        var f = (h * 6f) - i;
        var p = v * (1f - s);
        var q = v * (1f - (f * s));
        var t = v * (1f - ((1f - f) * s));
        return (((i % 6) + 6) % 6) switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }

    private async Task LoadDjProfileAvatarAsync(string profileId, string avatarBase64)
    {
        IDalamudTextureWrap? wrap = null;
        try
        {
            var bytes = Convert.FromBase64String(avatarBase64);
            wrap = await Plugin.TextureProvider.CreateFromImageAsync(bytes);
        }
        catch
        {
        }

        lock (djProfileImageGate)
        {
            djProfileAvatarCache[profileId] = wrap;
            djProfileAvatarLoading.Remove(profileId);
        }
    }

    /// The single-slot banner texture for whichever profile's detail view is currently open - not a
    /// dictionary like the avatar cache, since only one profile is ever open at once.
    private void DrawDjProfileBanner(string profileId, string? bannerBase64, float width, float height)
    {
        var bannerSize = new Vector2(width, height);
        bool hasTexture;
        IDalamudTextureWrap? texture;
        var shouldStartLoad = false;

        lock (djProfileImageGate)
        {
            hasTexture = djProfileBannerTextureForId == profileId;
            texture = djProfileBannerTexture;
            if (!string.IsNullOrEmpty(bannerBase64) && djProfileBannerTextureForId != profileId && !djProfileBannerLoading)
            {
                djProfileBannerLoading = true;
                shouldStartLoad = true;
            }
        }

        if (shouldStartLoad)
            _ = LoadDjProfileBannerAsync(profileId, bannerBase64!);

        if (hasTexture && texture != null)
        {
            ImGui.Image(texture.Handle, bannerSize);
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        drawList.AddRectFilled(origin, origin + bannerSize, ImGui.GetColorU32(Theme.Background), 4f);
        ImGui.Dummy(bannerSize);
    }

    private async Task LoadDjProfileBannerAsync(string profileId, string bannerBase64)
    {
        IDalamudTextureWrap? wrap = null;
        try
        {
            var bytes = Convert.FromBase64String(bannerBase64);
            wrap = await Plugin.TextureProvider.CreateFromImageAsync(bytes);
        }
        catch
        {
        }

        lock (djProfileImageGate)
        {
            djProfileBannerTexture?.Dispose();
            djProfileBannerTexture = wrap;
            djProfileBannerTextureForId = profileId;
            djProfileBannerLoading = false;
        }
    }

    /// 7 day-boxes (Sun..Sat) - read-only (profile view: filled/dim boxes, hover for a day's optional time
    /// note) or editable (edit form: click to toggle, plus a text input below for whichever days are
    /// currently on, since a note wouldn't fit legibly inside a 32px box).
    private void DrawAvailabilityStrip(List<DjAvailabilityDayDto> availability, bool readOnly)
    {
        var boxSize = 32f * Scale;
        var gap = 6f * Scale;
        var drawList = ImGui.GetWindowDrawList();

        for (var i = 0; i < 7 && i < availability.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, gap);

            var day = availability[i];
            if (readOnly)
            {
                var pos = ImGui.GetCursorScreenPos();
                var boxVec = new Vector2(boxSize, boxSize);
                var color = day.IsAvailable ? Theme.FixedCyan : new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.2f);
                drawList.AddRectFilled(pos, pos + boxVec, ImGui.GetColorU32(color), 4f);
                var labelSize = ImGui.CalcTextSize(DjAvailabilityDayLabels[i]);
                drawList.AddText(pos + ((boxVec - labelSize) / 2f), ImGui.GetColorU32(Theme.Background), DjAvailabilityDayLabels[i]);
                ImGui.Dummy(boxVec);
                if (day.IsAvailable && !string.IsNullOrWhiteSpace(day.TimeNote) && ImGui.IsItemHovered())
                    ImGui.SetTooltip(day.TimeNote);
            }
            else
            {
                ImGui.PushID(i);
                var accent = day.IsAvailable ? Theme.CyanAccent : Theme.NeutralAccent;
                if (PanelButton.Draw("##dayToggle", null, null, DjAvailabilityDayLabels[i], new Vector2(boxSize, boxSize), accent))
                    day.IsAvailable = !day.IsAvailable;
                ImGui.PopID();
            }
        }

        if (!readOnly && availability.Any(d => d.IsAvailable))
        {
            ImGui.Spacing();
            for (var i = 0; i < 7 && i < availability.Count; i++)
            {
                if (!availability[i].IsAvailable)
                    continue;

                ImGui.PushID(i);
                ImGui.SetNextItemWidth(200f * Scale);
                var note = availability[i].TimeNote ?? string.Empty;
                if (ImGui.InputTextWithHint("##dayNote", $"{DjAvailabilityDayLabels[i]} time (optional, e.g. 8-10pm EST)", ref note, 40))
                    availability[i].TimeNote = note;
                ImGui.PopID();
            }
        }
    }

    /// The read-only view of one DJ's profile, whether it's your own or someone else's - a short, wide banner
    /// with a big avatar overlapping its bottom-left edge (Twitter/Facebook- style, via the same
    /// absolute-position-then-restore overlay trick as the Report flag badge on a show card), then a
    /// two-column layout below it: the avatar's own column continues down into a compact weekly-availability
    /// list, while a right column carries the (bigger, optionally animated) name and action button(s) sharing
    /// one row, a sleek fading divider, the bio, and genres/venues as pill chips.
    private void DrawDjProfileBody()
    {
        var detail = plugin.AudioHostClient.LatestDjProfileDetail;
        if (detail == null)
        {
            ImGui.TextDisabled("Loading profile...");
            return;
        }

        if (!string.IsNullOrEmpty(detail.Error) || detail.Profile == null)
        {
            ImGui.TextColored(Theme.OrangeAccent, detail.Error ?? "That profile isn't available.");
            return;
        }

        var profile = detail.Profile;
        var contentWidth = ImGui.GetContentRegionAvail().X;
        var bannerHeight = contentWidth * (ShowImageProcessor.DjBannerHeight / (float)ShowImageProcessor.DjBannerWidth);
        var detailFrameColor = new Vector4(profile.FrameColorR, profile.FrameColorG, profile.FrameColorB, 1f);

        var baseX = ImGui.GetCursorPosX();
        var beforeBannerPos = ImGui.GetCursorPos();
        var bannerScreenPos = ImGui.GetCursorScreenPos();
        DrawDjProfileBanner(profile.Id, profile.BannerBase64, contentWidth, bannerHeight);

        var scrimTop = bannerScreenPos + new Vector2(0f, bannerHeight * 0.55f);
        var scrimBottom = bannerScreenPos + new Vector2(contentWidth, bannerHeight);
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(scrimTop, scrimBottom,
            0x00000000, 0x00000000, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)));

        var avatarSize = 170f * Scale;
        var leftPad = 16f * Scale;
        var leftColumnX = beforeBannerPos.X + leftPad;
        var avatarTopY = beforeBannerPos.Y + bannerHeight - (avatarSize / 2f);
        var avatarBottomY = avatarTopY + avatarSize;
        ImGui.SetCursorPos(new Vector2(leftColumnX, avatarTopY));
        var avatarScreenPos = ImGui.GetCursorScreenPos();
        DrawAvatarGlow(avatarScreenPos, avatarSize, detailFrameColor, profile.IsLiveNow);
        DrawDjProfileAvatar(profile.Id, profile.AvatarBase64, avatarSize, detailFrameColor, profile.FrameStyle);

        var columnGap = 20f * Scale;
        var rightColumnX = leftColumnX + avatarSize + columnGap;
        var rightColumnWidth = MathF.Max(120f * Scale, (beforeBannerPos.X + contentWidth) - rightColumnX);
        const float nameFontScale = 1.6f;
        float nameLineHeight;
        using (plugin.Fonts.Header.PushSafe())
            nameLineHeight = ImGui.GetFontSize() * nameFontScale;
        var nameRowY = avatarBottomY - nameLineHeight - (4f * Scale);

        ImGui.SetCursorPos(new Vector2(rightColumnX, nameRowY));
        var nameColor = new Vector4(profile.NameColorR, profile.NameColorG, profile.NameColorB, 1f);
        DrawDjName(profile.DjName, nameColor, profile.NameEffect, nameFontScale);

        var actionWidth = (profile.IsOwnProfile ? 228f : 110f) * Scale;
        ImGui.SameLine(baseX + contentWidth - actionWidth);
        ImGui.SetCursorPosY(nameRowY);
        DrawDjProfileActionButtons(profile);

        var rightColumnIndent = rightColumnX - baseX;
        ImGui.SetCursorPos(new Vector2(rightColumnX, nameRowY + nameLineHeight + (4f * Scale)));
        ImGui.Indent(rightColumnIndent);

        var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;
        DrawDjProfileLikeAndFollowToggles(profile.Id, profile.IsLikedByRequester, profile.IsFollowedByRequester, characterName);
        ImGui.TextDisabled($"{profile.LikeCount} like{(profile.LikeCount == 1 ? "" : "s")} · {profile.FollowerCount} follower{(profile.FollowerCount == 1 ? "" : "s")}");
        ImGui.Spacing();

        if (profile.IsLiveNow)
        {
            DrawLivePill();
            ImGui.SameLine(0, 8f * Scale);
            if (PanelButton.Draw("##joinFromDjProfile", plugin.Fonts.Icon, FontAwesomeIcon.SignInAlt, "Join", new Vector2(90, 22) * Scale, Theme.NeutralAccent))
                ConnectToRoom(profile.LiveRoomCode ?? string.Empty, string.Empty);
            ImGui.Spacing();
        }

        DrawSleekDivider(rightColumnWidth, detailFrameColor);

        if (!string.IsNullOrWhiteSpace(profile.Bio))
        {
            ImGui.Spacing();
            DrawDjProfileBioPanel(profile.Bio, rightColumnWidth, detailFrameColor);
        }

        ImGui.Spacing();
        ImGui.Spacing();
        DrawDjProfileStatsRow(profile, rightColumnWidth);

        ImGui.Unindent(rightColumnIndent);
        var rightColumnEndY = ImGui.GetCursorPosY();

        var availabilityWidth = MathF.Min(avatarSize + (30f * Scale), (rightColumnX - beforeBannerPos.X) - (8f * Scale));
        var availabilityX = leftColumnX + ((avatarSize - availabilityWidth) / 2f);
        var availabilityIndent = availabilityX - baseX;
        ImGui.SetCursorPos(new Vector2(availabilityX, avatarBottomY + (12f * Scale)));
        ImGui.Indent(availabilityIndent);
        DrawAvailabilityMiniList(profile.Availability, availabilityWidth, detailFrameColor);

        if (profile.ShowLinkedCharacters && profile.LinkedCharacterNames.Count > 0)
        {
            ImGui.Spacing();
            DrawAlsoKnownAsBox(profile.LinkedCharacterNames, availabilityWidth, detailFrameColor);
        }

        ImGui.Unindent(availabilityIndent);
        var leftColumnEndY = ImGui.GetCursorPosY();

        ImGui.SetCursorPos(new Vector2(beforeBannerPos.X, MathF.Max(rightColumnEndY, leftColumnEndY) + (8f * Scale)));

        if (djProfileDeleteResult != null && !djProfileDeleteResult.Success)
        {
            ImGui.TextColored(Theme.OrangeAccent, $"Couldn't delete: {djProfileDeleteResult.Error ?? "unknown error"}");
        }

        DrawDjProfileReportPopup(profile.Id);
    }

    /// A soft round halo behind the hero avatar - bigger and more atmospheric than the avatar's own drop
    /// shadow (see DrawDjProfileAvatar), giving the page's one big focal image some actual presence instead
    /// of sitting flat against the banner.
    private void DrawAvatarGlow(Vector2 avatarScreenPos, float avatarSize, Vector4 color, bool isLive)
    {
        var drawList = ImGui.GetWindowDrawList();
        var center = avatarScreenPos + new Vector2(avatarSize / 2f);
        var baseRadius = avatarSize / 2f;
        var pulse = isLive ? 0.75f + (0.25f * MathF.Sin((float)ImGui.GetTime() * 2.4f)) : 1f;
        var layers = isLive ? 5 : 3;
        var maxOutset = (isLive ? 26f : 14f) * Scale;
        var baseAlpha = isLive ? 0.1f : 0.06f;

        for (var i = layers; i >= 1; i--)
        {
            var t = i / (float)layers;
            var radius = baseRadius + (maxOutset * t);
            var alpha = baseAlpha * (1f - t + 0.15f) * pulse;
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha)));
        }
    }

    /// A small colored caps label with a short accent underline instead of DrawDjProfileBody's old icon+text
    /// section headers ("GENRES", "VENUES", etc.) - an icon next to plain text read as sloppy/didn't carry
    /// enough visual weight on its own; a tight colored underline gives the label a definite anchor without
    /// needing an icon at all.
    private void DrawDjProfileColumnLabel(string text, Vector4 accent, float? centerWidth = null)
    {
        var textSize = ImGui.CalcTextSize(text);
        if (centerWidth is { } width)
            CenterNextItem(textSize.X, width);

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        drawList.AddText(pos, ImGui.GetColorU32(accent), text);

        var underlineGap = 3f * Scale;
        var underlineWidth = MathF.Min(textSize.X, 26f * Scale);
        var underlineY = pos.Y + textSize.Y + underlineGap;
        drawList.AddLine(new Vector2(pos.X, underlineY), new Vector2(pos.X + underlineWidth, underlineY),
            ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.8f)), 2f * Scale);

        ImGui.Dummy(new Vector2(textSize.X, textSize.Y + underlineGap + (2f * Scale)));
    }

    /// An isolated "About" block for the bio - a faint tinted, bordered panel behind the wrapped text instead
    /// of it just sitting bare on the page background, so it reads as its own distinct section the way
    /// GENRES/VENUES/AETHERPHONE # now do as a row of columns (see DrawDjProfileStatsRow) rather than
    /// blending into everything around it.
    private void DrawDjProfileBioPanel(string bio, float width, Vector4 accent)
    {
        var drawList = ImGui.GetWindowDrawList();
        var padX = 12f * Scale;
        var padY = 10f * Scale;
        var pos = ImGui.GetCursorScreenPos();

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(pos + new Vector2(padX, padY));
        ImGui.PushTextWrapPos(pos.X + width - padX);
        ImGui.TextWrapped(bio);
        ImGui.PopTextWrapPos();
        var contentBottom = ImGui.GetCursorScreenPos().Y;

        drawList.ChannelsSetCurrent(0);
        var panelHeight = contentBottom - pos.Y + padY;
        drawList.AddRectFilled(pos, pos + new Vector2(width, panelHeight), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.07f)), 8f * Scale);
        drawList.AddRect(pos, pos + new Vector2(width, panelHeight), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.3f)), 8f * Scale, ImDrawFlags.None, 1f);

        drawList.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + panelHeight));
    }

    /// Genres/Venues/Aetherphone # in three fixed slots - left/middle/right, always in that order and always
    /// at the same position regardless of which are actually populated - with a soft fading vertical divider
    /// fixed at each of the two slot boundaries.
    private void DrawDjProfileStatsRow(DjProfileDetailDto profile, float rowWidth)
    {
        var hasGenres = profile.Genres.Count > 0;
        var hasVenues = profile.SavedVenues.Count > 0;
        var hasAetherphone = !string.IsNullOrWhiteSpace(profile.AetherphoneNumber);
        if (!hasGenres && !hasVenues && !hasAetherphone)
            return;

        var gap = 20f * Scale;
        var slotWidth = (rowWidth - (gap * 2f)) / 3f;
        var rowStartLocalPos = ImGui.GetCursorPos();
        var rowStartScreenPos = ImGui.GetCursorScreenPos();
        var maxSlotHeight = 0f;

        DrawSlot(0, hasGenres, "GENRES", Theme.FixedCyan, profile.Genres.Select(CapitalizeWords).ToList(), null, null);
        DrawSlot(1, hasVenues, "VENUES", Theme.FixedOrange, null, null, profile.SavedVenues);
        DrawSlot(2, hasAetherphone, "AETHERPHONE #", Theme.FixedCyan, null, profile.AetherphoneNumber, null);

        for (var i = 0; i < 2; i++)
        {
            var dividerX = rowStartScreenPos.X + ((i + 1) * slotWidth) + (i * gap) + (gap / 2f);
            DrawSleekVerticalDivider(new Vector2(dividerX, rowStartScreenPos.Y), maxSlotHeight, Theme.Border);
        }

        ImGui.SetCursorPos(new Vector2(rowStartLocalPos.X, rowStartLocalPos.Y + maxSlotHeight));

        void DrawSlot(int index, bool has, string label, Vector4 accent, List<string>? chips, string? copyValue, List<SavedVenueDto>? venueChips)
        {
            ImGui.SetCursorPos(new Vector2(rowStartLocalPos.X + (index * (slotWidth + gap)), rowStartLocalPos.Y));
            ImGui.BeginGroup();
            if (has)
            {
                DrawDjProfileColumnLabel(label, accent);
                ImGui.Spacing();
                if (chips != null)
                    DrawChipRow(chips, slotWidth, accent);
                else if (venueChips != null)
                    DrawVenueChipRow(venueChips, slotWidth, accent);
                else if (copyValue != null)
                    DrawClickToCopyChip(copyValue, accent);
            }
            else
            {
                ImGui.Dummy(new Vector2(slotWidth, 1f));
            }

            ImGui.EndGroup();
            maxSlotHeight = MathF.Max(maxSlotHeight, ImGui.GetItemRectSize().Y);
        }
    }

    /// Vertical counterpart to DrawSleekDivider - same fade-in/fade-out-at-the-ends idiom, just top-to-bottom
    /// instead of left-to-right, and drawn as pure overlay (no Dummy/cursor advance) since callers already
    /// know exactly where it needs to sit.
    private void DrawSleekVerticalDivider(Vector2 topScreenPos, float height, Vector4 color)
    {
        var drawList = ImGui.GetWindowDrawList();
        const int segments = 24;
        for (var i = 0; i < segments; i++)
        {
            var t0 = i / (float)segments;
            var t1 = (i + 1) / (float)segments;
            var alpha0 = 1f - MathF.Abs((t0 * 2f) - 1f);
            var alpha1 = 1f - MathF.Abs((t1 * 2f) - 1f);
            var ya = topScreenPos.Y + (t0 * height);
            var yb = topScreenPos.Y + (t1 * height);
            var segAlpha = ((alpha0 + alpha1) / 2f) * 0.85f;
            drawList.AddLine(new Vector2(topScreenPos.X, ya), new Vector2(topScreenPos.X, yb), ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, segAlpha)), 1.5f * Scale);
        }
    }

    /// Edit+Delete (own profile) or Report (someone else's) - pinned to the top-right of the name row by the
    /// caller, "Edit profile"-button-in-the-corner style.
    private void DrawDjProfileActionButtons(DjProfileDetailDto profile)
    {
        if (profile.IsOwnProfile)
        {
            if (PanelButton.Draw("##editDjProfile", plugin.Fonts.Icon, FontAwesomeIcon.Edit, "Edit", new Vector2(110, 28) * Scale, Theme.NeutralAccent))
            {
                SeedDjEditBuffersFrom(profile);
                editingDjProfileId = profile.Id;
                viewBeforeDjProfileEdit = ViewMode.DjProfile;
                pendingView = ViewMode.DjProfileEdit;
            }

            ImGui.SameLine();
            if (djProfileDeleteSending)
            {
                ImGui.TextDisabled("Deleting...");
            }
            else if (PanelButton.Draw("##deleteDjProfile", plugin.Fonts.Icon, FontAwesomeIcon.Trash, "Delete", new Vector2(110, 28) * Scale, Theme.OrangeAccent))
            {
                djProfileDeleteSending = true;
                djProfileDeleteResult = null;
                plugin.AudioHostClient.Send(MessageType.DeleteDjProfile, new DeleteDjProfileMessage
                {
                    Id = profile.Id,
                    CharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
                });
            }
        }
        else if (PanelButton.Draw("##reportDjProfile", plugin.Fonts.Icon, FontAwesomeIcon.Flag, "Report", new Vector2(110, 28) * Scale, Theme.OrangeAccent))
        {
            djProfileReportReasonBuffer = string.Empty;
            djProfileReportSendResult = null;
            ImGui.OpenPopup("##reportDjProfilePopup");
        }
    }

    private void DrawDjProfileReportPopup(string profileId)
    {
        if (!ImGui.BeginPopup("##reportDjProfilePopup"))
            return;

        ImGui.SetWindowFontScale(Scale);
        ImGui.TextColored(Theme.OrangeAccent, "Report This Profile");
        ImGui.TextDisabled("Sends this profile and your character name to the developer -");
        ImGui.TextDisabled("only use this for actual abuse.");
        ImGui.Spacing();

        ImGui.TextDisabled("What's wrong? (required)");
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Background);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.16f, 0.16f, 0.2f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.6f));
        ImGui.SetNextItemWidth(280f * Scale);
        WrappedInput.Multiline("##reportDjProfileReason", ref djProfileReportReasonBuffer, 500, new Vector2(280f, 60f) * Scale);
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        ImGui.Spacing();

        if (djProfileReportSending)
        {
            ImGui.TextDisabled("Sending...");
        }
        else if (PanelButton.Draw("##reportDjProfileSend", null, null, "Send Report", new Vector2(160, 28) * Scale, Theme.OrangeAccent))
        {
            if (string.IsNullOrWhiteSpace(djProfileReportReasonBuffer))
            {
                djProfileReportSendResult = new DjProfileReportAckMessage { Accepted = false, Error = "Please describe what's wrong first." };
            }
            else
            {
                djProfileReportSending = true;
                djProfileReportSendResult = null;
                plugin.AudioHostClient.Send(MessageType.ReportDjProfile, new SubmitDjProfileReportMessage
                {
                    ProfileId = profileId,
                    Reason = WrappedInput.Unfold(djProfileReportReasonBuffer, WrappedInput.WidthFor(new Vector2(280f, 60f) * Scale)).Trim(),
                    ReporterCharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
                });
            }
        }

        if (djProfileReportSendResult != null)
        {
            ImGui.Spacing();
            if (djProfileReportSendResult.Accepted)
                ImGui.TextColored(Theme.CyanAccent, "Reported - thank you.");
            else
                ImGui.TextColored(Theme.OrangeAccent, $"Couldn't send: {djProfileReportSendResult.Error ?? "unknown error"}");
        }

        ImGui.EndPopup();
    }

    /// The single rounded-pill visual both DrawChipRow and DrawCappedChipRow place at the current cursor -
    /// just the rect+text+Dummy triplet, with no wrapping logic of its own (the two callers wrap differently:
    /// DrawChipRow never truncates, DrawCappedChipRow hard-caps at a fixed number of lines).
    private void DrawSingleChip(string text, Vector4 accent, float padX, float padY)
    {
        var lineHeight = ImGui.GetTextLineHeight() + (padY * 2f);
        var chipSize = new Vector2(ImGui.CalcTextSize(text).X + (padX * 2f), lineHeight);
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        drawList.AddRectFilled(pos, pos + chipSize, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.18f)), lineHeight / 2f);
        drawList.AddText(pos + new Vector2(padX, padY), ImGui.GetColorU32(accent), text);
        ImGui.Dummy(chipSize);
    }

    /// Small rounded-pill tags (genres/venues) that wrap onto a new line once they'd overflow maxWidth - the
    /// "chip row" every social profile uses for tag-like data, instead of a plain comma-separated sentence.
    private void DrawChipRow(IReadOnlyList<string> items, float maxWidth, Vector4 accent)
    {
        if (items.Count == 0)
            return;

        var padX = 8f * Scale;
        var padY = 3f * Scale;
        var gap = 6f * Scale;

        var usedWidth = 0f;
        var isFirstOnLine = true;

        foreach (var rawItem in items)
        {
            var item = TruncateToWidth(rawItem, maxWidth - (padX * 2f));
            var chipWidth = ImGui.CalcTextSize(item).X + (padX * 2f);

            if (!isFirstOnLine && usedWidth + gap + chipWidth > maxWidth)
            {
                isFirstOnLine = true;
                usedWidth = 0f;
            }

            if (!isFirstOnLine)
                ImGui.SameLine(0, gap);

            DrawSingleChip(item, accent, padX, padY);

            usedWidth = (isFirstOnLine ? 0f : usedWidth + gap) + chipWidth;
            isFirstOnLine = false;
        }
    }

    /// "Data Center - World, Housing Area - Ward W, Plot P", skipping any blank part gracefully - the
    /// add-venue form always requires all five, but this stays tolerant in case a profile ever ends up with
    /// an incomplete one (a bug, or hand-edited data).
    private static string FormatVenueAddress(SavedVenueDto v)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(v.DataCenter))
            parts.Add(v.DataCenter);

        var worldLine = string.Join(", ", new[] { v.World, v.HousingArea }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(worldLine))
            parts.Add(worldLine);

        var plotLine = string.Join(", ", new[]
        {
            !string.IsNullOrWhiteSpace(v.Ward) ? $"Ward {v.Ward}{(v.IsApartment && v.Subdivision ? " (Subdivision)" : "")}" : null,
            !string.IsNullOrWhiteSpace(v.Plot) ? (v.IsApartment ? $"Apartment {v.Plot}" : $"Plot {v.Plot}") : null,
        }.Where(s => s != null));
        if (!string.IsNullOrWhiteSpace(plotLine))
            parts.Add(plotLine);

        return parts.Count > 0 ? string.Join(" - ", parts) : "(no address on file)";
    }

    /// Read-only venue chip showing the venue's Name, with the full address as a hover tooltip - same visual
    /// shape as DrawSingleChip, but hit-testable (DrawSingleChip's plain ImGui.Dummy has no hover support at
    /// all), copying DrawTagChipEditor's own already-working InvisibleButton/IsItemHovered/SetTooltip pattern
    /// instead.
    private void DrawVenueChip(SavedVenueDto venue, Vector4 accent, float padX, float padY)
    {
        var lineHeight = ImGui.GetTextLineHeight() + (padY * 2f);
        var chipSize = new Vector2(ImGui.CalcTextSize(venue.Name).X + (padX * 2f), lineHeight);
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        drawList.AddRectFilled(pos, pos + chipSize, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.18f)), lineHeight / 2f);
        drawList.AddText(pos + new Vector2(padX, padY), ImGui.GetColorU32(accent), venue.Name);
        ImGui.InvisibleButton("##venueChip", chipSize);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(FormatVenueAddress(venue));
    }

    /// Venue counterpart to DrawChipRow - same wrap-onto-a-new-line layout, but each chip is a SavedVenueDto
    /// (Name shown, full address on hover) rather than a plain string.
    private void DrawVenueChipRow(IReadOnlyList<SavedVenueDto> venues, float maxWidth, Vector4 accent)
    {
        if (venues.Count == 0)
            return;

        var padX = 8f * Scale;
        var padY = 3f * Scale;
        var gap = 6f * Scale;

        var usedWidth = 0f;
        var isFirstOnLine = true;

        for (var i = 0; i < venues.Count; i++)
        {
            var venue = venues[i];
            var truncatedName = TruncateToWidth(venue.Name, maxWidth - (padX * 2f));
            var displayVenue = truncatedName == venue.Name ? venue : new SavedVenueDto
            {
                Id = venue.Id, Name = truncatedName, DataCenter = venue.DataCenter, World = venue.World,
                HousingArea = venue.HousingArea, Ward = venue.Ward, Plot = venue.Plot,
                IsApartment = venue.IsApartment, Subdivision = venue.Subdivision,
            };
            var chipWidth = ImGui.CalcTextSize(truncatedName).X + (padX * 2f);

            if (!isFirstOnLine && usedWidth + gap + chipWidth > maxWidth)
            {
                isFirstOnLine = true;
                usedWidth = 0f;
            }

            if (!isFirstOnLine)
                ImGui.SameLine(0, gap);

            ImGui.PushID(i);
            DrawVenueChip(displayVenue, accent, padX, padY);
            ImGui.PopID();

            usedWidth = (isFirstOnLine ? 0f : usedWidth + gap) + chipWidth;
            isFirstOnLine = false;
        }
    }

    private const int GenreChipMaxLines = 2;

    /// The fixed height DrawCappedChipRow's genre area always occupies on a DJ List grid card, regardless of
    /// whether a given DJ listed zero, one, or a dozen genres - reserved unconditionally by the caller so
    /// every card in the grid keeps the same internal layout (Like/ Follow/View buttons land at the same Y on
    /// every card) instead of shorter genre lists leaving a gap or longer ones pushing everything below them
    /// down by a different amount per card.
    private float MeasureCappedChipRowHeight()
    {
        var lineHeight = ImGui.GetTextLineHeight() + (2f * 3f * Scale);
        var lineGap = ImGui.GetStyle().ItemSpacing.Y;
        return (lineHeight * GenreChipMaxLines) + (lineGap * (GenreChipMaxLines - 1));
    }

    /// Same chip visual as DrawChipRow, but hard-capped to GenreChipMaxLines lines - once a DJ's genre list
    /// would overflow that, the rest collapse into a trailing "+N" chip instead of growing the card taller
    /// (see MeasureCappedChipRowHeight's own doc comment for why every card needs the same height).
    private void DrawCappedChipRow(IReadOnlyList<string> items, float maxWidth, Vector4 accent)
    {
        if (items.Count == 0)
            return;

        var padX = 8f * Scale;
        var padY = 3f * Scale;
        var gap = 6f * Scale;

        var chipWidths = new float[items.Count];
        var usedWidthAfter = new float[items.Count];
        var line = 0;
        var usedWidth = 0f;
        var shownCount = items.Count;
        for (var i = 0; i < items.Count; i++)
        {
            chipWidths[i] = ImGui.CalcTextSize(items[i]).X + (padX * 2f);
            var widthWithGap = usedWidth == 0f ? chipWidths[i] : usedWidth + gap + chipWidths[i];

            if (widthWithGap > maxWidth)
            {
                line++;
                if (line >= GenreChipMaxLines)
                {
                    shownCount = i;
                    break;
                }

                usedWidth = chipWidths[i];
            }
            else
            {
                usedWidth = widthWithGap;
            }

            usedWidthAfter[i] = usedWidth;
        }

        string? moreLabel = null;
        if (shownCount < items.Count)
        {
            while (shownCount > 0)
            {
                moreLabel = $"+{items.Count - shownCount}";
                var moreWidth = ImGui.CalcTextSize(moreLabel).X + (padX * 2f);
                if (moreWidth + gap <= maxWidth - usedWidthAfter[shownCount - 1])
                    break;

                shownCount--;
            }

            if (shownCount == 0)
                moreLabel = $"+{items.Count}";
        }

        var lineUsedWidth = 0f;
        var isFirstOnLine = true;
        for (var i = 0; i < shownCount; i++)
        {
            if (!isFirstOnLine && lineUsedWidth + gap + chipWidths[i] > maxWidth)
            {
                isFirstOnLine = true;
                lineUsedWidth = 0f;
            }

            if (!isFirstOnLine)
                ImGui.SameLine(0, gap);

            DrawSingleChip(items[i], accent, padX, padY);

            lineUsedWidth = (isFirstOnLine ? 0f : lineUsedWidth + gap) + chipWidths[i];
            isFirstOnLine = false;
        }

        if (moreLabel != null)
        {
            if (shownCount > 0)
                ImGui.SameLine(0, gap);
            DrawSingleChip(moreLabel, accent, padX, padY);
        }
    }

    /// A thin horizontal rule that fades in from the left, peaks at the center, and fades back out toward the
    /// right - reads as a "sleek" accent divider rather than a flat line the same way ImGui.Separator's plain
    /// solid rule would.
    private void DrawSleekDivider(float width, Vector4 color)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        const int segments = 24;
        for (var i = 0; i < segments; i++)
        {
            var t0 = i / (float)segments;
            var t1 = (i + 1) / (float)segments;
            var alpha0 = 1f - MathF.Abs((t0 * 2f) - 1f);
            var alpha1 = 1f - MathF.Abs((t1 * 2f) - 1f);
            var xa = pos.X + (t0 * width);
            var xb = pos.X + (t1 * width);
            var segAlpha = ((alpha0 + alpha1) / 2f) * 0.85f;
            drawList.AddLine(new Vector2(xa, pos.Y), new Vector2(xb, pos.Y), ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, segAlpha)), 1.5f * Scale);
        }

        ImGui.Dummy(new Vector2(width, 2f * Scale));
    }

    /// A compact vertical weekly-availability list, sized to fit underneath the avatar itself rather than
    /// spanning the full card width like DrawAvailabilityStrip's horizontal 7-box row does - a filled/dim dot
    /// plus the day name per row, left-aligned, with its optional time note trailing in dim text when there's
    /// room, using a FIXED label column width (the widest day name) so a note always starts at the same X
    /// regardless of which day it's attached to.
    private void DrawAvailabilityMiniList(List<DjAvailabilityDayDto> availability, float width, Vector4 accent)
    {
        var drawList = ImGui.GetWindowDrawList();
        var padX = 10f * Scale;
        var padY = 8f * Scale;
        var panelPos = ImGui.GetCursorScreenPos();
        var innerWidth = width - (padX * 2f);

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(panelPos + new Vector2(0f, padY));
        ImGui.Indent(padX);

        DrawDjProfileColumnLabel("AVAILABILITY", accent);
        ImGui.Spacing();

        var dotSize = 9f * Scale;
        var dotGap = 6f * Scale;
        var rowHeight = ImGui.GetTextLineHeight() + (5f * Scale);
        var labelIndent = dotSize + dotGap;

        var maxLabelWidth = 0f;
        foreach (var label in DjAvailabilityDayLabels)
            maxLabelWidth = MathF.Max(maxLabelWidth, ImGui.CalcTextSize(label).X);

        var noteIndent = labelIndent + maxLabelWidth + dotGap;
        var sameLineNoteWidth = innerWidth - noteIndent;
        var noteColor = new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.45f);

        for (var i = 0; i < 7 && i < availability.Count; i++)
        {
            var day = availability[i];
            var rowStart = ImGui.GetCursorScreenPos();

            var dotColor = day.IsAvailable ? Theme.FixedCyan : new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.2f);
            drawList.AddCircleFilled(rowStart + new Vector2(dotSize / 2f, rowHeight / 2f), dotSize / 2f, ImGui.GetColorU32(dotColor));

            var label = DjAvailabilityDayLabels[i];
            var labelPos = rowStart + new Vector2(labelIndent, (rowHeight - ImGui.GetTextLineHeight()) / 2f);
            var labelColor = day.IsAvailable ? Theme.Text : new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.4f);
            drawList.AddText(labelPos, ImGui.GetColorU32(labelColor), label);

            var hasNote = day.IsAvailable && !string.IsNullOrWhiteSpace(day.TimeNote);
            if (hasNote && ImGui.CalcTextSize(day.TimeNote).X <= sameLineNoteWidth)
            {
                drawList.AddText(rowStart + new Vector2(noteIndent, (rowHeight - ImGui.GetTextLineHeight()) / 2f), ImGui.GetColorU32(noteColor), day.TimeNote!);
                ImGui.Dummy(new Vector2(innerWidth, rowHeight));
            }
            else if (hasNote)
            {
                ImGui.Dummy(new Vector2(innerWidth, rowHeight));
                ImGui.Indent(labelIndent);
                ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + (innerWidth - labelIndent));
                ImGui.PushStyleColor(ImGuiCol.Text, noteColor);
                ImGui.TextWrapped(day.TimeNote);
                ImGui.PopStyleColor();
                ImGui.PopTextWrapPos();
                ImGui.Unindent(labelIndent);
            }
            else
            {
                ImGui.Dummy(new Vector2(innerWidth, rowHeight));
            }
        }

        ImGui.Unindent(padX);
        var contentBottom = ImGui.GetCursorScreenPos().Y;

        drawList.ChannelsSetCurrent(0);
        var panelHeight = contentBottom - panelPos.Y + padY;
        drawList.AddRectFilled(panelPos, panelPos + new Vector2(width, panelHeight), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.07f)), 8f * Scale);
        drawList.AddRect(panelPos, panelPos + new Vector2(width, panelHeight), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.3f)), 8f * Scale, ImDrawFlags.None, 1f);

        drawList.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(panelPos.X, panelPos.Y + panelHeight));
    }

    /// Same boxed-panel treatment as DrawAvailabilityMiniList, stacked directly beneath it in the left column
    /// - a linked-character list read the same way availability does (its own titled box) rather than folded
    /// into the right column's Genres/Venues/Aetherphone row, which was never built with room for a fourth,
    /// variable-length column.
    private void DrawAlsoKnownAsBox(List<string> linkedCharacterNames, float width, Vector4 accent)
    {
        var drawList = ImGui.GetWindowDrawList();
        var padX = 10f * Scale;
        var padY = 8f * Scale;
        var panelPos = ImGui.GetCursorScreenPos();
        var innerWidth = width - (padX * 2f);

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(panelPos + new Vector2(0f, padY));
        ImGui.Indent(padX);

        DrawDjProfileColumnLabel("ALSO KNOWN AS", accent);
        ImGui.Spacing();

        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + innerWidth);
        foreach (var name in linkedCharacterNames)
            ImGui.TextUnformatted(name);
        ImGui.PopTextWrapPos();

        ImGui.Unindent(padX);
        var contentBottom = ImGui.GetCursorScreenPos().Y;

        drawList.ChannelsSetCurrent(0);
        var panelHeight = contentBottom - panelPos.Y + padY;
        drawList.AddRectFilled(panelPos, panelPos + new Vector2(width, panelHeight), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.07f)), 8f * Scale);
        drawList.AddRect(panelPos, panelPos + new Vector2(width, panelHeight), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.3f)), 8f * Scale, ImDrawFlags.None, 1f);

        drawList.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(panelPos.X, panelPos.Y + panelHeight));
    }

    private void ResetDjEditBuffersForNewProfile()
    {
        editingDjProfileId = null;
        djEditDjNameBuffer = string.Empty;
        djEditBioBuffer = string.Empty;
        djEditSavedVenues.Clear();
        ClearVenueEntryForm();
        djEditGenres.Clear();
        foreach (var day in djEditAvailability)
        {
            day.IsAvailable = false;
            day.TimeNote = null;
        }

        djEditFrameColor = new Vector3(0.25f, 0.85f, 0.95f);
        djEditFrameStyle = "Solid";
        djEditNameEffect = "None";
        djEditNameColor = new Vector3(0.25f, 0.85f, 0.95f);
        djEditAetherphoneNumber = string.Empty;
        djEditLinkedCharacterNames.Clear();
        djEditShowLinkedCharacters = false;
        djLinkCodeGenerated = null;
        djLinkCodeError = null;

        djEditAvatarPreview?.Dispose();
        djEditAvatarPreview = null;
        djEditAvatarUploadPath = null;
        djEditBannerPreview?.Dispose();
        djEditBannerPreview = null;
        djEditBannerUploadPath = null;
        djEditImageError = null;
        djProfileSaveResult = null;
    }

    private void SeedDjEditBuffersFrom(DjProfileDetailDto detail)
    {
        djEditDjNameBuffer = detail.DjName;
        djEditBioBuffer = detail.Bio ?? string.Empty;
        djEditBioWrapPending = true;
        djEditSavedVenues.Clear();
        djEditSavedVenues.AddRange(detail.SavedVenues.Select(v => new SavedVenueDto
        {
            Id = v.Id, Name = v.Name, DataCenter = v.DataCenter, World = v.World, HousingArea = v.HousingArea, Ward = v.Ward, Plot = v.Plot,
            IsApartment = v.IsApartment, Subdivision = v.Subdivision,
        }));
        ClearVenueEntryForm();
        djEditGenres.Clear();
        djEditGenres.AddRange(detail.Genres.Select(CapitalizeWords));
        for (var i = 0; i < 7 && i < detail.Availability.Count; i++)
        {
            djEditAvailability[i].IsAvailable = detail.Availability[i].IsAvailable;
            djEditAvailability[i].TimeNote = detail.Availability[i].TimeNote;
        }

        djEditFrameColor = new Vector3(detail.FrameColorR, detail.FrameColorG, detail.FrameColorB);
        djEditFrameStyle = string.IsNullOrEmpty(detail.FrameStyle) ? "Solid" : detail.FrameStyle;
        djEditNameEffect = string.IsNullOrEmpty(detail.NameEffect) ? "None" : detail.NameEffect;
        djEditNameColor = new Vector3(detail.NameColorR, detail.NameColorG, detail.NameColorB);
        djEditAetherphoneNumber = detail.AetherphoneNumber ?? string.Empty;
        djEditLinkedCharacterNames.Clear();
        djEditLinkedCharacterNames.AddRange(detail.LinkedCharacterNames);
        djEditShowLinkedCharacters = detail.ShowLinkedCharacters;
        djLinkCodeGenerated = null;
        djLinkCodeError = null;

        djEditAvatarPreview?.Dispose();
        djEditAvatarPreview = null;
        djEditAvatarUploadPath = null;
        djEditBannerPreview?.Dispose();
        djEditBannerPreview = null;
        djEditBannerUploadPath = null;
        djEditImageError = null;
        djProfileSaveResult = null;
    }

    /// Fixes up the first letter of every word, not just the very first character of the whole string -
    /// "heavy metal" becomes "Heavy Metal", "drum and bass" becomes "Drum And Bass".
    private static string CapitalizeWords(string value)
    {
        if (value.Length == 0)
            return value;

        var chars = value.ToCharArray();
        var atWordStart = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (atWordStart && char.IsLower(chars[i]))
                chars[i] = char.ToUpperInvariant(chars[i]);
            atWordStart = char.IsWhiteSpace(chars[i]) || chars[i] == '-';
        }

        return new string(chars);
    }

    /// Wrapping pill-chip tag editor - same chip visual language DrawChipRow/ DrawSingleChip already use for
    /// genres/venues everywhere else in the app (grid cards, the profile page), instead of a plain vertical
    /// list of "text + a separate x button" rows.
    private void DrawTagChipEditor(string idPrefix, List<string> items, ref string entryBuffer, string hint, float width, Vector4 accent, bool capitalizeWords = false)
    {
        ImGui.PushID(idPrefix);

        var buttonSize = new Vector2(26, 26) * Scale;
        ImGui.SetNextItemWidth(width - buttonSize.X - (8f * Scale));
        ImGui.InputTextWithHint("##entry", hint, ref entryBuffer, 40);
        ImGui.SameLine(width - buttonSize.X);
        if (PanelButton.Draw("##add", plugin.Fonts.Icon, FontAwesomeIcon.Plus, null, buttonSize, Theme.NeutralAccent)
            && !string.IsNullOrWhiteSpace(entryBuffer))
        {
            var value = entryBuffer.Trim();
            if (capitalizeWords)
                value = CapitalizeWords(value);
            if (!items.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)))
                items.Add(value);
            entryBuffer = string.Empty;
        }

        if (items.Count > 0)
        {
            ImGui.Spacing();
            var padX = 8f * Scale;
            var padY = 4f * Scale;
            var gap = 6f * Scale;
            var lineHeight = ImGui.GetTextLineHeight() + (padY * 2f);
            var usedWidth = 0f;
            var isFirstOnLine = true;
            int? removeIndex = null;
            var drawList = ImGui.GetWindowDrawList();

            for (var i = items.Count - 1; i >= 0; i--)
            {
                var label = items[i] + "  x";
                var chipWidth = ImGui.CalcTextSize(label).X + (padX * 2f);

                if (!isFirstOnLine && usedWidth + gap + chipWidth > width)
                {
                    isFirstOnLine = true;
                    usedWidth = 0f;
                }

                if (!isFirstOnLine)
                    ImGui.SameLine(0, gap);

                ImGui.PushID(i);
                var chipSize = new Vector2(chipWidth, lineHeight);
                var pos = ImGui.GetCursorScreenPos();
                var hovered = ImGui.IsMouseHoveringRect(pos, pos + chipSize);
                drawList.AddRectFilled(pos, pos + chipSize, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, hovered ? 0.30f : 0.18f)), lineHeight / 2f);
                drawList.AddText(pos + new Vector2(padX, padY), ImGui.GetColorU32(accent), label);
                if (ImGui.InvisibleButton("##chip", chipSize))
                    removeIndex = i;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Click to remove");
                ImGui.PopID();

                usedWidth = (isFirstOnLine ? 0f : usedWidth + gap) + chipWidth;
                isFirstOnLine = false;
            }

            if (removeIndex.HasValue)
                items.RemoveAt(removeIndex.Value);
        }

        ImGui.PopID();
    }

    private void ClearVenueEntryForm()
    {
        djEditVenueNameBuffer = string.Empty;
        djEditVenueDataCenterBuffer = string.Empty;
        djEditVenueWorldBuffer = string.Empty;
        djEditVenueHousingAreaBuffer = string.Empty;
        djEditVenueWardBuffer = string.Empty;
        djEditVenuePlotBuffer = string.Empty;
        djEditVenueIsApartment = false;
        djEditVenueSubdivision = false;
    }

    /// Replaces the old free-text "Common Venues" tag editor - a DJ's saved venues are now a real Name plus a
    /// fully dropdown-constrained address (same DrawVenueDropdown/ DataCenterOptions/etc.
    private void DrawSavedVenueEditor(float width)
    {
        int? removeIndex = null;
        for (var i = 0; i < djEditSavedVenues.Count; i++)
        {
            ImGui.PushID(i);
            var venue = djEditSavedVenues[i];
            ImGui.TextColored(Theme.FixedOrange, venue.Name);
            ImGui.SameLine(width - (26f * Scale));
            if (PanelButton.Draw("##deleteVenue", plugin.Fonts.Icon, FontAwesomeIcon.Trash, null, new Vector2(26, 26) * Scale, Theme.OrangeAccent))
                removeIndex = i;
            ImGui.PopID();
        }

        if (removeIndex.HasValue)
            djEditSavedVenues.RemoveAt(removeIndex.Value);

        if (djEditSavedVenues.Count >= MaxSavedVenues)
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"Up to {MaxSavedVenues} saved venues.");
            return;
        }

        ImGui.Spacing();
        DrawSleekDivider(width, Theme.Border);
        ImGui.Spacing();
        ImGui.TextDisabled("Add a venue");

        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##venueEntryName", "Venue name", ref djEditVenueNameBuffer, 40);

        var venueTypeIndex = djEditVenueIsApartment ? 1 : 0;
        if (SettingsSegmented.Draw("##venueEntryType", VenueTypeLabels, ref venueTypeIndex, width))
        {
            djEditVenueIsApartment = venueTypeIndex == 1;
            if (!djEditVenueIsApartment)
                djEditVenueSubdivision = false;
        }

        ImGui.Spacing();
        var halfWidth = (width - (8f * Scale)) / 2f;
        var newDataCenter = DrawVenueDropdown("##venueEntryDataCenter", "Data Center", djEditVenueDataCenterBuffer, DataCenterOptions, halfWidth);
        if (newDataCenter != djEditVenueDataCenterBuffer)
        {
            djEditVenueDataCenterBuffer = newDataCenter;
            djEditVenueWorldBuffer = ResetWorldIfInvalidForDataCenter(djEditVenueDataCenterBuffer, djEditVenueWorldBuffer);
        }

        ImGui.SameLine();
        var worldOptions = DataCenters.FirstOrDefault(dc => dc.DataCenter == djEditVenueDataCenterBuffer).Worlds ?? Array.Empty<string>();
        ImGui.BeginDisabled(string.IsNullOrEmpty(djEditVenueDataCenterBuffer));
        djEditVenueWorldBuffer = DrawVenueDropdown("##venueEntryWorld", "World", djEditVenueWorldBuffer, worldOptions, halfWidth);
        ImGui.EndDisabled();

        djEditVenueHousingAreaBuffer = DrawVenueDropdown("##venueEntryHousingArea", "Housing Area", djEditVenueHousingAreaBuffer, HousingAreaOptions, width);

        djEditVenueWardBuffer = DrawVenueDropdown("##venueEntryWard", "Ward", djEditVenueWardBuffer, WardOptions, halfWidth, selectedLabelPrefix: "Ward");
        ImGui.SameLine();
        djEditVenuePlotBuffer = djEditVenueIsApartment
            ? DrawVenueDropdown("##venueEntryApartment", "Apartment #", djEditVenuePlotBuffer, ApartmentOptions, halfWidth, selectedLabelPrefix: "Apartment")
            : DrawVenueDropdown("##venueEntryPlot", "Plot", djEditVenuePlotBuffer, PlotOptions, halfWidth, selectedLabelPrefix: "Plot");

        if (djEditVenueIsApartment)
        {
            ImGui.Spacing();
            SettingsToggle.Draw("##venueEntrySubdivision", "Subdivision", ref djEditVenueSubdivision,
                "Some housing areas have a second, separate apartment building added as a subdivision - enable this if that's the one you mean.");
        }

        var canAdd = !string.IsNullOrWhiteSpace(djEditVenueNameBuffer)
            && !string.IsNullOrEmpty(djEditVenueDataCenterBuffer) && !string.IsNullOrEmpty(djEditVenueWorldBuffer)
            && !string.IsNullOrEmpty(djEditVenueHousingAreaBuffer) && !string.IsNullOrEmpty(djEditVenueWardBuffer) && !string.IsNullOrEmpty(djEditVenuePlotBuffer);

        ImGui.Spacing();
        if (PanelButton.Draw("##addVenue", plugin.Fonts.Icon, FontAwesomeIcon.Plus, "Add Venue", new Vector2(160, 28) * Scale, Theme.NeutralAccent) && canAdd)
        {
            djEditSavedVenues.Add(new SavedVenueDto
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = djEditVenueNameBuffer.Trim(),
                DataCenter = djEditVenueDataCenterBuffer,
                World = djEditVenueWorldBuffer,
                HousingArea = djEditVenueHousingAreaBuffer,
                Ward = djEditVenueWardBuffer,
                Plot = djEditVenuePlotBuffer,
                IsApartment = djEditVenueIsApartment,
                Subdivision = djEditVenueSubdivision,
            });
            ClearVenueEntryForm();
        }

        if (!canAdd)
            ImGui.TextDisabled("Name and all five address fields are required.");
    }

    /// Opens a titled, tinted/bordered panel wrapping one logical group of controls - originally built for
    /// the DJ Profile edit form (Basic Info, Genres & Venues, Weekly Availability, Images, Appearance), now
    /// also used for Settings' own tabs in place of the old flat DrawSettingsSectionHeader (colored label + a
    /// plain native Separator, no visual grouping around the section's actual content).
    private float BeginSettingsPanel(string title, float width)
    {
        settingsPanelPos = ImGui.GetCursorScreenPos();
        settingsPanelWidth = width;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        var pad = SettingsPanelPad * Scale;
        ImGui.SetCursorScreenPos(settingsPanelPos + new Vector2(pad, pad));
        ImGui.BeginGroup();
        DrawDjProfileColumnLabel(title, Theme.NeutralAccent);
        ImGui.Spacing();

        ImGui.PushTextWrapPos((settingsPanelPos.X - ImGui.GetWindowPos().X) + width - pad);

        return width - (pad * 2f);
    }

    private void EndSettingsPanel()
    {
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var contentBottom = ImGui.GetCursorScreenPos().Y;

        var pad = SettingsPanelPad * Scale;
        var accent = Theme.NeutralAccent;
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        var panelHeight = contentBottom - settingsPanelPos.Y + pad;
        drawList.AddRectFilled(settingsPanelPos, settingsPanelPos + new Vector2(settingsPanelWidth, panelHeight), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.06f)), 10f * Scale);
        drawList.AddRect(settingsPanelPos, settingsPanelPos + new Vector2(settingsPanelWidth, panelHeight), ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, 0.25f)), 10f * Scale, ImDrawFlags.None, 1f);
        drawList.ChannelsMerge();

        ImGui.SetCursorScreenPos(new Vector2(settingsPanelPos.X, settingsPanelPos.Y + panelHeight));
    }

    /// A live mirror of what DrawDjProfileCard will actually render once this listing is saved, built
    /// straight from the in-progress edit buffers instead of a saved DTO - so a DJ sees what they're building
    /// as they type, pick colors, or upload images, rather than only finding out after hitting Save.
    private void DrawDjProfileEditPreview(float columnWidth)
    {
        DrawDjProfileColumnLabel("PREVIEW", Theme.NeutralAccent);
        ImGui.Spacing();

        var cardWidth = MathF.Min(240f * Scale, columnWidth);
        CenterNextItem(cardWidth, columnWidth);

        var frameColor = new Vector4(djEditFrameColor.X, djEditFrameColor.Y, djEditFrameColor.Z, 1f);
        var innerWidth = cardWidth - (28f * Scale);

        Theme.BeginCard("##djEditPreviewCard", new Vector2(cardWidth, djEditPreviewCardHeight), fontScale: Scale, gradientTint: frameColor);
        var contentStartY = ImGui.GetCursorPosY();

        ImGui.Spacing();
        var avatarSize = 110f * Scale;
        CenterNextItem(avatarSize, innerWidth);
        DrawDjProfileAvatar("##djEditPreviewAvatar", djEditAvatarPreview != null ? "preview" : null, avatarSize, frameColor, djEditFrameStyle, djEditAvatarPreview);
        ImGui.Spacing();

        var previewName = djEditDjNameBuffer.Trim() is { Length: > 0 } n ? n : "Your DJ Name";
        var nameColor = new Vector4(djEditNameColor.X, djEditNameColor.Y, djEditNameColor.Z, 1f);
        using (plugin.Fonts.Header.PushSafe())
        {
            var nameText = TruncateToWidth(previewName, innerWidth);
            CenterNextItem(ImGui.CalcTextSize(nameText).X, innerWidth);
            ImGui.TextColored(nameColor, nameText);
        }

        if (djEditGenres.Count > 0)
        {
            ImGui.Spacing();
            var totalChipWidth = MeasureChipRowWidth(djEditGenres);
            if (totalChipWidth <= innerWidth)
                CenterNextItem(totalChipWidth, innerWidth);
            DrawCappedChipRow(djEditGenres, innerWidth, Theme.FixedCyan);
        }

        var contentHeight = ImGui.GetCursorPosY() - contentStartY + 20f;
        djEditPreviewCardHeight = contentHeight / Math.Max(Scale, 0.01f);

        Theme.EndCard();
    }

    /// Draws a DJ's name at an explicit size (fontScale relative to the Header font's own natural size, same
    /// "AddText(font, size, ...)" technique as UiHelpers.DrawScaledIcon - no separate big-name font asset
    /// needed) in whichever text effect they picked.
    private void DrawDjName(string text, Vector4 color, string effect, float fontScale)
    {
        ImFontPtr font;
        using (plugin.Fonts.HeaderLarge.PushSafe())
            font = ImGui.GetFont();

        using (plugin.Fonts.Header.PushSafe())
        {
            var drawFontSize = ImGui.GetFontSize() * fontScale;
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();

            switch (effect)
            {
                case "Pulse":
                {
                    var pulse = 0.6f + (0.4f * MathF.Sin((float)ImGui.GetTime() * 2.2f));
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, pulse)), text);
                    break;
                }

                case "Rainbow":
                {
                    var hue = (float)(ImGui.GetTime() * 0.15 % 1.0);
                    var rainbow = HsvToRgb(hue, 0.7f, 1f);
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(new Vector4(rainbow.X, rainbow.Y, rainbow.Z, 1f)), text);
                    break;
                }

                case "Wave":
                {
                    var x = pos.X;
                    var time = (float)ImGui.GetTime();
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var yOffset = MathF.Sin((time * 4f) + (i * 0.6f)) * 3f * Scale;
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y + yOffset), ImGui.GetColorU32(color), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Gradient":
                {
                    var x = pos.X;
                    var tint = new Vector4(MathF.Min(1f, color.X + 0.4f), MathF.Min(1f, color.Y + 0.4f), MathF.Min(1f, color.Z + 0.4f), 1f);
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var t = text.Length > 1 ? i / (float)(text.Length - 1) : 0f;
                        var segColor = Vector4.Lerp(color, tint, t);
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y), ImGui.GetColorU32(segColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Glow":
                {
                    var haloColor = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.22f));
                    var haloOffset = 2.2f * Scale;
                    Span<Vector2> haloDirs = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];
                    foreach (var d in haloDirs)
                        drawList.AddText(font, drawFontSize, pos + (d * haloOffset), haloColor, text);
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    break;
                }

                case "Shimmer":
                {
                    var x = pos.X;
                    var totalWidth = ImGui.CalcTextSize(text).X * fontScale;
                    var sweep = ((float)(ImGui.GetTime() * 0.6 % 1.6)) - 0.3f;
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var charT = totalWidth > 0f ? (x - pos.X + (charWidth / 2f)) / totalWidth : 0f;
                        var dist = MathF.Abs(charT - sweep);
                        var highlight = MathF.Max(0f, 1f - (dist * 4f));
                        var segColor = Vector4.Lerp(color, new Vector4(1f, 1f, 1f, 1f), highlight);
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y), ImGui.GetColorU32(segColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Chase":
                {
                    var x = pos.X;
                    var timeOffset = (float)(ImGui.GetTime() * 0.4 % 1.0);
                    var tint = new Vector4(MathF.Min(1f, color.X + 0.4f), MathF.Min(1f, color.Y + 0.4f), MathF.Min(1f, color.Z + 0.4f), 1f);
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var t = text.Length > 1 ? i / (float)(text.Length - 1) : 0f;
                        var blend = (MathF.Sin((t + timeOffset) * MathF.PI * 2f) + 1f) / 2f;
                        var segColor = Vector4.Lerp(color, tint, blend);
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y), ImGui.GetColorU32(segColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Flicker":
                {
                    var t = (float)ImGui.GetTime();
                    var flicker = 0.65f + (0.35f * MathF.Sin(t * 13f) * MathF.Sin(t * 7f));
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, MathF.Max(0.3f, flicker))), text);
                    break;
                }

                case "Typewriter":
                {
                    const float cycleSeconds = 3.2f;
                    const float holdFraction = 0.25f;                    var cyclePos = (float)(ImGui.GetTime() % cycleSeconds) / cycleSeconds;
                    var revealPortion = MathF.Min(1f, cyclePos / (1f - holdFraction));
                    var revealCount = (int)MathF.Ceiling(revealPortion * text.Length);
                    var showCursor = ((int)(ImGui.GetTime() * 2f) % 2) == 0 && revealCount < text.Length;

                    var x = pos.X;
                    for (var i = 0; i < revealCount; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y), ImGui.GetColorU32(color), ch);
                        x += charWidth;
                    }
                    if (showCursor)
                        drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + drawFontSize), ImGui.GetColorU32(color), 2f * Scale);
                    break;
                }

                case "Marquee":
                {
                    var textWidth = ImGui.CalcTextSize(text).X * fontScale;
                    var gap = textWidth * 0.6f + (20f * Scale);
                    var cycleWidth = textWidth + gap;
                    var scrollX = ((float)ImGui.GetTime() * 40f * Scale) % cycleWidth;

                    drawList.PushClipRect(pos, pos + new Vector2(textWidth, drawFontSize), true);
                    drawList.AddText(font, drawFontSize, new Vector2(pos.X - scrollX, pos.Y), ImGui.GetColorU32(color), text);
                    drawList.AddText(font, drawFontSize, new Vector2(pos.X - scrollX + cycleWidth, pos.Y), ImGui.GetColorU32(color), text);
                    drawList.PopClipRect();
                    break;
                }

                case "Glitch":
                {
                    var bucket = (int)((float)ImGui.GetTime() * 8f);
                    var x = pos.X;
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var charRng = new Random((bucket * 131) + i);
                        var jitterX = ((float)charRng.NextDouble() - 0.5f) * 3f * Scale;
                        var jitterY = ((float)charRng.NextDouble() - 0.5f) * 3f * Scale;
                        var corrupted = charRng.NextDouble() < 0.12;
                        var charColor = corrupted ? new Vector4(1f, 1f, 1f, 0.9f) : color;
                        drawList.AddText(font, drawFontSize, new Vector2(x + jitterX, pos.Y + jitterY), ImGui.GetColorU32(charColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Outline":
                {
                    var outlineColor = ImGui.GetColorU32(new Vector4(color.X * 0.25f, color.Y * 0.25f, color.Z * 0.25f, 1f));
                    var outlineOffset = 1.4f * Scale;
                    Span<Vector2> dirs = [new(-1, -1), new(1, -1), new(-1, 1), new(1, 1), new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];
                    foreach (var d in dirs)
                        drawList.AddText(font, drawFontSize, pos + (d * outlineOffset), outlineColor, text);
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    break;
                }

                case "Underline":
                {
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    var underlineWidth = ImGui.CalcTextSize(text).X * fontScale;
                    var lineY = pos.Y + drawFontSize + (2f * Scale);
                    drawList.AddLine(new Vector2(pos.X, lineY), new Vector2(pos.X + underlineWidth, lineY), ImGui.GetColorU32(color), 2f * Scale);
                    drawList.AddCircleFilled(new Vector2(pos.X, lineY), 2.2f * Scale, ImGui.GetColorU32(color));
                    drawList.AddCircleFilled(new Vector2(pos.X + underlineWidth, lineY), 2.2f * Scale, ImGui.GetColorU32(color));
                    break;
                }

                case "Embossed":
                {
                    var shadowOffset = 1.6f * Scale;
                    var shadowColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f));
                    var highlightColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f));
                    drawList.AddText(font, drawFontSize, pos + new Vector2(shadowOffset, shadowOffset), shadowColor, text);
                    drawList.AddText(font, drawFontSize, pos - new Vector2(shadowOffset, shadowOffset), highlightColor, text);
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    break;
                }

                case "Cascade":
                {
                    const float cycleSeconds = 2.2f;
                    const float dropDuration = 0.45f;
                    var time = (float)ImGui.GetTime();
                    var x = pos.X;
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var staggerStart = i * 0.05f;
                        var localT = (time + cycleSeconds - staggerStart) % cycleSeconds;
                        var dropProgress = Math.Clamp(localT / dropDuration, 0f, 1f);
                        var eased = 1f - MathF.Pow(1f - dropProgress, 3f);
                        var yOffset = (1f - eased) * -12f * Scale;
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y + yOffset), ImGui.GetColorU32(color), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Heatwave":
                {
                    var x = pos.X;
                    var time = (float)ImGui.GetTime();
                    var warm = new Vector4(MathF.Min(1f, color.X + 0.3f), color.Y, MathF.Max(0f, color.Z - 0.2f), 1f);
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var wobble = MathF.Sin((time * 5f) + (i * 1.1f)) * 1.5f * Scale;
                        var warmth = (MathF.Sin((time * 2f) + (i * 0.4f)) + 1f) / 2f;
                        var charColor = Vector4.Lerp(color, warm, warmth * 0.6f);
                        drawList.AddText(font, drawFontSize, new Vector2(x + wobble, pos.Y), ImGui.GetColorU32(charColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Blink":
                {
                    const float onSeconds = 1.6f;
                    const float offSeconds = 0.35f;
                    var cyclePos = (float)ImGui.GetTime() % (onSeconds + offSeconds);
                    if (cyclePos < onSeconds)
                        drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    break;
                }

                case "Split":
                {
                    var x = pos.X;
                    var tint = new Vector4(MathF.Min(1f, color.X + 0.45f), MathF.Min(1f, color.Y + 0.45f), MathF.Min(1f, color.Z + 0.45f), 1f);
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var charColor = i % 2 == 0 ? color : tint;
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y), ImGui.GetColorU32(charColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                default:                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    break;
            }

            var textSize = ImGui.CalcTextSize(text) * fontScale;
            ImGui.Dummy(textSize);
        }
    }

    private void DrawDjProfileImagePicker(string label, string slot, IDalamudTextureWrap? preview, int targetWidth, int targetHeight)
    {
        ImGui.TextDisabled(label);
        if (preview != null)
        {
            var previewWidth = 160f * Scale;
            var previewHeight = previewWidth * (targetHeight / (float)targetWidth);
            ImGui.Image(preview.Handle, new Vector2(previewWidth, previewHeight));
        }

        if (PanelButton.Draw($"##pick{slot}", plugin.Fonts.Icon, FontAwesomeIcon.Upload, preview != null ? "Change" : "Upload", new Vector2(110, 26) * Scale, Theme.NeutralAccent))
            OpenDjProfileImageDialog(slot, targetWidth, targetHeight);
    }

    private void OpenDjProfileImageDialog(string slot, int targetWidth, int targetHeight)
    {
        djProfileImageFileDialogManager.OpenFileDialog(
            slot == "avatar" ? "Select an avatar image" : "Select a banner image",
            "Image files{.png,.jpg,.jpeg}",
            (success, paths) =>
            {
                if (success && paths.Count > 0)
                    _ = imageCropDialog.OpenAsync(paths[0], targetWidth, targetHeight, processed => FinishDjProfileImageStaging(slot, processed));
            },
            1,
            null,
            false);
    }

    /// Builds a preview from the already-cropped bytes ImageCropDialog hands back, then always STAGES the
    /// processed temp file path rather than uploading right away - picking an avatar/banner is part of
    /// editing the listing, not its own separate save action, so it shouldn't hit the server until Save
    /// Listing is actually clicked (matching every other field in this form).
    private async void FinishDjProfileImageStaging(string slot, byte[] processed)
    {
        try
        {
            var wrap = await Plugin.TextureProvider.CreateFromImageAsync(processed);
            if (slot == "avatar")
            {
                djEditAvatarPreview?.Dispose();
                djEditAvatarPreview = wrap;
            }
            else
            {
                djEditBannerPreview?.Dispose();
                djEditBannerPreview = wrap;
            }

            djEditImageError = null;
        }
        catch (Exception ex)
        {
            djEditImageError = $"Couldn't preview that image: {ex.Message}";
            return;
        }

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"echomix-djprofile-{slot}-{Guid.NewGuid():N}.jpg");
            await File.WriteAllBytesAsync(tempPath, processed);

            if (slot == "avatar")
            {
                djEditAvatarUploadPath = tempPath;
            }
            else
            {
                djEditBannerUploadPath = tempPath;
            }
        }
        catch (Exception ex)
        {
            djEditImageError = $"Couldn't upload that image: {ex.Message}";
        }
    }

    /// Back from the DJ Profile edit form always saves first - there's no separate Save Listing button
    /// anymore (it sat at the bottom-right of a form that can scroll well past it, and forgetting to click it
    /// before backing out silently discarded every edit).
    private void TriggerDjProfileSaveThenNavigateTo(ViewMode destination)
    {
        if (editingDjProfileId == null && string.IsNullOrWhiteSpace(djEditDjNameBuffer))
        {
            pendingView = destination;
            return;
        }

        pendingDjProfileEditExit = destination;
        if (djProfileSaveSending)
            return;

        djProfileSaveSending = true;
        djProfileSaveElapsed = 0f;
        djProfileSaveResult = null;
        plugin.AudioHostClient.Send(MessageType.SaveDjProfile, new SaveDjProfileMessage
        {
            CharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
            DjName = djEditDjNameBuffer.Trim(),
            Bio = WrappedInput.Unfold(djEditBioBuffer, djEditBioWrapWidth).Trim(),
            SavedVenues = new List<SavedVenueDto>(djEditSavedVenues),
            Genres = new List<string>(djEditGenres),
            Availability = djEditAvailability,
            FrameColorR = djEditFrameColor.X,
            FrameColorG = djEditFrameColor.Y,
            FrameColorB = djEditFrameColor.Z,
            FrameStyle = djEditFrameStyle,
            NameEffect = djEditNameEffect,
            NameColorR = djEditNameColor.X,
            NameColorG = djEditNameColor.Y,
            NameColorB = djEditNameColor.Z,
            AetherphoneNumber = djEditAetherphoneNumber.Trim(),
            ShowLinkedCharacters = djEditShowLinkedCharacters,
        });
    }

    /// Create-or-edit form for the current player's own DJ List listing - see
    /// ResetDjEditBuffersForNewProfile/SeedDjEditBuffersFrom for how the buffers get into their starting
    /// state before this is ever drawn.
    private void DrawDjProfileEditBody()
    {
        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextColored(Theme.NeutralAccent, editingDjProfileId == null ? "Add Listing" : "Edit Listing");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();

        var avail = ImGui.GetContentRegionAvail().X;
        var columnGap = 24f * Scale;
        var leftWidth = MathF.Min(440f * Scale, (avail - columnGap) * 0.5f);
        var rightWidth = avail - leftWidth - columnGap;
        var columnsStartPos = ImGui.GetCursorPos();

        ImGui.SetCursorPos(columnsStartPos);
        ImGui.BeginGroup();

        var basicInfoWidth = BeginSettingsPanel("BASIC INFO", leftWidth);
        ImGui.TextDisabled("DJ Name");
        ImGui.SetNextItemWidth(basicInfoWidth);
        ImGui.InputTextWithHint("##djEditName", "Your DJ name", ref djEditDjNameBuffer, 32);

        ImGui.Spacing();
        ImGui.TextDisabled("Bio");
        var djEditBioBoxSize = new Vector2(basicInfoWidth, 60f * Scale);
        djEditBioWrapWidth = WrappedInput.WidthFor(djEditBioBoxSize);
        if (djEditBioWrapPending)
        {
            djEditBioBuffer = WrappedInput.Fold(djEditBioBuffer, djEditBioWrapWidth);
            djEditBioWrapPending = false;
        }
        WrappedInput.Multiline("##djEditBio", ref djEditBioBuffer, 300, djEditBioBoxSize);

        ImGui.Spacing();
        ImGui.TextDisabled("Aetherphone # (optional)");
        ImGui.SetNextItemWidth(basicInfoWidth);
        ImGui.InputTextWithHint("##djEditAetherphoneNumber", "Shown on your profile for listeners to copy", ref djEditAetherphoneNumber, 32);
        EndSettingsPanel();

        ImGui.Spacing();
        var genresWidth = BeginSettingsPanel("GENRES", leftWidth);
        DrawTagChipEditor("djEditGenre", djEditGenres, ref djEditGenreEntryBuffer, "Genre (e.g. House)", genresWidth, Theme.FixedCyan, capitalizeWords: true);
        EndSettingsPanel();

        ImGui.Spacing();
        var venuesWidth = BeginSettingsPanel("SAVED VENUES", leftWidth);
        ImGui.TextDisabled("These become selectable when you go live with a Proximity or Global venue address set.");
        ImGui.Spacing();
        DrawSavedVenueEditor(venuesWidth);
        EndSettingsPanel();

        ImGui.Spacing();
        BeginSettingsPanel("WEEKLY AVAILABILITY", leftWidth);
        DrawAvailabilityStrip(djEditAvailability, readOnly: false);
        EndSettingsPanel();

        ImGui.EndGroup();
        var leftColumnHeight = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorPos(new Vector2(columnsStartPos.X + leftWidth + columnGap, columnsStartPos.Y));
        ImGui.BeginGroup();

        DrawDjProfileEditPreview(rightWidth);

        ImGui.Spacing();
        BeginSettingsPanel("IMAGES", rightWidth);
        DrawDjProfileImagePicker($"Avatar ({ShowImageProcessor.DjAvatarSize}x{ShowImageProcessor.DjAvatarSize})", "avatar", djEditAvatarPreview, ShowImageProcessor.DjAvatarSize, ShowImageProcessor.DjAvatarSize);
        ImGui.Spacing();
        DrawDjProfileImagePicker($"Banner ({ShowImageProcessor.DjBannerWidth}x{ShowImageProcessor.DjBannerHeight})", "banner", djEditBannerPreview, ShowImageProcessor.DjBannerWidth, ShowImageProcessor.DjBannerHeight);
        EndSettingsPanel();

        ImGui.Spacing();
        BeginSettingsPanel("APPEARANCE", rightWidth);
        ImGui.TextDisabled("Avatar Frame");
        ImGui.SetNextItemWidth(160f * Scale);
        ImGui.ColorEdit3("##djEditFrameColor", ref djEditFrameColor);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160f * Scale);
        if (ImGui.BeginCombo("##djEditFrameStyle", djEditFrameStyle))
        {
            foreach (var style in DjFrameStyleOptions)
            {
                if (ImGui.Selectable(style, style == djEditFrameStyle))
                    djEditFrameStyle = style;
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (12f * Scale));
        var previewColor = new Vector4(djEditFrameColor.X, djEditFrameColor.Y, djEditFrameColor.Z, 1f);
        DrawDjProfileAvatar("##framePreview", djEditAvatarPreview != null ? "preview" : null, 90f * Scale, previewColor, djEditFrameStyle, djEditAvatarPreview);

        ImGui.Spacing();
        ImGui.TextDisabled("Name");
        ImGui.SetNextItemWidth(160f * Scale);
        ImGui.ColorEdit3("##djEditNameColor", ref djEditNameColor);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160f * Scale);
        if (ImGui.BeginCombo("##djEditNameEffect", djEditNameEffect))
        {
            foreach (var effect in DjNameEffectOptions)
            {
                if (ImGui.Selectable(effect, effect == djEditNameEffect))
                    djEditNameEffect = effect;
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();
        var nameColorPreview = new Vector4(djEditNameColor.X, djEditNameColor.Y, djEditNameColor.Z, 1f);
        DrawDjName(djEditDjNameBuffer.Trim() is { Length: > 0 } previewName ? previewName : "Your DJ Name", nameColorPreview, djEditNameEffect, 1.3f);
        EndSettingsPanel();

        if (editingDjProfileId != null)
        {
            ImGui.Spacing();
            var linkedWidth = BeginSettingsPanel("LINKED CHARACTERS", rightWidth);
            ImGui.TextDisabled("Other characters with full access to this same listing - their broadcasts,");
            ImGui.TextDisabled("likes, and follows all count as this profile's.");
            ImGui.Spacing();

            if (djEditLinkedCharacterNames.Count == 0)
            {
                ImGui.TextDisabled("None linked yet.");
            }
            else
            {
                var unlinkButtonSize = new Vector2(70, 22) * Scale;
                foreach (var linkedName in djEditLinkedCharacterNames.ToArray())
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(linkedName);
                    ImGui.SameLine(linkedWidth - unlinkButtonSize.X);
                    var pending = djUnlinkPending.Contains(linkedName);
                    if (PanelButton.Draw($"##unlink{linkedName}", null, null, pending ? "..." : "Unlink", unlinkButtonSize, Theme.OrangeAccent) && !pending)
                    {
                        djUnlinkPending.Add(linkedName);
                        plugin.AudioHostClient.Send(MessageType.UnlinkProfileCharacter, new UnlinkProfileCharacterMessage
                        {
                            ProfileId = editingDjProfileId ?? string.Empty,
                            RequesterCharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
                            CharacterNameToRemove = linkedName,
                        });
                    }
                }
            }

            ImGui.Spacing();
            ImGui.Checkbox("Show linked characters on my public profile", ref djEditShowLinkedCharacters);
            ImGui.Spacing();

            var atCap = djEditLinkedCharacterNames.Count >= 5;
            if (djLinkCodeGenerated != null)
            {
                var minutes = (int)djLinkCodeExpiresInSeconds / 60;
                var seconds = (int)djLinkCodeExpiresInSeconds % 60;
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Theme.CyanAccent, $"Code: {djLinkCodeGenerated}   (expires in {minutes}:{seconds:D2})");
                ImGui.SameLine();
                var copyButtonSize = new Vector2(26, 22) * Scale;
                if (PanelButton.Draw("##copyLinkCode", plugin.Fonts.Icon, FontAwesomeIcon.Copy, null, copyButtonSize, Theme.NeutralAccent))
                {
                    ImGui.SetClipboardText(djLinkCodeGenerated);
                    djLinkCodeCopiedAt = ImGui.GetTime();
                }

                if (djLinkCodeCopiedAt is { } copiedAt && ImGui.GetTime() - copiedAt < 1.5)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.CyanAccent, "Copied!");
                }

                ImGui.TextDisabled("Give this to the other character - enter it via \"Link a Character\"");
                ImGui.TextDisabled("on the DJ List, from that character.");
                ImGui.Spacing();
            }
            else if (djLinkCodeError != null)
            {
                ImGui.TextColored(Theme.OrangeAccent, djLinkCodeError);
                ImGui.Spacing();
            }

            if (atCap)
            {
                ImGui.TextDisabled("Already at the 5-character link limit.");
            }
            else if (PanelButton.Draw("##generateLinkCode", plugin.Fonts.Icon, FontAwesomeIcon.Link,
                         djLinkCodeGenerating ? "Generating..." : "Generate Link Code", new Vector2(200, 28) * Scale, Theme.NeutralAccent)
                     && !djLinkCodeGenerating)
            {
                djLinkCodeGenerating = true;
                djLinkCodeError = null;
                plugin.AudioHostClient.Send(MessageType.GenerateProfileLinkCode, new GenerateProfileLinkCodeMessage
                {
                    ProfileId = editingDjProfileId ?? string.Empty,
                    RequesterCharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
                });
            }

            EndSettingsPanel();
        }

        ImGui.EndGroup();
        var rightColumnHeight = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorPos(new Vector2(columnsStartPos.X, columnsStartPos.Y + MathF.Max(leftColumnHeight, rightColumnHeight)));

        if (!string.IsNullOrEmpty(djEditImageError))
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.OrangeAccent, djEditImageError);
        }

        var rightEdge = columnsStartPos.X + avail;

        ImGui.Spacing();
        ImGui.Spacing();

        if (djProfileSaveSending)
        {
            ImGui.SetCursorPosX(rightEdge - ImGui.CalcTextSize("Saving...").X);
            ImGui.TextDisabled("Saving...");
        }
        else if (djProfileSaveResult != null)
        {
            var resultText = djProfileSaveResult.Success ? "Saved!" : $"Couldn't save: {djProfileSaveResult.Error ?? "unknown error"}";
            var resultColor = djProfileSaveResult.Success ? Theme.CyanAccent : Theme.OrangeAccent;
            ImGui.SetCursorPosX(rightEdge - ImGui.CalcTextSize(resultText).X);
            ImGui.TextColored(resultColor, resultText);
        }
        else
        {
            const string hint = "Changes save automatically when you click Back.";
            ImGui.SetCursorPosX(rightEdge - ImGui.CalcTextSize(hint).X);
            ImGui.TextDisabled(hint);
        }
    }

    private void DrawDeckBody(MixerStatusMessage status)
    {
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0f, 0f, 0f, 0f));
        ImGui.BeginChild("##deckBodyScroll", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false, ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(Scale);

        var scrollbarInset = ImGui.GetStyle().ScrollbarSize;
        ImGui.Indent(scrollbarInset);

        DrawDecksRow(status);
        ImGui.Separator();
        ImGui.Spacing();
        DrawBottomSection(status);

        ImGui.Unindent(scrollbarInset);
        ImGui.EndChild();
        ImGui.PopStyleColor(4);
    }

    private static readonly string[] SettingsTabLabels = { "General", "Library", "Broadcast", "Listen", "Spotify", "Changelog" };
    private const int SettingsChangelogTabIndex = 5;

    private void DrawSettingsBody()
    {
        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextColored(Theme.NeutralAccent, "SETTINGS");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0f, 0f, 0f, 0f));
        ImGui.BeginChild("##settingsScroll", new Vector2(0f, ImGui.GetContentRegionAvail().Y), false, ImGuiWindowFlags.AlwaysVerticalScrollbar | ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(Scale);

        var scrollbarInset = ImGui.GetStyle().ScrollbarSize;
        ImGui.Indent(scrollbarInset);

        var settingsTab = settingsTabStrip.Draw("##settingsTabs", SettingsTabLabels, badgeOn: SettingsChangelogTabIndex, badgeText: HasUnseenChangelog ? string.Empty : null);
        ImGui.Spacing();

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * settingsTabStrip.ContentAlpha);

        switch (settingsTab)
        {
            case 0:
                DrawGeneralSettingsTab();
                break;
            case 1:
                playlistPanel.Draw();
                break;
            case 2:
                DrawBroadcastTab();
                break;
            case 3:
                DrawListenTab();
                break;
            case 4:
                DrawSpotifyTab();
                break;
            default:
                DrawChangelogTab();
                break;
        }

        ImGui.PopStyleVar();

        ImGui.Unindent(scrollbarInset);
        ImGui.EndChild();
        ImGui.PopStyleColor(4);
    }

    /// True until the player actually opens the Changelog tab while ChangelogData.Entries has a newer version
    /// than they've last seen - drives the badge dot on both the Settings header icon and the Changelog tab
    /// itself (see DrawHeader/DrawSettingsBody).
    private bool HasUnseenChangelog => plugin.Configuration.LastSeenChangelogVersion != ChangelogData.LatestVersion;

    /// A small dot in the corner of whatever was just drawn (a tab item or a header icon button) - both are
    /// plain ImGui items under the hood, so GetItemRectMin/Max right after either one gives the exact rect to
    /// badge.
    private void DrawUnseenChangelogBadge()
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var radius = 4f * Scale;
        var dotPos = new Vector2(max.X - radius, min.Y + radius);
        ImGui.GetWindowDrawList().AddCircleFilled(dotPos, radius, ImGui.GetColorU32(Theme.OrangeAccent));
    }

    /// Marks the changelog as seen the moment this tab is actually drawn (i.e.
    private void DrawChangelogTab()
    {
        if (plugin.Configuration.LastSeenChangelogVersion != ChangelogData.LatestVersion)
        {
            plugin.Configuration.LastSeenChangelogVersion = ChangelogData.LatestVersion;
            plugin.Configuration.Save();
        }

        for (var i = 0; i < ChangelogData.Entries.Length; i++)
        {
            var entry = ChangelogData.Entries[i];
            using (plugin.Fonts.Header.PushSafe())
                ImGui.TextColored(Theme.CyanAccent, $"v{entry.Version}");
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
    }


    private void DrawGeneralSettingsTab()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var columnGap = 24f * Scale;
        var leftWidth = MathF.Min(SettingsPanelWidth * Scale, (avail - columnGap) * 0.5f);
        var rightWidth = avail - leftWidth - columnGap;
        var columnsStartPos = ImGui.GetCursorPos();

        ImGui.SetCursorPos(columnsStartPos);
        ImGui.BeginGroup();

        var appearanceWidth = BeginSettingsPanel("APPEARANCE", leftWidth);
        ImGui.TextDisabled("UI Scale");
        var uiScale = plugin.Configuration.UiScale;
        if (SettingsSlider.Draw("##uiScale", ref uiScale, 0.75f, 1.5f, appearanceWidth, "{0:F2}x", 1f))
        {
            plugin.Configuration.UiScale = uiScale;
            plugin.Configuration.Save();
        }
        SettingsSlider.ResetOnRightClick(() =>
        {
            plugin.Configuration.UiScale = 1f;
            plugin.Configuration.Save();
        });
        ImGui.TextDisabled("Scales the whole window - handy on a bigger or smaller monitor.");
        EndSettingsPanel();

        ImGui.Spacing();

        void ApplyDeckColors() => Theme.ApplyCustomColors(
            new Vector4(plugin.Configuration.DeckAAccentR, plugin.Configuration.DeckAAccentG, plugin.Configuration.DeckAAccentB, 1f),
            new Vector4(plugin.Configuration.DeckBAccentR, plugin.Configuration.DeckBAccentG, plugin.Configuration.DeckBAccentB, 1f),
            new Vector4(plugin.Configuration.BlendAccentR, plugin.Configuration.BlendAccentG, plugin.Configuration.BlendAccentB, 1f));

        BeginSettingsPanel("DECK COLORS", leftWidth);
        var deckAColor = new Vector3(plugin.Configuration.DeckAAccentR, plugin.Configuration.DeckAAccentG, plugin.Configuration.DeckAAccentB);
        ImGui.SetNextItemWidth(220 * Scale);
        if (ImGui.ColorEdit3("Deck A Color", ref deckAColor))
        {
            plugin.Configuration.DeckAAccentR = deckAColor.X;
            plugin.Configuration.DeckAAccentG = deckAColor.Y;
            plugin.Configuration.DeckAAccentB = deckAColor.Z;
            plugin.Configuration.Save();
            ApplyDeckColors();
        }

        var deckBColor = new Vector3(plugin.Configuration.DeckBAccentR, plugin.Configuration.DeckBAccentG, plugin.Configuration.DeckBAccentB);
        ImGui.SetNextItemWidth(220 * Scale);
        if (ImGui.ColorEdit3("Deck B Color", ref deckBColor))
        {
            plugin.Configuration.DeckBAccentR = deckBColor.X;
            plugin.Configuration.DeckBAccentG = deckBColor.Y;
            plugin.Configuration.DeckBAccentB = deckBColor.Z;
            plugin.Configuration.Save();
            ApplyDeckColors();
        }

        var blendColor = new Vector3(plugin.Configuration.BlendAccentR, plugin.Configuration.BlendAccentG, plugin.Configuration.BlendAccentB);
        ImGui.SetNextItemWidth(220 * Scale);
        if (ImGui.ColorEdit3("Blend Color", ref blendColor))
        {
            plugin.Configuration.BlendAccentR = blendColor.X;
            plugin.Configuration.BlendAccentG = blendColor.Y;
            plugin.Configuration.BlendAccentB = blendColor.Z;
            plugin.Configuration.Save();
            ApplyDeckColors();
        }

        ImGui.Spacing();
        if (PanelButton.Draw("##resetDeckColors", plugin.Fonts.Icon, FontAwesomeIcon.Undo, "Reset to Default", new Vector2(170, 28) * Scale, Theme.NeutralAccent))
        {
            plugin.Configuration.DeckAAccentR = 0.25f; plugin.Configuration.DeckAAccentG = 0.85f; plugin.Configuration.DeckAAccentB = 0.95f;
            plugin.Configuration.DeckBAccentR = 1f; plugin.Configuration.DeckBAccentG = 0.6f; plugin.Configuration.DeckBAccentB = 0.15f;
            plugin.Configuration.BlendAccentR = 0.625f; plugin.Configuration.BlendAccentG = 0.725f; plugin.Configuration.BlendAccentB = 0.55f;
            plugin.Configuration.Save();
            ApplyDeckColors();
        }
        ImGui.TextDisabled("Changes your window border and every themed element on both decks live.");
        EndSettingsPanel();

        ImGui.Spacing();

        BeginSettingsPanel("VISUALIZER", leftWidth);
        ImGui.TextDisabled("DJ - your own deck displays and minimized box, doesn't affect what listeners see.");
        ImGui.Spacing();

        ImGui.TextDisabled("Style");
        var deckVisualizerStyle = plugin.Configuration.DeckVisualizerStyle;
        ImGui.SetNextItemWidth(220 * Scale);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.NeutralAccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.NeutralAccentActive);
        var djStyleChanged = ImGui.Combo("##dj", ref deckVisualizerStyle, ListenerVisualizerStyleNames, ListenerVisualizerStyleNames.Length);
        ImGui.PopStyleColor(3);
        if (djStyleChanged)
        {
            plugin.Configuration.DeckVisualizerStyle = deckVisualizerStyle;
            plugin.Configuration.Save();
        }

        ImGui.TextDisabled("Sensitivity");
        var deckVisualizerSensitivity = plugin.Configuration.DeckVisualizerSensitivity;
        if (SettingsSlider.Draw("##djVisualizerSensitivity", ref deckVisualizerSensitivity, 0.5f, 3f, 220 * Scale, "{0:F1}x", 1f))
        {
            plugin.Configuration.DeckVisualizerSensitivity = deckVisualizerSensitivity;
            plugin.Configuration.Save();
        }
        SettingsSlider.ResetOnRightClick(() =>
        {
            plugin.Configuration.DeckVisualizerSensitivity = 1f;
            plugin.Configuration.Save();
        });

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextDisabled("Listener - what you see while listening to someone else's show.");
        ImGui.Spacing();

        ImGui.TextDisabled("Style");
        var visualizerStyle = plugin.Configuration.ListenerVisualizerStyle;
        ImGui.SetNextItemWidth(220 * Scale);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.NeutralAccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.NeutralAccentActive);
        var listenerStyleChanged = ImGui.Combo("##listener", ref visualizerStyle, ListenerVisualizerStyleNames, ListenerVisualizerStyleNames.Length);
        ImGui.PopStyleColor(3);
        if (listenerStyleChanged)
        {
            plugin.Configuration.ListenerVisualizerStyle = visualizerStyle;
            plugin.Configuration.Save();
        }

        ImGui.TextDisabled("Sensitivity");
        var sensitivity = plugin.Configuration.ListenerVisualizerSensitivity;
        if (SettingsSlider.Draw("##listenerVisualizerSensitivity", ref sensitivity, 0.5f, 3f, 220 * Scale, "{0:F1}x", 1.6f))
        {
            plugin.Configuration.ListenerVisualizerSensitivity = sensitivity;
            plugin.Configuration.Save();
        }
        SettingsSlider.ResetOnRightClick(() =>
        {
            plugin.Configuration.ListenerVisualizerSensitivity = 1.6f;
            plugin.Configuration.Save();
        });
        ImGui.TextDisabled("How much the bars react to the music - higher animates more.");
        EndSettingsPanel();

        ImGui.EndGroup();
        var leftColumnHeight = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorPos(new Vector2(columnsStartPos.X + leftWidth + columnGap, columnsStartPos.Y));
        ImGui.BeginGroup();

        BeginSettingsPanel("BEHAVIOR", rightWidth);
        var muteWhenUnfocused = plugin.Configuration.MuteWhenUnfocused;
        if (SettingsToggle.Draw("##muteWhenUnfocused", "Mute EchoMix in background", ref muteWhenUnfocused))
        {
            plugin.Configuration.MuteWhenUnfocused = muteWhenUnfocused;
            plugin.Configuration.Save();
        }

        ImGui.Spacing();
        var showWelcomeOnEnable = plugin.Configuration.ShowWelcomeOnEnable;
        if (SettingsToggle.Draw("##showWelcomeOnEnable", "Show Welcome screen on plugin start", ref showWelcomeOnEnable))
        {
            plugin.Configuration.ShowWelcomeOnEnable = showWelcomeOnEnable;
            plugin.Configuration.Save();
        }
        ImGui.TextDisabled("The DJ/Listener choice you saw the first time you opened EchoMix.");
        EndSettingsPanel();

        ImGui.Spacing();
        BeginSettingsPanel("NOTIFICATIONS", rightWidth);
        ImGui.TextDisabled("Where the \"DJ went live\" toast (see Follow, in the DJ List) appears.");
        ImGui.Spacing();
        DrawToastAnchorPicker();

        ImGui.Spacing();
        var notifyOnListenerJoin = plugin.Configuration.NotifyOnListenerJoin;
        if (SettingsToggle.Draw("##notifyOnListenerJoin", "Show a toast when someone joins your show", ref notifyOnListenerJoin))
        {
            plugin.Configuration.NotifyOnListenerJoin = notifyOnListenerJoin;
            plugin.Configuration.Save();
        }
        ImGui.TextDisabled("Applies whether your show is public or private.");

        ImGui.Spacing();
        var autoJoinNotifications = plugin.Configuration.ListenerAutoJoinNotifications;
        if (SettingsToggle.Draw("##autoJoinNotifications", "Show a toast when auto-join joins/leaves a show", ref autoJoinNotifications))
        {
            plugin.Configuration.ListenerAutoJoinNotifications = autoJoinNotifications;
            plugin.Configuration.Save();
        }
        ImGui.TextDisabled("Only matters with Auto-Join Nearby Shows (Beta) turned on.");
        EndSettingsPanel();

        ImGui.Spacing();
        BeginSettingsPanel("AUDIO ENGINE", rightWidth);
        var connected = plugin.AudioHostClient.IsConnected;
        var statusColor = connected ? Theme.CyanAccent : Theme.OrangeAccent;
        var dotRadius = 4f * Scale;
        var dotCenter = ImGui.GetCursorScreenPos() + new Vector2(dotRadius, ImGui.GetTextLineHeight() / 2f);
        ImGui.GetWindowDrawList().AddCircleFilled(dotCenter, dotRadius, ImGui.GetColorU32(statusColor));
        ImGui.Dummy(new Vector2((dotRadius * 2f) + (6f * Scale), 0f));
        ImGui.SameLine();
        ImGui.TextColored(statusColor, connected ? "Connected" : "Connecting...");
        ImGui.TextDisabled("Runs as a separate background process, independent of the game's frame rate.");

        ImGui.Spacing();
        ImGui.TextDisabled("Master Volume");
        var masterVolume = plugin.AudioHostClient.LatestStatus.MasterVolume;
        if (SettingsSlider.Draw("##masterVolume", ref masterVolume, 0f, 2f, rightWidth, "{0:F2}x", 0.8f))
            plugin.AudioHostClient.Send(MessageType.SetMasterVolume, new SetMasterVolumeCommand { Volume = masterVolume });
        SettingsSlider.ResetOnRightClick(() =>
            plugin.AudioHostClient.Send(MessageType.SetMasterVolume, new SetMasterVolumeCommand { Volume = 0.8f }));
        ImGui.TextDisabled("This is in case it changes for any reason.");
        EndSettingsPanel();

        ImGui.Spacing();
        BeginSettingsPanel("COMMUNITY", rightWidth);
        DrawClickableLink("Join our Discord", DiscordBlurple, DiscordInviteUrl, "Failed to open Discord invite link");
        EndSettingsPanel();

        ImGui.EndGroup();
        var rightColumnHeight = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorPos(new Vector2(columnsStartPos.X, columnsStartPos.Y + MathF.Max(leftColumnHeight, rightColumnHeight)));
    }

    /// Plain colored text that opens a URL on click, with a hand cursor and underline on hover so it reads as
    /// a link despite ImGui having no native link widget.
    private static void DrawClickableLink(string label, Vector4 color, string url, string failureLogMessage)
    {
        var hovered = ImGui.IsMouseHoveringRect(ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + ImGui.CalcTextSize(label));
        ImGui.TextColored(color, label);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), ImGui.GetColorU32(color));

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error(ex, $"[EchoMix] {failureLogMessage}");
                }
            }
        }
    }

    /// A single pill-shaped chip - same rounded-rect-behind-text visual as DrawChipRow's genre/venue chips -
    /// that copies itself to the OS clipboard on click, with a hand cursor and a brighter fill on hover, plus
    /// a brief "Copied!" confirmation next to it.
    private void DrawClickToCopyChip(string label, Vector4 accent)
    {
        var padX = 8f * Scale;
        var padY = 3f * Scale;
        var lineHeight = ImGui.GetTextLineHeight() + (padY * 2f);
        var chipSize = new Vector2(ImGui.CalcTextSize(label).X + (padX * 2f), lineHeight);

        var pos = ImGui.GetCursorScreenPos();
        var hovered = ImGui.IsMouseHoveringRect(pos, pos + chipSize);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + chipSize, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, hovered ? 0.30f : 0.18f)), lineHeight / 2f);
        drawList.AddText(pos + new Vector2(padX, padY), ImGui.GetColorU32(accent), label);
        ImGui.Dummy(chipSize);

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                ImGui.SetClipboardText(label);
                djProfileNumberCopiedAt = ImGui.GetTime();
            }
        }

        if (djProfileNumberCopiedAt is { } copiedAt && ImGui.GetTime() - copiedAt < 1.5)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.CyanAccent, "Copied!");
        }
    }

    private static readonly string[] HousingAreaOptions = { "The Lavender Beds", "Mist", "The Goblet", "Shirogane", "Empyreum" };
    private static readonly string[] WardOptions = Enumerable.Range(1, 30).Select(n => n.ToString()).ToArray();
    private static readonly string[] PlotOptions = Enumerable.Range(1, 60).Select(n => n.ToString()).ToArray();

    private static readonly string[] ApartmentOptions = Enumerable.Range(1, 90).Select(n => n.ToString()).ToArray();
    private static readonly string[] VenueTypeLabels = { "House", "Apartment" };

    private static readonly (string DataCenter, string[] Worlds)[] DataCenters =
    {
        ("Aether", new[] { "Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren" }),
        ("Crystal", new[] { "Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera" }),
        ("Dynamis", new[] { "Halicarnassus", "Maduin", "Marilith", "Seraph" }),
        ("Primal", new[] { "Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros" }),
        ("Chaos", new[] { "Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan" }),
        ("Light", new[] { "Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark" }),
        ("Materia", new[] { "Bismarck", "Ravana", "Sephirot", "Sophia", "Zurvan" }),
    };

    private static readonly string[] DataCenterOptions = DataCenters.Select(dc => dc.DataCenter).ToArray();

    private const int MaxSavedVenues = 10;

    /// Clears World if it isn't one of newDataCenter's own worlds - shared by the Broadcast tab's venue
    /// fields and the profile editor's "add a venue" form, both of which pair a Data Center dropdown with a
    /// World dropdown filtered to it.
    private static string ResetWorldIfInvalidForDataCenter(string newDataCenter, string currentWorld)
    {
        var worldsInNewDc = DataCenters.FirstOrDefault(dc => dc.DataCenter == newDataCenter).Worlds;
        return worldsInNewDc != null && worldsInNewDc.Contains(currentWorld) ? currentWorld : string.Empty;
    }

    /// A themed dropdown (same FrameBg-blending Button color override the line-in device picker and playlist
    /// picker already use) offering a fixed set of choices plus "(none)" - returns whatever's now selected,
    /// which the caller compares against the buffer it passed in to know whether to persist a change, same
    /// one-frame-lag-tolerant pattern as everywhere else in this file that reacts to ImGui widget return
    /// values.
    private string DrawVenueDropdown(string id, string hint, string currentValue, IReadOnlyList<string> options, float width, string? selectedLabelPrefix = null, string clearLabel = "(none)")
    {
        ImGui.SetNextItemWidth(width);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.NeutralAccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.NeutralAccentActive);
        var previewLabel = string.IsNullOrEmpty(currentValue)
            ? hint
            : selectedLabelPrefix == null ? currentValue : $"{selectedLabelPrefix} {currentValue}";
        var comboOpen = ImGui.BeginCombo(id, previewLabel);
        ImGui.PopStyleColor(3);

        var result = currentValue;
        if (comboOpen)
        {
            if (ImGui.Selectable(clearLabel, string.IsNullOrEmpty(currentValue)))
                result = string.Empty;

            foreach (var option in options)
            {
                if (ImGui.Selectable(option, option == currentValue))
                    result = option;
            }

            ImGui.EndCombo();
        }

        return result;
    }

    /// Same combo widget/styling as DrawVenueDropdown, but keyed on Id rather than string equality - two
    /// saved venues could legitimately share a Name, so selection has to track which specific entry was
    /// picked, not just its display text.
    private string? DrawSavedVenuePicker(string id, IReadOnlyList<SavedVenueDto> venues, string? selectedId, float width)
    {
        ImGui.SetNextItemWidth(width);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.NeutralAccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.NeutralAccentActive);
        var selected = selectedId != null ? venues.FirstOrDefault(v => v.Id == selectedId) : null;
        var comboOpen = ImGui.BeginCombo(id, selected?.Name ?? "Manual Entry");
        ImGui.PopStyleColor(3);

        var result = selectedId;
        if (comboOpen)
        {
            if (ImGui.Selectable("Manual Entry", selectedId == null))
                result = null;

            foreach (var venue in venues)
            {
                if (ImGui.Selectable(venue.Name, venue.Id == selectedId))
                    result = venue.Id;
            }

            ImGui.EndCombo();
        }

        return result;
    }

    /// The Broadcast tab's venue-address section - only ever drawn for a publicly-listed show (see the
    /// "PUBLIC LISTING" panel's own call site), since a private show is never shown anywhere an address would
    /// matter (no browse grid card, no Lifestream Visit target).
    private void DrawVenueAddressSection(string characterName)
    {
        if (ownSavedVenuesCache == null && !ownSavedVenuesRequestSent && !string.IsNullOrEmpty(characterName))
        {
            ownSavedVenuesRequestSent = true;
            plugin.AudioHostClient.Send(MessageType.RequestDjProfiles, new RequestDjProfilesMessage { RequesterCharacterName = characterName });
        }

        if (ownSavedVenuesCache == null && ownSavedVenuesRequestSent && !ownSavedVenuesDetailRequestSent
            && plugin.AudioHostClient.LatestDjProfiles?.Profiles.FirstOrDefault(p => p.IsOwnProfile) is { } ownSummary)
        {
            ownSavedVenuesDetailRequestSent = true;
            plugin.AudioHostClient.Send(MessageType.GetDjProfileDetail, new GetDjProfileDetailMessage { Id = ownSummary.Id, RequesterCharacterName = characterName });
        }

        ImGui.TextDisabled(plugin.Configuration.IsProximityAudio
            ? "Proximity Audio requires an address so nearby listeners can find you:"
            : "Global Audio - venue address is optional (shown to listeners if set):");

        var selectedId = plugin.Configuration.LastSelectedSavedVenueId;
        var savedVenues = ownSavedVenuesCache;
        if (savedVenues != null && selectedId != null && !savedVenues.Any(v => v.Id == selectedId))
        {
            selectedId = null;
            plugin.Configuration.LastSelectedSavedVenueId = null;
            plugin.Configuration.Save();
        }

        if (savedVenues is { Count: > 0 })
        {
            var newSelectedId = DrawSavedVenuePicker("##savedVenuePicker", savedVenues, selectedId, 280 * Scale);
            if (newSelectedId != selectedId)
            {
                selectedId = newSelectedId;
                plugin.Configuration.LastSelectedSavedVenueId = selectedId;

                var picked = savedVenues.FirstOrDefault(v => v.Id == selectedId);
                if (picked != null)
                {
                    venueNameBuffer = picked.Name;
                    venueDataCenterBuffer = picked.DataCenter;
                    venueWorldBuffer = picked.World;
                    venueHousingAreaBuffer = picked.HousingArea;
                    venueWardBuffer = picked.Ward;
                    venuePlotBuffer = picked.Plot;
                    venueIsApartmentBuffer = picked.IsApartment;
                    venueSubdivisionBuffer = picked.Subdivision;
                    plugin.Configuration.LastVenueName = venueNameBuffer;
                    plugin.Configuration.LastVenueDataCenter = venueDataCenterBuffer;
                    plugin.Configuration.LastVenueWorld = venueWorldBuffer;
                    plugin.Configuration.LastVenueHousingArea = venueHousingAreaBuffer;
                    plugin.Configuration.LastVenueWard = venueWardBuffer;
                    plugin.Configuration.LastVenuePlot = venuePlotBuffer;
                    plugin.Configuration.LastVenueIsApartment = venueIsApartmentBuffer;
                    plugin.Configuration.LastVenueSubdivision = venueSubdivisionBuffer;
                }

                plugin.Configuration.Save();
            }

            ImGui.TextDisabled("Manage your saved venues from your DJ Profile > Edit Listing > Saved Venues.");
            ImGui.Spacing();
        }

        var locked = selectedId != null;
        ImGui.BeginDisabled(locked);

        ImGui.SetNextItemWidth(280 * Scale);
        if (ImGui.InputTextWithHint("##venueName", "Venue name", ref venueNameBuffer, 64) && !locked)
        {
            plugin.Configuration.LastVenueName = venueNameBuffer.Trim();
            plugin.Configuration.Save();
        }

        var venueTypeIndex = venueIsApartmentBuffer ? 1 : 0;
        if (SettingsSegmented.Draw("##venueType", VenueTypeLabels, ref venueTypeIndex, 280 * Scale) && !locked)
        {
            venueIsApartmentBuffer = venueTypeIndex == 1;
            if (!venueIsApartmentBuffer)
                venueSubdivisionBuffer = false;
            plugin.Configuration.LastVenueIsApartment = venueIsApartmentBuffer;
            plugin.Configuration.LastVenueSubdivision = venueSubdivisionBuffer;
            plugin.Configuration.Save();
        }
        ImGui.Spacing();

        var halfWidth = 137f * Scale;
        var newDataCenter = DrawVenueDropdown("##venueDataCenter", "Data Center", venueDataCenterBuffer, DataCenterOptions, halfWidth);
        if (!locked && newDataCenter != venueDataCenterBuffer)
        {
            venueDataCenterBuffer = newDataCenter;
            venueWorldBuffer = ResetWorldIfInvalidForDataCenter(venueDataCenterBuffer, venueWorldBuffer);
            plugin.Configuration.LastVenueDataCenter = venueDataCenterBuffer;
            plugin.Configuration.LastVenueWorld = venueWorldBuffer;
            plugin.Configuration.Save();
        }

        ImGui.SameLine();
        var worldOptions = DataCenters.FirstOrDefault(dc => dc.DataCenter == venueDataCenterBuffer).Worlds ?? Array.Empty<string>();
        ImGui.BeginDisabled(locked || string.IsNullOrEmpty(venueDataCenterBuffer));
        var newWorld = DrawVenueDropdown("##venueWorld", "World", venueWorldBuffer, worldOptions, halfWidth);
        ImGui.EndDisabled();
        if (!locked && newWorld != venueWorldBuffer)
        {
            venueWorldBuffer = newWorld;
            plugin.Configuration.LastVenueWorld = venueWorldBuffer;
            plugin.Configuration.Save();
        }

        var newHousingArea = DrawVenueDropdown("##venueHousingArea", "Housing Area", venueHousingAreaBuffer, HousingAreaOptions, 280 * Scale);
        if (!locked && newHousingArea != venueHousingAreaBuffer)
        {
            venueHousingAreaBuffer = newHousingArea;
            plugin.Configuration.LastVenueHousingArea = venueHousingAreaBuffer;
            plugin.Configuration.Save();
        }

        var newWard = DrawVenueDropdown("##venueWard", "Ward", venueWardBuffer, WardOptions, halfWidth, selectedLabelPrefix: "Ward");
        if (!locked && newWard != venueWardBuffer)
        {
            venueWardBuffer = newWard;
            plugin.Configuration.LastVenueWard = venueWardBuffer;
            plugin.Configuration.Save();
        }

        ImGui.SameLine();
        var newPlot = venueIsApartmentBuffer
            ? DrawVenueDropdown("##venueApartment", "Apartment #", venuePlotBuffer, ApartmentOptions, halfWidth, selectedLabelPrefix: "Apartment")
            : DrawVenueDropdown("##venuePlot", "Plot", venuePlotBuffer, PlotOptions, halfWidth, selectedLabelPrefix: "Plot");
        if (!locked && newPlot != venuePlotBuffer)
        {
            venuePlotBuffer = newPlot;
            plugin.Configuration.LastVenuePlot = venuePlotBuffer;
            plugin.Configuration.Save();
        }

        if (venueIsApartmentBuffer)
        {
            ImGui.Spacing();
            if (SettingsToggle.Draw("##venueSubdivision", "Subdivision", ref venueSubdivisionBuffer,
                    "Some housing areas have a second, separate apartment building added as a subdivision - enable this if that's the one you mean.") && !locked)
            {
                plugin.Configuration.LastVenueSubdivision = venueSubdivisionBuffer;
                plugin.Configuration.Save();
            }
        }

        ImGui.EndDisabled();
    }

    /// Builds its own option list from whatever distinct genre values are actually present in `allGenreLists`
    /// right now (case-insensitively deduped, first-seen casing kept - same dedup rule DrawTagListEditor
    /// already applies when a DJ enters their own genres), further narrowed to ones MusicGenres.IsKnown
    /// recognizes - a DJ's own Genres tags stay unrestricted free text (see MusicGenres' own doc comment),
    /// but a filter offering every one-off joke tag anyone's ever typed ("Nonsense Mostly") stopped being a
    /// useful "browse by genre" tool.
    private string DrawGenreFilter(string id, IReadOnlyList<string> options, string currentFilter, float width) =>
        DrawVenueDropdown(id, "All Genres", currentFilter, options, width, clearLabel: "All Genres");

    /// Distinct known genres across every list in allGenreLists, grouped case-insensitively (first-seen
    /// casing kept) and sorted - the actual computation DrawGenreFilter's options list needs, factored out so
    /// callers can cache it instead of rebuilding it every single frame.
    private static List<string> ComputeGenreFilterOptions(IEnumerable<IEnumerable<string>> allGenreLists) =>
        allGenreLists
            .SelectMany(g => g)
            .Where(g => !string.IsNullOrWhiteSpace(g) && MusicGenres.IsKnown(g))
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static readonly (ToastAnchor Anchor, string Label, string FullName)[] ToastAnchorGrid =
    {
        (ToastAnchor.TopLeft, "TL", "Top Left"), (ToastAnchor.TopCenter, "TC", "Top Center"), (ToastAnchor.TopRight, "TR", "Top Right"),
        (ToastAnchor.MiddleLeft, "ML", "Middle Left"), (ToastAnchor.MiddleCenter, "MC", "Middle Center"), (ToastAnchor.MiddleRight, "MR", "Middle Right"),
        (ToastAnchor.BottomLeft, "BL", "Bottom Left"), (ToastAnchor.BottomCenter, "BC", "Bottom Center"), (ToastAnchor.BottomRight, "BR", "Bottom Right"),
    };

    /// A 3x3 grid mirroring the actual screen layout (same row/column meaning as where each cell will place
    /// the toast) - clicking a cell both sets Configuration.FollowToastAnchor and immediately fires a real
    /// preview toast there (FollowNotificationToast.Show with placeholder content, not a separate mock), so
    /// what's previewed is exactly what a real notification will look like.
    private void DrawToastAnchorPicker()
    {
        var current = plugin.Configuration.FollowToastAnchor;
        var cellSize = new Vector2(50, 32) * Scale;
        var gap = 4f * Scale;

        for (var i = 0; i < ToastAnchorGrid.Length; i++)
        {
            var (anchor, label, fullName) = ToastAnchorGrid[i];
            if (i % 3 != 0)
                ImGui.SameLine(0, gap);
            else if (i > 0)
                ImGui.Spacing();

            var isSelected = anchor == current;
            var accent = isSelected ? Theme.CyanAccent : Theme.NeutralAccent;
            if (PanelButton.Draw($"##toastAnchor{anchor}", null, null, label, cellSize, accent))
            {
                plugin.Configuration.FollowToastAnchor = anchor;
                plugin.Configuration.Save();
                plugin.FollowNotificationToast.Show("Example DJ", "DEMO1");
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(fullName);
        }

        ImGui.Spacing();
        var previewSize = new Vector2(160, 26) * Scale;
        if (PanelButton.Draw("##previewToastAnchor", plugin.Fonts.Icon, FontAwesomeIcon.Eye, "Preview", previewSize, Theme.NeutralAccent))
            plugin.FollowNotificationToast.Show("Example DJ", "DEMO1");
    }

    /// Turns this DJ into a Host: registers a room on the relay and starts streaming the mix to it.
    private void DrawBroadcastTab()
    {
        var broadcast = plugin.AudioHostClient.LatestStatus.Broadcast;
        var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

        var avail = ImGui.GetContentRegionAvail().X;
        var columnGap = 24f * Scale;
        var leftWidth = MathF.Min(SettingsPanelWidth * Scale, (avail - columnGap) * 0.5f);
        var rightWidth = avail - leftWidth - columnGap;
        var columnsStartPos = ImGui.GetCursorPos();

        ImGui.SetCursorPos(columnsStartPos);
        ImGui.BeginGroup();

        BeginSettingsPanel("GO LIVE", leftWidth);

        if (broadcast.IsLive)
        {
            ImGui.TextColored(Theme.CyanAccent, $"You're live - room code: {broadcast.RoomCode}");
            ImGui.TextDisabled($"{broadcast.ListenerCount} listener(s) connected.");
            ImGui.Spacing();

            if (PanelButton.Draw("##copyRoomCode", plugin.Fonts.Icon, FontAwesomeIcon.Copy, "Copy Room Code", new Vector2(180, 28) * Scale, Theme.NeutralAccent))
            {
                ImGui.SetClipboardText(plugin.Configuration.IsPubliclyListed
                    ? $"{broadcast.RoomCode} (publicly listed - no password needed)"
                    : $"{broadcast.RoomCode} (password protected - send them the password separately)");
            }

            ImGui.SameLine();
            if (PanelButton.Draw("##endBroadcast", plugin.Fonts.Icon, FontAwesomeIcon.Stop, "End Broadcast", new Vector2(150, 28) * Scale, Theme.OrangeAccent))
                plugin.AudioHostClient.Send(MessageType.StopBroadcast, new object());

            ImGui.Spacing();
            ImGui.Spacing();

            var webListenEnabled = !string.IsNullOrEmpty(broadcast.WebListenUrl);
            if (SettingsToggle.Draw("##webListenLink", "Web Listen Link",
                    ref webListenEnabled, "Generates a link anyone can open in a browser - or any other program can tap into directly - to hear this show, no game or plugin required."))
            {
                plugin.AudioHostClient.Send(MessageType.SetWebListenLink, new SetWebListenLinkCommand { Enabled = webListenEnabled });
            }

            if (!string.IsNullOrEmpty(broadcast.WebListenUrl))
            {
                ImGui.Spacing();
                ImGui.TextColored(Theme.CyanAccent, broadcast.WebListenUrl);
                if (PanelButton.Draw("##copyWebListenLink", plugin.Fonts.Icon, FontAwesomeIcon.Copy, "Copy Link", new Vector2(140, 26) * Scale, Theme.NeutralAccent))
                    ImGui.SetClipboardText(broadcast.WebListenUrl);

                if (plugin.Configuration.IsProximityAudio)
                {
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (320f * Scale));
                    ImGui.TextColored(Theme.OrangeAccent, "Proximity Audio is on - anyone with this link hears the mix regardless of in-game distance.");
                    ImGui.PopTextWrapPos();
                }
            }
        }
        else if (broadcast.IsHostReconnecting)
        {
            ImGui.TextColored(Theme.OrangeAccent, $"Reconnecting to room {broadcast.RoomCode}...");
            ImGui.Spacing();
            if (PanelButton.Draw("##endBroadcastWhileReconnecting", plugin.Fonts.Icon, FontAwesomeIcon.Stop, "Give Up & Stop", new Vector2(180, 28) * Scale, Theme.OrangeAccent))
                plugin.AudioHostClient.Send(MessageType.StopBroadcast, new object());
        }
        else
        {
            var placeholder = string.IsNullOrEmpty(characterName) ? "DJ name" : $"DJ name (defaults to {characterName})";

            ImGui.SetNextItemWidth(240 * Scale);
            ImGui.InputTextWithHint("##djName", placeholder, ref broadcastDjNameBuffer, 32);
            ImGui.SetNextItemWidth(240 * Scale);
            var isPubliclyListed = plugin.Configuration.IsPubliclyListed;
            ImGui.InputTextWithHint("##broadcastPassword", isPubliclyListed ? "Password (not needed - publicly listed)" : "Password (required)",
                ref broadcastPasswordBuffer, 32, ImGuiInputTextFlags.Password);
            ImGui.SetNextItemWidth(240 * Scale);
            ImGui.InputTextWithHint("##broadcastHostPassword", "Host password, for co-DJs (required)", ref broadcastHostPasswordBuffer, 32, ImGuiInputTextFlags.Password);
            ImGui.SetNextItemWidth(240 * Scale);
            ImGui.InputTextWithHint("##vanityRoomCode", "Custom room code (optional)", ref broadcastRoomCodeBuffer, 16);
            ImGui.TextDisabled("Leave the room code blank for a random one.");

            ImGui.Spacing();
            var proximityAddressComplete = !(plugin.Configuration.IsProximityAudio && isPubliclyListed)
                || (!string.IsNullOrWhiteSpace(venueDataCenterBuffer) && !string.IsNullOrWhiteSpace(venueWorldBuffer)
                    && !string.IsNullOrWhiteSpace(venueHousingAreaBuffer) && !string.IsNullOrWhiteSpace(venueWardBuffer)
                    && !string.IsNullOrWhiteSpace(venuePlotBuffer));
            var canGoLive = !string.IsNullOrWhiteSpace(broadcastHostPasswordBuffer)
                && (isPubliclyListed || !string.IsNullOrWhiteSpace(broadcastPasswordBuffer))
                && proximityAddressComplete;
            if (PanelButton.Draw("##goLive", plugin.Fonts.Icon, FontAwesomeIcon.BroadcastTower, "Go Live", new Vector2(160, 32) * Scale, Theme.NeutralAccent)
                && canGoLive)
            {
                var djName = string.IsNullOrWhiteSpace(broadcastDjNameBuffer) ? characterName : broadcastDjNameBuffer.Trim();
                plugin.Configuration.HostDisplayName = broadcastDjNameBuffer.Trim();
                plugin.Configuration.LastVanityRoomCode = broadcastRoomCodeBuffer.Trim();
                plugin.Configuration.LastShowName = publicShowNameBuffer.Trim();
                plugin.Configuration.LastVenueName = venueNameBuffer.Trim();
                plugin.Configuration.LastVenueDataCenter = venueDataCenterBuffer.Trim();
                plugin.Configuration.LastVenueWorld = venueWorldBuffer.Trim();
                plugin.Configuration.LastVenueHousingArea = venueHousingAreaBuffer.Trim();
                plugin.Configuration.LastVenueWard = venueWardBuffer.Trim();
                plugin.Configuration.LastVenuePlot = venuePlotBuffer.Trim();
                plugin.Configuration.LastVenueIsApartment = venueIsApartmentBuffer;
                plugin.Configuration.LastVenueSubdivision = venueSubdivisionBuffer;
                plugin.Configuration.Save();

                plugin.AudioHostClient.Send(MessageType.StartBroadcast, new StartBroadcastCommand
                {
                    RoomCode = string.IsNullOrWhiteSpace(broadcastRoomCodeBuffer) ? null : broadcastRoomCodeBuffer.Trim(),
                    Password = broadcastPasswordBuffer,
                    HostPassword = broadcastHostPasswordBuffer,
                    DjName = djName,
                    CharacterName = characterName,
                    IsProximityAudio = plugin.Configuration.IsProximityAudio,
                    ProximityRange = plugin.Configuration.ProximityRange,
                    IsPubliclyListed = isPubliclyListed,
                    ShowName = publicShowNameBuffer.Trim(),
                    VenueName = venueNameBuffer.Trim(),
                    VenueDataCenter = venueDataCenterBuffer.Trim(),
                    VenueWorld = venueWorldBuffer.Trim(),
                    VenueHousingArea = venueHousingAreaBuffer.Trim(),
                    VenueWard = venueWardBuffer.Trim(),
                    VenuePlot = venuePlotBuffer.Trim(),
                    VenueIsApartment = venueIsApartmentBuffer,
                    VenueSubdivision = venueSubdivisionBuffer,
                });
            }

            if (!canGoLive)
            {
                string reason;
                if (string.IsNullOrWhiteSpace(broadcastHostPasswordBuffer))
                    reason = isPubliclyListed ? "A host password is required before you can go live." : "Both passwords are required before you can go live.";
                else if (!isPubliclyListed && string.IsNullOrWhiteSpace(broadcastPasswordBuffer))
                    reason = "Both passwords are required before you can go live.";
                else
                    reason = "Proximity Audio requires a full address (Data Center, World, Housing Area, Ward, Plot) before you can go live.";
                ImGui.TextColored(Theme.OrangeAccent, reason);
            }
        }

        if (!string.IsNullOrEmpty(broadcast.BroadcastError))
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.OrangeAccent, broadcast.BroadcastError);
        }

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextDisabled("Toggle Proximity vs Global Audio from the icon next to the lock button in the header.");
        EndSettingsPanel();

        ImGui.Spacing();
        BeginSettingsPanel("PUBLIC LISTING", leftWidth);
        {
            var isPubliclyListed = plugin.Configuration.IsPubliclyListed;
            if (SettingsToggle.Draw("##isPubliclyListed", "List this show in \"View Live Shows\"", ref isPubliclyListed))
            {
                plugin.Configuration.IsPubliclyListed = isPubliclyListed;
                plugin.Configuration.Save();
            }
            ImGui.TextDisabled("Lets any listener browse straight in - no room code or password needed.");
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (320f * Scale));
            ImGui.TextColored(Theme.OrangeAccent, "This is for legitimate shows and venues only - misuse (harassment, offensive content, impersonation, etc.) will get your access to public listing revoked.");
            ImGui.PopTextWrapPos();

            if (isPubliclyListed)
            {
                ImGui.Spacing();
                ImGui.SetNextItemWidth(280 * Scale);
                if (ImGui.InputTextWithHint("##publicShowName", "Show name (optional - defaults to your DJ name)", ref publicShowNameBuffer, 48))
                {
                    plugin.Configuration.LastShowName = publicShowNameBuffer.Trim();
                    plugin.Configuration.Save();

                    if (broadcast.IsLive)
                    {
                        plugin.AudioHostClient.Send(MessageType.SetShowName, new SetShowNameCommand
                        {
                            ShowName = publicShowNameBuffer.Trim(),
                        });
                    }
                }
                if (broadcast.IsLive)
                    ImGui.TextDisabled("Updates your live show's name right away.");

                ImGui.Spacing();
                DrawVenueAddressSection(characterName);

                ImGui.Spacing();
                ImGui.Spacing();
                if (PanelButton.Draw("##uploadShowImage", plugin.Fonts.Icon, FontAwesomeIcon.Upload, "Upload Image", new Vector2(160, 28) * Scale, Theme.NeutralAccent))
                    OpenShowImageDialog();
                ImGui.TextDisabled($"Recommended: {ShowImageProcessor.TargetWidth}x{ShowImageProcessor.TargetHeight} (16:9) - anything else gets auto-cropped/resized.");
                if (!broadcast.IsLive)
                    ImGui.TextDisabled("Uploads automatically the moment you go live.");

                if (showImagePreview != null)
                {
                    ImGui.Spacing();
                    var previewWidth = 280f * Scale;
                    var previewHeight = previewWidth * (ShowImageProcessor.TargetHeight / (float)ShowImageProcessor.TargetWidth);
                    ImGui.Image(showImagePreview.Handle, new Vector2(previewWidth, previewHeight));
                }

                if (!string.IsNullOrEmpty(showImageError))
                    ImGui.TextColored(Theme.OrangeAccent, showImageError);
            }
        }
        EndSettingsPanel();

        ImGui.EndGroup();
        var leftColumnHeight = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorPos(new Vector2(columnsStartPos.X + leftWidth + columnGap, columnsStartPos.Y));
        ImGui.BeginGroup();

        BeginSettingsPanel("SONG PREVIEW", rightWidth);

        ImGui.TextDisabled("Current Track Dampen %");
        var dampenPercent = plugin.Configuration.PreviewDampenVolume * 100f;
        if (SettingsSlider.Draw("##previewDampen", ref dampenPercent, 0f, 100f, 220 * Scale, "{0:F0}%", 50f))
        {
            plugin.Configuration.PreviewDampenVolume = dampenPercent / 100f;
            plugin.Configuration.Save();
            plugin.AudioHostClient.Send(MessageType.SetPreviewDampenVolume, new SetPreviewDampenVolumeCommand { Volume = plugin.Configuration.PreviewDampenVolume });
        }
        SettingsSlider.ResetOnRightClick(() =>
        {
            plugin.Configuration.PreviewDampenVolume = 0.5f;
            plugin.Configuration.Save();
            plugin.AudioHostClient.Send(MessageType.SetPreviewDampenVolume, new SetPreviewDampenVolumeCommand { Volume = plugin.Configuration.PreviewDampenVolume });
        });
        ImGui.TextDisabled("How much your current track quiets down for you (never for listeners) while you preview an upcoming song.");

        ImGui.Spacing();
        ImGui.TextDisabled("Preview Song Volume %");
        var previewPercent = plugin.Configuration.PreviewVolume * 100f;
        if (SettingsSlider.Draw("##previewVolume", ref previewPercent, 0f, 150f, 220 * Scale, "{0:F0}%", 60f))
        {
            plugin.Configuration.PreviewVolume = previewPercent / 100f;
            plugin.Configuration.Save();
            plugin.AudioHostClient.Send(MessageType.SetPreviewVolume, new SetPreviewVolumeCommand { Volume = plugin.Configuration.PreviewVolume });
        }
        SettingsSlider.ResetOnRightClick(() =>
        {
            plugin.Configuration.PreviewVolume = 0.6f;
            plugin.Configuration.Save();
            plugin.AudioHostClient.Send(MessageType.SetPreviewVolume, new SetPreviewVolumeCommand { Volume = plugin.Configuration.PreviewVolume });
        });
        ImGui.TextDisabled("How loud the previewed song plays for you. Middle-click an upcoming song in the Deck view to try it.");
        EndSettingsPanel();

        ImGui.Spacing();
        BeginSettingsPanel("SONG REQUESTS", rightWidth);
        ImGui.TextDisabled("Controls who can send you a song request while you're live - see the Requests tab next to your Playlist.");
        ImGui.Spacing();

        var accessMode = plugin.Configuration.SongRequestAccessMode;
        var accessModeIndex = (int)accessMode;
        if (SettingsSegmented.Draw("##songRequestAccessMode", SongRequestAccessModeLabels, ref accessModeIndex, 260 * Scale))
            SetSongRequestAccessMode((SongRequestAccessMode)accessModeIndex);

        if (accessMode != SongRequestAccessMode.Open)
        {
            ImGui.Spacing();
            var list = accessMode == SongRequestAccessMode.Whitelist ? plugin.Configuration.SongRequestWhitelist : plugin.Configuration.SongRequestBlacklist;
            var emptyHint = accessMode == SongRequestAccessMode.Whitelist ? "Nobody's allowed yet - add a character name below." : "Nobody's blocked.";

            ImGui.SetNextItemWidth(200 * Scale);
            ImGui.InputTextWithHint("##songRequestNameEntry", "Character name", ref songRequestNameBuffer, 32);
            ImGui.SameLine();
            if (PanelButton.Draw("##songRequestNameAdd", plugin.Fonts.Icon, FontAwesomeIcon.Plus, null, new Vector2(30, 24) * Scale, Theme.NeutralAccent)
                && !string.IsNullOrWhiteSpace(songRequestNameBuffer))
            {
                var name = songRequestNameBuffer.Trim();
                if (!list.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(name);
                    plugin.Configuration.Save();
                    SendSongRequestAccessControl();
                }
                songRequestNameBuffer = string.Empty;
            }

            ImGui.Spacing();
            if (list.Count == 0)
            {
                ImGui.TextDisabled(emptyHint);
            }
            else
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    ImGui.PushID(i);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(list[i]);
                    var removeSize = new Vector2(24, 22) * Scale;
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - removeSize.X + ImGui.GetCursorPosX());
                    if (PanelButton.Draw("##songRequestNameRemove", plugin.Fonts.Icon, FontAwesomeIcon.Times, null, removeSize, Theme.OrangeAccent))
                    {
                        list.RemoveAt(i);
                        plugin.Configuration.Save();
                        SendSongRequestAccessControl();
                    }
                    ImGui.PopID();
                }
            }
        }
        EndSettingsPanel();

        var externalInputClient = plugin.AudioHostClient;
        var externalInputMode = externalInputClient.LatestStatus.ExternalInputMode;
        var externalInputDevices = externalInputClient.LatestAudioInputDevices;

        ImGui.Spacing();
        BeginSettingsPanel("LINE IN DEVICE", rightWidth);
        ImGui.TextDisabled("Pick the Windows recording device your own mixer or mixing software");
        ImGui.TextDisabled("sends audio to - or capture a whole application's own audio instead,");
        ImGui.TextDisabled("the same way OBS's Window Capture audio source does.");
        ImGui.Spacing();

        var sourceModeIndex = plugin.Configuration.ExternalInputUseProcessCapture ? 1 : 0;
        if (SettingsSegmented.Draw("##externalInputSourceMode", LineInSourceModeLabels, ref sourceModeIndex, 220 * Scale))
        {
            plugin.Configuration.ExternalInputUseProcessCapture = sourceModeIndex == 1;
            plugin.Configuration.Save();
        }
        ImGui.Spacing();

        if (plugin.Configuration.ExternalInputUseProcessCapture)
        {
            var externalInputProcesses = externalInputClient.LatestCapturableProcesses;
            var selectedProcessName = plugin.Configuration.ExternalInputProcessName;
            var selectedProcessDisplay = externalInputProcesses.FirstOrDefault(p => p.ProcessName == selectedProcessName)?.DisplayName
                ?? plugin.Configuration.ExternalInputProcessDisplayName;

            ImGui.SetNextItemWidth(320 * Scale);
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Panel);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.NeutralAccentHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.NeutralAccentActive);
            var processComboOpen = ImGui.BeginCombo("##externalInputProcess", string.IsNullOrEmpty(selectedProcessName) ? "(none)" : selectedProcessDisplay ?? "Select an application...");
            ImGui.PopStyleColor(3);
            if (processComboOpen)
            {
                if (ImGui.Selectable("(none)", string.IsNullOrEmpty(selectedProcessName)))
                {
                    plugin.Configuration.ExternalInputProcessName = null;
                    plugin.Configuration.ExternalInputProcessDisplayName = null;
                    plugin.Configuration.Save();
                }

                if (externalInputProcesses.Count == 0)
                    ImGui.TextDisabled("No capturable applications found - it needs a visible window.");

                foreach (var process in externalInputProcesses)
                {
                    if (ImGui.Selectable(process.DisplayName, process.ProcessName == selectedProcessName))
                    {
                        plugin.Configuration.ExternalInputProcessName = process.ProcessName;
                        plugin.Configuration.ExternalInputProcessDisplayName = process.DisplayName;
                        plugin.Configuration.Save();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();
            if (PanelButton.Draw("##refreshCapturableProcesses", plugin.Fonts.Icon, FontAwesomeIcon.Sync, "Refresh", new Vector2(90, 28) * Scale, Theme.NeutralAccent))
                externalInputClient.Send(MessageType.RequestCapturableProcesses, new object());
        }
        else
        {
            var selectedId = plugin.Configuration.ExternalInputDeviceId;
            var selectedName = externalInputDevices.FirstOrDefault(d => d.Id == selectedId)?.Name ?? plugin.Configuration.ExternalInputDeviceName;

            ImGui.SetNextItemWidth(320 * Scale);
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Panel);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.NeutralAccentHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.NeutralAccentActive);
            var comboOpen = ImGui.BeginCombo("##externalInputDevice", string.IsNullOrEmpty(selectedId) ? "(none)" : selectedName ?? "Select a device...");
            ImGui.PopStyleColor(3);
            if (comboOpen)
            {
                if (ImGui.Selectable("(none)", string.IsNullOrEmpty(selectedId)))
                {
                    plugin.Configuration.ExternalInputDeviceId = null;
                    plugin.Configuration.ExternalInputDeviceName = null;
                    plugin.Configuration.Save();
                }

                if (externalInputDevices.Count == 0)
                    ImGui.TextDisabled("No recording devices found.");

                foreach (var device in externalInputDevices)
                {
                    if (ImGui.Selectable(device.Name, device.Id == selectedId))
                    {
                        plugin.Configuration.ExternalInputDeviceId = device.Id;
                        plugin.Configuration.ExternalInputDeviceName = device.Name;
                        plugin.Configuration.Save();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();
            if (PanelButton.Draw("##refreshInputDevices", plugin.Fonts.Icon, FontAwesomeIcon.Sync, "Refresh", new Vector2(90, 28) * Scale, Theme.NeutralAccent))
                externalInputClient.Send(MessageType.RequestAudioInputDevices, new object());
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Second device (optional) - some controllers/software route separate");
        ImGui.TextDisabled("decks to separate devices; picking one here captures both and sums");
        ImGui.TextDisabled("them together, with Deck B's own dials leveling this one independently.");
        ImGui.Spacing();

        var selectedId2 = plugin.Configuration.ExternalInputDeviceId2;
        var selectedName2 = externalInputDevices.FirstOrDefault(d => d.Id == selectedId2)?.Name ?? plugin.Configuration.ExternalInputDeviceName2;

        ImGui.SetNextItemWidth(320 * Scale);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.NeutralAccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.NeutralAccentActive);
        var comboOpen2 = ImGui.BeginCombo("##externalInputDevice2", string.IsNullOrEmpty(selectedId2) ? "(none)" : selectedName2 ?? "Select a device...");
        ImGui.PopStyleColor(3);
        if (comboOpen2)
        {
            if (ImGui.Selectable("(none)", string.IsNullOrEmpty(selectedId2)))
            {
                plugin.Configuration.ExternalInputDeviceId2 = null;
                plugin.Configuration.ExternalInputDeviceName2 = null;
                plugin.Configuration.Save();
            }

            foreach (var device in externalInputDevices)
            {
                if (ImGui.Selectable(device.Name, device.Id == selectedId2))
                {
                    plugin.Configuration.ExternalInputDeviceId2 = device.Id;
                    plugin.Configuration.ExternalInputDeviceName2 = device.Name;
                    plugin.Configuration.Save();
                }
            }

            ImGui.EndCombo();
        }
        EndSettingsPanel();

        ImGui.Spacing();
        BeginSettingsPanel("LINE IN MONITORING", rightWidth);
        ImGui.TextDisabled("EchoMix broadcasts this device without playing it back through your own");
        ImGui.TextDisabled("speakers - you're already hearing your real mix through your own gear.");
        ImGui.TextDisabled("Sound pads still play (and are heard) locally as normal.");

        if (!string.IsNullOrEmpty(externalInputMode.Error))
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.OrangeAccent, externalInputMode.Error);
        }
        EndSettingsPanel();

        ImGui.EndGroup();
        var rightColumnHeight = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorPos(new Vector2(columnsStartPos.X, columnsStartPos.Y + MathF.Max(leftColumnHeight, rightColumnHeight)));
    }

    private static readonly string[] SongRequestAccessModeLabels = { "Anyone", "Whitelist", "Blacklist" };

    private static readonly string[] LineInSourceModeLabels = { "Device", "Application" };

    private void SetSongRequestAccessMode(SongRequestAccessMode mode)
    {
        plugin.Configuration.SongRequestAccessMode = mode;
        plugin.Configuration.Save();
        SendSongRequestAccessControl();
    }

    private void SendSongRequestAccessControl() =>
        plugin.AudioHostClient.Send(MessageType.SetSongRequestAccessControl, new SetSongRequestAccessControlCommand
        {
            Mode = plugin.Configuration.SongRequestAccessMode,
            Whitelist = plugin.Configuration.SongRequestWhitelist,
            Blacklist = plugin.Configuration.SongRequestBlacklist,
        });

    /// Joins someone else's show as a Listener - the window itself switches to the compact Listener view
    /// automatically once AudioHost's connection actually comes up (see UpdateListenerViewTransition), not
    /// from anything clicked here.
    private static readonly string[] ListenJoinModeLabels = { "Listen", "Join as DJ" };

    private void DrawListenTab()
    {
        var broadcast = plugin.AudioHostClient.LatestStatus.Broadcast;
        BeginSettingsPanel("JOIN A SHOW", SettingsPanelWidth * Scale);

        var autoJoin = plugin.Configuration.ListenerAutoJoinNearbyShows;

        if (broadcast.IsListening)
        {
            var isAutoJoined = broadcast.RoomCode != null && broadcast.RoomCode == plugin.AutoJoinTracker.AutoJoinedRoomCode;
            ImGui.TextColored(Theme.CyanAccent, isAutoJoined ? $"Connected to {broadcast.HostDjName} (Auto-joined)" : $"Connected to {broadcast.HostDjName}");
            ImGui.TextDisabled(broadcast.IsProximityAudio ? "Proximity Audio - volume follows your distance from the DJ." : "Global Audio.");
            ImGui.Spacing();
            if (PanelButton.Draw("##disconnect", plugin.Fonts.Icon, FontAwesomeIcon.SignOutAlt, "Disconnect", new Vector2(140, 28) * Scale, Theme.OrangeAccent))
            {
                plugin.NotifyManualDisconnectRequested();
                plugin.AudioHostClient.Send(MessageType.DisconnectFromRemote, new object());
            }
        }
        else if (broadcast.IsLive)
        {
            ImGui.TextDisabled("You're already live - see the Broadcast tab.");
        }
        else
        {
            ImGui.BeginDisabled(autoJoin);

            var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

            var joinModeIndex = joinAsDjMode ? 1 : 0;
            if (SettingsSegmented.Draw("##joinMode", ListenJoinModeLabels, ref joinModeIndex, 220 * Scale))
                joinAsDjMode = joinModeIndex == 1;
            ImGui.Spacing();

            ImGui.SetNextItemWidth(160 * Scale);
            ImGui.InputTextWithHint("##connectRoomCode", "Room code", ref connectRoomCodeBuffer, 16);

            if (joinAsDjMode)
            {
                var djPlaceholder = string.IsNullOrEmpty(characterName) ? "DJ name" : $"DJ name (defaults to {characterName})";
                ImGui.SetNextItemWidth(160 * Scale);
                ImGui.InputTextWithHint("##connectDjName", djPlaceholder, ref connectDjNameBuffer, 32);
                ImGui.SetNextItemWidth(160 * Scale);
                ImGui.InputTextWithHint("##connectHostPassword", "Host password", ref connectHostPasswordBuffer, 32, ImGuiInputTextFlags.Password);
            }
            else
            {
                ImGui.SetNextItemWidth(160 * Scale);
                ImGui.InputTextWithHint("##connectPassword", "Password (if set)", ref connectPasswordBuffer, 32, ImGuiInputTextFlags.Password);
            }

            ImGui.Spacing();
            var connectLabel = joinAsDjMode ? "Join as DJ" : "Connect";
            var canConnect = !string.IsNullOrWhiteSpace(connectRoomCodeBuffer);
            if (PanelButton.Draw("##connect", plugin.Fonts.Icon, FontAwesomeIcon.SignInAlt, connectLabel, new Vector2(140, 28) * Scale, Theme.NeutralAccent)
                && canConnect && !autoJoin)
            {
                plugin.Configuration.LastRoomCode = connectRoomCodeBuffer.Trim();
                plugin.Configuration.Save();

                if (joinAsDjMode)
                {
                    var djName = string.IsNullOrWhiteSpace(connectDjNameBuffer) ? characterName : connectDjNameBuffer.Trim();
                    plugin.AudioHostClient.Send(MessageType.JoinAsHost, new JoinAsHostCommand
                    {
                        RoomCode = connectRoomCodeBuffer.Trim(),
                        HostPassword = connectHostPasswordBuffer,
                        DjName = djName,
                        CharacterName = characterName,
                    });
                }
                else
                {
                    plugin.AudioHostClient.Send(MessageType.ConnectToRemote, new ConnectToRemoteCommand
                    {
                        RoomCode = connectRoomCodeBuffer.Trim(),
                        Password = connectPasswordBuffer,
                        CharacterName = characterName,
                    });
                }
            }

            if (!canConnect && !autoJoin)
            {
                ImGui.TextColored(Theme.OrangeAccent, "Enter a room code first.");
            }

            var joinError = joinAsDjMode ? broadcast.BroadcastError : broadcast.ListenError;
            if (!string.IsNullOrEmpty(joinError) && !autoJoin)
            {
                ImGui.Spacing();
                ImGui.TextColored(Theme.OrangeAccent, joinError);
            }

            ImGui.EndDisabled();

            if (autoJoin)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Manual joining is off while auto-join is on (see the Join a Show screen).");
                ImGui.Spacing();
                DrawAutoJoinStatusLine(SettingsPanelWidth * Scale);
            }
        }
        EndSettingsPanel();
    }

    /// The live diagnostic readout for AutoJoinTracker's own state - needed because this is a Beta feature
    /// the developer can't realistically field-test alone, so what it's currently doing (and why) has to be
    /// visible in the UI, not just inferred from whether audio starts playing.
    private void DrawAutoJoinStatusLine(float wrapWidth)
    {
        var tracker = plugin.AutoJoinTracker;

        string DisplayNameFor(string? roomCode)
        {
            if (roomCode == null)
                return "a nearby show";
            var show = plugin.AudioHostClient.LatestPublicShows?.Shows.FirstOrDefault(s => s.RoomCode == roomCode);
            return show?.ShowName ?? show?.DjName ?? "a nearby show";
        }

        var suppressedSuffix = tracker.SuppressedCandidateCount > 0
            ? $", {tracker.SuppressedCandidateCount} suppressed after a manual leave"
            : string.Empty;

        var statusText = tracker.PendingSwitchRoomCode != null
            ? $"Auto-join (Beta): considering switching to {DisplayNameFor(tracker.PendingSwitchRoomCode)} in {tracker.SwitchDwellRemainingSeconds:F1}s..."
            : tracker.PendingJoinRoomCode != null
                ? $"Auto-join (Beta): found {DisplayNameFor(tracker.PendingJoinRoomCode)}, joining in {tracker.JoinDwellRemainingSeconds:F1}s..."
                : $"Auto-join (Beta): scanning - no public proximity shows in range ({tracker.TrackedCandidateCount} tracked{suppressedSuffix}).";

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapWidth);
        ImGui.TextDisabled(statusText);
        ImGui.PopTextWrapPos();
    }

    /// Now-playing title/artist reads straight off Windows' own System Media Transport Controls (see
    /// SpotifyNowPlayingReader) with no login or settings of its own needed, so this tab is just the
    /// output-device reminder plus whatever error Spotify Mode itself hit.
    private void DrawSpotifyTab()
    {
        var spotifyMode = plugin.AudioHostClient.LatestStatus.SpotifyMode;

        var avail = ImGui.GetContentRegionAvail().X;
        var columnGap = 24f * Scale;
        var leftWidth = MathF.Min(SettingsPanelWidth * Scale, (avail - columnGap) * 0.5f);
        var rightWidth = avail - leftWidth - columnGap;
        var columnsStartPos = ImGui.GetCursorPos();

        ImGui.SetCursorPos(columnsStartPos);
        ImGui.BeginGroup();

        BeginSettingsPanel("OUTPUT DEVICE", leftWidth);
        ImGui.TextDisabled("Spotify plays through its own output too. Switch it to a device you're");
        ImGui.TextDisabled("not using before Spotify Mode, and back again when you're done:");
        ImGui.Spacing();

        if (PanelButton.Draw("##openSoundSettings", plugin.Fonts.Icon, FontAwesomeIcon.ExternalLinkAlt, "Open Windows Sound Settings", new Vector2(240, 28) * Scale, Theme.NeutralAccent))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "ms-settings:apps-volume", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "[EchoMix] Failed to open Windows Sound Settings");
            }
        }

        if (!string.IsNullOrEmpty(spotifyMode.Error))
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.OrangeAccent, spotifyMode.Error);
        }
        EndSettingsPanel();

        ImGui.EndGroup();
        var leftColumnHeight = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorPos(new Vector2(columnsStartPos.X + leftWidth + columnGap, columnsStartPos.Y));
        ImGui.BeginGroup();

        BeginSettingsPanel("REMOTE CONTROLS", rightWidth);
        ImGui.TextDisabled("While Spotify Mode is active, minimize the window and use Deck A's own");
        ImGui.TextDisabled("half of the minimized box as a quick remote:");
        ImGui.Spacing();
        ImGui.BulletText("Right-click - skip to the next track");
        ImGui.BulletText("Shift+Right-click - go back a track");
        ImGui.BulletText("Shift+Click - play/pause (stays minimized)");
        EndSettingsPanel();

        ImGui.EndGroup();
        var rightColumnHeight = ImGui.GetItemRectSize().Y;

        ImGui.SetCursorPos(new Vector2(columnsStartPos.X, columnsStartPos.Y + MathF.Max(leftColumnHeight, rightColumnHeight)));
    }

    private float ListenerVisualizerHeight => 260f * Scale;

    /// The compact, visualizer-focused view a Listener sees instead of the normal deck UI - deliberately
    /// small (see ListenerSize) since it's meant to sit on screen unobtrusively while someone else's set
    /// plays.
    private void DrawListenerBody(BroadcastStatusMessage broadcast)
    {
        ImGui.Spacing();

        float headerLineHeight;
        using (plugin.Fonts.Header.PushSafe())
        {
            headerLineHeight = ImGui.GetTextLineHeight();
            ImGui.TextColored(Theme.NeutralAccent, broadcast.HostDjName ?? "Live");
        }
        if (broadcast.LiveSinceUtc.HasValue)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (headerLineHeight - ImGui.GetTextLineHeight()));
            ImGui.TextDisabled($"-  {FormatElapsed(DateTime.UtcNow - broadcast.LiveSinceUtc.Value)}");
        }
        ImGui.Spacing();

        var avail = ImGui.GetContentRegionAvail();
        var style = (VisualizerWidget.Style)plugin.Configuration.ListenerVisualizerStyle;
        var overlayAccent = Theme.NeutralAccent;

        if (broadcast.IsHostSpotifyModeActive)
        {
            var fullSize = new Vector2(avail.X, ListenerVisualizerHeight);
            DrawListenerDeckHalf("##listenerA", broadcast.NowPlayingTitleA, broadcast.NowPlayingPositionSecondsA, broadcast.NowPlayingDurationSecondsA,
                broadcast.IsListenerReconnecting, "", fullSize, Theme.CyanAccent, overlayAccent, plugin.AudioHostClient.LatestListenSpectrumA, style, plugin.Configuration.ListenerVisualizerSensitivity);
        }
        else
        {
            var halfSize = new Vector2(avail.X, ListenerVisualizerHeight / 2f);
            DrawListenerDeckHalf("##listenerA", broadcast.NowPlayingTitleA, broadcast.NowPlayingPositionSecondsA, broadcast.NowPlayingDurationSecondsA,
                broadcast.IsListenerReconnecting, "A", halfSize, Theme.CyanAccent, overlayAccent, plugin.AudioHostClient.LatestListenSpectrumA, style, plugin.Configuration.ListenerVisualizerSensitivity);
            DrawListenerDeckHalf("##listenerB", broadcast.NowPlayingTitleB, broadcast.NowPlayingPositionSecondsB, broadcast.NowPlayingDurationSecondsB,
                broadcast.IsListenerReconnecting, "B", halfSize, Theme.OrangeAccent, overlayAccent, plugin.AudioHostClient.LatestListenSpectrumB, style, plugin.Configuration.ListenerVisualizerSensitivity);
        }

        ImGui.Spacing();
        ImGui.Spacing();
        if (PanelButton.Draw("##leaveShow", plugin.Fonts.Icon, FontAwesomeIcon.SignOutAlt, "Leave Show", new Vector2(avail.X, 32f * Scale), Theme.NeutralAccent))
        {
            plugin.NotifyManualDisconnectRequested();
            plugin.AudioHostClient.Send(MessageType.DisconnectFromRemote, new object());
        }

        ImGui.Spacing();
        if (PanelButton.Draw("##viewOtherShows", plugin.Fonts.Icon, FontAwesomeIcon.Globe, "View Other Shows", new Vector2(avail.X, 32f * Scale), Theme.CyanAccent))
        {
            viewBeforeBrowseShows = currentView;
            pendingView = ViewMode.BrowseShows;
            plugin.AudioHostClient.Send(MessageType.RequestPublicShows, new object());
        }
    }

    /// The minimized Listener box: Deck A's and Deck B's own spectrums stacked (cyan over orange) filling the
    /// whole (small) window, same as the expanded Listener view and the DJ's own minimized dual-deck box,
    /// each with its own track overlay on top - using the exact same compact overlay style as
    /// DrawMiniDeckHalfOverlay (see DrawMinimizedListenerTrackOverlay) so a listener's minimized box looks
    /// identical to the DJ's own, just read-only.
    private void DrawMinimizedListenerBody(BroadcastStatusMessage broadcast)
    {
        var size = ImGui.GetContentRegionAvail();
        var style = (VisualizerWidget.Style)plugin.Configuration.ListenerVisualizerStyle;
        var overlayAccent = Theme.NeutralAccent;

        if (broadcast.IsHostSpotifyModeActive)
        {
            var originSpotify = ImGui.GetCursorScreenPos();
            var clickedSpotify = VisualizerWidget.Draw("##listenerMiniA", plugin.AudioHostClient.LatestListenSpectrumA, size, Theme.CyanAccent, style, plugin.Configuration.ListenerVisualizerSensitivity, MinimizedMirrorCenterOffset);
            var restoreSpotify = DrawMinimizedInteraction(clickedSpotify);
            if (broadcast.IsListenerReconnecting)
                DrawReconnectingBanner(originSpotify, size, Scale);
            else
                DrawMinimizedListenerTrackOverlay(originSpotify, size, broadcast.NowPlayingTitleA, broadcast.NowPlayingPositionSecondsA, broadcast.NowPlayingDurationSecondsA, "", overlayAccent, Scale);

            if (restoreSpotify)
                isMinimized = false;
            return;
        }

        var halfSize = new Vector2(size.X, size.Y / 2f);

        var originA = ImGui.GetCursorScreenPos();
        var clickedA = VisualizerWidget.Draw("##listenerMiniA", plugin.AudioHostClient.LatestListenSpectrumA, halfSize, Theme.CyanAccent, style, plugin.Configuration.ListenerVisualizerSensitivity, MinimizedMirrorCenterOffset);
        var restoreA = DrawMinimizedInteraction(clickedA);
        if (broadcast.IsListenerReconnecting)
            DrawReconnectingBanner(originA, halfSize, Scale);
        else
            DrawMinimizedListenerTrackOverlay(originA, halfSize, broadcast.NowPlayingTitleA, broadcast.NowPlayingPositionSecondsA, broadcast.NowPlayingDurationSecondsA, "A", overlayAccent, Scale);

        var originB = ImGui.GetCursorScreenPos();
        var clickedB = VisualizerWidget.Draw("##listenerMiniB", plugin.AudioHostClient.LatestListenSpectrumB, halfSize, Theme.OrangeAccent, style, plugin.Configuration.ListenerVisualizerSensitivity, MinimizedMirrorCenterOffset);
        var restoreB = DrawMinimizedInteraction(clickedB);
        if (broadcast.IsListenerReconnecting)
            DrawReconnectingBanner(originB, halfSize, Scale);
        else
            DrawMinimizedListenerTrackOverlay(originB, halfSize, broadcast.NowPlayingTitleB, broadcast.NowPlayingPositionSecondsB, broadcast.NowPlayingDurationSecondsB, "B", overlayAccent, Scale);

        if (restoreA || restoreB)
            isMinimized = false;
    }

    /// Takes over a half's whole overlay while AudioHost is quietly retrying a dropped connection (see
    /// BroadcastListenClient.ReconnectLoopAsync) - track/seek info is stale during this window anyway
    /// (nothing's arriving to update it), so this is clearer than leaving the last known title sitting there
    /// next to silence with no explanation.
    private static void DrawReconnectingBanner(Vector2 origin, Vector2 size, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var stripHeight = 34f * scale;
        drawList.AddRectFilled(origin, origin + new Vector2(size.X, stripHeight), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)));
        var padding = new Vector2(6f * scale, 3f * scale);
        drawList.AddText(origin + padding, ImGui.GetColorU32(Theme.OrangeAccent), "Reconnecting...");
    }

    /// The expanded Listener view's per-deck header - title, a read-only seek bar, and an elapsed/duration
    /// readout - drawn as real, top-to-bottom layout inside its own fixed-size child (same technique
    /// DrawDisplayAndKnobs's own digital display uses: title -&gt; seek -&gt; time -&gt; visualizer, each
    /// consuming its own space) rather than as a floating overlay drawn on top of an already-full-height
    /// visualizer.
    private void DrawListenerDeckHalf(string id, string? title, double positionSeconds, double durationSeconds, bool isReconnecting,
        string label, Vector2 size, Vector4 spectrumAccent, Vector4 overlayAccent, float[] spectrum, VisualizerWidget.Style style, float reactivity = 1f)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild(id + "Half", size, false, ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(Scale);

        string displayTitle;
        if (string.IsNullOrEmpty(label))
            displayTitle = isReconnecting ? "Reconnecting..." : title ?? "No track loaded";
        else
            displayTitle = isReconnecting ? $"{label}: Reconnecting..." : $"{label}: {title ?? "No track loaded"}";
        ImGui.TextColored(isReconnecting ? Theme.OrangeAccent : spectrumAccent, TruncateToWidth(displayTitle, size.X));

        ImGui.Spacing();
        var barSize = new Vector2(size.X, 5f * Scale);
        var duration = Math.Max(durationSeconds, 0.01);
        var progress = (float)Math.Clamp(positionSeconds / duration, 0.0, 1.0);
        DrawMiniProgressBar(ImGui.GetCursorScreenPos(), barSize, progress, overlayAccent);
        ImGui.Dummy(barSize);

        var total = FormatTime(durationSeconds);
        ImGui.TextDisabled(FormatTime(positionSeconds));
        ImGui.SameLine(size.X - ImGui.CalcTextSize(total).X);
        ImGui.TextDisabled(total);

        ImGui.Spacing();
        var visualizerSize = new Vector2(size.X, MathF.Max(1f, ImGui.GetContentRegionAvail().Y));
        VisualizerWidget.Draw(id, spectrum, visualizerSize, spectrumAccent, style, reactivity);

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    /// Same compact title-strip-plus-slim-bar layout as DrawMiniDeckHalfOverlay (no separate time readout, no
    /// full backdrop under the bar) so the Listener's minimized box looks identical to the DJ's own minimized
    /// dual-deck box - just read-only, since a listener can't seek the host's playback.
    private static void DrawMinimizedListenerTrackOverlay(Vector2 origin, Vector2 size, string? title, double positionSeconds, double durationSeconds, string label, Vector4 accent, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var stripHeight = 20f * scale;
        var padding = new Vector2(6f * scale, 2f * scale);

        var displayText = string.IsNullOrEmpty(label) ? (title ?? "No track loaded") : $"{label}: {title ?? "No track loaded"}";
        drawList.AddRectFilled(origin, origin + new Vector2(size.X, stripHeight), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)));
        drawList.AddText(origin + padding, ImGui.GetColorU32(Theme.Text), TruncateToWidth(displayText, size.X - (padding.X * 2f)));

        if (string.IsNullOrEmpty(title))
            return;

        var barSize = new Vector2(size.X - (padding.X * 2f), 5f * scale);
        var barOrigin = new Vector2(origin.X + padding.X, origin.Y + stripHeight + (2f * scale));
        var progress = durationSeconds > 0 ? (float)(positionSeconds / durationSeconds) : 0f;
        DrawMiniProgressBar(barOrigin, barSize, progress, accent);
    }

    /// A static, non-interactive progress bar - same track/fill visual language as the DJ's own draggable
    /// SeekBar control, without the InvisibleButton/interaction behind it, since a listener can't actually
    /// seek the host's playback.
    private static void DrawMiniProgressBar(Vector2 origin, Vector2 size, float progress, Vector4 accent)
    {
        var drawList = ImGui.GetWindowDrawList();
        var rounding = size.Y / 2f;
        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.03f, 0.03f, 0.045f, 1f)), rounding);

        var fillWidth = size.X * Math.Clamp(progress, 0f, 1f);
        if (fillWidth > 1f)
        {
            var fillFlags = progress >= 0.995f ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersLeft;
            drawList.AddRectFilled(origin, origin + new Vector2(fillWidth, size.Y), ImGui.GetColorU32(accent), rounding, fillFlags);
        }
    }

    /// The minimized Deck box: Deck A's own visualizer/track info stacked above Deck B's, each reading that
    /// deck's own spectrum rather than a blended one - so both decks stay visible (and seekable) while
    /// minimized instead of only whichever one happens to be playing.
    private void DrawMinimizedDeckBody(MixerStatusMessage status)
    {
        var origin = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        var restore = false;

        if (status.SpotifyMode.IsActive)
        {
            ImGui.SetCursorScreenPos(origin);
            if (DrawMinimizedDeckHalf(DeckId.A, status, size, Theme.CyanAccent))
                restore = true;
        }
        else
        {
            var halfHeight = size.Y / 2f;

            ImGui.SetCursorScreenPos(origin);
            if (DrawMinimizedDeckHalf(DeckId.A, status, new Vector2(size.X, halfHeight), Theme.CyanAccent))
                restore = true;

            ImGui.SetCursorScreenPos(origin + new Vector2(0f, halfHeight));
            if (DrawMinimizedDeckHalf(DeckId.B, status, new Vector2(size.X, halfHeight), Theme.OrangeAccent))
                restore = true;
        }

        if (restore)
            isMinimized = false;
    }

    /// One deck's own slice of the minimized box - its spectrum plus a compact track overlay.
    private bool DrawMinimizedDeckHalf(DeckId deckId, MixerStatusMessage status, Vector2 size, Vector4 accent)
    {
        var origin = ImGui.GetCursorScreenPos();
        var spectrum = deckId == DeckId.A ? plugin.AudioHostClient.LatestSpectrumA : plugin.AudioHostClient.LatestSpectrumB;
        var deckStyle = (VisualizerWidget.Style)plugin.Configuration.DeckVisualizerStyle;
        var clicked = VisualizerWidget.Draw($"##deckMini{deckId}", spectrum, size, accent, deckStyle, plugin.Configuration.DeckVisualizerSensitivity, MinimizedMirrorCenterOffset);

        var isSpotifyHalf = deckId == DeckId.A && status.SpotifyMode.IsActive;
        var shiftHeld = ImGui.GetIO().KeyShift;
        if (isSpotifyHalf && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            plugin.AudioHostClient.Send(
                shiftHeld ? MessageType.SpotifySkipPrevious : MessageType.SpotifySkipNext,
                new object());
        }

        var shiftClicked = isSpotifyHalf && shiftHeld && clicked;
        if (shiftClicked)
            plugin.AudioHostClient.Send(MessageType.SpotifyTogglePlayPause, new object());

        var shouldRestore = DrawMinimizedInteraction(shiftClicked ? false : clicked);

        DrawMiniDeckHalfOverlay(deckId, status, origin, size, accent);
        return shouldRestore;
    }

    /// Spotify Mode and External Input Mode both replace Deck A specifically (see DrawDisplayAndKnobs), so
    /// Deck A's half shows whichever one's now-playing info (or, for External Input, just its device name) in
    /// its place while active; Deck B's half shows its own deck state, unless a second External Input device
    /// is active too, in which case it mirrors Deck A's treatment for that second device.
    private void DrawMiniDeckHalfOverlay(DeckId deckId, MixerStatusMessage status, Vector2 origin, Vector2 size, Vector4 accent)
    {
        var spotifyMode = status.SpotifyMode;
        var externalInputMode = status.ExternalInputMode;
        var showSpotify = deckId == DeckId.A && spotifyMode.IsActive && !string.IsNullOrEmpty(spotifyMode.NowPlayingTitle);
        var showExternalInput = deckId == DeckId.A ? externalInputMode.IsActive : externalInputMode.IsSecondActive;

        string? title;
        double positionSeconds, durationSeconds;
        if (showSpotify)
        {
            var artistPart = string.IsNullOrEmpty(spotifyMode.NowPlayingArtist) ? string.Empty : $" - {spotifyMode.NowPlayingArtist}";
            title = spotifyMode.NowPlayingTitle + artistPart;
            positionSeconds = spotifyMode.NowPlayingPositionSeconds;
            durationSeconds = spotifyMode.NowPlayingDurationSeconds;
        }
        else if (showExternalInput)
        {
            title = (deckId == DeckId.A ? externalInputMode.DeviceName : externalInputMode.DeviceName2) ?? "External Input";
            positionSeconds = 1;
            durationSeconds = 1;
        }
        else
        {
            var deck = deckId == DeckId.A ? status.DeckA : status.DeckB;
            title = deck.HasTrack ? deck.TrackTitle : null;
            positionSeconds = deck.PositionSeconds;
            durationSeconds = deck.DurationSeconds;
        }

        var drawList = ImGui.GetWindowDrawList();
        var stripHeight = 20f * Scale;        drawList.AddRectFilled(origin, origin + new Vector2(size.X, stripHeight), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)));

        var isSpotifyHalf = deckId == DeckId.A && spotifyMode.IsActive;
        var displayText = isSpotifyHalf ? (title ?? "No track loaded") : $"{(deckId == DeckId.A ? "A" : "B")}: {title ?? "No track loaded"}";
        var padding = new Vector2(6f * Scale, 2f * Scale);
        drawList.AddText(origin + padding, ImGui.GetColorU32(Theme.Text), TruncateToWidth(displayText, size.X - (padding.X * 2f)));

        if (title == null)
            return;

        var barSize = new Vector2(size.X - (padding.X * 2f), 5f * Scale);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padding.X, origin.Y + stripHeight + (2f * Scale)));

        ref var pendingSeek = ref (deckId == DeckId.A ? ref pendingSeekA : ref pendingSeekB);
        if (pendingSeek.HasValue && Math.Abs(positionSeconds - pendingSeek.Value) < 0.5)
            pendingSeek = null;

        var position = pendingSeek ?? (float)positionSeconds;
        var duration = (float)Math.Max(durationSeconds, 0.01);
        if (SeekBar.Draw($"##miniSeek{deckId}", ref position, 0f, duration, barSize, accent, interactive: !showSpotify && !showExternalInput))
        {
            pendingSeek = position;
            plugin.AudioHostClient.Send(MessageType.SetPosition, new SetPositionCommand { Deck = deckId, PositionSeconds = position });
        }
    }

    /// Shared drag-to-move/click-to-restore handling for both minimized boxes - `clicked` is whatever
    /// VisualizerWidget.Draw just returned for the same InvisibleButton this reads state from.
    private bool DrawMinimizedInteraction(bool clicked)
    {
        if (ImGui.IsItemActivated())
            miniBoxDraggedThisPress = false;

        if (ImGui.IsItemActive() && !plugin.Configuration.IsWindowLocked && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta = ImGui.GetIO().MouseDelta;
            if (delta != Vector2.Zero)
            {
                positionAnimTarget = null;
                ImGui.SetWindowPos(ImGui.GetWindowPos() + delta);
                miniBoxDraggedThisPress = true;
            }
        }

        return clicked && !miniBoxDraggedThisPress;
    }

    /// Ellipsizes to fit `maxWidth` rather than letting a long track title overflow the small minimized box -
    /// see UiHelpers.TruncateToWidth, which this delegates to (shared with ToastWindow subclasses, which
    /// can't reach a private method on this class).
    private static string TruncateToWidth(string text, float maxWidth) => UiHelpers.TruncateToWidth(text, maxWidth);

    /// A 3-stop Deck A -> Blend -> Deck B gradient color at position t in [0,1] - used anywhere the app draws
    /// a smooth cyan/orange gradient (the window border, the wordmark and its underline), so the midpoint
    /// always reflects the user's own chosen Blend color (Theme.NeutralAccent, Settings > General > Deck
    /// Colors) instead of independently recomputing a plain 50/50 average of the other two - a straight
    /// two-color Lerp always lands on that mathematical midpoint regardless of what Theme.NeutralAccent
    /// actually is, which is why a custom Blend pick that isn't exactly the midpoint was silently ignored on
    /// these gradients even though every other themed element picked it up correctly.
    private static Vector4 DeckGradientColor(float t) =>
        t <= 0.5f
            ? Vector4.Lerp(Theme.CyanAccent, Theme.NeutralAccent, t * 2f)
            : Vector4.Lerp(Theme.NeutralAccent, Theme.OrangeAccent, (t - 0.5f) * 2f);

    /// A bold glowing border around the window's own rect, on the WINDOW's own draw list, so the window pops
    /// against the game view instead of sitting perfectly flat - a smooth cyan-to-orange left-to-right
    /// gradient rather than a single color or a hard clip-rect seam, so the deck's own theme reads at a
    /// glance on the window itself.
    private void DrawWindowChrome()
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        var rounding = ImGui.GetStyle().WindowRounding;

        var thickness = 3f * Scale;

        drawList.PushClipRect(pos - new Vector2(thickness, thickness), pos + size + new Vector2(thickness, thickness), false);

        var points = BuildRoundedRectPoints(pos, size, rounding);
        for (var i = 0; i < points.Count; i++)
        {
            var p0 = points[i];
            var p1 = points[(i + 1) % points.Count];

            var blend = Math.Clamp(((p0.X + p1.X) / 2f - pos.X) / MathF.Max(1f, size.X), 0f, 1f);
            var color = DeckGradientColor(blend);
            drawList.AddLine(p0, p1, ImGui.GetColorU32(color), thickness);
        }

        drawList.PopClipRect();
    }

    /// Traces a rounded rect clockwise from the top edge's left end, giving each of the 4 corner arcs a fixed
    /// number of segments (so they stay smoothly curved no matter how big the rect is) and spacing the 4
    /// straight edges by roughly constant pixel distance (so a per-segment color gradient along them still
    /// looks continuous).
    private static List<Vector2> BuildRoundedRectPoints(Vector2 pos, Vector2 size, float r)
    {
        const int cornerSegments = 16;
        const float edgeStep = 20f;

        var points = new List<Vector2>();
        var straightX = MathF.Max(0f, size.X - (2f * r));
        var straightY = MathF.Max(0f, size.Y - (2f * r));

        void AddArc(Vector2 center, float startAngle)
        {
            for (var i = 0; i <= cornerSegments; i++)
            {
                var a = startAngle + (i / (float)cornerSegments * (MathF.PI / 2f));
                points.Add(center + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * r));
            }
        }

        void AddEdge(Vector2 from, Vector2 to)
        {
            var steps = Math.Max(1, (int)(Vector2.Distance(from, to) / edgeStep));
            for (var i = 0; i < steps; i++)
                points.Add(Vector2.Lerp(from, to, i / (float)steps));
        }

        AddEdge(new Vector2(pos.X + r, pos.Y), new Vector2(pos.X + r + straightX, pos.Y));
        AddArc(new Vector2(pos.X + size.X - r, pos.Y + r), -MathF.PI / 2f);
        AddEdge(new Vector2(pos.X + size.X, pos.Y + r), new Vector2(pos.X + size.X, pos.Y + r + straightY));
        AddArc(new Vector2(pos.X + size.X - r, pos.Y + size.Y - r), 0f);
        AddEdge(new Vector2(pos.X + size.X - r, pos.Y + size.Y), new Vector2(pos.X + size.X - r - straightX, pos.Y + size.Y));
        AddArc(new Vector2(pos.X + r, pos.Y + size.Y - r), MathF.PI / 2f);
        AddEdge(new Vector2(pos.X, pos.Y + size.Y - r), new Vector2(pos.X, pos.Y + r));
        AddArc(new Vector2(pos.X + r, pos.Y + r), MathF.PI);

        return points;
    }

    private static readonly Vector4 LiveGreen = new(0.35f, 0.85f, 0.4f, 1f);
    private static readonly Vector4 DiscordBlurple = new(0.345f, 0.396f, 0.949f, 1f);
    private static readonly Vector4 SpotifyGreen = new(0.118f, 0.843f, 0.376f, 1f);
    private const string DiscordInviteUrl = "https://discord.gg/nJauXrNWx3";

    /// The header row: the centered wordmark, plus lock/settings/close always, and two context-dependent
    /// slots - Proximity (Deck/Settings, the host's controls) and Minimize (Deck/Listener, since Settings has
    /// nothing to minimize into).
    private void DrawHeader()
    {
        if (reportBugSending)
        {
            var result = plugin.AudioHostClient.ConsumeBugReportResult();
            if (result != null)
            {
                reportBugSending = false;
                reportBugSendResult = result;
            }
        }

        if (reportShowSending)
        {
            var result = plugin.AudioHostClient.ConsumeReportShowResult();
            if (result != null)
            {
                reportShowSending = false;
                reportShowSendResult = result;
            }
        }

        if (djProfileSaveSending)
        {
            var result = plugin.AudioHostClient.ConsumeDjProfileSaveResult();
            if (result == null)
            {
                djProfileSaveElapsed += ImGui.GetIO().DeltaTime;
                if (djProfileSaveElapsed >= DjProfileSaveTimeoutSeconds)
                {
                    djProfileSaveSending = false;
                    djProfileSaveResult = new DjProfileSaveResultMessage
                    {
                        Success = false,
                        Error = "No response from AudioHost - try again, or restart the plugin if this keeps happening.",
                    };
                }
            }

            if (result != null)
            {
                djProfileSaveSending = false;
                djProfileSaveResult = result;

                if (result.Success && result.ProfileId != null)
                {
                    editingDjProfileId = result.ProfileId;
                    var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

                    if (djEditAvatarUploadPath != null)
                    {
                        plugin.AudioHostClient.Send(MessageType.SetDjProfileImage, new SetDjProfileImageCommand
                        {
                            ProfileId = result.ProfileId,
                            CharacterName = characterName,
                            Slot = "avatar",
                            SourceFilePath = djEditAvatarUploadPath,
                        });
                        djEditAvatarUploadPath = null;
                    }

                    if (djEditBannerUploadPath != null)
                    {
                        plugin.AudioHostClient.Send(MessageType.SetDjProfileImage, new SetDjProfileImageCommand
                        {
                            ProfileId = result.ProfileId,
                            CharacterName = characterName,
                            Slot = "banner",
                            SourceFilePath = djEditBannerUploadPath,
                        });
                        djEditBannerUploadPath = null;
                    }

                    plugin.AudioHostClient.Send(MessageType.RequestDjProfiles, new RequestDjProfilesMessage { RequesterCharacterName = characterName });
                    plugin.AudioHostClient.Send(MessageType.GetDjProfileDetail, new GetDjProfileDetailMessage { Id = result.ProfileId, RequesterCharacterName = characterName });
                }

                if (result.Success && pendingDjProfileEditExit.HasValue)
                {
                    pendingView = pendingDjProfileEditExit.Value;
                    pendingDjProfileEditExit = null;
                }
            }
        }

        if (djProfileDeleteSending)
        {
            var result = plugin.AudioHostClient.ConsumeDjProfileDeleteResult();
            if (result != null)
            {
                djProfileDeleteSending = false;
                djProfileDeleteResult = result;

                if (result.Success)
                {
                    plugin.AudioHostClient.Send(MessageType.RequestDjProfiles, new RequestDjProfilesMessage
                    {
                        RequesterCharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
                    });
                    pendingView = ViewMode.DjList;
                }
            }
        }

        if (djProfileReportSending)
        {
            var result = plugin.AudioHostClient.ConsumeDjProfileReportResult();
            if (result != null)
            {
                djProfileReportSending = false;
                djProfileReportSendResult = result;
            }
        }

        var likeResult = plugin.AudioHostClient.ConsumeDjProfileLikeResult();
        if (likeResult != null)
        {
            djProfileLikePending.Remove(likeResult.ProfileId);
            if (likeResult.Success)
            {
                var cachedProfile = plugin.AudioHostClient.LatestDjProfiles?.Profiles.FirstOrDefault(p => p.Id == likeResult.ProfileId);
                if (cachedProfile != null)
                {
                    cachedProfile.LikeCount = likeResult.LikeCount;
                    cachedProfile.IsLikedByRequester = likeResult.IsLiked;
                }

                var cachedDetail = plugin.AudioHostClient.LatestDjProfileDetail;
                if (cachedDetail?.Profile != null && cachedDetail.Profile.Id == likeResult.ProfileId)
                {
                    cachedDetail.Profile.LikeCount = likeResult.LikeCount;
                    cachedDetail.Profile.IsLikedByRequester = likeResult.IsLiked;
                }
            }
        }

        var followResult = plugin.AudioHostClient.ConsumeDjProfileFollowResult();
        if (followResult != null)
        {
            djProfileFollowPending.Remove(followResult.ProfileId);
            if (followResult.Success)
            {
                var cachedProfile = plugin.AudioHostClient.LatestDjProfiles?.Profiles.FirstOrDefault(p => p.Id == followResult.ProfileId);
                if (cachedProfile != null)
                {
                    cachedProfile.FollowerCount = followResult.FollowerCount;
                    cachedProfile.IsFollowedByRequester = followResult.IsFollowing;
                }

                var cachedDetail = plugin.AudioHostClient.LatestDjProfileDetail;
                if (cachedDetail?.Profile != null && cachedDetail.Profile.Id == followResult.ProfileId)
                {
                    cachedDetail.Profile.FollowerCount = followResult.FollowerCount;
                    cachedDetail.Profile.IsFollowedByRequester = followResult.IsFollowing;
                }
            }
        }

        var linkCodeResult = plugin.AudioHostClient.ConsumeProfileLinkCodeResult();
        if (linkCodeResult != null)
        {
            djLinkCodeGenerating = false;
            if (linkCodeResult.Success)
            {
                djLinkCodeGenerated = linkCodeResult.Code;
                djLinkCodeExpiresInSeconds = linkCodeResult.ExpiresInSeconds;
                djLinkCodeError = null;
            }
            else
            {
                djLinkCodeError = linkCodeResult.Error ?? "Couldn't generate a code.";
            }
        }
        else if (djLinkCodeGenerated != null)
        {
            djLinkCodeExpiresInSeconds -= ImGui.GetIO().DeltaTime;
            if (djLinkCodeExpiresInSeconds <= 0f)
                djLinkCodeGenerated = null;
        }

        var redeemResult = plugin.AudioHostClient.ConsumeProfileLinkRedeemResult();
        if (redeemResult != null)
        {
            djRedeemSending = false;
            djRedeemResult = redeemResult;
            if (redeemResult.Success)
            {
                djRedeemCodeBuffer = string.Empty;
                plugin.AudioHostClient.Send(MessageType.RequestDjProfiles, new RequestDjProfilesMessage
                {
                    RequesterCharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
                });
            }
        }

        var unlinkResult = plugin.AudioHostClient.ConsumeProfileUnlinkResult();
        if (unlinkResult != null)
        {
            djUnlinkPending.Clear();
            if (unlinkResult.Success)
            {
                djEditLinkedCharacterNames.Clear();
                djEditLinkedCharacterNames.AddRange(unlinkResult.LinkedCharacterNames);
            }
        }

        var isBroadcastLiveNow = plugin.AudioHostClient.LatestStatus.Broadcast.IsLive;
        if (isBroadcastLiveNow && !wasBroadcastLiveLastFrame)
        {
            var imageToSend = stagedShowImagePath ?? (File.Exists(LastShowImagePath) ? LastShowImagePath : null);
            if (imageToSend != null)
                plugin.AudioHostClient.Send(MessageType.SetShowImage, new SetShowImageCommand { SourceFilePath = imageToSend });
            stagedShowImagePath = null;
        }
        wasBroadcastLiveLastFrame = isBroadcastLiveNow;

        var imageResult = plugin.AudioHostClient.ConsumeDjProfileImageResult();
        if (imageResult is { Success: true })
        {
            lock (djProfileImageGate)
            {
                if (string.Equals(imageResult.Slot, "banner", StringComparison.OrdinalIgnoreCase))
                {
                    if (djProfileBannerTextureForId == imageResult.ProfileId)
                    {
                        djProfileBannerTexture?.Dispose();
                        djProfileBannerTexture = null;
                        djProfileBannerTextureForId = null;
                    }
                }
                else
                {
                    if (djProfileAvatarCache.TryGetValue(imageResult.ProfileId, out var staleTexture))
                        staleTexture?.Dispose();
                    djProfileAvatarCache.Remove(imageResult.ProfileId);
                    djProfileAvatarLoading.Remove(imageResult.ProfileId);
                }
            }

            var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;
            plugin.AudioHostClient.Send(MessageType.RequestDjProfiles, new RequestDjProfilesMessage { RequesterCharacterName = characterName });
            plugin.AudioHostClient.Send(MessageType.GetDjProfileDetail, new GetDjProfileDetailMessage { Id = imageResult.ProfileId, RequesterCharacterName = characterName });
        }

        if (pendingEditProfileIdOnDetailLoad != null)
        {
            var loadedDetail = plugin.AudioHostClient.LatestDjProfileDetail;
            if (loadedDetail?.Profile?.Id == pendingEditProfileIdOnDetailLoad)
            {
                SeedDjEditBuffersFrom(loadedDetail.Profile);
                editingDjProfileId = loadedDetail.Profile.Id;
                viewBeforeDjProfileEdit = ViewMode.DjList;
                pendingView = ViewMode.DjProfileEdit;
                pendingEditProfileIdOnDetailLoad = null;
            }
            else if (loadedDetail?.Error != null)
            {
                pendingEditProfileIdOnDetailLoad = null;
            }
        }

        if (plugin.AudioHostClient.LatestDjProfileDetail?.Profile is { IsOwnProfile: true } ownDetail)
            ownSavedVenuesCache = ownDetail.SavedVenues;

        if (currentView == ViewMode.Welcome || currentView == ViewMode.JoinShow || currentView == ViewMode.BrowseShows
            || currentView == ViewMode.DjList || currentView == ViewMode.DjProfile || currentView == ViewMode.DjProfileEdit)
        {
            DrawOnboardingHeader();
            return;
        }

        var buttonSize = 32f * Scale;
        var gap = 10f * Scale;

        var isListenerView = currentView == ViewMode.Listener;
        var showLock = !isListenerView;
        var showClose = !isListenerView;
        var showInDeckOrSettings = currentView == ViewMode.Deck || currentView == ViewMode.Settings;
        var showProximity = showInDeckOrSettings;
        var showMinimize = showInDeckOrSettings || currentView == ViewMode.Listener;
        var showReportBug = showInDeckOrSettings;
        var showSpotify = showInDeckOrSettings;
        var showExternalInput = showInDeckOrSettings;
        var showHostLobby = showInDeckOrSettings && plugin.AudioHostClient.LatestStatus.Broadcast.IsLive;
        var showBrowseShows = showInDeckOrSettings;
        var showSongRequest = isListenerView;
        var showListenerVolume = isListenerView;
        var buttonCount = (showLock ? 1 : 0) + (showProximity ? 1 : 0) + (showReportBug ? 1 : 0) + (showSpotify ? 1 : 0) + (showExternalInput ? 1 : 0) + (showHostLobby ? 1 : 0) + (showBrowseShows ? 1 : 0) + 1 + (showMinimize ? 1 : 0) + (showClose ? 1 : 0);
        var totalWidth = (buttonSize * buttonCount) + (gap * (buttonCount - 1));
        var fullWidth = ImGui.GetContentRegionAvail().X;
        var rowStart = ImGui.GetCursorScreenPos();

        float rowHeight;
        using (plugin.Fonts.Header.PushSafe())
        {
            rowHeight = ImGui.GetFontSize() + 6f;
            DrawCenteredLogo(Scale, totalWidth + gap);
        }

        if (showProximity)
        {
            DrawLiveIndicator(rowStart, rowHeight, plugin.AudioHostClient.LatestStatus.Broadcast);
        }
        else if (showSongRequest)
        {
            var savedPos = ImGui.GetCursorPos();
            ImGui.SetCursorScreenPos(rowStart);
            if (DrawIconOnlyButton("##songRequest", plugin.Fonts.Icon, FontAwesomeIcon.Upload, "Request a Song", buttonSize, Theme.NeutralAccent, plugin.SongRequestWindow.IsExpanded))
                plugin.SongRequestWindow.IsExpanded = !plugin.SongRequestWindow.IsExpanded;

            if (showListenerVolume)
            {
                ImGui.SameLine(0, gap);
                var volumeIcon = plugin.Configuration.ListenerVolume <= 0.01f ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
                if (DrawIconOnlyButton("##listenerVolume", plugin.Fonts.Icon, volumeIcon, "Your Volume", buttonSize, Theme.NeutralAccent, false))
                    ImGui.OpenPopup(ListenerVolumePopupId);

                if (ImGui.BeginPopup(ListenerVolumePopupId, ImGuiWindowFlags.NoMove))
                {
                    ImGui.SetWindowFontScale(Scale);
                    var volumePercent = plugin.Configuration.ListenerVolume * 100f;
                    ImGui.SetNextItemWidth(180f * Scale);

                    ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f * Scale, 8f * Scale));
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Background);
                    ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.16f, 0.16f, 0.2f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.25f));
                    ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.6f));
                    var changed = ImGui.SliderFloat("##listenVolume", ref volumePercent, 0f, 150f, "%.0f%%");
                    ImGui.PopStyleColor(3);
                    ImGui.PopStyleVar(2);

                    if (changed)
                    {
                        plugin.Configuration.ListenerVolume = volumePercent / 100f;
                        plugin.Configuration.Save();
                    }
                    ImGui.EndPopup();
                }
            }

            ImGui.SetCursorPos(savedPos);
        }

        var isFirstButton = true;
        void NextButtonSlot()
        {
            if (isFirstButton)
                ImGui.SameLine(fullWidth - totalWidth + ImGui.GetCursorPosX());
            else
                ImGui.SameLine(0, gap);
            isFirstButton = false;
        }

        if (showLock)
        {
            NextButtonSlot();
            var locked = plugin.Configuration.IsWindowLocked;
            var lockIcon = locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
            var lockTooltip = locked ? "Unlock window position" : "Lock window position";
            if (DrawIconOnlyButton("##lock", plugin.Fonts.Icon, lockIcon, lockTooltip, buttonSize, Theme.NeutralAccent, locked))
            {
                plugin.Configuration.IsWindowLocked = !locked;
                plugin.Configuration.Save();
            }
        }

        if (showProximity)
        {
            NextButtonSlot();
            var proximity = plugin.Configuration.IsProximityAudio;
            var proximityIcon = proximity ? FontAwesomeIcon.MapMarkerAlt : FontAwesomeIcon.Wifi;
            var proximityTooltip = proximity
                ? "Proximity Audio - listeners only hear you near your character (right-click to set the range)"
                : "Global Audio - listeners hear you from anywhere";
            if (DrawIconOnlyButton("##proximity", plugin.Fonts.Icon, proximityIcon, proximityTooltip, buttonSize, Theme.NeutralAccent, proximity))
            {
                plugin.Configuration.IsProximityAudio = !proximity;
                plugin.Configuration.Save();
                SendProximitySettings();
            }

            if (ImGui.BeginPopupContextItem("##proximity"))
            {
                ImGui.TextDisabled("Proximity Range");
                var range = plugin.Configuration.ProximityRange;
                ImGui.SetNextItemWidth(160 * Scale);
                ImGui.SliderFloat("##proximityRange", ref range, 5f, 100f, "%.0f yalms");
                plugin.Configuration.ProximityRange = range;
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    plugin.Configuration.Save();
                    SendProximitySettings();
                }

                ImGui.EndPopup();
            }
        }

        if (showReportBug)
        {
            NextButtonSlot();
            if (DrawIconOnlyButton("##reportBug", plugin.Fonts.Icon, FontAwesomeIcon.Bug, "Report a Bug", buttonSize, Theme.OrangeAccent, false))
            {
                reportBugDescriptionBuffer = string.Empty;
                reportBugDiscordNameBuffer = plugin.Configuration.LastReportBugDiscordName ?? string.Empty;
                reportBugSendResult = null;
                ImGui.OpenPopup(ReportBugPopupId);
            }

            if (ImGui.BeginPopup(ReportBugPopupId))
            {
                ImGui.SetWindowFontScale(Scale);
                ImGui.TextColored(Theme.OrangeAccent, "Report a Bug");
                ImGui.TextDisabled("Sends your AudioHost session log, plugin version, and current");
                ImGui.TextDisabled("status straight to the developer - nothing else on this PC.");
                ImGui.Spacing();

                ImGui.TextDisabled("What happened? (optional)");

                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
                ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Background);
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.16f, 0.16f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.25f));
                ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(Theme.NeutralAccent.X, Theme.NeutralAccent.Y, Theme.NeutralAccent.Z, 0.6f));

                ImGui.SetNextItemWidth(280f * Scale);
                WrappedInput.Multiline("##reportBugDescription", ref reportBugDescriptionBuffer, 1000, new Vector2(280f, 80f) * Scale);

                ImGui.Spacing();
                ImGui.TextDisabled("Your Discord name, if you'd like a reply (optional)");
                ImGui.SetNextItemWidth(280f * Scale);
                ImGui.InputTextWithHint("##reportBugDiscordName", "e.g. username#0000", ref reportBugDiscordNameBuffer, 64);

                ImGui.PopStyleColor(4);
                ImGui.PopStyleVar();
                ImGui.Spacing();

                if (reportBugSending)
                {
                    ImGui.TextDisabled("Sending...");
                }
                else if (PanelButton.Draw("##reportBugSend", null, null, "Send Report", new Vector2(160, 28) * Scale, Theme.OrangeAccent))
                {
                    reportBugSending = true;
                    reportBugSendResult = null;
                    plugin.Configuration.LastReportBugDiscordName = reportBugDiscordNameBuffer.Trim();
                    plugin.Configuration.Save();
                    plugin.AudioHostClient.Send(MessageType.SubmitBugReport, new SubmitBugReportCommand
                    {
                        Description = WrappedInput.Unfold(reportBugDescriptionBuffer, WrappedInput.WidthFor(new Vector2(280f, 80f) * Scale)).Trim(),
                        DiscordName = string.IsNullOrWhiteSpace(reportBugDiscordNameBuffer) ? null : reportBugDiscordNameBuffer.Trim(),
                        PluginVersion = ChangelogData.LatestVersion,
                        CharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
                        SystemInfo = SystemDiagnostics.Capture(plugin.Configuration.UiScale),
                    });
                }

                if (reportBugSendResult != null)
                {
                    ImGui.Spacing();
                    if (reportBugSendResult.Success)
                        ImGui.TextColored(Theme.CyanAccent, "Sent - thank you!");
                    else
                        ImGui.TextColored(Theme.OrangeAccent, $"Couldn't send: {reportBugSendResult.Error ?? "unknown error"}");
                }

                ImGui.EndPopup();
            }
        }

        if (showSpotify)
        {
            NextButtonSlot();
            var spotifyMode = plugin.AudioHostClient.LatestStatus.SpotifyMode;
            var spotifyTooltip = spotifyMode.IsActive
                ? "Spotify Mode - broadcasting only your Spotify audio (click to return to the decks)"
                : "Spotify Mode - broadcast only your Spotify audio instead of the decks";
            spotifyTooltip += "\nHearing it twice? See Settings > Spotify to switch Spotify's output (and back again when done).";
            if (!string.IsNullOrEmpty(spotifyMode.Error))
                spotifyTooltip += $"\n{spotifyMode.Error}";

            if (DrawIconOnlyButton("##spotifyMode", plugin.Fonts.Icon, FontAwesomeIcon.Music, spotifyTooltip, buttonSize, SpotifyGreen, spotifyMode.IsActive))
            {
                plugin.AudioHostClient.Send(
                    spotifyMode.IsActive ? MessageType.StopSpotifyMode : MessageType.StartSpotifyMode,
                    new object());
            }
        }

        if (showExternalInput)
        {
            NextButtonSlot();
            var externalInputMode = plugin.AudioHostClient.LatestStatus.ExternalInputMode;
            var usingProcessCapture = plugin.Configuration.ExternalInputUseProcessCapture;
            var pickedSourceLabel = usingProcessCapture ? plugin.Configuration.ExternalInputProcessDisplayName : plugin.Configuration.ExternalInputDeviceName;
            var deviceLabel = externalInputMode.DeviceName ?? pickedSourceLabel;
            var externalInputTooltip = externalInputMode.IsActive
                ? externalInputMode.IsSecondActive
                    ? $"External Input Mode - broadcasting \"{externalInputMode.DeviceName}\" and \"{externalInputMode.DeviceName2}\" instead of the decks (click to return to the decks)"
                    : $"External Input Mode - broadcasting \"{externalInputMode.DeviceName}\" instead of the decks (click to return to the decks)"
                : deviceLabel != null
                    ? $"External Input Mode - broadcast \"{deviceLabel}\" instead of the decks"
                    : "External Input Mode - pick a device or application in Settings > Line In first";
            externalInputTooltip += "\nYou won't hear it echoed back locally - monitor your mix through your own hardware.";
            if (!string.IsNullOrEmpty(externalInputMode.Error))
                externalInputTooltip += $"\n{externalInputMode.Error}";

            var canStartExternalInput = usingProcessCapture
                ? !string.IsNullOrEmpty(plugin.Configuration.ExternalInputProcessName)
                : !string.IsNullOrEmpty(plugin.Configuration.ExternalInputDeviceId);

            if (DrawIconOnlyButton("##externalInputMode", plugin.Fonts.Icon, FontAwesomeIcon.Plug, externalInputTooltip, buttonSize, Theme.NeutralAccent, externalInputMode.IsActive))
            {
                if (externalInputMode.IsActive)
                {
                    plugin.AudioHostClient.Send(MessageType.StopExternalInputMode, new object());
                }
                else if (canStartExternalInput)
                {
                    plugin.AudioHostClient.Send(MessageType.StartExternalInputMode,
                        new StartExternalInputModeCommand
                        {
                            DeviceId = usingProcessCapture ? string.Empty : plugin.Configuration.ExternalInputDeviceId ?? string.Empty,
                            ProcessName = usingProcessCapture ? plugin.Configuration.ExternalInputProcessName : null,
                            DeviceId2 = string.IsNullOrEmpty(plugin.Configuration.ExternalInputDeviceId2) ? null : plugin.Configuration.ExternalInputDeviceId2,
                        });
                }
            }
        }

        if (showHostLobby)
        {
            NextButtonSlot();
            if (DrawIconOnlyButton("##hostLobby", plugin.Fonts.Icon, FontAwesomeIcon.Users, "Host Lobby - see who's connected and hand off lead", buttonSize, Theme.NeutralAccent, plugin.HostLobbyWindow.IsExpanded))
                plugin.HostLobbyWindow.IsExpanded = !plugin.HostLobbyWindow.IsExpanded;
        }

        if (showBrowseShows)
        {
            NextButtonSlot();
            if (DrawIconOnlyButton("##browseShows", plugin.Fonts.Icon, FontAwesomeIcon.Globe, "Social", buttonSize, Theme.NeutralAccent, false))
            {
                viewBeforeBrowseShows = currentView;
                pendingView = ViewMode.BrowseShows;
                plugin.AudioHostClient.Send(MessageType.RequestPublicShows, new object());
            }
        }

        NextButtonSlot();
        if (DrawIconOnlyButton("##settings", plugin.Fonts.Icon, FontAwesomeIcon.Cog, "Settings", buttonSize, Theme.NeutralAccent, currentView == ViewMode.Settings))
            ToggleSettingsView();
        if (HasUnseenChangelog)
            DrawUnseenChangelogBadge();

        if (showMinimize)
        {
            NextButtonSlot();
            if (DrawIconOnlyButton("##minimize", plugin.Fonts.Icon, FontAwesomeIcon.Compress, "Minimize to a small visualizer", buttonSize, Theme.NeutralAccent, false)
                && currentView != ViewMode.Settings)
                isMinimized = true;
        }

        if (showClose)
        {
            NextButtonSlot();
            if (DrawIconOnlyButton("##close", plugin.Fonts.Icon, FontAwesomeIcon.Times, "Close", buttonSize, Theme.NeutralAccent, false))
            {
                IsOpen = false;
                plugin.HostLobbyWindow.IsExpanded = false;
            }
        }
    }

    /// The stripped header shown on Welcome/JoinShow - just the wordmark, a Close button, and (JoinShow only)
    /// a Back arrow.
    private void DrawOnboardingHeader()
    {
        var buttonSize = 32f * Scale;
        var gap = 10f * Scale;
        var showBack = currentView == ViewMode.JoinShow || currentView == ViewMode.BrowseShows
            || currentView == ViewMode.DjList || currentView == ViewMode.DjProfile || currentView == ViewMode.DjProfileEdit;
        var showClose = !plugin.AudioHostClient.LatestStatus.Broadcast.IsListening;
        var buttonCount = (showBack ? 1 : 0) + (showClose ? 1 : 0);
        var totalWidth = (buttonSize * buttonCount) + (gap * (buttonCount - 1));
        var fullWidth = ImGui.GetContentRegionAvail().X;

        using (plugin.Fonts.Header.PushSafe())
            DrawCenteredLogo(Scale, totalWidth + gap);

        var isFirstButton = true;
        void NextButtonSlot()
        {
            if (isFirstButton)
                ImGui.SameLine(fullWidth - totalWidth + ImGui.GetCursorPosX());
            else
                ImGui.SameLine(0, gap);
            isFirstButton = false;
        }

        if (showBack)
        {
            NextButtonSlot();
            var isProfileEdit = currentView == ViewMode.DjProfileEdit;
            if (DrawIconOnlyButton("##welcomeBack", plugin.Fonts.Icon, FontAwesomeIcon.ArrowLeft, isProfileEdit ? "Save & Back" : "Back", buttonSize, Theme.NeutralAccent, false))
            {
                if (isProfileEdit)
                {
                    TriggerDjProfileSaveThenNavigateTo(viewBeforeDjProfileEdit);
                }
                else
                {
                    pendingView = currentView switch
                    {
                        ViewMode.BrowseShows => viewBeforeBrowseShows,
                        ViewMode.DjList => viewBeforeBrowseShows,
                        ViewMode.DjProfile => ViewMode.DjList,
                        _ => ViewMode.Welcome,
                    };
                }
            }
        }

        if (showClose)
        {
            NextButtonSlot();
            if (DrawIconOnlyButton("##welcomeClose", plugin.Fonts.Icon, FontAwesomeIcon.Times, "Close", buttonSize, Theme.NeutralAccent, false))
            {
                IsOpen = false;
                plugin.HostLobbyWindow.IsExpanded = false;
            }
        }
    }

    /// Pushes both the Proximity/Global toggle and the range together, whichever one just changed - the wire
    /// message always carries both current values (see SetProximityModeCommand) so listeners never need to
    /// reconcile a partial update.
    private void SendProximitySettings() =>
        plugin.AudioHostClient.Send(MessageType.SetProximityMode, new SetProximityModeCommand
        {
            IsProximityAudio = plugin.Configuration.IsProximityAudio,
            ProximityRange = plugin.Configuration.ProximityRange,
        });

    /// A small "LIVE - N listeners" readout at the header row's left edge, mirroring the icon buttons on the
    /// right - drawn via the draw list at an absolute position (like DrawCenteredLogo) rather than through
    /// normal layout flow, so it doesn't disturb the centered wordmark or the right-aligned buttons.
    private void DrawLiveIndicator(Vector2 rowStart, float rowHeight, BroadcastStatusMessage broadcast)
    {
        if (!broadcast.IsLive)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var dotRadius = 5f * Scale;
        var padding = 4f * Scale;
        var dotCenter = rowStart + new Vector2(padding + dotRadius, rowHeight / 2f);
        drawList.AddCircleFilled(dotCenter, dotRadius, ImGui.GetColorU32(LiveGreen));

        var count = broadcast.ListenerCount;
        var text = $"LIVE  -  {count} listener{(count == 1 ? "" : "s")}";
        if (broadcast.LiveSinceUtc.HasValue)
            text += $"  -  {FormatElapsed(DateTime.UtcNow - broadcast.LiveSinceUtc.Value)}";
        var textPos = new Vector2(dotCenter.X + dotRadius + (6f * Scale), rowStart.Y + ((rowHeight - ImGui.GetTextLineHeight()) / 2f));
        drawList.AddText(textPos, ImGui.GetColorU32(Theme.Text), text);
    }

    /// Just the icon, no dark-panel body or ring - unlike TransportButton/PanelButton, these two header
    /// controls aren't meant to look like physical instrument-panel buttons, just clickable symbols.
    private static bool DrawIconOnlyButton(string id, IFontHandle iconFont, FontAwesomeIcon icon, string tooltip, float size, Vector4 accent, bool active)
    {
        var pos = ImGui.GetCursorScreenPos();
        var sizeVec = new Vector2(size, size);
        var center = pos + (sizeVec / 2f);

        var clicked = ImGui.InvisibleButton(id, sizeVec);
        var isActive = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        var pressOffset = isActive ? 1.5f : 0f;

        var restColor = new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, active ? 1f : 0.7f);
        var color = isActive
            ? new Vector4(accent.X, accent.Y, accent.Z, 0.7f)
            : hovered || active ? accent : restColor;

        var drawList = ImGui.GetWindowDrawList();
        using (iconFont.PushSafe())
            UiHelpers.DrawScaledIcon(drawList, icon, center + new Vector2(0f, pressOffset), ImGui.GetColorU32(color));

        if (hovered)
            ImGui.SetTooltip(tooltip);

        return clicked;
    }

    /// The plugin's wordmark: centered, letter-spaced, a cyan-to-orange gradient (echoing each deck's own
    /// accent color), a soft drop shadow for depth, and a thin matching gradient underline.
    private static void DrawCenteredLogo(float scale, float reservedRightWidth)
    {
        const string text = "ECHOMIX";
        var letterSpacing = 8f * scale;

        var fontSize = ImGui.GetFontSize();
        var lineStart = ImGui.GetCursorScreenPos();
        var lineWidth = ImGui.GetContentRegionAvail().X;

        Span<float> widths = stackalloc float[text.Length];
        var totalWidth = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            widths[i] = ImGui.CalcTextSize(text[i].ToString()).X;
            totalWidth += widths[i];
        }
        totalWidth += letterSpacing * (text.Length - 1);

        var idealStartX = MathF.Max(0f, (lineWidth - totalWidth) / 2f);
        var maxStartX = MathF.Max(0f, lineWidth - reservedRightWidth - totalWidth);
        var startX = lineStart.X + MathF.Min(idealStartX, maxStartX);
        var drawList = ImGui.GetWindowDrawList();

        var x = startX;
        for (var i = 0; i < text.Length; i++)
        {
            var t = i / (float)(text.Length - 1);
            var color = DeckGradientColor(t);
            var ch = text[i].ToString();

            drawList.AddText(new Vector2(x, lineStart.Y + 2f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)), ch);
            drawList.AddText(new Vector2(x, lineStart.Y), ImGui.GetColorU32(color), ch);

            x += widths[i] + letterSpacing;
        }

        var underlineY = lineStart.Y + fontSize + 3f;
        const int segments = 24;
        for (var i = 0; i < segments; i++)
        {
            var t0 = i / (float)segments;
            var t1 = (i + 1) / (float)segments;
            var segColor = DeckGradientColor((t0 + t1) / 2f);
            var xa = startX + (t0 * totalWidth);
            var xb = startX + (t1 * totalWidth);
            drawList.AddLine(new Vector2(xa, underlineY), new Vector2(xb, underlineY), ImGui.GetColorU32(new Vector4(segColor.X, segColor.Y, segColor.Z, 0.7f)), 2f);
        }

        ImGui.Dummy(new Vector2(lineWidth, fontSize + 6f));
    }

    private float DeckGap => 12f * Scale;
    private float MasterColumnWidth => 140f * Scale;
    private float PeakColumnHeight => 420f * Scale;

    private void DrawDecksRow(MixerStatusMessage status)
    {
        var decksRowTop = ImGui.GetCursorScreenPos();
        var totalWidth = ImGui.GetContentRegionAvail().X;
        var widthA = MathF.Max(0f, (totalWidth - MasterColumnWidth - (DeckGap * 2f)) / 2f);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 8f) * Scale);

        ImGui.BeginChild("##deckA", new Vector2(widthA, deckColumnHeightCache), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(Scale);
        DrawDeck(DeckId.A, "A", status.DeckA, glowA, Theme.CyanAccent);
        ImGui.EndChild();

        ImGui.PopStyleVar();
        ImGui.SameLine(0, DeckGap);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2f, 8f) * Scale);
        ImGui.BeginChild("##masterColumn", new Vector2(MasterColumnWidth, PeakColumnHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(Scale);
        DrawMasterControls(status);
        ImGui.EndChild();
        ImGui.PopStyleVar();

        ImGui.SameLine(0, DeckGap);

        var widthB = MathF.Max(0f, totalWidth - widthA - MasterColumnWidth - (DeckGap * 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 8f) * Scale);
        ImGui.BeginChild("##deckB", new Vector2(widthB, deckColumnHeightCache), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(Scale);
        DrawDeck(DeckId.B, "B", status.DeckB, glowB, Theme.OrangeAccent);
        ImGui.EndChild();
        ImGui.PopStyleVar();

        DrawWideCrossfader(status, decksRowTop, widthA);
    }

    private float CrossfaderHeight => 26f * Scale;

    /// The crossfader, widened to span the entire gap between the two decks (both DeckGap margins plus the
    /// peak-meter column's own width) instead of being confined to that column alone - drawn directly on the
    /// window, after both deck children and the peak column have closed, rather than living inside any of
    /// them.
    private float CrossfaderCurveToggleDiameter => 16f * Scale;
    private float CrossfaderCurveToggleGap => 8f * Scale;

    private void DrawWideCrossfader(MixerStatusMessage status, Vector2 decksRowTop, float widthA)
    {
        var client = plugin.AudioHostClient;
        var left = decksRowTop.X + widthA;
        var width = (DeckGap * 2f) + MasterColumnWidth;
        var y = tempoBarScreenY;

        ImGui.SetCursorScreenPos(new Vector2(left, y));
        var crossfade = status.CrossfaderPosition;
        if (HorizontalFader.Draw("##crossfader", ref crossfade, 0f, 1f, 0.5f, new Vector2(width, CrossfaderHeight), Theme.CyanAccent, Theme.OrangeAccent, out _))
            client.Send(MessageType.SetCrossfader, new SetCrossfaderCommand { Position = crossfade });

        var toggleY = y + CrossfaderHeight + CrossfaderCurveToggleGap;
        DrawCrossfaderCurveToggles(status, left, width, toggleY);

        var autoDjY = toggleY + CrossfaderCurveToggleDiameter + CrossfaderCurveToggleGap;
        DrawAutoDjToggle(status, left, width, autoDjY);

        ImGui.SetCursorScreenPos(new Vector2(decksRowTop.X, decksRowTop.Y + deckColumnHeightCache));
    }

    /// Sits directly below the curve toggles - lit cyan while on.
    private void DrawAutoDjToggle(MixerStatusMessage status, float left, float width, float y)
    {
        var buttonSize = new Vector2(60f, 20f) * Scale;
        var startX = left + ((width - buttonSize.X) / 2f);
        ImGui.SetCursorScreenPos(new Vector2(startX, y));

        var accent = status.AutoDjEnabled ? Theme.CyanAccent : Theme.NeutralAccent;
        if (PanelButton.Draw("##autoDjToggle", null, null, "AUTO", buttonSize, accent))
        {
            var newEnabled = !status.AutoDjEnabled;
            plugin.AudioHostClient.Send(MessageType.SetAutoDj, new SetAutoDjCommand { Enabled = newEnabled, FadeSeconds = status.AutoDjFadeSeconds });
            plugin.Configuration.AutoDjEnabled = newEnabled;
            plugin.Configuration.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(status.AutoDjEnabled
                ? $"Auto-DJ is on - crossfades automatically about {status.AutoDjFadeSeconds:0}s before each track ends. Right-click to adjust."
                : "Auto-DJ is off - click to enable, right-click to configure the fade first.");
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup("##autoDjPopup");

        if (ImGui.BeginPopup("##autoDjPopup"))
        {
            ImGui.SetWindowFontScale(Scale);
            ImGui.TextDisabled("Auto-DJ fade duration");
            var fadeSeconds = status.AutoDjFadeSeconds;
            ImGui.SetNextItemWidth(160f * Scale);
            if (ImGui.SliderFloat("##autoDjFadeSeconds", ref fadeSeconds, 2f, 15f, "%.0fs"))
            {
                plugin.AudioHostClient.Send(MessageType.SetAutoDj, new SetAutoDjCommand { Enabled = status.AutoDjEnabled, FadeSeconds = fadeSeconds });
                plugin.Configuration.AutoDjFadeSeconds = fadeSeconds;
                plugin.Configuration.Save();
            }

            ImGui.EndPopup();
        }
    }

    /// Three small toggle circles below the crossfader, ordered Cut/Power/Linear so the true default - Power
    /// - sits in the visual middle.
    private void DrawCrossfaderCurveToggles(MixerStatusMessage status, float left, float width, float y)
    {
        var diameter = CrossfaderCurveToggleDiameter;
        var gap = CrossfaderCurveToggleGap;
        var totalWidth = (diameter * 3f) + (gap * 2f);
        var startX = left + ((width - totalWidth) / 2f);
        var active = status.CrossfaderCurve;
        var dt = ImGui.GetIO().DeltaTime;

        var cutOn = active == CrossfaderCurve.Cut;
        var powerOn = active == CrossfaderCurve.Power || active == null;
        var linearOn = active == CrossfaderCurve.Linear;

        const float GlowLerpSpeed = 12f;
        crossfaderCurveGlowC = UiHelpers.Lerp(crossfaderCurveGlowC, cutOn ? 1f : 0f, GlowLerpSpeed, dt);
        crossfaderCurveGlowP = UiHelpers.Lerp(crossfaderCurveGlowP, powerOn ? 1f : 0f, GlowLerpSpeed, dt);
        crossfaderCurveGlowL = UiHelpers.Lerp(crossfaderCurveGlowL, linearOn ? 1f : 0f, GlowLerpSpeed, dt);

        if (DrawCrossfaderCurveToggle(new Vector2(startX, y), diameter, "C", "Cut - each deck drops out fast on its way past center, minimal overlap", crossfaderCurveGlowC))
            SendCrossfaderCurve(cutOn ? null : CrossfaderCurve.Cut);

        if (DrawCrossfaderCurveToggle(new Vector2(startX + diameter + gap, y), diameter, "P", "Equal Power - constant loudness through the sweep (the crossfader's original feel)", crossfaderCurveGlowP))
            SendCrossfaderCurve(null);

        if (DrawCrossfaderCurveToggle(new Vector2(startX + ((diameter + gap) * 2f), y), diameter, "L", "Linear - a plain, even ramp between decks", crossfaderCurveGlowL))
            SendCrossfaderCurve(linearOn ? null : CrossfaderCurve.Linear);
    }

    private void SendCrossfaderCurve(CrossfaderCurve? curve)
    {
        plugin.AudioHostClient.Send(MessageType.SetCrossfaderCurve, new SetCrossfaderCurveCommand { Curve = curve });
        plugin.Configuration.CrossfaderCurve = curve;
        plugin.Configuration.Save();
    }

    /// A small glassy toggle circle - drop shadow, a soft accent bloom that grows with `glow`, a body that
    /// blends from dark panel to accent-filled, and a ring that thickens and brightens as it lights up -
    /// meant to read as a genuine lit indicator rather than a flat colored dot, echoing the glow/shadow
    /// language Theme.BeginCard and PanelButton already use elsewhere.
    private bool DrawCrossfaderCurveToggle(Vector2 pos, float diameter, string label, string tooltip, float glow)
    {
        var accent = Theme.NeutralAccent;

        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##crossfaderCurve" + label, new Vector2(diameter, diameter));
        var hovered = ImGui.IsItemHovered();

        var center = pos + new Vector2(diameter / 2f, diameter / 2f);
        var radius = diameter / 2f;
        var drawList = ImGui.GetWindowDrawList();

        if (glow > 0.02f)
        {
            for (var i = 3; i >= 1; i--)
            {
                var haloRadius = radius + (i * 2.2f * Scale);
                var haloAlpha = glow * 0.1f * (4 - i);
                drawList.AddCircleFilled(center, haloRadius, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, haloAlpha)));
            }
        }

        drawList.AddCircleFilled(center + new Vector2(0f, 1.5f * Scale), radius, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)));

        var bodyColor = Vector4.Lerp(Theme.Panel, accent, glow * 0.6f);
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(bodyColor));

        var ringAlpha = MathF.Min(1f, 0.35f + (glow * 0.65f) + (hovered ? 0.2f : 0f));
        var ringThickness = 1.3f + (glow * 0.9f);
        drawList.AddCircle(center, radius, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, ringAlpha)), 0, ringThickness);

        var textColor = Vector4.Lerp(new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.75f), Theme.Background, glow);
        var textSize = ImGui.CalcTextSize(label);
        var centeringNudgeX = label == "C" ? -textSize.X * 0.18f : 0f;
        var textPos = center - (textSize / 2f) + new Vector2(centeringNudgeX, 0f);
        textPos = new Vector2(MathF.Round(textPos.X), MathF.Round(textPos.Y));
        drawList.AddText(textPos, ImGui.GetColorU32(textColor), label);

        if (hovered)
            ImGui.SetTooltip(tooltip);

        return clicked;
    }

    /// The peak meters, sitting between the two decks (not tied to either one's own single accent - themed
    /// cyan/orange for both instead; the per-deck gain knobs live under each deck's own FILTER knob, and
    /// master volume no longer has a dedicated control here).
    private void DrawMasterControls(MixerStatusMessage status)
    {
        var columnWidth = ImGui.GetContentRegionAvail().X;
        var columnTop = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(new Vector2(columnTop.X, columnTop.Y + displayTopYOffsetCache));
        DrawPeakMeterCard(columnWidth, DisplayHeight, status);
    }

    private float PeakCardWidth => 100f * Scale;

    /// A small "digital display" card (same border/shadow/chasing-highlight treatment as the two deck
    /// displays) holding a pair of vertical peak meters, one per deck - fills the gap between the two decks'
    /// EQ knob columns with something functional instead of empty space.
    private void DrawPeakMeterCard(float width, float height, MixerStatusMessage status)
    {
        var glow = MathF.Max(glowA, glowB);

        var outerOrigin = ImGui.GetCursorScreenPos();
        var cardSize = new Vector2(MathF.Min(PeakCardWidth, width), MathF.Max(20f * Scale, height));
        var cardOrigin = new Vector2(outerOrigin.X + MathF.Max(0f, (width - cardSize.X) / 2f), outerOrigin.Y);
        ImGui.SetCursorScreenPos(cardOrigin);

        Theme.BeginCard("##peakCard", cardSize, glow, Theme.NeutralAccent, Scale);
        var contentTopY = ImGui.GetCursorScreenPos().Y;

        var barWidth = 16f * Scale;
        var barGap = 14f * Scale;
        var barsTotalWidth = (barWidth * 2f) + barGap;
        var barsLeftX = cardOrigin.X + MathF.Max(0f, (cardSize.X - barsTotalWidth) / 2f);
        var barSize = new Vector2(barWidth, MathF.Max(10f * Scale, cardSize.Y - (24f * Scale)));

        ImGui.SetCursorScreenPos(new Vector2(barsLeftX, contentTopY));
        PeakMeter.Draw("##peakA", barSize, status.DeckA.PeakLevel);
        ImGui.SetCursorScreenPos(new Vector2(barsLeftX + barWidth + barGap, contentTopY));
        PeakMeter.Draw("##peakB", barSize, status.DeckB.PeakLevel);

        Theme.EndCard();

        ImGui.SetCursorScreenPos(new Vector2(outerOrigin.X, outerOrigin.Y + cardSize.Y));
    }

    private float FaderWidth => 30f * Scale;
    private float DisplayHeight => 360f * Scale;
    private float ElementGap => 12f * Scale;
    private const int PadsPerDeck = 4;    private float PadGap => 8f * Scale;
    private float PadHeight => 50f * Scale;
    private float TransportButtonSize => 40f * Scale;
    private float TempoSliderHeight => 26f * Scale;    private const float TempoSliderRangePercent = 16f;

    private void DrawDeck(DeckId id, string label, DeckStatus deck, float glow, Vector4 accent)
    {
        var isDeckA = id == DeckId.A;
        var deckTop = ImGui.GetCursorScreenPos();

        var knobColumnWidth = MathF.Max(44f * Scale, ImGui.CalcTextSize("FILTER").X);
        var displayWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - knobColumnWidth - FaderWidth - (ElementGap * 2f));
        var displayStartX = isDeckA ? FaderWidth + ElementGap : knobColumnWidth + ElementGap;

        DrawTransportButtons(id, deck, accent, isDeckA);
        ImGui.Spacing();

        var beforeDisplayY = ImGui.GetCursorScreenPos().Y;
        displayTopYOffsetCache = beforeDisplayY - deckTop.Y;
        DrawDisplayAndKnobs(id, label, deck, glow, accent, isDeckA, displayWidth, knobColumnWidth);
        ImGui.Dummy(new Vector2(0f, 8f * Scale));
        DrawTempoSlider(id, deck, accent, displayStartX, displayWidth);
        ImGui.Dummy(new Vector2(0f, 10f * Scale));
        var deckPadBase = isDeckA ? 0 : PadsPerDeck;
        DrawSoundPadRow(deckPadBase, accent, displayStartX, displayWidth);

        deckColumnHeightCache = (ImGui.GetCursorScreenPos().Y - deckTop.Y) + (16f * Scale);
    }

    /// Manual tempo control spanning the full display width, right below it - the same hardware-fader look as
    /// the crossfader/gain faders (just single-toned, since it belongs to one deck rather than blending two),
    /// with the same right-click-to-reset-to-default convention.
    private void DrawTempoSlider(DeckId id, DeckStatus deck, Vector4 accent, float startX, float displayWidth)
    {
        var client = plugin.AudioHostClient;
        var isDeckA = id == DeckId.A;
        var spotifyMode = client.LatestStatus.SpotifyMode;
        var externalInputMode = client.LatestStatus.ExternalInputMode;
        var showSpotify = isDeckA && spotifyMode.IsActive && !string.IsNullOrEmpty(spotifyMode.NowPlayingTitle);
        var showExternalInput = isDeckA ? externalInputMode.IsActive : externalInputMode.IsSecondActive;

        var rowPos = ImGui.GetCursorScreenPos();
        var minRatio = 1f - (TempoSliderRangePercent / 100f);
        var maxRatio = 1f + (TempoSliderRangePercent / 100f);
        var ratio = deck.TempoRatio;

        var percentText = $"{(ratio * 100f) - 100f:+0.0;-0.0}%";
        var label = deck.Bpm is > 0f ? $"{deck.Bpm * ratio:0.#} BPM  ({percentText})" : $"Tempo  {percentText}";

        ImGui.BeginDisabled(!deck.HasTrack || showSpotify || showExternalInput);

        ImGui.SetCursorScreenPos(new Vector2(rowPos.X + startX, rowPos.Y));
        ImGui.TextColored(accent, label);

        ImGui.SetCursorScreenPos(new Vector2(rowPos.X + startX, ImGui.GetCursorScreenPos().Y));
        tempoBarScreenY = ImGui.GetCursorScreenPos().Y;
        var barSize = new Vector2(displayWidth, TempoSliderHeight);
        if (HorizontalFader.Draw("##tempo" + id, ref ratio, minRatio, maxRatio, 1f, barSize, accent, accent, out _, midValue: 1f))
            client.Send(MessageType.SetDeckTempo, new SetDeckTempoCommand { Deck = id, Ratio = ratio });

        ImGui.EndDisabled();
    }

    /// A row of 4 sound-effect pads that together exactly span the display's width (wide rectangles, not
    /// square) - left-click plays the assigned sound over whatever's already playing (or opens the upload
    /// dialog if the pad is empty), right-click opens a menu to upload a sound or rename the pad.
    private void DrawSoundPadRow(int baseIndex, Vector4 accent, float startX, float displayWidth)
    {
        var pads = plugin.AudioHostClient.LatestSoundPads;
        var padWidth = MathF.Max(1f, (displayWidth - (PadGap * (PadsPerDeck - 1))) / PadsPerDeck);
        var padSize = new Vector2(padWidth, PadHeight);

        var rowPos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(rowPos.X + startX, rowPos.Y));

        for (var i = 0; i < PadsPerDeck; i++)
        {
            var index = baseIndex + i;
            var pad = index < pads.Count ? pads[index] : new SoundPadDto { Label = $"PAD {index + 1}" };
            if (i > 0)
                ImGui.SameLine(0, PadGap);
            DrawSoundPad(index, pad, padSize, accent);
        }
    }

    private void DrawSoundPad(int index, SoundPadDto pad, Vector2 size, Vector4 accent)
    {
        var client = plugin.AudioHostClient;
        var hasSound = !string.IsNullOrEmpty(pad.FilePath);
        var id = $"##pad{index}";

        if (SoundPadButton.Draw(id, pad.Label, hasSound, pad.Looping, size, accent, out var middleClicked))
        {
            if (hasSound)
                client.Send(MessageType.PlaySoundPad, new SoundPadIndexCommand { PadIndex = index });
            else
                OpenSoundPadUploadDialog(index);
        }

        if (middleClicked && hasSound)
            client.Send(MessageType.SetSoundPadLoop, new SetSoundPadLoopCommand { PadIndex = index, Looping = !pad.Looping });

        if (ImGui.BeginPopupContextItem(id))
        {
            if (ImGui.IsWindowAppearing())
            {
                padLabelEditBuffers[index] = pad.Label;
                padLoopIntervalBuffers[index] = pad.LoopIntervalSeconds;
                padVolumeBuffers[index] = pad.Volume;
            }

            if (ImGui.MenuItem("Upload Sound Effect"))
                OpenSoundPadUploadDialog(index);

            if (hasSound && ImGui.MenuItem("Remove Sound Effect"))
            {
                client.Send(MessageType.RemoveSoundPad, new SoundPadIndexCommand { PadIndex = index });
                ImGui.CloseCurrentPopup();
            }

            ImGui.Separator();
            ImGui.SetNextItemWidth(160 * Scale);
            ImGui.InputText("##padLabel" + index, ref padLabelEditBuffers[index], 32);
            ImGui.SameLine();
            if (PanelButton.Draw("##setPadLabel" + index, null, null, "Set", new Vector2(40, 24) * Scale, accent) && !string.IsNullOrWhiteSpace(padLabelEditBuffers[index]))
            {
                client.Send(MessageType.SetSoundPadLabel, new SetSoundPadLabelCommand { PadIndex = index, Label = padLabelEditBuffers[index].Trim() });
                ImGui.CloseCurrentPopup();
            }

            ImGui.Separator();
            ImGui.SetNextItemWidth(100 * Scale);
            ImGui.InputFloat("Interval (sec)##" + index, ref padLoopIntervalBuffers[index], 0f, 0f, "%.1f");
            padLoopIntervalBuffers[index] = MathF.Max(0.1f, padLoopIntervalBuffers[index]);
            ImGui.SameLine();
            if (PanelButton.Draw("##setPadInterval" + index, null, null, "Set", new Vector2(40, 24) * Scale, accent))
                client.Send(MessageType.SetSoundPadLoopInterval, new SetSoundPadLoopIntervalCommand { PadIndex = index, IntervalSeconds = padLoopIntervalBuffers[index] });

            ImGui.SetNextItemWidth(100 * Scale);
            ImGui.InputFloat("Volume##" + index, ref padVolumeBuffers[index], 0f, 0f, "%.2f");
            padVolumeBuffers[index] = Math.Clamp(padVolumeBuffers[index], 0f, 2f);
            ImGui.SameLine();
            if (PanelButton.Draw("##setPadVolume" + index, null, null, "Set", new Vector2(40, 24) * Scale, accent))
                client.Send(MessageType.SetSoundPadVolume, new SetSoundPadVolumeCommand { PadIndex = index, Volume = padVolumeBuffers[index] });

            ImGui.EndPopup();
        }
    }

    private void OpenSoundPadUploadDialog(int index)
    {
        soundPadFileDialogManager.OpenFileDialog(
            "Select a sound effect",
            "Audio files{.mp3,.wav,.wma,.aac,.m4a,.flac,.ogg}",
            (success, paths) =>
            {
                if (success && paths.Count > 0)
                    plugin.AudioHostClient.Send(MessageType.UploadSoundPad, new UploadSoundPadCommand { PadIndex = index, SourceFilePath = paths[0] });
            },
            1,
            null,
            false);
    }

    private void OpenShowImageDialog()
    {
        showImageFileDialogManager.OpenFileDialog(
            "Select a show/venue image",
            "Image files{.png,.jpg,.jpeg}",
            (success, paths) =>
            {
                if (success && paths.Count > 0)
                    _ = imageCropDialog.OpenAsync(paths[0], ShowImageProcessor.TargetWidth, ShowImageProcessor.TargetHeight, FinishShowImageUpload);
            },
            1,
            null,
            false);
    }

    /// Builds a preview texture from the already-cropped bytes ImageCropDialog hands back so the DJ sees
    /// exactly what listeners will see, then only sends SetShowImage right away if there's actually a live
    /// broadcast connection for AudioHost to send it over (see
    /// IpcServer.broadcastHost/BroadcastHostConnection.SendShowImageAsync - both require IsLive, so sending
    /// before that would silently go nowhere) - otherwise STAGES the durable path, same "no identity to
    /// attach an image to yet" pattern FinishDjProfileImageStaging uses for a DJ profile's avatar/banner
    /// (there, no ProfileId yet; here, no live RoomCode/connection yet).
    private async void FinishShowImageUpload(byte[] processed)
    {
        try
        {
            var wrap = await Plugin.TextureProvider.CreateFromImageAsync(processed);
            showImagePreview?.Dispose();
            showImagePreview = wrap;
            showImageError = null;
        }
        catch (Exception ex)
        {
            showImageError = $"Couldn't preview that image: {ex.Message}";
        }

        try
        {
            await File.WriteAllBytesAsync(LastShowImagePath, processed);

            if (plugin.AudioHostClient.LatestStatus.Broadcast.IsLive)
                plugin.AudioHostClient.Send(MessageType.SetShowImage, new SetShowImageCommand { SourceFilePath = LastShowImagePath });
            else
                stagedShowImagePath = LastShowImagePath;
        }
        catch (Exception ex)
        {
            showImageError = $"Couldn't upload that image: {ex.Message}";
        }
    }

    /// Restores the Broadcast tab's image preview from the durably-saved last show image (see
    /// LastShowImagePath) on plugin start, so a returning DJ sees "yep, this is still your show's image"
    /// without needing to re-pick it just to confirm - fire-and-forget from the constructor, same shape as
    /// every other async image load in this file.
    private async void LoadLastShowImagePreview()
    {
        try
        {
            if (!File.Exists(LastShowImagePath))
                return;

            var bytes = await File.ReadAllBytesAsync(LastShowImagePath);
            var wrap = await Plugin.TextureProvider.CreateFromImageAsync(bytes);
            showImagePreview?.Dispose();
            showImagePreview = wrap;
        }
        catch
        {
        }
    }

    /// The digital display is its own standalone card (border + shadow + play glow) - nothing but the track
    /// title, seek bar, timer, and visualizer live inside it.
    private void DrawDisplayAndKnobs(DeckId id, string label, DeckStatus deck, float glow, Vector4 accent, bool isDeckA, float displayWidth, float knobColumnWidth)
    {
        var client = plugin.AudioHostClient;
        var faderSize = new Vector2(FaderWidth, DisplayHeight);
        var gain = deck.Gain;
        var rowTop = ImGui.GetCursorScreenPos();

        void DrawFader()
        {
            var changed = Fader.Draw("##gain" + label, ref gain, 0f, 1.5f, 1f, faderSize, out var activated, accent, midValue: 1f);

            if (activated && ImGui.GetIO().KeyShift)
            {
                mirrorGainDragDeck = id;
                mirrorGainDragSelfBaseline = gain;
                var otherStatus = isDeckA ? client.LatestStatus.DeckB : client.LatestStatus.DeckA;
                mirrorGainDragOtherBaseline = otherStatus.Gain;
            }

            if (changed)
            {
                client.Send(MessageType.SetGain, new SetGainCommand { Deck = id, Gain = gain });

                if (mirrorGainDragDeck == id)
                {
                    var otherId = isDeckA ? DeckId.B : DeckId.A;
                    var mirroredGain = Math.Clamp(mirrorGainDragOtherBaseline - (gain - mirrorGainDragSelfBaseline), 0f, 1.5f);
                    client.Send(MessageType.SetGain, new SetGainCommand { Deck = otherId, Gain = mirroredGain });
                }
            }

            if (mirrorGainDragDeck == id && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
                mirrorGainDragDeck = null;
        }

        void DrawDisplay()
        {
            Theme.BeginCard("##display" + label, new Vector2(displayWidth, DisplayHeight), glow, accent, Scale);

            var spotifyMode = client.LatestStatus.SpotifyMode;
            var externalInputMode = client.LatestStatus.ExternalInputMode;

            var broadcastStatus = client.LatestStatus.Broadcast;
            var showLeadDeck = broadcastStatus.IsLive && !broadcastStatus.IsLead;
            var showSpotify = !showLeadDeck && isDeckA && spotifyMode.IsActive && !string.IsNullOrEmpty(spotifyMode.NowPlayingTitle);
            var showExternalInput = !showLeadDeck && (isDeckA ? externalInputMode.IsActive : externalInputMode.IsSecondActive);

            var actualPosition = showLeadDeck ? (float)(isDeckA ? broadcastStatus.NowPlayingPositionSecondsA : broadcastStatus.NowPlayingPositionSecondsB)
                : showSpotify ? (float)spotifyMode.NowPlayingPositionSeconds
                : showExternalInput ? 1f
                : (float)deck.PositionSeconds;
            ref var pendingSeek = ref (id == DeckId.A ? ref pendingSeekA : ref pendingSeekB);
            if (pendingSeek.HasValue && Math.Abs(actualPosition - pendingSeek.Value) < 0.5)
                pendingSeek = null;
            string title;
            float duration, cuePoint;
            if (showLeadDeck)
            {
                if (!isDeckA && broadcastStatus.IsHostSpotifyModeActive)
                {
                    title = "Deck B (unused - Spotify Mode)";
                    duration = 0.01f;
                }
                else
                {
                    var leadTitle = isDeckA ? broadcastStatus.NowPlayingTitleA : broadcastStatus.NowPlayingTitleB;
                    title = string.IsNullOrEmpty(leadTitle) ? "No track loaded" : leadTitle;
                    duration = (float)Math.Max(isDeckA ? broadcastStatus.NowPlayingDurationSecondsA : broadcastStatus.NowPlayingDurationSecondsB, 0.01);
                }
                cuePoint = 0f;
            }
            else if (showSpotify)
            {
                var artistPart = string.IsNullOrEmpty(spotifyMode.NowPlayingArtist) ? string.Empty : $" - {spotifyMode.NowPlayingArtist}";
                title = spotifyMode.NowPlayingTitle! + artistPart;
                duration = (float)Math.Max(spotifyMode.NowPlayingDurationSeconds, 0.01);
                cuePoint = 0f;
            }
            else if (showExternalInput)
            {
                var deviceName = isDeckA ? externalInputMode.DeviceName : externalInputMode.DeviceName2;
                title = deviceName != null ? $"External Input: {deviceName}" : "External Input";
                duration = 1f;
                cuePoint = 0f;
            }
            else
            {
                title = deck.HasTrack ? deck.TrackTitle! : "No track loaded";
                duration = (float)Math.Max(deck.DurationSeconds, 0.01);
                cuePoint = (float)deck.CuePointSeconds;
            }

            var position = pendingSeek ?? actualPosition;

            ImGui.TextColored(accent, TruncateToWidth(title, displayWidth - (12f * Scale)));
            ImGui.Spacing();

            var barWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - (12f * Scale));
            if (SeekBar.Draw("##seek" + label, ref position, 0f, duration, new Vector2(barWidth, 8f * Scale), accent, cuePoint, interactive: !showLeadDeck && !showSpotify && !showExternalInput))
            {
                pendingSeek = position;
                client.Send(MessageType.SetPosition, new SetPositionCommand { Deck = id, PositionSeconds = position });
            }

            var elapsed = showExternalInput ? "● LIVE" : FormatTime(position);
            var total = showExternalInput ? string.Empty : FormatTime(duration);
            ImGui.TextDisabled(elapsed);
            ImGui.SameLine(barWidth - ImGui.CalcTextSize(total).X);
            ImGui.TextDisabled(total);

            if (!showLeadDeck && !showSpotify && !showExternalInput)
            {
                var bpmText = deck.Bpm is > 0f
                    ? deck.SyncEnabled && deck.TempoRatio != 1f
                        ? $"{deck.Bpm:0.#} BPM → {deck.Bpm * deck.TempoRatio:0.#} BPM"
                        : $"{deck.Bpm:0.#} BPM"
                    : "BPM: --";
                ImGui.TextDisabled(bpmText);
            }

            ImGui.Spacing();
            var avail = ImGui.GetContentRegionAvail();
            var visualizerSize = new Vector2(MathF.Max(1f, avail.X - (12f * Scale)), MathF.Max(1f, avail.Y - (12f * Scale)));
            var spectrum = showLeadDeck
                ? (isDeckA ? broadcastStatus.LeadSpectrumBandsA : broadcastStatus.LeadSpectrumBandsB) ?? Array.Empty<float>()
                : isDeckA ? client.LatestSpectrumA : client.LatestSpectrumB;
            var deckStyle = (VisualizerWidget.Style)plugin.Configuration.DeckVisualizerStyle;
            VisualizerWidget.Draw(label, spectrum, visualizerSize, accent, deckStyle, plugin.Configuration.DeckVisualizerSensitivity);
            Theme.EndCard();
        }

        void DrawKnobs()
        {
            const int knobCount = 5;
            var knobRadius = 22f * Scale;
            var groupTop = ImGui.GetCursorScreenPos();
            var totalUnits = knobCount * knobUnitHeightCache;
            var gapSize = MathF.Max(0f, (DisplayHeight - totalUnits) / (knobCount - 1));
            var step = knobUnitHeightCache + gapSize;

            void PositionKnob(int index) => ImGui.SetCursorScreenPos(new Vector2(groupTop.X, groupTop.Y + (index * step)));

            PositionKnob(0);
            var high = deck.HighGainDb;
            if (DrawLabeledKnob("HIGH##" + label, ref high, -15f, 15f, 0f, accent, knobRadius))
                client.Send(MessageType.SetEq, new SetEqCommand { Deck = id, Band = EqBand.High, GainDb = high });
            knobUnitHeightCache = ImGui.GetItemRectSize().Y;

            PositionKnob(1);
            var mid = deck.MidGainDb;
            if (DrawLabeledKnob("MID##" + label, ref mid, -15f, 15f, 0f, accent, knobRadius))
                client.Send(MessageType.SetEq, new SetEqCommand { Deck = id, Band = EqBand.Mid, GainDb = mid });

            PositionKnob(2);
            var low = deck.LowGainDb;
            if (DrawLabeledKnob("LOW##" + label, ref low, -15f, 15f, 0f, accent, knobRadius))
                client.Send(MessageType.SetEq, new SetEqCommand { Deck = id, Band = EqBand.Low, GainDb = low });

            PositionKnob(3);
            var filter = deck.FilterKnob;
            if (DrawLabeledKnob("FILTER##" + label, ref filter, -1f, 1f, 0f, accent, knobRadius))
                client.Send(MessageType.SetFilter, new SetFilterCommand { Deck = id, Knob = filter });

            PositionKnob(4);
            var trim = deck.Trim;
            if (DrawLabeledKnob("GAIN##" + label, ref trim, 0f, 2f, 1f, accent, knobRadius))
                client.Send(MessageType.SetTrim, new SetTrimCommand { Deck = id, Trim = trim });
        }

        if (isDeckA)
        {
            DrawFader();
            ImGui.SameLine(0, ElementGap);
            DrawDisplay();
        }
        else
        {
            ImGui.SetCursorScreenPos(new Vector2(rowTop.X + knobColumnWidth + ElementGap, rowTop.Y));
            DrawDisplay();
            ImGui.SameLine(0, ElementGap);
            DrawFader();
        }

        var knobsX = isDeckA
            ? rowTop.X + FaderWidth + ElementGap + displayWidth + ElementGap
            : rowTop.X;
        ImGui.SetCursorScreenPos(new Vector2(knobsX, rowTop.Y));
        DrawKnobs();

        ImGui.SetCursorScreenPos(new Vector2(rowTop.X, rowTop.Y + DisplayHeight));
    }

    /// Play/pause, Next Song, the autoplay toggle, and the two cue buttons, sharing the deck's title row on
    /// its outer edge (top-left for Deck A, top-right for Deck B), colored with the deck's own accent instead
    /// of the generic purple.
    private void DrawTransportButtons(DeckId id, DeckStatus deck, Vector4 accent, bool isDeckA)
    {
        var client = plugin.AudioHostClient;
        var buttonSize = TransportButtonSize;
        var gap = 10f * Scale;

        var totalWidth = (buttonSize * 6f) + (gap * 5f);
        if (!isDeckA)
            ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X - totalWidth + ImGui.GetCursorPosX());

        var playIcon = deck.IsPlaying ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play;
        if (TransportButton.Draw("##play" + id, plugin.Fonts.Icon, playIcon, deck.IsPlaying ? "Pause" : "Play", buttonSize, accent, primary: true))
            client.Send(MessageType.TogglePlay, new DeckCommand { Deck = id });
        ImGui.SameLine(0, gap);
        if (TransportButton.Draw("##nextSong" + id, plugin.Fonts.Icon, FontAwesomeIcon.StepForward, "Next song", buttonSize, accent, primary: false))
            client.Send(MessageType.UnloadDeck, new DeckCommand { Deck = id });
        ImGui.SameLine(0, gap);
        var autoplayTooltip = deck.AutoplayEnabled ? "Autoplay next song: On" : "Autoplay next song: Off";
        if (TransportButton.Draw("##autoplay" + id, plugin.Fonts.Icon, FontAwesomeIcon.Forward, autoplayTooltip, buttonSize, accent, primary: false, toggledOn: deck.AutoplayEnabled))
            client.Send(MessageType.SetDeckAutoplay, new SetDeckAutoplayCommand { Deck = id, Enabled = !deck.AutoplayEnabled });
        ImGui.SameLine(0, gap);
        if (TransportButton.Draw("##cueJump" + id, plugin.Fonts.Icon, FontAwesomeIcon.StepBackward, "Jump to cue", buttonSize, accent, primary: false))
            client.Send(MessageType.JumpToCue, new DeckCommand { Deck = id });
        ImGui.SameLine(0, gap);
        if (TransportButton.Draw("##cueSet" + id, plugin.Fonts.Icon, FontAwesomeIcon.MapMarkerAlt, "Set cue here", buttonSize, accent, primary: false))
            client.Send(MessageType.SetCue, new DeckCommand { Deck = id });
        ImGui.SameLine(0, gap);

        var otherDeck = id == DeckId.A ? client.LatestStatus.DeckB : client.LatestStatus.DeckA;
        var syncAvailable = deck.HasTrack && deck.Bpm is > 0f && otherDeck.HasTrack && otherDeck.Bpm is > 0f;
        var syncTooltip = deck.SyncEnabled
            ? $"Sync: On ({(deck.TempoRatio * 100f) - 100f:+0.0;-0.0}%)"
            : syncAvailable ? "Sync to other deck's BPM" : "Sync unavailable - both decks need a known BPM";
        ImGui.BeginDisabled(!syncAvailable && !deck.SyncEnabled);
        if (TransportButton.Draw("##sync" + id, plugin.Fonts.Icon, FontAwesomeIcon.Link, syncTooltip, buttonSize, accent, primary: false, toggledOn: deck.SyncEnabled))
            client.Send(MessageType.SetDeckSync, new SetDeckSyncCommand { Deck = id, Enabled = !deck.SyncEnabled });
        ImGui.EndDisabled();
    }

    private float QueueColumnWidth => 230f * Scale;
    private float BottomSectionPadding => 10f * Scale;

    /// Right-aligns an element against a fixed reserved margin (this child's own WindowPadding plus a
    /// vertical scrollbar's width) instead of ImGui.GetContentRegionAvail() - that value only shrinks once a
    /// scrollbar actually starts rendering, which was shifting these buttons sideways the moment enough rows
    /// appeared to need one.
    private float RightAlignedX(float outerWidth, float elementsWidth) =>
        outerWidth - BottomSectionPadding - ImGui.GetStyle().ScrollbarSize - elementsWidth;

    /// The redesigned bottom section: a deck-colored "upcoming songs" queue on each outer edge (mirroring the
    /// decks above them) with a playlist browser in between - replaces the old single playlist
    /// sidebar+track-list, which has moved to Settings > Library since playlist creation/upload/management is
    /// a setup task, not something done mid-set.
    private void DrawBottomSection(MixerStatusMessage status)
    {
        var client = plugin.AudioHostClient;
        var queues = client.LatestDeckQueues;
        var totalWidth = ImGui.GetContentRegionAvail().X;
        var bottomHeight = MathF.Max(200f, ImGui.GetContentRegionAvail().Y);
        var middleWidth = MathF.Max(0f, totalWidth - (QueueColumnWidth * 2f) - (DeckGap * 2f));

        DrawDeckQueueColumn(DeckId.A, "A", status.DeckA, queues.QueueA.Tracks, Theme.CyanAccent, QueueColumnWidth, bottomHeight);
        ImGui.SameLine(0, DeckGap);
        DrawPlaylistBrowser(middleWidth, bottomHeight);
        ImGui.SameLine(0, DeckGap);
        DrawDeckQueueColumn(DeckId.B, "B", status.DeckB, queues.QueueB.Tracks, Theme.OrangeAccent, QueueColumnWidth, bottomHeight);
    }

    /// A deck's "now playing" slot (with an unload button) plus its upcoming-songs queue - what's assigned to
    /// play next once the current track finishes (see DeckQueueManager on the host side).
    private void DrawDeckQueueColumn(DeckId id, string label, DeckStatus deckStatus, IReadOnlyList<TrackDto> queue, Vector4 accent, float width, float height)
    {
        var client = plugin.AudioHostClient;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(BottomSectionPadding, BottomSectionPadding));
        ImGui.BeginChild($"##queue{id}", new Vector2(width, height), false, ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(Scale);

        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextColored(accent, $"DECK {label} PLAYING");
        ImGui.Separator();
        ImGui.Spacing();

        if (!deckStatus.HasTrack)
        {
            ImGui.TextDisabled("Nothing loaded.");
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextWrapped(deckStatus.TrackTitle ?? string.Empty);
            ImGui.SameLine(RightAlignedX(width, 26f));
            if (PanelButton.Draw("##unloadDeck", plugin.Fonts.Icon, FontAwesomeIcon.Times, null, new Vector2(24, 22) * Scale, accent))
                client.Send(MessageType.UnloadDeck, new DeckCommand { Deck = id });
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextColored(accent, $"DECK {label} UPCOMING");
        ImGui.Separator();
        ImGui.Spacing();

        if (queue.Count == 0)
        {
            ImGui.TextDisabled("Nothing queued.");
        }
        else
        {
            var previewingPath = client.LatestStatus.PreviewingFilePath;

            for (var i = 0; i < queue.Count; i++)
            {
                var track = queue[i];
                ImGui.PushID(i);
                var isPreviewing = previewingPath == track.FilePath;

                ImGui.AlignTextToFramePadding();
                ImGui.TextWrapped(track.Title);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
                {
                    if (isPreviewing)
                        client.Send(MessageType.StopPreviewTrack, new object());
                    else
                        client.Send(MessageType.PreviewTrack, new PreviewTrackCommand { FilePath = track.FilePath });
                }
                else if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(isPreviewing
                        ? "Middle-click to stop previewing"
                        : "Middle-click to preview (only you can hear it)");
                }

                ImGui.SameLine(RightAlignedX(width, 26f));
                if (PanelButton.Draw("##removeQueued", plugin.Fonts.Icon, FontAwesomeIcon.Times, null, new Vector2(24, 22) * Scale, accent))
                    client.Send(MessageType.RemoveFromDeckQueue, new RemoveFromDeckQueueCommand { Deck = id, Index = i });

                ImGui.TextDisabled(FormatTime(track.DurationSeconds));
                if (isPreviewing)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(accent, "● Cueing");
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PopID();
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    /// The middle of the bottom section: pick a playlist, see its songs (with length), and assign any of them
    /// to Deck A or B's upcoming queue.
    private void DrawPlaylistBrowser(float width, float height)
    {
        var client = plugin.AudioHostClient;
        UpdatePlaylistSectionFade(ImGui.GetIO().DeltaTime);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(BottomSectionPadding, BottomSectionPadding));
        ImGui.BeginChild("##playlistBrowser", new Vector2(width, height), false, ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowFontScale(Scale);

        DrawPlaylistRequestsToggle(client.LatestPendingSongRequests.Count);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, playlistSectionAlpha);
        if (showingSongRequests)
            DrawSongRequestsList(client, width);
        else
            DrawPlaylistTracks(client, width);
        ImGui.PopStyleVar();

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    /// "Playlist"/"Requests" as two plain clickable words, not an actual ImGui tab bar - there wasn't room to
    /// spare for real tab chrome here, and this reads clean enough on its own.
    private void DrawPlaylistRequestsToggle(int pendingRequestCount)
    {
        using (plugin.Fonts.Header.PushSafe())
        {
            if (DrawSectionToggleWord("Playlist", !showingSongRequests))
                SetShowingSongRequests(false);
            ImGui.SameLine(0, 10f * Scale);
            ImGui.TextDisabled("|");
            ImGui.SameLine(0, 10f * Scale);
            if (DrawSectionToggleWord("Requests", showingSongRequests))
                SetShowingSongRequests(true);
        }

        if (pendingRequestCount > 0)
            DrawPendingRequestCountBadge(pendingRequestCount);
    }

    private bool DrawSectionToggleWord(string label, bool isActive)
    {
        var size = ImGui.CalcTextSize(label);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##sectionToggle" + label, size);
        var hovered = ImGui.IsItemHovered();

        var color = isActive ? Theme.NeutralAccent : hovered ? Theme.Text : new Vector4(Theme.Text.X, Theme.Text.Y, Theme.Text.Z, 0.55f);
        ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(color), label);

        return clicked;
    }

    /// A small filled circle with the actual pending count inside it - unlike DrawUnseenChangelogBadge's
    /// plain dot, this needs to fit a number, so it's drawn with whatever the ambient (non-Header) font is,
    /// at a size that dot never needed to worry about.
    private void DrawPendingRequestCountBadge(int count)
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var text = count > 9 ? "9+" : count.ToString();
        var textSize = ImGui.CalcTextSize(text);
        var radius = MathF.Max(9f * Scale, (textSize.X / 2f) + (3f * Scale));
        var center = new Vector2(max.X + radius + (4f * Scale), (min.Y + max.Y) / 2f);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(Theme.OrangeAccent));
        drawList.AddText(center - (textSize / 2f), ImGui.GetColorU32(Theme.Background), text);
    }

    private void SetShowingSongRequests(bool value)
    {
        if (showingSongRequests == value || pendingShowingSongRequests == value)
            return;
        pendingShowingSongRequests = value;
    }

    /// Same fade-out/swap/fade-in two-phase idea as the window-level UpdateViewTransition, just scoped to
    /// this one child region instead of the whole window - "phase ONLY the playlist part," not the
    /// header/decks/queues around it.
    private void UpdatePlaylistSectionFade(float dt)
    {
        var fadeStep = dt / PlaylistSectionFadeSeconds;

        if (pendingShowingSongRequests.HasValue)
        {
            playlistSectionAlpha = MathF.Max(0f, playlistSectionAlpha - fadeStep);
            if (playlistSectionAlpha <= 0f)
            {
                showingSongRequests = pendingShowingSongRequests.Value;
                pendingShowingSongRequests = null;
            }
        }
        else if (playlistSectionAlpha < 1f)
        {
            playlistSectionAlpha = MathF.Min(1f, playlistSectionAlpha + fadeStep);
        }
    }

    private void DrawPlaylistTracks(AudioHostClient client, float width)
    {
        var playlists = client.LatestPlaylists;
        var selected = playlists.FirstOrDefault(p => p.Name == selectedPlaylistName);

        var broadcast = client.LatestStatus.Broadcast;
        var isCoHost = broadcast.IsLive && !broadcast.IsLead;

        ImGui.SetNextItemWidth(-1);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.NeutralAccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.NeutralAccentActive);
        var comboOpen = ImGui.BeginCombo("##playlistSelect", selected?.Name ?? "Select a playlist...");
        ImGui.PopStyleColor(3);
        if (comboOpen)
        {
            foreach (var p in playlists)
            {
                if (ImGui.Selectable(p.Name, p.Name == selectedPlaylistName))
                    selectedPlaylistName = p.Name;
            }

            ImGui.EndCombo();
        }

        ImGui.Separator();
        ImGui.Spacing();

        if (selected == null)
        {
            ImGui.TextDisabled("Pick a playlist above to see its songs.");
        }
        else if (selected.Tracks.Count == 0)
        {
            ImGui.TextDisabled("This playlist has no songs yet - add some from Settings > Library.");
        }
        else
        {
            foreach (var track in selected.Tracks)
            {
                ImGui.PushID(track.FilePath);

                ImGui.AlignTextToFramePadding();
                ImGui.Text(track.Title);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Right-click to set gain/BPM");
                DrawTrackGainContextMenu(client, selected.Name, track);

                ImGui.SameLine();
                var bpmSuffix = track.Bpm is > 0f ? $" - {track.Bpm:0.#} BPM" : string.Empty;
                ImGui.TextDisabled($"({FormatTime(track.DurationSeconds)}{bpmSuffix})");

                ImGui.SameLine(RightAlignedX(width, isCoHost ? 166f : 68f));
                if (isCoHost)
                {
                    if (PanelButton.Draw("##requestToLead", plugin.Fonts.Icon, FontAwesomeIcon.Upload, "Request", new Vector2(90, 24) * Scale, Theme.OrangeAccent))
                    {
                        var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "Unknown";
                        client.Send(MessageType.RequestSong, new RequestSongCommand
                        {
                            SourceFilePath = track.FilePath,
                            RequesterName = characterName,
                        });
                    }
                    ImGui.SameLine();
                }
                if (PanelButton.Draw("##assignA", null, null, "A", new Vector2(30, 24) * Scale, Theme.CyanAccent))
                    AssignTrack(client, DeckId.A, track);
                ImGui.SameLine();
                if (PanelButton.Draw("##assignB", null, null, "B", new Vector2(30, 24) * Scale, Theme.OrangeAccent))
                    AssignTrack(client, DeckId.B, track);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PopID();
            }
        }
    }

    /// Mirrors DrawPlaylistTracks' own row layout exactly (title/duration, right-aligned deck-assign buttons)
    /// plus a requester-name line and a decline button.
    private void DrawSongRequestsList(AudioHostClient client, float width)
    {
        var requests = client.LatestPendingSongRequests;
        if (requests.Count == 0)
        {
            ImGui.TextDisabled("No song requests yet - listeners can send one from their own Listener window.");
            return;
        }

        foreach (var request in requests)
        {
            ImGui.PushID(request.RequestId.GetHashCode());

            ImGui.AlignTextToFramePadding();
            ImGui.Text(request.FileName);
            ImGui.SameLine();
            ImGui.TextDisabled($"({FormatTime(request.DurationSeconds)})");

            ImGui.SameLine(RightAlignedX(width, 98f));
            if (PanelButton.Draw("##acceptRequestA", null, null, "A", new Vector2(30, 24) * Scale, Theme.CyanAccent))
                client.Send(MessageType.AcceptSongRequest, new AcceptSongRequestCommand { RequestId = request.RequestId, Deck = DeckId.A });
            ImGui.SameLine();
            if (PanelButton.Draw("##acceptRequestB", null, null, "B", new Vector2(30, 24) * Scale, Theme.OrangeAccent))
                client.Send(MessageType.AcceptSongRequest, new AcceptSongRequestCommand { RequestId = request.RequestId, Deck = DeckId.B });
            ImGui.SameLine();
            if (PanelButton.Draw("##declineRequest", plugin.Fonts.Icon, FontAwesomeIcon.Times, null, new Vector2(24, 24) * Scale, Theme.OrangeAccent))
                client.Send(MessageType.DeclineSongRequest, new DeclineSongRequestCommand { RequestId = request.RequestId });

            ImGui.TextDisabled($"Requested by {request.RequesterName}");
            if (request.IsFromCoHost)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.CyanAccent, "(co-host)");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PopID();
        }
    }

    private static void AssignTrack(AudioHostClient client, DeckId deck, TrackDto track) =>
        client.Send(MessageType.AssignTrackToDeck, new AssignTrackToDeckCommand
        {
            Deck = deck,
            Title = track.Title,
            FilePath = track.FilePath,
            Gain = track.Gain,
            DurationSeconds = track.DurationSeconds,
            Bpm = track.Bpm,
            BeatGridOffsetSeconds = track.BeatGridOffsetSeconds,
        });

    /// Right-click a song's title to adjust its saved gain (applied automatically to the deck's fader every
    /// time this specific song loads, so tracks that are mixed quieter/ louder than the rest of a playlist
    /// don't need the fader ridden manually each time they come up) or correct its BPM (auto-detected by
    /// BpmAnalyzer at import time, but no beat detector is perfect - this is the fallback when it's wrong, or
    /// hasn't run yet).
    private void DrawTrackGainContextMenu(AudioHostClient client, string playlistName, TrackDto track)
    {
        if (!ImGui.BeginPopupContextItem("##trackGain" + track.FilePath))
            return;

        if (ImGui.IsWindowAppearing())
        {
            trackGainEditBuffers[track.FilePath] = track.Gain;
            trackBpmEditBuffers[track.FilePath] = track.Bpm ?? 120f;
        }

        using (plugin.Fonts.Header.PushSafe())
            ImGui.Text(track.Title);
        ImGui.Separator();

        var gain = trackGainEditBuffers.TryGetValue(track.FilePath, out var g) ? g : track.Gain;
        ImGui.SetNextItemWidth(160 * Scale);
        ImGui.SliderFloat("Gain##" + track.FilePath, ref gain, 0f, 2f, "%.2f");
        trackGainEditBuffers[track.FilePath] = gain;
        ImGui.SameLine();
        if (PanelButton.Draw("##setTrackGain", null, null, "Set", new Vector2(40, 24) * Scale, Theme.CyanAccent))
        {
            client.Send(MessageType.SetTrackGain, new SetTrackGainCommand { PlaylistName = playlistName, TrackFilePath = track.FilePath, Gain = gain });
            ImGui.CloseCurrentPopup();
        }

        ImGui.Separator();
        ImGui.TextDisabled(track.Bpm is > 0f ? $"Detected/set BPM: {track.Bpm:0.#}" : "BPM: not yet analyzed");
        var bpm = trackBpmEditBuffers.TryGetValue(track.FilePath, out var b) ? b : track.Bpm ?? 120f;
        ImGui.SetNextItemWidth(160 * Scale);
        ImGui.InputFloat("BPM##" + track.FilePath, ref bpm, 0f, 0f, "%.1f");
        trackBpmEditBuffers[track.FilePath] = bpm;
        ImGui.SameLine();
        if (PanelButton.Draw("##setTrackBpm", null, null, "Set", new Vector2(40, 24) * Scale, Theme.CyanAccent) && bpm > 0f)
        {
            client.Send(MessageType.SetTrackBpm, new SetTrackBpmCommand { PlaylistName = playlistName, TrackFilePath = track.FilePath, Bpm = bpm });
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// Draws a knob with its display label (text before "##") centered above it.
    private static bool DrawLabeledKnob(string label, ref float value, float min, float max, float defaultValue, Vector4 accent, float radius = 22f, Vector4? accent2 = null)
    {
        var displayLabel = label.Split("##")[0];

        ImGui.BeginGroup();
        var diameter = radius * 2f;
        var textWidth = ImGui.CalcTextSize(displayLabel).X;
        var startX = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(startX + MathF.Max(0f, (diameter - textWidth) / 2f));
        ImGui.TextDisabled(displayLabel);
        ImGui.SetCursorPosX(startX);
        var changed = Knob.Draw("##" + label, ref value, min, max, defaultValue, radius, accent, accent2);
        ImGui.EndGroup();
        return changed;
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    /// Same idea as FormatTime, but for a "how long has this lobby been live" readout that can realistically
    /// run past an hour, unlike a single track's mm:ss position.
    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}
