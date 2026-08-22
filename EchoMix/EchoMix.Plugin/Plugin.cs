using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EchoMix.Plugin.Audio;
using EchoMix.Plugin.Ipc;
using EchoMix.Plugin.UI;
using EchoMix.Shared;

namespace EchoMix.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/echomix";

    private const string ListenerCommandName = "/el";

    private const string DeckCommandName = "/emix";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    public Configuration Configuration { get; }
    public AudioHostClient AudioHostClient { get; } = new();
    public Fonts Fonts { get; }

    public readonly WindowSystem WindowSystem = new("EchoMix");
    private readonly DjDeckWindow djDeckWindow;
    public DjDeckWindow DjDeckWindow => djDeckWindow;
    public HostLobbyWindow HostLobbyWindow { get; }
    public SongRequestWindow SongRequestWindow { get; }
    public FollowNotificationToast FollowNotificationToast { get; }
    public ServerNoticeToast ServerNoticeToast { get; }
    public ListenerJoinedToast ListenerJoinedToast { get; }
    public BroadcastReconnectToast BroadcastReconnectToast { get; }
    public AutoJoinedShowToast AutoJoinedShowToast { get; }
    public AutoLeftShowToast AutoLeftShowToast { get; }
    private readonly GameSoundMuteController gameSoundMuteController;
    private readonly ProximityTracker proximityTracker;
    public AutoJoinTracker AutoJoinTracker { get; }
    private readonly EchoMixIpcProvider ipcProvider;
    private int connectingFlag;
    private bool settingsRestored;
    private bool? lastSentMuteState;
    private float lastSentListenVolume = -1f;
    private bool wasListening;
    private bool wasMultiHost;
    private bool wasLive;
    private bool wasHostReconnecting;
    private readonly HashSet<Guid> knownListenerIds = new();
    private Timer? focusMuteTimer;
    private string? lastSentCharacterName;

    private float autoJoinPollAccumulator;
    private bool manualDisconnectRequested;
    private string? lastKnownAutoJoinedRoomCode;
    private string? pendingAutoJoinToastRoomCode;
    private string? pendingAutoLeaveToastDjName;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Theme.ApplyCustomColors(
            new Vector4(Configuration.DeckAAccentR, Configuration.DeckAAccentG, Configuration.DeckAAccentB, 1f),
            new Vector4(Configuration.DeckBAccentR, Configuration.DeckBAccentG, Configuration.DeckBAccentB, 1f),
            new Vector4(Configuration.BlendAccentR, Configuration.BlendAccentG, Configuration.BlendAccentB, 1f));
        Fonts = new Fonts(PluginInterface);
        gameSoundMuteController = new GameSoundMuteController(GameConfig);
        proximityTracker = new ProximityTracker(ObjectTable);
        AutoJoinTracker = new AutoJoinTracker(proximityTracker);

        djDeckWindow = new DjDeckWindow(this);
        WindowSystem.AddWindow(djDeckWindow);
        djDeckWindow.IsOpen = Configuration.IsDjWindowOpen;

        HostLobbyWindow = new HostLobbyWindow(this);
        WindowSystem.AddWindow(HostLobbyWindow);

        SongRequestWindow = new SongRequestWindow(this);
        WindowSystem.AddWindow(SongRequestWindow);

        FollowNotificationToast = new FollowNotificationToast(this);
        WindowSystem.AddWindow(FollowNotificationToast);

        ServerNoticeToast = new ServerNoticeToast(this);
        WindowSystem.AddWindow(ServerNoticeToast);

        ListenerJoinedToast = new ListenerJoinedToast(this);
        WindowSystem.AddWindow(ListenerJoinedToast);

        BroadcastReconnectToast = new BroadcastReconnectToast(this);
        WindowSystem.AddWindow(BroadcastReconnectToast);

        AutoJoinedShowToast = new AutoJoinedShowToast(this);
        WindowSystem.AddWindow(AutoJoinedShowToast);

        AutoLeftShowToast = new AutoLeftShowToast(this);
        WindowSystem.AddWindow(AutoLeftShowToast);

        ipcProvider = new EchoMixIpcProvider(this);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the EchoMix window."
        });
        CommandManager.AddHandler(ListenerCommandName, new CommandInfo(OnListenerCommand)
        {
            HelpMessage = "Jumps straight to the EchoMix Listener view."
        });
        CommandManager.AddHandler(DeckCommandName, new CommandInfo(OnDeckCommand)
        {
            HelpMessage = "Jumps straight to the EchoMix DJ Deck view."
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleDjDeckWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsWindow;
        Framework.Update += OnFrameworkUpdate;

        focusMuteTimer = new Timer(CheckFocusMute, null, 0, 15);

        _ = EnsureAudioHostConnectedAsync();
    }

    public void Dispose()
    {
        var status = AudioHostClient.LatestStatus;
        Configuration.CrossfaderPosition = status.CrossfaderPosition;
        Configuration.MasterVolume = status.MasterVolume;
        Configuration.DeckAGain = status.DeckA.Gain;
        Configuration.DeckBGain = status.DeckB.Gain;
        Configuration.IsDjWindowOpen = djDeckWindow.IsOpen;
        Configuration.Save();

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleDjDeckWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsWindow;
        Framework.Update -= OnFrameworkUpdate;
        focusMuteTimer?.Dispose();
        gameSoundMuteController.SetShouldMute(false);
        ipcProvider.Dispose();

        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ListenerCommandName);
        CommandManager.RemoveHandler(DeckCommandName);
        djDeckWindow.Dispose();

        AudioHostClient.Send(MessageType.Shutdown, new object());
        AudioHostClient.Dispose();
    }

    private void OnCommand(string command, string args) => ToggleDjDeckWindow();

    private void OnListenerCommand(string command, string args)
    {
        djDeckWindow.IsOpen = true;
        djDeckWindow.ShowListenerView();
    }

    private void OnDeckCommand(string command, string args)
    {
        djDeckWindow.IsOpen = true;
        djDeckWindow.ShowDeckImmediately();
    }

    /// NOTHING DRAWS OUTSIDE THE WORLD - same guard EchoGlam/EchoNav/EchoSim already ship: none of this
    /// (decks, playlists, broadcasts, listener toasts) has an answer at the title screen or character select.
    private void DrawUi()
    {
        if (!ClientState.IsLoggedIn)
            return;

        WindowSystem.Draw();
    }

    private void ToggleDjDeckWindow()
    {
        if (!djDeckWindow.IsOpen)
            djDeckWindow.ShowInitialView();
        djDeckWindow.IsOpen = !djDeckWindow.IsOpen;
    }

    /// Dalamud's own "open config" entry point (gear icon in the plugin installer).
    private void ToggleSettingsWindow()
    {
        if (!djDeckWindow.IsOpen)
        {
            djDeckWindow.ShowSettingsImmediately();
            djDeckWindow.IsOpen = true;
        }
        else
        {
            djDeckWindow.ToggleSettingsView();
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!AudioHostClient.IsConnected)
        {
            _ = EnsureAudioHostConnectedAsync();
        }
        else if (AudioHostClient.IsStale)
        {
            Log.Warning("[EchoMix] AudioHost stopped responding - forcing a reconnect.");
            AudioHostClient.ForceDisconnect();
        }

        var characterName = ObjectTable.LocalPlayer?.Name.TextValue;
        if (!string.IsNullOrEmpty(characterName) && characterName != lastSentCharacterName && AudioHostClient.IsConnected)
        {
            lastSentCharacterName = characterName;
            AudioHostClient.Send(MessageType.SetLocalIdentity, new SetLocalIdentityCommand { CharacterName = characterName });
        }

        var status = AudioHostClient.LatestStatus;
        var isPlayingAnyAudio = status.DeckA.IsPlaying || status.DeckB.IsPlaying || status.Broadcast.IsListening
            || status.SpotifyMode.IsActive || status.ExternalInputMode.IsActive;
        gameSoundMuteController.SetShouldMute(isPlayingAnyAudio);
        UpdateListenVolume(status.Broadcast);
        UpdateAutoJoin(status.Broadcast, framework.UpdateDelta);
        UpdateHostLobbyVisibility(status.Broadcast);

        SongRequestWindow.IsOpen = status.Broadcast.IsListening;
        if (!status.Broadcast.IsListening)
            SongRequestWindow.IsExpanded = false;

        var wentLive = AudioHostClient.ConsumeFollowedDjWentLive();
        if (wentLive != null)
            FollowNotificationToast.Show(wentLive.DjName, wentLive.RoomCode);

        ServerNoticeToast.ShowIfNew(status.Broadcast.ServerNotice);

        if (status.Broadcast.IsHostReconnecting && !wasHostReconnecting)
        {
            BroadcastReconnectToast.ShowDropped(status.Broadcast.RoomCode);
        }
        else if (!status.Broadcast.IsHostReconnecting && wasHostReconnecting)
        {
            if (status.Broadcast.IsLive)
                BroadcastReconnectToast.ShowRecovered(status.Broadcast.RoomCode);
            else
                BroadcastReconnectToast.ShowGaveUp(status.Broadcast.BroadcastError);
        }
        wasHostReconnecting = status.Broadcast.IsHostReconnecting;

        if (status.Broadcast.IsLive)
        {
            foreach (var listener in status.Broadcast.ListenerRoster)
            {
                if (knownListenerIds.Add(listener.ListenerId) && wasLive && Configuration.NotifyOnListenerJoin)
                    ListenerJoinedToast.Show(listener.CharacterName);
            }
        }
        else
        {
            knownListenerIds.Clear();
        }
        wasLive = status.Broadcast.IsLive;

        ipcProvider.CheckForChanges();
    }

    /// HostLobbyWindow is drawn (IsOpen) for the whole time a broadcast is live, solo or not, since it's a
    /// slide-out drawer attached to DjDeckWindow rather than an independent floating window - it needs to
    /// keep running its own PreDraw/collapse-width Lerp even while fully collapsed.
    private void UpdateHostLobbyVisibility(BroadcastStatusMessage broadcast)
    {
        HostLobbyWindow.IsOpen = broadcast.IsLive;

        var isMultiHost = broadcast.IsLive && broadcast.HostRoster.Count > 1;
        if (isMultiHost && !wasMultiHost)
            HostLobbyWindow.IsExpanded = true;
        wasMultiHost = isMultiHost;

        if (!broadcast.IsLive)
            HostLobbyWindow.IsExpanded = false;
    }

    /// The final listen volume is always proximityFactor * the listener's own volume preference - not an
    /// either/or between Proximity and a manual slider, so someone can still turn themselves down further
    /// even while standing right next to the DJ.
    private void UpdateListenVolume(BroadcastStatusMessage broadcast)
    {
        if (!broadcast.IsListening)
        {
            wasListening = false;
            return;
        }

        if (!wasListening)
            lastSentListenVolume = -1f;
        wasListening = true;

        var proximityFactor = broadcast.IsProximityAudio ? proximityTracker.ComputeVolume(broadcast.HostCharacterName, broadcast.ProximityRange) : 1f;
        var volume = Math.Clamp(proximityFactor * Configuration.ListenerVolume, 0f, 1.5f);
        if (MathF.Abs(volume - lastSentListenVolume) < 0.01f)
            return;

        lastSentListenVolume = volume;
        AudioHostClient.Send(MessageType.SetListenVolume, new SetListenVolumeCommand { Volume = volume });
    }

    /// Called by DjDeckWindow right before it sends a manual DisconnectFromRemote (the Disconnect/Leave Show
    /// buttons) - the only reliable, unambiguous signal that a disconnect was actually a deliberate click, as
    /// opposed to AutoJoinTracker's own out-of-range Leave, or the relay/AudioHost's own connection
    /// self-healing (a dropped network connection, or the DJ ending their show) neither of which the listener
    /// asked for at all.
    public void NotifyManualDisconnectRequested() => manualDisconnectRequested = true;

    /// Translates AutoJoinTracker's own decisions into the actual ConnectToRemote/ DisconnectFromRemote sends
    /// - the tracker only ever decides, this is the one place that acts on it.
    private void UpdateAutoJoin(BroadcastStatusMessage broadcast, TimeSpan updateDelta)
    {
        if (lastKnownAutoJoinedRoomCode != null && !broadcast.IsListening && manualDisconnectRequested)
            AutoJoinTracker.NotifyManualLeave(lastKnownAutoJoinedRoomCode);
        manualDisconnectRequested = false;

        autoJoinPollAccumulator += (float)updateDelta.TotalSeconds;
        if (Configuration.ListenerAutoJoinNearbyShows && autoJoinPollAccumulator >= AutoJoinTuning.ListPollIntervalSeconds)
        {
            autoJoinPollAccumulator = 0f;
            AudioHostClient.Send(MessageType.RequestPublicShows, new object());
        }

        var decision = AutoJoinTracker.Tick(Configuration, broadcast, AudioHostClient.LatestPublicShows, (float)updateDelta.TotalSeconds);
        switch (decision.Kind)
        {
            case AutoJoinDecisionKind.Join:
                pendingAutoJoinToastRoomCode = decision.RoomCode;
                AudioHostClient.Send(MessageType.ConnectToRemote, new ConnectToRemoteCommand
                {
                    RoomCode = decision.RoomCode!,
                    Password = string.Empty,
                    CharacterName = ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
                });
                break;

            case AutoJoinDecisionKind.Leave:
                pendingAutoLeaveToastDjName = broadcast.HostDjName;
                AudioHostClient.Send(MessageType.DisconnectFromRemote, new object());
                break;

            case AutoJoinDecisionKind.SwitchTo:
                AudioHostClient.Send(MessageType.DisconnectFromRemote, new object());
                break;
        }

        if (pendingAutoJoinToastRoomCode != null && broadcast.IsListening && broadcast.RoomCode == pendingAutoJoinToastRoomCode)
        {
            if (Configuration.ListenerAutoJoinNotifications)
                AutoJoinedShowToast.Show(broadcast.HostDjName ?? "Unknown DJ");
            pendingAutoJoinToastRoomCode = null;
        }

        if (pendingAutoLeaveToastDjName != null && !broadcast.IsListening)
        {
            if (Configuration.ListenerAutoJoinNotifications)
                AutoLeftShowToast.Show(pendingAutoLeaveToastDjName);
            pendingAutoLeaveToastDjName = null;
        }

        lastKnownAutoJoinedRoomCode = AutoJoinTracker.AutoJoinedRoomCode;
    }

    /// Runs on its own Timer thread, not tied to Framework.Update, specifically so mute response time doesn't
    /// get caught behind FFXIV's own reduced frame rate while unfocused.
    private void CheckFocusMute(object? state)
    {
        var shouldMute = Configuration.MuteWhenUnfocused && !GameWindowFocus.IsGameFocused;
        if (lastSentMuteState != shouldMute)
        {
            lastSentMuteState = shouldMute;
            Log.Debug($"[EchoMix] Sending SetOutputMuted={shouldMute} (gameFocused={GameWindowFocus.IsGameFocused})");
            AudioHostClient.Send(MessageType.SetOutputMuted, new SetOutputMutedCommand { Muted = shouldMute });
        }
    }

    private async System.Threading.Tasks.Task EnsureAudioHostConnectedAsync()
    {
        if (AudioHostClient.IsConnected)
            return;

        if (Interlocked.CompareExchange(ref connectingFlag, 1, 0) != 0)
            return;

        try
        {
            var assemblyDirectory = PluginInterface.AssemblyLocation.DirectoryName
                ?? throw new InvalidOperationException("Could not determine plugin assembly directory.");

            await AudioHostLauncher.ConnectOrLaunchAsync(
                AudioHostClient, assemblyDirectory, PluginInterface.GetPluginConfigDirectory(), Log);

            if (AudioHostClient.IsConnected && !settingsRestored)
            {
                settingsRestored = true;
                AudioHostClient.Send(MessageType.SetCrossfader, new SetCrossfaderCommand { Position = Configuration.CrossfaderPosition });
                AudioHostClient.Send(MessageType.SetCrossfaderCurve, new SetCrossfaderCurveCommand { Curve = Configuration.CrossfaderCurve });
                AudioHostClient.Send(MessageType.SetAutoDj, new SetAutoDjCommand { Enabled = Configuration.AutoDjEnabled, FadeSeconds = Configuration.AutoDjFadeSeconds });
                AudioHostClient.Send(MessageType.SetMasterVolume, new SetMasterVolumeCommand { Volume = Configuration.MasterVolume });
                AudioHostClient.Send(MessageType.SetGain, new SetGainCommand { Deck = DeckId.A, Gain = Configuration.DeckAGain });
                AudioHostClient.Send(MessageType.SetGain, new SetGainCommand { Deck = DeckId.B, Gain = Configuration.DeckBGain });
                AudioHostClient.Send(MessageType.SetPreviewVolume, new SetPreviewVolumeCommand { Volume = Configuration.PreviewVolume });
                AudioHostClient.Send(MessageType.SetPreviewDampenVolume, new SetPreviewDampenVolumeCommand { Volume = Configuration.PreviewDampenVolume });
                AudioHostClient.Send(MessageType.SetSongRequestAccessControl, new SetSongRequestAccessControlCommand
                {
                    Mode = Configuration.SongRequestAccessMode,
                    Whitelist = Configuration.SongRequestWhitelist,
                    Blacklist = Configuration.SongRequestBlacklist,
                });

                lastSentMuteState = null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EchoMix] Error while connecting to AudioHost");
        }
        finally
        {
            Interlocked.Exchange(ref connectingFlag, 0);
        }
    }
}
