using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.Ipc;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Settings, redesigned as a two-column master/detail.
public sealed class SettingsScreen
{
    private enum Category
    {
        Appearance,
        Audio,
        Visualizer,
        Source,
        Notifications,
        Changelog,
        About,
    }

    private static readonly CategoryList.Item[] Categories =
    {
        new(nameof(Category.Appearance), "Appearance", FontAwesomeIcon.Palette, "Window and colours"),
        new(nameof(Category.Audio), "Audio", FontAwesomeIcon.VolumeUp, "Volume and engine"),
        new(nameof(Category.Visualizer), "Visualizer", FontAwesomeIcon.ChartBar, "Style and sensitivity"),
        new(nameof(Category.Source), "Source", FontAwesomeIcon.ExchangeAlt, "Where audio comes from"),
        new(nameof(Category.Notifications), "Notifications", FontAwesomeIcon.Bell, "Toasts and position"),
        new(nameof(Category.Changelog), "Changelog", FontAwesomeIcon.History, "What's new"),
        new(nameof(Category.About), "About", FontAwesomeIcon.InfoCircle, "Version and links"),
    };

    private readonly Plugin plugin;
    private readonly CategoryList categoryList = new();
    private Category selected = Category.Appearance;

    private readonly ReportBugDialog reportBug;

    public SettingsScreen(Plugin plugin)
    {
        this.plugin = plugin;
        reportBug = new ReportBugDialog(plugin);
    }

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var listWidth = CategoryList.DefaultWidth;
        var origin = ImGui.GetCursorScreenPos();

        var picked = categoryList.Draw("##settingsCat", origin, new Vector2(listWidth, avail.Y),
            Categories, selected.ToString(),
            key => key == nameof(Category.Changelog) && ChangelogData.HasUnseen(plugin.Configuration));
        if (Enum.TryParse<Category>(picked, out var next))
            selected = next;

        ImGui.SetCursorScreenPos(origin + new Vector2(listWidth + Metrics.Xxl, 0f));
        var detailWidth = MathF.Max(1f, avail.X - listWidth - Metrics.Xxl);

        Surfaces.ReserveScrollbar = true;

        ImGui.BeginChild("##settingsDetail", new Vector2(detailWidth, avail.Y), false);
        DrawDetail();
        ImGui.EndChild();

        Surfaces.ReserveScrollbar = false;
    }

    private void DrawDetail()
    {
        switch (selected)
        {
            case Category.Appearance:
                DrawAppearance();
                break;
            case Category.Audio:
                DrawAudio();
                break;
            case Category.Visualizer:
                DrawVisualizer();
                break;
            case Category.Source:
                DrawSource();
                break;
            case Category.Notifications:
                DrawNotifications();
                break;
            case Category.Changelog:
                DrawChangelog();
                break;
            default:
                DrawAbout();
                break;
        }
    }

    private void DrawAppearance()
    {
        var config = plugin.Configuration;

        Surfaces.SectionHeader("Window");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var uiScale = config.UiScale;
        if (Fields.Slider("##v2UiScale", "UI Scale", ref uiScale, 0.75f, 1.5f, "{0:F2}x", 1f,
                "Scales the whole window, for a bigger or smaller monitor."))
        {
            config.UiScale = Math.Clamp(uiScale, 0.75f, 1.5f);
            config.Save();
        }

        Fields.Divider();

        var useNewDesign = config.UseNewDesign;
        if (Fields.Switch("##v2UseNewDesign", "EchoMix 2.0 Design", ref useNewDesign,
                "Turn off to return to the 1.0 interface."))
        {
            config.UseNewDesign = useNewDesign;
            config.Save();
        }

        Fields.Divider();

        var showWelcome = config.ShowWelcomeOnEnable;
        if (Fields.Switch("##v2ShowWelcome", "Show Welcome on Start", ref showWelcome,
                "The DJ or Listener choice shown on first open."))
        {
            config.ShowWelcomeOnEnable = showWelcome;
            config.Save();
        }

#if DEBUG

        Fields.Divider();

        var sampleBrowse = config.UseSampleBrowseData;
        if (Fields.Switch("##v2SampleBrowse", "Sample Browse Data", ref sampleBrowse,
                "Fills Browse with made-up shows so the grid can be judged without a dozen people live."))
        {
            config.UseSampleBrowseData = sampleBrowse;
            config.Save();
        }

        Fields.Divider();

        var row = Fields.BeginRow("##v2ReplayNote", "Replay the 2.0 Note",
            "Clears the once-ever flag so the welcome letter shows again on the Mixer.",
            MathF.Round(110f * Metrics.Scale), rowClickable: false);

        ImGui.SetCursorScreenPos(row.ControlMin);
        if (Fields.Button("Show Again", Fields.ButtonStyle.Secondary, row.ControlWidth,
                enabled: config.HasSeenTwoPointOhNote, height: row.ControlHeight, idSuffix: "replayNote"))
        {
            config.HasSeenTwoPointOhNote = false;
            config.Save();
        }

        Fields.EndRow(row);
#endif

        Surfaces.EndPanel();

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("Deck Colours");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        DrawThemePicker();
        Fields.Divider();

        DrawColorRow("##v2DeckA", "Deck A", new Vector3(config.DeckAAccentR, config.DeckAAccentG, config.DeckAAccentB),
            c => { config.DeckAAccentR = c.X; config.DeckAAccentG = c.Y; config.DeckAAccentB = c.Z; });
        Fields.Divider();
        DrawColorRow("##v2DeckB", "Deck B", new Vector3(config.DeckBAccentR, config.DeckBAccentG, config.DeckBAccentB),
            c => { config.DeckBAccentR = c.X; config.DeckBAccentG = c.Y; config.DeckBAccentB = c.Z; });
        Fields.Divider();
        DrawColorRow("##v2Blend", "Blend", new Vector3(config.BlendAccentR, config.BlendAccentG, config.BlendAccentB),
            c => { config.BlendAccentR = c.X; config.BlendAccentG = c.Y; config.BlendAccentB = c.Z; });

        Surfaces.Gap(Metrics.Md);
        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "Changes the window border and every themed element on both decks, live.");

        Surfaces.Gap(Metrics.Md);
        if (Fields.Button("Reset to Default", icon: FontAwesomeIcon.Undo))
        {
            config.DeckAAccentR = 0.25f; config.DeckAAccentG = 0.85f; config.DeckAAccentB = 0.95f;
            config.DeckBAccentR = 1f; config.DeckBAccentG = 0.6f; config.DeckBAccentB = 0.15f;
            config.BlendAccentR = 0.625f; config.BlendAccentG = 0.725f; config.BlendAccentB = 0.55f;
            config.Save();
            ApplyDeckColors();
        }

        Surfaces.EndPanel();
    }

    /// A named starting point for the three pickers below it.
    private void DrawThemePicker()
    {
        var config = plugin.Configuration;

        var deckA = new Vector3(config.DeckAAccentR, config.DeckAAccentG, config.DeckAAccentB);
        var deckB = new Vector3(config.DeckBAccentR, config.DeckBAccentG, config.DeckBAccentB);
        var blend = new Vector3(config.BlendAccentR, config.BlendAccentG, config.BlendAccentB);

        var matched = DeckColourThemes.IndexOf(deckA, deckB, blend);

        var options = new string[DeckColourThemes.Names.Count + 1];
        for (var i = 0; i < DeckColourThemes.Names.Count; i++)
            options[i] = DeckColourThemes.Names[i];

        var customIndex = options.Length - 1;
        options[customIndex] = "Custom";

        var selected = matched >= 0 ? matched : customIndex;

        if (!Fields.Dropdown("##v2DeckTheme", "Theme", ref selected, options,
                "Sets all three colours at once.", controlWidth: 260f * Metrics.Scale))
        {
            return;
        }

        if (selected == customIndex || selected < 0 || selected >= DeckColourThemes.All.Count)
            return;

        var theme = DeckColourThemes.All[selected];
        config.DeckAAccentR = theme.DeckA.X; config.DeckAAccentG = theme.DeckA.Y; config.DeckAAccentB = theme.DeckA.Z;
        config.DeckBAccentR = theme.DeckB.X; config.DeckBAccentG = theme.DeckB.Y; config.DeckBAccentB = theme.DeckB.Z;
        config.BlendAccentR = theme.Blend.X; config.BlendAccentG = theme.Blend.Y; config.BlendAccentB = theme.Blend.Z;
        config.Save();
        ApplyDeckColors();
    }

    private void DrawColorRow(string id, string label, Vector3 current, Action<Vector3> write)
    {
        var value = current;
        if (!Fields.ColorRow(id, label, ref value))
            return;

        write(value);
        plugin.Configuration.Save();
        ApplyDeckColors();
    }

    private void ApplyDeckColors()
    {
        var config = plugin.Configuration;
        Theme.ApplyCustomColors(
            new Vector4(config.DeckAAccentR, config.DeckAAccentG, config.DeckAAccentB, 1f),
            new Vector4(config.DeckBAccentR, config.DeckBAccentG, config.DeckBAccentB, 1f),
            new Vector4(config.BlendAccentR, config.BlendAccentG, config.BlendAccentB, 1f));
    }

    private void DrawAudio()
    {
        var health = plugin.AudioHostClient.Health;

        var (statusColor, statusText, statusDetail) = health switch
        {
            AudioHostHealth.Connected => (Semantic.Success, "Connected",
                "AudioHost runs as a separate background process, independent of the game's frame rate."),
            AudioHostHealth.NotResponding => (Semantic.Danger, "Not Responding",
                "The connection is open but AudioHost has stopped sending updates. Everything shown "
                + "in EchoMix is the last thing it reported. Restarting the game is the reliable fix."),
            _ => (Semantic.Warning, "Connecting...",
                "EchoMix can't reach AudioHost, so nothing here is live and no control will take "
                + "effect. It retries automatically and normally recovers in a second or two. If it "
                + "doesn't, end EchoMix.AudioHost.exe in Task Manager - a leftover one from an "
                + "earlier session holds the connection open and blocks the new one."),
        };

        Surfaces.SectionHeader("Engine");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var drawList = ImGui.GetWindowDrawList();
        var dotPos = ImGui.GetCursorScreenPos();
        drawList.AddCircleFilled(
            Chrome.Snap(new Vector2(dotPos.X + (4f * Metrics.Scale), dotPos.Y + (ImGui.GetTextLineHeight() * 0.5f))),
            4f * Metrics.Scale, ImGui.GetColorU32(statusColor));

        ImGui.Dummy(new Vector2(14f * Metrics.Scale, 0f));
        ImGui.SameLine();
        using (TypeScale.Body())
            ImGui.TextColored(statusColor, statusText);

        using (TypeScale.Caption())
        {
            using var detailColor = Sty.New().Col(ImGuiCol.Text, Semantic.TextTertiary);
            ImGui.TextWrapped(statusDetail);
        }

        Surfaces.EndPanel();

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("Output");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var masterVolume = plugin.AudioHostClient.LatestStatus.MasterVolume;
        if (Fields.Slider("##v2MasterVolume", "Master Volume", ref masterVolume, 0f, 2f, "{0:F2}x", 0.8f))
            plugin.AudioHostClient.Send(MessageType.SetMasterVolume, new SetMasterVolumeCommand { Volume = masterVolume });

        Fields.Divider();

        var muteWhenUnfocused = plugin.Configuration.MuteWhenUnfocused;
        if (Fields.Switch("##v2MuteUnfocused", "Mute in Background", ref muteWhenUnfocused,
                "Silences EchoMix when the game isn't focused."))
        {
            plugin.Configuration.MuteWhenUnfocused = muteWhenUnfocused;
            plugin.Configuration.Save();
        }

        Surfaces.EndPanel();

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("Song Preview");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "Middle-click an upcoming song in the Library to hear it privately. Listeners never hear "
                + "the preview, and never hear the current track dip for it.");

        Surfaces.Gap(Metrics.Lg);

        var dampen = plugin.Configuration.PreviewDampenVolume * 100f;
        if (Fields.Slider("##v2PreviewDampen", "Current Track Dampen", ref dampen, 0f, 100f, "{0:F0}%", 50f,
                "How far the track you're playing drops for you while a preview runs."))
        {
            plugin.Configuration.PreviewDampenVolume = dampen / 100f;
            plugin.Configuration.Save();
            plugin.AudioHostClient.Send(MessageType.SetPreviewDampenVolume,
                new SetPreviewDampenVolumeCommand { Volume = plugin.Configuration.PreviewDampenVolume });
        }

        Fields.Divider();

        var previewVolume = plugin.Configuration.PreviewVolume * 100f;
        if (Fields.Slider("##v2PreviewVolume", "Preview Volume", ref previewVolume, 0f, 150f, "{0:F0}%", 60f,
                "How loud the previewed song plays for you."))
        {
            plugin.Configuration.PreviewVolume = previewVolume / 100f;
            plugin.Configuration.Save();
            plugin.AudioHostClient.Send(MessageType.SetPreviewVolume,
                new SetPreviewVolumeCommand { Volume = plugin.Configuration.PreviewVolume });
        }

        Surfaces.EndPanel();
    }

    private void DrawVisualizer()
    {
        var config = plugin.Configuration;

        Surfaces.SectionHeader("Your Decks");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();
        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "Your own deck displays and minimized box. Doesn't affect what listeners see.");
        Surfaces.Gap(Metrics.Md);

        DrawStyleRow("##v2DeckVisStyle", "Style", config.DeckVisualizerStyle, v =>
        {
            config.DeckVisualizerStyle = v;
            config.Save();
        });

        Fields.Divider();

        var deckSensitivity = config.DeckVisualizerSensitivity;
        if (Fields.Slider("##v2DeckVisSens", "Sensitivity", ref deckSensitivity, 0.5f, 3f, "{0:F1}x", 1f,
                "How much the bars react. Higher animates more."))
        {
            config.DeckVisualizerSensitivity = deckSensitivity;
            config.Save();
        }

        Surfaces.EndPanel();

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("While Listening");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();
        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "What you see while listening to someone else's show.");
        Surfaces.Gap(Metrics.Md);

        DrawStyleRow("##v2ListenerVisStyle", "Style", config.ListenerVisualizerStyle, v =>
        {
            config.ListenerVisualizerStyle = v;
            config.Save();
        });

        Fields.Divider();

        var listenerSensitivity = config.ListenerVisualizerSensitivity;
        if (Fields.Slider("##v2ListenerVisSens", "Sensitivity", ref listenerSensitivity, 0.5f, 3f, "{0:F1}x", 1.6f,
                "How much the bars react. Higher animates more."))
        {
            config.ListenerVisualizerSensitivity = listenerSensitivity;
            config.Save();
        }

        Surfaces.EndPanel();
    }

    private void DrawStyleRow(string id, string label, int current, Action<int> write)
    {
        var value = current;
        if (Fields.Dropdown(id, label, ref value, DjDeckWindow.VisualizerStyleNames))
            write(value);
    }

    /// Spotify Mode's own settings, the important one being where Spotify's audio goes.
    private void DrawSource()
    {
        var client = plugin.AudioHostClient;
        var spotify = client.LatestStatus.SpotifyMode;
        var devices = client.LatestAudioOutputDevices;

        if (!requestedOutputDevices)
        {
            requestedOutputDevices = true;
            client.Send(MessageType.RequestAudioOutputDevices, new object());
            client.Send(MessageType.RequestAudioInputDevices, new object());
            client.Send(MessageType.RequestCapturableProcesses, new object());
        }

        DrawLineInSection();
        Surfaces.Gap(Metrics.Xxl);
        DrawApplicationCaptureSection();
        Surfaces.Gap(Metrics.Xxl);

        Surfaces.SectionHeader("Spotify Output Device");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "Spotify Mode broadcasts Spotify's audio, but Spotify also keeps playing out of your own speakers. "
                + "Send it to a device you aren't listening to and it goes quiet locally while the broadcast keeps "
                + "full volume - turning Spotify down can't work, because that would mute the broadcast too.");

        Surfaces.Gap(Metrics.Lg);

        if (!client.IsOutputRoutingAvailable)
        {
            using (TypeScale.Body())
                ImGui.TextColored(Semantic.Warning, "Automatic routing isn't available on this version of Windows.");

            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextTertiary, "Set Spotify's output device by hand instead:");

            Surfaces.Gap(Metrics.Md);
            if (Fields.Button("Open Windows Volume Mixer", icon: FontAwesomeIcon.SlidersH, idSuffix: "noRouting"))
                OpenVolumeMixer();

            Surfaces.EndPanel();
            return;
        }

        var names = new List<string> { "System default (follow Windows)" };
        foreach (var device in devices)
            names.Add(device.IsDefault ? $"{device.FriendlyName}  (default)" : device.FriendlyName);

        var selected = 0;
        for (var i = 0; i < devices.Count; i++)
        {
            if (string.Equals(devices[i].Id, spotify.OutputDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                selected = i + 1;
                break;
            }
        }

        var dropdownWidth = MathF.Round(220f * Metrics.Scale);
        var refreshSize = MathF.Round(Metrics.ControlSm);

        var deviceRow = Fields.BeginRow("##v2SpotifyDevice", "Send Spotify To", null,
            dropdownWidth + refreshSize + Metrics.Md, rowClickable: false);

        ImGui.SetCursorScreenPos(new Vector2(
            deviceRow.ControlMin.X,
            deviceRow.ControlMin.Y + ((deviceRow.ControlHeight - refreshSize) * 0.5f)));

        if (Fields.IconButton("##v2refreshSpotifyDevices", FontAwesomeIcon.Sync, refreshSize,
                "Refresh", "Re-reads the Windows output devices."))
        {
            client.Send(MessageType.RequestAudioOutputDevices, new object());
        }

        ImGui.SetCursorScreenPos(new Vector2(
            deviceRow.ControlMin.X + refreshSize + Metrics.Md, deviceRow.ControlMin.Y));

        if (Fields.DropdownInline("##v2SpotifyDevice", ref selected, names, dropdownWidth, deviceRow.ControlHeight))
        {
            client.Send(MessageType.SetSpotifyOutputDevice, new SetSpotifyOutputDeviceCommand
            {
                DeviceId = selected <= 0 ? null : devices[selected - 1].Id,
            });
        }

        Fields.EndRow(deviceRow);

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                spotify.IsActive
                    ? "Applies immediately."
                    : "Applies when Spotify Mode starts - Spotify has to be playing for Windows to route it.");

        if (!string.IsNullOrEmpty(spotify.Error))
        {
            Surfaces.Gap(Metrics.Md);
            using (TypeScale.Body())
                ImGui.TextColored(Semantic.Danger, spotify.Error);
        }

        Surfaces.EndPanel();

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("Remote Controls");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "While Spotify Mode is on, Deck A's transport drives Spotify directly - play, pause, and skip. "
                + "Minimize EchoMix and Deck A's half of the small box works as a remote too: right-click skips "
                + "forward, left-click plays or pauses.");

        Surfaces.EndPanel();
    }

    private bool requestedOutputDevices;

    /// Line In: broadcast a Windows recording device instead of the decks, so a DJ can run their own hardware
    /// mixer or separate software and pipe its output through EchoMix.
    private void DrawLineInSection()
    {
        var client = plugin.AudioHostClient;
        var config = plugin.Configuration;
        var inputs = client.LatestAudioInputDevices;

        Surfaces.SectionHeader("Line In");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "Captures a Windows recording device. Pick it here, then choose Line In as the source on the Mixer.");

        Surfaces.Gap(Metrics.Lg);

        DrawDevicePicker("##v2LineInDevice", "Device", inputs,
            config.ExternalInputDeviceId, (id, name) =>
            {
                config.ExternalInputDeviceId = id;
                config.ExternalInputDeviceName = name;
                config.Save();
            });

        Fields.Divider();

        DrawDevicePicker("##v2LineInDevice2", "Second Device", inputs,
            config.ExternalInputDeviceId2, (id, name) =>
            {
                config.ExternalInputDeviceId2 = id;
                config.ExternalInputDeviceName2 = name;
                config.Save();
            });

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "Optional. Some controllers route each deck to its own device - pick one here and both are "
                + "captured and summed, with Deck B's dials levelling this one on its own.");

        Surfaces.Gap(Metrics.Md);
        if (Fields.Button("Refresh", icon: FontAwesomeIcon.Sync, idSuffix: "lineInDevices"))
            client.Send(MessageType.RequestAudioInputDevices, new object());

        Fields.Divider();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "EchoMix broadcasts this device without playing it back through your own speakers - you're "
                + "already hearing your real mix through your own gear. Sound pads still play locally.");

        var lineInError = client.LatestStatus.ExternalInputMode.Error;
        if (!string.IsNullOrEmpty(lineInError))
        {
            Surfaces.Gap(Metrics.Md);
            using (TypeScale.Body())
                ImGui.TextColored(Semantic.Danger, lineInError);
        }

        Surfaces.EndPanel();
    }

    /// Application capture: broadcast one running app's own audio output.
    private void DrawApplicationCaptureSection()
    {
        var client = plugin.AudioHostClient;
        var config = plugin.Configuration;
        var processes = client.LatestCapturableProcesses;

        Surfaces.SectionHeader("Application");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "Captures one running application's audio. Pick it here, then choose Application as the source on the Mixer.");

        Surfaces.Gap(Metrics.Lg);

        var names = new List<string> { "(None)" };
        foreach (var process in processes)
            names.Add(process.DisplayName);

        var selected = 0;
        for (var i = 0; i < processes.Count; i++)
        {
            if (string.Equals(processes[i].ProcessName, config.ExternalInputProcessName, StringComparison.OrdinalIgnoreCase))
            {
                selected = i + 1;
                break;
            }
        }

        if (Fields.Dropdown("##v2AppCapture", "Application", ref selected, names))
        {
            config.ExternalInputProcessName = selected <= 0 ? null : processes[selected - 1].ProcessName;
            config.ExternalInputProcessDisplayName = selected <= 0 ? null : processes[selected - 1].DisplayName;
            config.ExternalInputUseProcessCapture = selected > 0;
            config.Save();
        }

        Surfaces.Gap(Metrics.Md);
        if (Fields.Button("Refresh", icon: FontAwesomeIcon.Sync, idSuffix: "processes"))
            client.Send(MessageType.RequestCapturableProcesses, new object());

        Surfaces.EndPanel();
    }

    private void DrawDevicePicker(
        string id,
        string label,
        IReadOnlyList<AudioInputDeviceDto> devices,
        string? currentId,
        Action<string?, string?> write)
    {
        var names = new List<string> { "(None)" };
        foreach (var device in devices)
            names.Add(device.Name);

        var selected = 0;
        for (var i = 0; i < devices.Count; i++)
        {
            if (string.Equals(devices[i].Id, currentId, StringComparison.OrdinalIgnoreCase))
            {
                selected = i + 1;
                break;
            }
        }

        if (Fields.Dropdown(id, label, ref selected, names))
        {
            if (selected <= 0)
                write(null, null);
            else
                write(devices[selected - 1].Id, devices[selected - 1].Name);
        }
    }

    private static void OpenVolumeMixer()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "ms-settings:apps-volume", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoMix] Failed to open Windows Volume Mixer");
        }
    }

    private void DrawNotifications()
    {
        var config = plugin.Configuration;

        Surfaces.SectionHeader("Toasts");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var notifyOnListenerJoin = config.NotifyOnListenerJoin;
        if (Fields.Switch("##v2NotifyJoin", "Listener Joined", ref notifyOnListenerJoin,
                "Public and private shows alike."))
        {
            config.NotifyOnListenerJoin = notifyOnListenerJoin;
            config.Save();
        }

        Fields.Divider();

        var autoJoinNotifications = config.ListenerAutoJoinNotifications;
        if (Fields.Switch("##v2AutoJoinNotify", "Auto-Join Activity", ref autoJoinNotifications,
                "Needs Auto-Join Nearby Shows on."))
        {
            config.ListenerAutoJoinNotifications = autoJoinNotifications;
            config.Save();
        }

        Surfaces.Gap(Metrics.Md);

        if (Fields.Button("Show Test Toasts", icon: FontAwesomeIcon.Bell))
        {
            plugin.ListenerJoinedToast.Show("Averylongfirstname Averylonglastname");
            plugin.AutoJoinedShowToast.Show("Test DJ");
            plugin.ServerNoticeToast.ShowTestNotice(
                "The relay is restarting for maintenance in about five minutes. Your show will "
                + "reconnect on its own once it is back.");
        }

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "Three real toasts, stacked where yours will appear.");

        Surfaces.EndPanel();

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("Position");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "Drag the toast in the preview to place it freely, or pick one of the nine positions.");

        Surfaces.Gap(Metrics.Lg);
        DrawToastPreview();
        Surfaces.Gap(Metrics.Lg);
        DrawAnchorGrid();

        if (config.ToastUseCustomPosition)
        {
            Surfaces.Gap(Metrics.Md);
            if (Fields.Button("Snap Back to a Fixed Position"))
            {
                config.ToastUseCustomPosition = false;
                config.Save();
            }
        }

        Surfaces.EndPanel();
    }

    /// A scale model of the screen with a draggable toast in it.
    private void DrawToastPreview()
    {
        var config = plugin.Configuration;
        var viewport = ImGui.GetMainViewport();
        var workSize = viewport.WorkSize;

        var previewWidth = MathF.Min(ImGui.GetContentRegionAvail().X, MathF.Round(380f * Metrics.Scale));
        var previewHeight = MathF.Round(previewWidth * (workSize.Y / MathF.Max(1f, workSize.X)));
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        Elevation.DrawSurface(drawList, origin, origin + new Vector2(previewWidth, previewHeight),
            Elevation.Sunken, Metrics.RadiusSoft, Elevation.ShadowSpec.None, topEdge: false, Elevation.Line);

        var scaleFactor = previewWidth / MathF.Max(1f, workSize.X);
        var toastSize = new Vector2(
            MathF.Max(28f * Metrics.Scale, ToastWindow.NominalSize.X * scaleFactor),
            MathF.Max(12f * Metrics.Scale, ToastWindow.NominalSize.Y * scaleFactor));

        var resolved = ToastWindow.ResolvePosition(config, Vector2.Zero, workSize, ToastWindow.NominalSize);
        var toastMin = Chrome.Snap(origin + (resolved * scaleFactor));

        ImGui.SetCursorScreenPos(toastMin);
        ImGui.InvisibleButton("##toastPreviewDrag", toastSize);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (hovered || active)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var mouse = ImGui.GetIO().MousePos;
            var local = (mouse - origin - (toastSize * 0.5f)) / scaleFactor;

            config.ToastUseCustomPosition = true;
            config.ToastCustomX = Math.Clamp(local.X / MathF.Max(1f, workSize.X), 0f, 1f);
            config.ToastCustomY = Math.Clamp(local.Y / MathF.Max(1f, workSize.Y), 0f, 1f);
            config.Save();
        }

        var accent = config.ToastUseCustomPosition ? Semantic.Primary : Semantic.TextSecondary;
        drawList.AddRectFilled(toastMin, toastMin + toastSize,
            ImGui.GetColorU32(Semantic.Alpha(accent, hovered || active ? 0.4f : 0.28f)), Metrics.RadiusSharp);
        drawList.AddRect(toastMin, toastMin + toastSize, ImGui.GetColorU32(accent),
            Metrics.RadiusSharp, ImDrawFlags.None, Metrics.Hairline);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(previewWidth, previewHeight));
    }

    /// A 3x3 grid standing in for the screen, which is a far more direct way to pick a corner than a dropdown
    /// of nine compass names.
    private void DrawAnchorGrid()
    {
        var config = plugin.Configuration;
        var cell = MathF.Round(Metrics.ControlLg);
        var gap = Metrics.Sm;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var anchors = new[]
        {
            ToastAnchor.TopLeft, ToastAnchor.TopCenter, ToastAnchor.TopRight,
            ToastAnchor.MiddleLeft, ToastAnchor.MiddleCenter, ToastAnchor.MiddleRight,
            ToastAnchor.BottomLeft, ToastAnchor.BottomCenter, ToastAnchor.BottomRight,
        };

        for (var i = 0; i < anchors.Length; i++)
        {
            var col = i % 3;
            var rowIndex = i / 3;
            var min = Chrome.Snap(origin + new Vector2(col * (cell + gap), rowIndex * (cell + gap)));

            ImGui.SetCursorScreenPos(min);
            if (ImGui.InvisibleButton($"##anchor{anchors[i]}", new Vector2(cell, cell)))
            {
                config.FollowToastAnchor = anchors[i];
                config.ToastUseCustomPosition = false;
                config.Save();
            }

            var hovered = ImGui.IsItemHovered();
            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            var isSelected = !config.ToastUseCustomPosition && config.FollowToastAnchor == anchors[i];
            var fill = isSelected
                ? Semantic.Alpha(Semantic.Primary, 0.25f)
                : Semantic.Alpha(Semantic.TextPrimary, hovered ? 0.1f : 0.04f);

            drawList.AddRectFilled(min, min + new Vector2(cell, cell), ImGui.GetColorU32(fill), Metrics.RadiusSharp);
            drawList.AddRect(min, min + new Vector2(cell, cell),
                ImGui.GetColorU32(isSelected ? Semantic.Primary : Elevation.Line),
                Metrics.RadiusSharp, ImDrawFlags.None, Metrics.Hairline);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2((cell * 3f) + (gap * 2f), (cell * 3f) + (gap * 2f)));
    }

    private void DrawChangelog()
    {
        Surfaces.SectionHeader($"Version {ChangelogData.LatestVersion}");
        Surfaces.Gap(Metrics.Md);

        if (plugin.Configuration.LastSeenChangelogVersion != ChangelogData.LatestVersion)
        {
            plugin.Configuration.LastSeenChangelogVersion = ChangelogData.LatestVersion;
            plugin.Configuration.Save();
        }

        ImGui.BeginChild("##v2Changelog", ImGui.GetContentRegionAvail(), false,
            ImGuiWindowFlags.AlwaysVerticalScrollbar);

        foreach (var entry in ChangelogData.Entries)
        {
            Surfaces.BeginPanel();

            using (TypeScale.Heading())
                ImGui.TextColored(Semantic.Primary, $"v{entry.Version}");

            Surfaces.Gap(Metrics.Sm);

            var gutter = Metrics.Xl;
            for (var g = 0; g < entry.Groups.Length; g++)
            {
                var group = entry.Groups[g];

                if (g > 0)
                    Surfaces.Gap(Metrics.Sm);

                using (TypeScale.Caption())
                    ImGui.TextColored(Semantic.TextSecondary, group.Heading);

                Surfaces.Gap(Metrics.Xs);

                foreach (var line in group.Lines)
                {
                    var bulletPos = ImGui.GetCursorScreenPos();
                    ImGui.GetWindowDrawList().AddCircleFilled(
                        Chrome.Snap(new Vector2(bulletPos.X + (Metrics.Sm * 0.5f), bulletPos.Y + (ImGui.GetTextLineHeight() * 0.5f))),
                        MathF.Max(1.5f, 2f * Metrics.Scale),
                        ImGui.GetColorU32(Semantic.TextTertiary));

                    ImGui.Indent(gutter);
                    using (TypeScale.Body())
                        ImGui.TextWrapped(line);
                    ImGui.Unindent(gutter);

                    Surfaces.Gap(Metrics.Sm);
                }
            }

            Surfaces.EndPanel();
            Surfaces.Gap(Metrics.Md);
        }

        ImGui.EndChild();
    }

    private void DrawAbout()
    {
        Surfaces.SectionHeader("EchoMix");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextSecondary, $"Version {ChangelogData.LatestVersion}");

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "A DJ deck and synced listening for Final Fantasy XIV.");

        Surfaces.EndPanel();

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("Community");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        if (Fields.Button("Join Our Discord", icon: FontAwesomeIcon.Comments))
            DjDeckWindow.OpenDiscordInvite();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "Questions and show announcements.");

        Surfaces.EndPanel();

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("Something Wrong?");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        if (Fields.Button("Report a Bug", icon: FontAwesomeIcon.Bug))
            reportBug.Open();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "Sends your AudioHost session log, plugin version and current status - nothing else on this PC.");

        reportBug.Draw();

        Surfaces.EndPanel();
    }

    /// Moves the category selection one place, for the Up/Down hotkeys while the nav rail is collapsed - see
    /// EchoMixShellWindow.StepRailSelection.
    public bool StepCategory(int delta)
    {
        if (CategoryList.StepKey(Categories, selected.ToString(), delta) is not { } key
            || !Enum.TryParse<Category>(key, out var next))
        {
            return false;
        }

        selected = next;
        return true;
    }

}
