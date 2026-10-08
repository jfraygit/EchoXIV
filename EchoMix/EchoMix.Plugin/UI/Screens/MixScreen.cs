using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// The mixer, redesigned.
public sealed class MixScreen
{
    private readonly Plugin plugin;

    private float? pendingSeekA;
    private float? pendingSeekB;

    private readonly Dictionary<DeckId, float> deckGlow = new();

    public MixScreen(Plugin plugin) => this.plugin = plugin;

    private static Vector4 AccentFor(DeckId id) => id == DeckId.A ? Semantic.DeckA : Semantic.DeckB;

    public void Draw()
    {
        var status = plugin.AudioHostClient.LatestStatus;
        var avail = ImGui.GetContentRegionAvail();

        var masterBarHeight = MathF.Round(96f * Metrics.Scale);
        var deckAreaHeight = MathF.Max(1f, avail.Y - masterBarHeight - Metrics.Lg);

        var deckWidthA = MathF.Round((avail.X - Metrics.Lg) * 0.5f);
        var deckWidthB = avail.X - deckWidthA - Metrics.Lg;

        var origin = ImGui.GetCursorScreenPos();

        DrawDeck(DeckId.A, status.DeckA, new Vector2(deckWidthA, deckAreaHeight));

        ImGui.SetCursorScreenPos(origin + new Vector2(deckWidthA + Metrics.Lg, 0f));
        DrawDeck(DeckId.B, status.DeckB, new Vector2(deckWidthB, deckAreaHeight));

        ImGui.SetCursorScreenPos(origin + new Vector2(0f, deckAreaHeight + Metrics.Lg));
        DrawMasterBar(status, new Vector2(avail.X, masterBarHeight));
    }

    /// Whether this deck is the thing listeners are currently hearing, from any source.
    private static bool IsDeckOnAir(DeckId id, DeckStatus deck, MixerStatusMessage status)
    {
        if (id == DeckId.A)
        {
            if (status.SpotifyMode.IsActive)
                return status.SpotifyMode.NowPlayingIsPlaying;

            if (status.ExternalInputMode.IsActive)
                return true;
        }
        else if (status.ExternalInputMode.IsSecondActive)
        {
            return true;
        }

        return deck.IsPlaying;
    }

    private void DrawDeck(DeckId id, DeckStatus deck, Vector2 size)
    {
        var accent = AccentFor(id);
        var client = plugin.AudioHostClient;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var glow = deckGlow.TryGetValue(id, out var previousGlow) ? previousGlow : 0f;
        glow = Motion.Approach(glow, IsDeckOnAir(id, deck, client.LatestStatus) ? 1f : 0f, Motion.SpeedFast);
        deckGlow[id] = glow;

        Elevation.DrawSurface(drawList, origin, origin + size, Elevation.Surface, Metrics.RadiusCard,
            Elevation.ShadowSpec.Low, topEdge: true, Semantic.Alpha(accent, 0.2f));

        if (glow > 0.01f)
        {
            drawList.AddRect(origin, origin + size,
                ImGui.GetColorU32(Semantic.Alpha(accent, glow * 0.18f)),
                Metrics.RadiusCard, ImDrawFlags.None, 5f * Metrics.Scale);
            drawList.AddRect(origin, origin + size,
                ImGui.GetColorU32(Semantic.Alpha(accent, 0.2f + (glow * 0.7f))),
                Metrics.RadiusCard, ImDrawFlags.None, MathF.Max(1.5f, 2f * Metrics.Scale));
        }

        ImGui.SetCursorScreenPos(origin + Metrics.PanelPadding);
        ImGui.BeginGroup();

        var inner = size.X - (Metrics.PanelPadding.X * 2f);

        DrawTransport(id, deck, accent);
        Surfaces.Gap(Metrics.Md);
        DrawDisplay(id, deck, accent, inner);
        Surfaces.Gap(Metrics.Md);
        DrawKnobsAndFader(id, deck, accent, inner);
        Surfaces.Gap(Metrics.Md);

        var usedHeight = ImGui.GetCursorScreenPos().Y - origin.Y;
        var queueHeight = MathF.Max(60f * Metrics.Scale, size.Y - usedHeight - Metrics.PanelPadding.Y);
        DrawQueue(id, accent, inner, queueHeight);

        ImGui.EndGroup();

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    /// Transport, built around one obviously-primary control.
    private void DrawTransport(DeckId id, DeckStatus deck, Vector4 accent)
    {
        var client = plugin.AudioHostClient;
        var status = client.LatestStatus;
        var spotify = status.SpotifyMode;
        var spotifyDrivesThisDeck = spotify.IsActive && id == DeckId.A;

        var playSize = MathF.Round(46f * Metrics.Scale);
        var buttonSize = MathF.Round(34f * Metrics.Scale);
        var gap = Metrics.Md;
        var origin = ImGui.GetCursorScreenPos();

        if (spotify.IsActive && id == DeckId.B)
        {
            const string notice = "Deck B is unavailable while Spotify Mode is on.";
            using (TypeScale.Body())
            {
                var textSize = ImGui.CalcTextSize(notice);
                Chrome.Text(ImGui.GetWindowDrawList(),
                    Chrome.CenterY(origin.X, origin.Y, playSize, textSize.Y),
                    ImGui.GetColorU32(Semantic.TextTertiary), notice);
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + playSize));
            ImGui.Dummy(new Vector2(0f, 0f));
            return;
        }

        var isPlaying = spotifyDrivesThisDeck ? spotify.NowPlayingIsPlaying : deck.IsPlaying;

        if (Mixer.PlayButton($"##v2play{id}", isPlaying, playSize, accent,
                isPlaying ? "Pause" : "Play",
                spotifyDrivesThisDeck ? "Controls Spotify playback." : null))
        {
            if (spotifyDrivesThisDeck)
                client.Send(MessageType.SpotifyTogglePlayPause, new object());
            else
                client.Send(MessageType.TogglePlay, new DeckCommand { Deck = id });
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X + playSize + Metrics.Lg,
            origin.Y + MathF.Round((playSize - buttonSize) * 0.5f)));

        using var spacing = Sty.New().Var(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));

        if (Mixer.TransportButton($"##v2prev{id}", FontAwesomeIcon.StepBackward,
                spotifyDrivesThisDeck ? "Previous Track" : "Jump to Cue", null, buttonSize, accent))
        {
            if (spotifyDrivesThisDeck)
                client.Send(MessageType.SpotifySkipPrevious, new object());
            else
                client.Send(MessageType.JumpToCue, new DeckCommand { Deck = id });
        }

        ImGui.SameLine();
        if (Mixer.TransportButton($"##v2next{id}", FontAwesomeIcon.StepForward,
                spotifyDrivesThisDeck ? "Next Track" : "Next Song",
                spotifyDrivesThisDeck ? null : "Loads whatever is next in this deck's queue.",
                buttonSize, accent))
        {
            if (spotifyDrivesThisDeck)
                client.Send(MessageType.SpotifySkipNext, new object());
            else
                client.Send(MessageType.UnloadDeck, new DeckCommand { Deck = id });
        }

        ImGui.SameLine();
        if (Mixer.TransportButton($"##v2cueSet{id}", FontAwesomeIcon.MapMarkerAlt, "Set Cue Here", null,
                buttonSize, accent, enabled: !spotifyDrivesThisDeck)
            && !spotifyDrivesThisDeck)
            client.Send(MessageType.SetCue, new DeckCommand { Deck = id });

        ImGui.SameLine();
        if (Mixer.TransportButton($"##v2autoplay{id}", FontAwesomeIcon.Forward, "Autoplay",
                deck.AutoplayEnabled ? "On. Starts the next queued track automatically." : "Off.",
                buttonSize, accent, toggledOn: deck.AutoplayEnabled, enabled: !spotifyDrivesThisDeck)
            && !spotifyDrivesThisDeck)
            client.Send(MessageType.SetDeckAutoplay, new SetDeckAutoplayCommand { Deck = id, Enabled = !deck.AutoplayEnabled });

        var other = id == DeckId.A ? status.DeckB : status.DeckA;
        var syncAvailable = !spotifyDrivesThisDeck && deck.HasTrack && deck.Bpm is > 0f && other.HasTrack && other.Bpm is > 0f;

        ImGui.SameLine();
        if (Mixer.TransportButton($"##v2sync{id}", FontAwesomeIcon.Link, "Sync",
                deck.SyncEnabled
                    ? $"On, pulling this deck to {(deck.TempoRatio * 100f) - 100f:+0.0;-0.0}%."
                    : syncAvailable ? "Match this deck's tempo to the other." : "Both decks need a known BPM.",
                buttonSize, accent, toggledOn: deck.SyncEnabled, enabled: syncAvailable || deck.SyncEnabled)
            && (syncAvailable || deck.SyncEnabled))
            client.Send(MessageType.SetDeckSync, new SetDeckSyncCommand { Deck = id, Enabled = !deck.SyncEnabled });

        if (id == DeckId.A)
        {
            ImGui.SameLine(0f, Metrics.Xl);
            DrawSourceChip(status, buttonSize);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + playSize));
        ImGui.Dummy(new Vector2(0f, 0f));
    }

    /// What this deck is playing: its own tracks, Spotify, a line-in device, or another application's audio.
    private void DrawSourceChip(MixerStatusMessage status, float height)
    {
        var client = plugin.AudioHostClient;
        var config = plugin.Configuration;
        var spotify = status.SpotifyMode;
        var lineIn = status.ExternalInputMode;

        var usingProcess = config.ExternalInputUseProcessCapture;
        var label = spotify.IsActive ? "SPOTIFY"
            : lineIn.IsActive ? (usingProcess ? "APP" : "LINE IN")
            : "DECKS";

        var accent = spotify.IsActive ? Semantic.Spotify
            : lineIn.IsActive ? Semantic.Warning
            : Semantic.TextSecondary;

        var active = spotify.IsActive || lineIn.IsActive;

        var sourceError = spotify.Error ?? lineIn.Error;
        var chipTip = sourceError ?? "What listeners hear. Click to change.";

        if (Mixer.ToggleChip("##v2source", FontAwesomeIcon.ExchangeAlt, label, active, height,
                sourceError != null ? Semantic.Danger : accent, "Broadcast Source", chipTip))
            ImGui.OpenPopup("##v2sourcePopup");

        using var popupStyle = Sty.Popup();
        if (!ImGui.BeginPopup("##v2sourcePopup"))
            return;

        if (SourceOption("Decks", "Your own two decks, mixed with the crossfader.", !active))
        {
            if (spotify.IsActive)
                client.Send(MessageType.StopSpotifyMode, new object());
            if (lineIn.IsActive)
                client.Send(MessageType.StopExternalInputMode, new object());
            ImGui.CloseCurrentPopup();
        }

        if (SourceOption("Spotify", "Captures the Spotify desktop app.", spotify.IsActive,
                error: spotify.Error))
        {
            if (lineIn.IsActive)
                client.Send(MessageType.StopExternalInputMode, new object());

            client.Send(MessageType.StartSpotifyMode, new object());
            ImGui.CloseCurrentPopup();
        }

        var hasDevice = !string.IsNullOrWhiteSpace(config.ExternalInputDeviceId);
        if (SourceOption("Line In", hasDevice
                ? config.ExternalInputDeviceName ?? "Selected device"
                : "Choose a recording device in Settings > Source.",
                lineIn.IsActive && !usingProcess, hasDevice, error: usingProcess ? null : lineIn.Error))
        {
            client.Send(MessageType.StopSpotifyMode, new object());
            client.Send(MessageType.StartExternalInputMode, new StartExternalInputModeCommand
            {
                DeviceId = config.ExternalInputDeviceId ?? string.Empty,
                DeviceId2 = config.ExternalInputDeviceId2,
            });
            ImGui.CloseCurrentPopup();
        }

        var hasProcess = !string.IsNullOrWhiteSpace(config.ExternalInputProcessName);
        if (SourceOption("Application", hasProcess
                ? config.ExternalInputProcessDisplayName ?? config.ExternalInputProcessName!
                : "Choose an application in Settings > Source.",
                lineIn.IsActive && usingProcess, hasProcess, error: usingProcess ? lineIn.Error : null))
        {
            client.Send(MessageType.StopSpotifyMode, new object());
            client.Send(MessageType.StartExternalInputMode, new StartExternalInputModeCommand
            {
                ProcessName = config.ExternalInputProcessName,
            });
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// One row of the source popup.
    private bool SourceOption(string label, string detail, bool selected, bool enabled = true, string? error = null)
    {
        var width = MathF.Round(300f * Metrics.Scale);
        var height = MathF.Round(Metrics.ControlXl + Metrics.Sm);

        var errorLines = string.IsNullOrWhiteSpace(error)
            ? Array.Empty<string>()
            : UiHelpers.WrapToWidth(error, width - (Metrics.Lg * 2f), 3);

        if (errorLines.Length > 0)
            height = MathF.Round(height + Metrics.Xs + (errorLines.Length * ImGui.GetTextLineHeight()));

        var pos = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##src{label}", new Vector2(width, height)) && enabled && !selected;
        var hovered = ImGui.IsItemHovered() && enabled;
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        if (selected || hovered)
            drawList.AddRectFilled(Chrome.Snap(pos), Chrome.Snap(pos + new Vector2(width, height)),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, selected ? 0.2f : 0.1f)), Metrics.RadiusSoft);

        var labelColor = !enabled ? Semantic.TextDisabled
            : selected ? Semantic.TextPrimary : Semantic.TextSecondary;

        var labelSize = TypeScale.Measure(TypeScale.Body, label);
        var detailShown = UiHelpers.TruncateToWidth(detail, width - (Metrics.Lg * 2f));
        var detailSize = TypeScale.Measure(TypeScale.Caption, detailShown);

        var stack = labelSize.Y + Metrics.Xs + detailSize.Y;
        if (errorLines.Length > 0)
            stack += Metrics.Xs + (errorLines.Length * ImGui.GetTextLineHeight());

        var top = pos.Y + ((height - stack) * 0.5f);

        using (TypeScale.Body())
            Chrome.Text(drawList, new Vector2(pos.X + Metrics.Lg, top), ImGui.GetColorU32(labelColor), label);

        top += labelSize.Y + Metrics.Xs;

        using (TypeScale.Caption())
        {
            Chrome.Text(drawList, new Vector2(pos.X + Metrics.Lg, top),
                ImGui.GetColorU32(enabled ? Semantic.TextTertiary : Semantic.TextDisabled), detailShown);

            top += detailSize.Y + Metrics.Xs;

            foreach (var line in errorLines)
            {
                Chrome.Text(drawList, new Vector2(pos.X + Metrics.Lg, top),
                    ImGui.GetColorU32(Semantic.Danger), line);
                top += ImGui.GetTextLineHeight();
            }
        }

        return clicked;
    }

    private void DrawDisplay(DeckId id, DeckStatus deck, Vector4 accent, float width)
    {
        var client = plugin.AudioHostClient;
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        var titleHeight = MathF.Round(Metrics.ControlMd);
        var seekHeight = MathF.Round(8f * Metrics.Scale);
        var visualizerHeight = MathF.Round(118f * Metrics.Scale);
        var totalHeight = titleHeight + seekHeight + visualizerHeight + (Metrics.Md * 3f);

        drawList.AddRectFilled(origin, origin + new Vector2(width, totalHeight),
            ImGui.GetColorU32(Elevation.Sunken), Metrics.RadiusSoft);

        var padding = Metrics.Md;
        var innerWidth = width - (padding * 2f);

        var spotify = client.LatestStatus.SpotifyMode;
        var spotifyHere = spotify.IsActive && id == DeckId.A;

        using (TypeScale.Body())
        {
            string title;
            string time;

            if (spotifyHere)
            {
                title = string.IsNullOrWhiteSpace(spotify.NowPlayingTitle)
                    ? "Spotify"
                    : string.IsNullOrWhiteSpace(spotify.NowPlayingArtist)
                        ? spotify.NowPlayingTitle!
                        : $"{spotify.NowPlayingTitle}  -  {spotify.NowPlayingArtist}";
                time = spotify.NowPlayingDurationSeconds > 0.01
                    ? $"{Format(spotify.NowPlayingPositionSeconds)} / {Format(spotify.NowPlayingDurationSeconds)}"
                    : "--:-- / --:--";
            }
            else
            {
                title = deck.HasTrack ? deck.TrackTitle ?? "Untitled" : "No track loaded";
                time = deck.HasTrack ? $"{Format(deck.PositionSeconds)} / {Format(deck.DurationSeconds)}" : "--:-- / --:--";
            }

            var timeSize = ImGui.CalcTextSize(time);
            var right = origin.X + width - padding;

            Chrome.Text(drawList,
                Chrome.CenterY(right - timeSize.X, origin.Y + padding, titleHeight, timeSize.Y),
                ImGui.GetColorU32(Semantic.TextSecondary), time);
            right -= timeSize.X + Metrics.Lg;

            if (!spotifyHere)
            {
                var bpm = deck.Bpm is > 0f ? $"{deck.Bpm:0.0} BPM" : "-- BPM";
                var bpmSize = ImGui.CalcTextSize(bpm);
                Chrome.Text(drawList, Chrome.CenterY(right - bpmSize.X, origin.Y + padding, titleHeight, bpmSize.Y),
                    ImGui.GetColorU32(deck.Bpm is > 0f ? Semantic.TextSecondary : Semantic.TextTertiary), bpm);
                right -= bpmSize.X + Metrics.Lg;

                if (deck.SyncEnabled && MathF.Abs(deck.TempoRatio - 1f) > 0.0005f)
                {
                    var offset = $"{(deck.TempoRatio * 100f) - 100f:+0.0;-0.0}%";
                    var offsetSize = ImGui.CalcTextSize(offset);
                    Chrome.Text(drawList,
                        Chrome.CenterY(right - offsetSize.X, origin.Y + padding, titleHeight, offsetSize.Y),
                        ImGui.GetColorU32(accent), offset);
                    right -= offsetSize.X + Metrics.Lg;
                }
            }

            var shownTitle = UiHelpers.TruncateToWidth(title, MathF.Max(40f, right - origin.X - padding));
            var titleSize = ImGui.CalcTextSize(shownTitle);

            Chrome.Text(drawList, Chrome.CenterY(origin.X + padding, origin.Y + padding, titleHeight, titleSize.Y),
                ImGui.GetColorU32(spotifyHere || deck.HasTrack ? Semantic.TextPrimary : Semantic.TextTertiary),
                shownTitle);
        }

        ref var pendingSeek = ref (id == DeckId.A ? ref pendingSeekA : ref pendingSeekB);
        if (pendingSeek.HasValue && Math.Abs(deck.PositionSeconds - pendingSeek.Value) < 0.5)
            pendingSeek = null;

        var position = spotifyHere
            ? (float)spotify.NowPlayingPositionSeconds
            : pendingSeek ?? (float)deck.PositionSeconds;
        var duration = spotifyHere
            ? (float)Math.Max(spotify.NowPlayingDurationSeconds, 0.01)
            : (float)Math.Max(deck.DurationSeconds, 0.01);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + padding, origin.Y + padding + titleHeight + padding));
        if (Mixer.SeekBar($"##v2seek{id}", ref position, duration,
                spotifyHere ? null : (float)deck.CuePointSeconds,
                new Vector2(innerWidth, seekHeight), accent, !spotifyHere && deck.HasTrack))
        {
            pendingSeek = position;
            client.Send(MessageType.SetPosition, new SetPositionCommand { Deck = id, PositionSeconds = position });
        }

        var spectrum = id == DeckId.A ? client.LatestSpectrumA : client.LatestSpectrumB;
        var style = (VisualizerWidget.Style)plugin.Configuration.DeckVisualizerStyle;

        var visualizerPos = new Vector2(origin.X + padding,
            origin.Y + padding + titleHeight + padding + seekHeight + padding);

        ImGui.SetCursorScreenPos(visualizerPos);

        drawList.PushClipRect(visualizerPos, visualizerPos + new Vector2(innerWidth, visualizerHeight), true);
        VisualizerWidget.Draw($"##v2vis{id}", spectrum, new Vector2(innerWidth, visualizerHeight),
            accent, style, plugin.Configuration.DeckVisualizerSensitivity);
        drawList.PopClipRect();

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, totalHeight));
    }

    private void DrawKnobsAndFader(DeckId id, DeckStatus deck, Vector4 accent, float width)
    {
        var client = plugin.AudioHostClient;
        var origin = ImGui.GetCursorScreenPos();

        var faderWidth = MathF.Round(52f * Metrics.Scale);
        var faderHeight = MathF.Round(150f * Metrics.Scale);
        var knobRadius = MathF.Round(21f * Metrics.Scale);
        var knobArea = width - faderWidth - Metrics.Lg;

        var faderOnLeft = id == DeckId.A;
        var knobsX = faderOnLeft ? origin.X + faderWidth + Metrics.Lg : origin.X;
        var faderX = faderOnLeft ? origin.X : origin.X + knobArea + Metrics.Lg;

        ImGui.SetCursorScreenPos(new Vector2(faderX, origin.Y));
        var gain = deck.Gain;
        if (Mixer.Fader($"##v2fader{id}", ref gain, 0f, 1.5f, 1f, new Vector2(faderWidth, faderHeight), accent, out _))
            client.Send(MessageType.SetGain, new SetGainCommand { Deck = id, Gain = gain });

        var slot = knobArea / 5f;
        var knobTop = origin.Y + Metrics.Sm;

        DrawKnob(ref knobsX, knobTop, slot, knobRadius, $"##v2trim{id}", "GAIN", deck.Trim, 0f, 2f, 1f, accent,
            v => client.Send(MessageType.SetTrim, new SetTrimCommand { Deck = id, Trim = v }), "{0:F2}x");

        DrawKnob(ref knobsX, knobTop, slot, knobRadius, $"##v2high{id}", "HIGH", deck.HighGainDb, -26f, 6f, 0f, accent,
            v => client.Send(MessageType.SetEq, new SetEqCommand { Deck = id, Band = EqBand.High, GainDb = v }), "{0:+0.0;-0.0} dB");

        DrawKnob(ref knobsX, knobTop, slot, knobRadius, $"##v2mid{id}", "MID", deck.MidGainDb, -26f, 6f, 0f, accent,
            v => client.Send(MessageType.SetEq, new SetEqCommand { Deck = id, Band = EqBand.Mid, GainDb = v }), "{0:+0.0;-0.0} dB");

        DrawKnob(ref knobsX, knobTop, slot, knobRadius, $"##v2low{id}", "LOW", deck.LowGainDb, -26f, 6f, 0f, accent,
            v => client.Send(MessageType.SetEq, new SetEqCommand { Deck = id, Band = EqBand.Low, GainDb = v }), "{0:+0.0;-0.0} dB");

        DrawKnob(ref knobsX, knobTop, slot, knobRadius, $"##v2filter{id}", "FILTER", deck.FilterKnob, -1f, 1f, 0f, accent,
            v => client.Send(MessageType.SetFilter, new SetFilterCommand { Deck = id, Knob = v }), "{0:+0.00;-0.00}");

        var knobsBottom = knobTop + ImGui.GetTextLineHeight() + Metrics.Xs + (knobRadius * 2f) + Metrics.Md;
        var padsLeft = faderOnLeft ? origin.X + faderWidth + Metrics.Lg : origin.X;
        var padsHeight = MathF.Max(0f, origin.Y + faderHeight - knobsBottom);

        DrawSoundPads(id, accent, new Vector2(padsLeft, knobsBottom), knobArea, padsHeight);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, faderHeight));
    }

    private static void DrawKnob(
        ref float x,
        float y,
        float slot,
        float radius,
        string id,
        string label,
        float current,
        float min,
        float max,
        float defaultValue,
        Vector4 accent,
        Action<float> send,
        string format)
    {
        ImGui.SetCursorScreenPos(new Vector2(MathF.Round(x + ((slot - (radius * 2f)) * 0.5f)), y));

        var value = current;
        if (Mixer.Knob(id, label, ref value, min, max, defaultValue, radius, accent, format))
            send(value);

        x += slot;
    }

    /// Four sound pads per deck - pads 0-3 belong to A, 4-7 to B, matching 1.0's single row of eight split
    /// across the two decks.
    private void DrawSoundPads(DeckId id, Vector4 accent, Vector2 origin, float width, float height)
    {
        if (height < 20f)
            return;

        var client = plugin.AudioHostClient;
        var pads = client.LatestSoundPads;
        var baseIndex = id == DeckId.A ? 0 : 4;

        var gap = Metrics.Sm;
        var padWidth = MathF.Round((width - gap) * 0.5f);
        var padHeight = MathF.Round(MathF.Min(36f * Metrics.Scale, (height - gap) * 0.5f));

        for (var i = 0; i < 4; i++)
        {
            var index = baseIndex + i;
            var pad = index < pads.Count ? pads[index] : new SoundPadDto();
            var hasSound = !string.IsNullOrEmpty(pad.FilePath);
            var padId = $"##v2pad{index}";

            var col = i % 2;
            var row = i / 2;
            ImGui.SetCursorScreenPos(Chrome.Snap(new Vector2(
                origin.X + (col * (padWidth + gap)),
                origin.Y + (row * (padHeight + gap)))));

            var label = string.IsNullOrWhiteSpace(pad.Label) ? $"Pad {index + 1}" : pad.Label;
            if (Mixer.PadButton(padId, label, hasSound, pad.Looping, new Vector2(padWidth, padHeight), accent,
                    out var middleClicked))
            {
                if (hasSound)
                    client.Send(MessageType.PlaySoundPad, new SoundPadIndexCommand { PadIndex = index });
                else
                    plugin.DjDeckWindow.OpenSoundPadUpload(index);
            }

            if (middleClicked && hasSound)
                client.Send(MessageType.SetSoundPadLoop, new SetSoundPadLoopCommand { PadIndex = index, Looping = !pad.Looping });

            DrawPadContextMenu(padId, index, hasSound, pad, accent);
        }
    }

    /// The pad's full settings, matching what 1.0's own right-click menu offers: the file, its label, the
    /// loop interval and its volume.
    private void DrawPadContextMenu(string padId, int index, bool hasSound, SoundPadDto pad, Vector4 accent)
    {
        var client = plugin.AudioHostClient;

        using var popupStyle = Sty.Popup();
        if (!ImGui.BeginPopupContextItem(padId))
            return;

        if (ImGui.IsWindowAppearing())
        {
            padLabelBuffers[index] = pad.Label;
            padIntervalBuffers[index] = pad.LoopIntervalSeconds;
            padVolumeBuffers[index] = pad.Volume;
        }

        var menuWidth = MathF.Round(200f * Metrics.Scale);

        if (Fields.MenuRow(hasSound ? "Replace Sound Effect" : "Upload Sound Effect", menuWidth))
            plugin.DjDeckWindow.OpenSoundPadUpload(index);

        if (hasSound && Fields.MenuRow("Remove Sound Effect", menuWidth))
        {
            client.Send(MessageType.RemoveSoundPad, new SoundPadIndexCommand { PadIndex = index });
            ImGui.CloseCurrentPopup();
        }

        if (!hasSound)
        {
            ImGui.EndPopup();
            return;
        }

        ImGui.Separator();

        var fieldWidth = MathF.Round(150f * Metrics.Scale);

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "Label");

        ImGui.SetNextItemWidth(fieldWidth);
        if (ImGui.InputTextWithHint($"##v2padLabel{index}", "Pad name", ref padLabelBuffers[index], 32,
                ImGuiInputTextFlags.EnterReturnsTrue) && !string.IsNullOrWhiteSpace(padLabelBuffers[index]))
        {
            client.Send(MessageType.SetSoundPadLabel, new SetSoundPadLabelCommand
            {
                PadIndex = index,
                Label = padLabelBuffers[index].Trim(),
            });
        }

        ImGui.SameLine(0f, Metrics.Md);
        if (Fields.Button("Set", height: ImGui.GetFrameHeight(), idSuffix: $"padLabel{index}")
            && !string.IsNullOrWhiteSpace(padLabelBuffers[index]))
        {
            client.Send(MessageType.SetSoundPadLabel, new SetSoundPadLabelCommand
            {
                PadIndex = index,
                Label = padLabelBuffers[index].Trim(),
            });
        }

        ImGui.Separator();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "Repeat delay (seconds)");

        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputFloat($"##v2padInterval{index}", ref padIntervalBuffers[index], 0.1f, 0.5f, "%.1f");
        padIntervalBuffers[index] = MathF.Max(0.1f, padIntervalBuffers[index]);

        ImGui.SameLine(0f, Metrics.Md);
        if (Fields.Button("Set", height: ImGui.GetFrameHeight(), idSuffix: $"padInterval{index}"))
            client.Send(MessageType.SetSoundPadLoopInterval, new SetSoundPadLoopIntervalCommand
            {
                PadIndex = index,
                IntervalSeconds = padIntervalBuffers[index],
            });

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "How long between repeats while the pad is looping.");

        ImGui.Separator();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "Volume");

        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputFloat($"##v2padVolume{index}", ref padVolumeBuffers[index], 0.05f, 0.25f, "%.2f");
        padVolumeBuffers[index] = Math.Clamp(padVolumeBuffers[index], 0f, 2f);

        ImGui.SameLine(0f, Metrics.Md);
        if (Fields.Button("Set", height: ImGui.GetFrameHeight(), idSuffix: $"padVolume{index}"))
            client.Send(MessageType.SetSoundPadVolume, new SetSoundPadVolumeCommand
            {
                PadIndex = index,
                Volume = padVolumeBuffers[index],
            });

        ImGui.EndPopup();
    }

    private readonly string[] padLabelBuffers = Enumerable.Repeat(string.Empty, 8).ToArray();
    private readonly float[] padIntervalBuffers = new float[8];
    private readonly float[] padVolumeBuffers = new float[8];

    /// The deck's own up-next list.
    private static bool QueueIcon(string id, FontAwesomeIcon icon, float size, bool enabled, string tooltip, bool danger = false)
    {
        var pos = Chrome.Snap(ImGui.GetCursorScreenPos());
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered() && enabled;

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        var centre = Chrome.Snap(pos + new Vector2(size * 0.5f, size * 0.5f));
        var tint = danger ? Semantic.Danger : Semantic.Primary;

        if (hovered)
            drawList.AddCircleFilled(centre, size * 0.5f, ImGui.GetColorU32(Semantic.Alpha(tint, 0.2f)));

        var color = !enabled
            ? Semantic.TextDisabled
            : hovered ? tint : Semantic.TextTertiary;

        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, icon, centre, ImGui.GetColorU32(color));

        if (hovered)
            Tip.Hovered(tooltip, string.Empty);

        return clicked && enabled;
    }

    private void DrawQueue(DeckId id, Vector4 accent, float width, float height)
    {
        var client = plugin.AudioHostClient;
        var queues = client.LatestDeckQueues;
        var queue = id == DeckId.A ? queues.QueueA.Tracks : queues.QueueB.Tracks;

        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(origin, origin + new Vector2(width, height),
            ImGui.GetColorU32(Elevation.Sunken), Metrics.RadiusSoft);

        var padding = Metrics.Md;
        var headerHeight = ImGui.GetTextLineHeight() + Metrics.Sm;

        using (TypeScale.Caption())
        {
            var header = queue.Count == 0 ? "UP NEXT" : $"UP NEXT  ·  {queue.Count}";
            Chrome.Text(drawList, Chrome.Snap(origin + new Vector2(padding, padding)),
                ImGui.GetColorU32(Semantic.TextTertiary), header);
        }

        var listTop = origin.Y + padding + headerHeight;
        var listHeight = MathF.Max(1f, height - padding - headerHeight - padding);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + padding, listTop));
        ImGui.BeginChild($"##v2queue{id}", new Vector2(width - (padding * 2f), listHeight), false);

        if (queue.Count == 0)
        {
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextTertiary, "Queue songs from the Library.");
        }

        var rowHeight = MathF.Round(Metrics.ControlSm);
        var iconSize = MathF.Round(Metrics.ControlXs);
        var controlsWidth = (iconSize * 3f) + (Metrics.Xs * 2f);

        for (var i = 0; i < queue.Count; i++)
        {
            var track = queue[i];
            var rowPos = Chrome.Snap(ImGui.GetCursorScreenPos());
            var rowWidth = ImGui.GetContentRegionAvail().X;
            var rowDrawList = ImGui.GetWindowDrawList();

            var hitWidth = MathF.Max(Metrics.Xl, rowWidth - controlsWidth - Metrics.Xs);
            ImGui.InvisibleButton($"##v2q{id}{i}", new Vector2(hitWidth, rowHeight));
            var hovered = ImGui.IsItemHovered();

            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
            {
                if (client.LatestStatus.PreviewingFilePath == track.FilePath)
                    client.Send(MessageType.StopPreviewTrack, new object());
                else
                    client.Send(MessageType.PreviewTrack, new PreviewTrackCommand { FilePath = track.FilePath });
            }

            var rowHovered = hovered || ImGui.IsMouseHoveringRect(
                rowPos, rowPos + new Vector2(rowWidth, rowHeight));

            if (rowHovered)
                rowDrawList.AddRectFilled(rowPos, Chrome.Snap(rowPos + new Vector2(rowWidth, rowHeight)),
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.05f)), Metrics.RadiusSharp);

            var previewing = client.LatestStatus.PreviewingFilePath == track.FilePath;

            using (TypeScale.Caption())
            {
                var title = UiHelpers.TruncateToWidth($"{i + 1}.  {track.Title}", hitWidth - Metrics.Md);
                var size = ImGui.CalcTextSize(title);
                Chrome.Text(rowDrawList, Chrome.CenterY(rowPos.X + Metrics.Sm, rowPos.Y, rowHeight, size.Y),
                    ImGui.GetColorU32(previewing ? accent : Semantic.TextSecondary), title);
            }

            if (rowHovered)
            {
                var controlsX = rowPos.X + rowWidth - controlsWidth;
                var controlsY = rowPos.Y + ((rowHeight - iconSize) * 0.5f);
                var move = 0;

                ImGui.SetCursorScreenPos(new Vector2(controlsX, controlsY));
                if (QueueIcon($"##v2qup{id}{i}", FontAwesomeIcon.ChevronUp, iconSize, i > 0, "Move up"))
                    move = -1;

                ImGui.SetCursorScreenPos(new Vector2(controlsX + iconSize + Metrics.Xs, controlsY));
                if (QueueIcon($"##v2qdn{id}{i}", FontAwesomeIcon.ChevronDown, iconSize, i < queue.Count - 1, "Move down"))
                    move = 1;

                ImGui.SetCursorScreenPos(new Vector2(controlsX + ((iconSize + Metrics.Xs) * 2f), controlsY));
                var removed = QueueIcon($"##v2qrm{id}{i}", FontAwesomeIcon.Times, iconSize, true, "Remove", danger: true);

                if (removed)
                {
                    client.Send(MessageType.RemoveFromDeckQueue, new RemoveFromDeckQueueCommand { Deck = id, Index = i });
                }
                else if (move != 0)
                {
                    client.Send(MessageType.MoveInDeckQueue,
                        new MoveInDeckQueueCommand { Deck = id, Index = i, Up = move < 0 });
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(rowPos.X, rowPos.Y + rowHeight));
        }

        ImGui.EndChild();

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    /// The bottom master bar: deck meters flanking the crossfader, with curve and Auto-DJ beneath it and
    /// master level on the right.
    private void DrawMasterBar(MixerStatusMessage status, Vector2 size)
    {
        var client = plugin.AudioHostClient;
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        Elevation.DrawSurface(drawList, origin, origin + size, Elevation.Surface, Metrics.RadiusCard,
            Elevation.ShadowSpec.Low, topEdge: true, border: null);

        drawList.AddRect(origin, origin + size,
            ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.2f)),
            Metrics.RadiusCard, ImDrawFlags.None, 1f);

        var padding = Metrics.Lg;
        var innerTop = origin.Y + padding;
        var innerHeight = size.Y - (padding * 2f);

        var meterWidth = MathF.Round(130f * Metrics.Scale);
        var meterHeight = MathF.Round(10f * Metrics.Scale);
        var masterWidth = MathF.Round(170f * Metrics.Scale);

        DrawMeterBlock(DeckId.A, "##v2meterA", "A", Semantic.DeckA, status.DeckA,
            new Vector2(origin.X + padding, innerTop), meterWidth, meterHeight);

        DrawMeterBlock(DeckId.B, "##v2meterB", "B", Semantic.DeckB, status.DeckB,
            new Vector2(origin.X + size.X - padding - masterWidth - Metrics.Xxl - meterWidth, innerTop),
            meterWidth, meterHeight);

        var crossLeft = origin.X + padding + meterWidth + Metrics.Xxl;
        var crossRight = origin.X + size.X - padding - masterWidth - Metrics.Xxl - meterWidth - Metrics.Xxl;
        var crossWidth = MathF.Max(80f, crossRight - crossLeft);
        var crossHeight = MathF.Round(34f * Metrics.Scale);

        ImGui.SetCursorScreenPos(new Vector2(crossLeft, innerTop));
        var crossfade = status.CrossfaderPosition;
        if (Mixer.Crossfader("##v2crossfader", ref crossfade, new Vector2(crossWidth, crossHeight),
                Semantic.DeckA, Semantic.DeckB, out _))
            client.Send(MessageType.SetCrossfader, new SetCrossfaderCommand { Position = crossfade });

        DrawCurveAndAutoDj(status, new Vector2(crossLeft, innerTop + crossHeight + Metrics.Md), crossWidth);

        DrawMasterLevel(status, new Vector2(origin.X + size.X - padding - masterWidth, innerTop), masterWidth, innerHeight);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    /// Deck label, peak meter, and that deck's tempo fader stacked under them.
    private void DrawMeterBlock(
        DeckId deckId,
        string id,
        string label,
        Vector4 accent,
        DeckStatus deck,
        Vector2 pos,
        float width,
        float height)
    {
        var client = plugin.AudioHostClient;
        var drawList = ImGui.GetWindowDrawList();
        var ratio = deck.TempoRatio;

        var spotify = client.LatestStatus.SpotifyMode;
        var external = client.LatestStatus.ExternalInputMode;
        var replaced = deckId == DeckId.A
            ? spotify.IsActive || external.IsActive
            : external.IsSecondActive;
        var enabled = deck.HasTrack && !replaced;

        float captionHeight;
        using (TypeScale.Caption())
        {
            captionHeight = ImGui.CalcTextSize(label).Y;
            Chrome.Text(drawList, Chrome.Snap(pos), ImGui.GetColorU32(accent), label);

            var percent = $"{(ratio * 100f) - 100f:+0.0;-0.0}%";
            var readout = deck.Bpm is > 0f ? $"{deck.Bpm * ratio:0.#} BPM  {percent}" : percent;
            var readoutSize = ImGui.CalcTextSize(readout);
            Chrome.Text(drawList, Chrome.Snap(new Vector2(pos.X + width - readoutSize.X, pos.Y)),
                ImGui.GetColorU32(enabled ? Semantic.TextTertiary : Semantic.TextDisabled), readout);
        }

        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + captionHeight + Metrics.Xs));
        Mixer.PeakMeter(id, new Vector2(width, height), deck.PeakLevel, vertical: false);

        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + captionHeight + Metrics.Xs + height + Metrics.Md));

        ImGui.BeginDisabled(!enabled);

        var min = 1f - (TempoRangePercent / 100f);
        var max = 1f + (TempoRangePercent / 100f);
        var fraction = Math.Clamp((ratio - min) / (max - min), 0f, 1f);

        if (Mixer.Crossfader($"##v2tempo{deckId}", ref fraction,
                new Vector2(width, MathF.Round(22f * Metrics.Scale)), accent, accent, out _)
            && enabled)
        {
            client.Send(MessageType.SetDeckTempo, new SetDeckTempoCommand
            {
                Deck = deckId,
                Ratio = min + (fraction * (max - min)),
            });
        }

        ImGui.EndDisabled();
    }

    /// Matches 1.0's TempoSliderRangePercent, so a tempo set in one look reads identically in the other - the
    /// engine stores a ratio, and the range is purely the UI's own convention.
    private const float TempoRangePercent = 16f;

    private void DrawCurveAndAutoDj(MixerStatusMessage status, Vector2 pos, float width)
    {
        var client = plugin.AudioHostClient;
        var config = plugin.Configuration;
        var buttonSize = MathF.Round(Metrics.ControlSm);

        ImGui.SetCursorScreenPos(pos);

        using var spacing = Sty.New().Var(ImGuiStyleVar.ItemSpacing, new Vector2(Metrics.Sm, Metrics.Sm));

        DrawCurveButton("##v2curveC", "C", CrossfaderCurve.Cut, status.CrossfaderCurve, buttonSize);
        ImGui.SameLine();
        DrawCurveButton("##v2curveP", "P", CrossfaderCurve.Power, status.CrossfaderCurve, buttonSize);
        ImGui.SameLine();
        DrawCurveButton("##v2curveL", "L", CrossfaderCurve.Linear, status.CrossfaderCurve, buttonSize);

        ImGui.SameLine(0f, Metrics.Lg);

        var autoDj = status.AutoDjEnabled;
        if (Mixer.ToggleChip("##v2autodj", FontAwesomeIcon.Random, "AUTO", autoDj, buttonSize, Semantic.Primary,
                "Auto-DJ",
                autoDj
                    ? $"On. Crossfades between decks automatically over {status.AutoDjFadeSeconds:0.#}s."
                    : "Off. Mix manually."))
        {
            client.Send(MessageType.SetAutoDj, new SetAutoDjCommand
            {
                Enabled = !autoDj,
                FadeSeconds = status.AutoDjFadeSeconds,
            });
            config.AutoDjEnabled = !autoDj;
            config.Save();
        }
    }

    private void DrawCurveButton(string id, string label, CrossfaderCurve curve, CrossfaderCurve? active, float size)
    {
        var client = plugin.AudioHostClient;
        var isOn = active == curve;
        var box = MathF.Round(size);
        var min = Chrome.Snap(ImGui.GetCursorScreenPos());

        var clicked = ImGui.InvisibleButton(id, new Vector2(box, box));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        var fill = isOn ? Semantic.Alpha(Semantic.Primary, 0.25f) : Elevation.Raised;
        if (hovered)
            fill = Semantic.Lift(fill, 0.1f);

        drawList.AddRectFilled(min, min + new Vector2(box, box), ImGui.GetColorU32(fill), box * 0.5f);
        drawList.AddRect(min, min + new Vector2(box, box),
            ImGui.GetColorU32(isOn ? Semantic.Primary : Elevation.Line), box * 0.5f, ImDrawFlags.None, Metrics.Hairline);

        using (TypeScale.Caption())
        {
            var textSize = ImGui.CalcTextSize(label);
            Chrome.Text(drawList,
                Chrome.Snap(min + new Vector2((box - textSize.X) * 0.5f, (box - textSize.Y) * 0.5f)),
                ImGui.GetColorU32(isOn ? Semantic.TextPrimary : Semantic.TextTertiary), label);
        }

        Tip.Hovered($"{CurveName(curve)} Crossfade",
            isOn ? "Currently active. Click to turn off." : "Click to use this crossfader curve.");

        if (!clicked)
            return;

        var next = isOn ? (CrossfaderCurve?)null : curve;
        client.Send(MessageType.SetCrossfaderCurve, new SetCrossfaderCurveCommand { Curve = next });
        plugin.Configuration.CrossfaderCurve = next;
        plugin.Configuration.Save();
    }

    private static string CurveName(CrossfaderCurve curve) => curve switch
    {
        CrossfaderCurve.Cut => "Cut",
        CrossfaderCurve.Power => "Power",
        _ => "Linear",
    };

    private void DrawMasterLevel(MixerStatusMessage status, Vector2 pos, float width, float height)
    {
        var client = plugin.AudioHostClient;
        var drawList = ImGui.GetWindowDrawList();

        using (TypeScale.Caption())
        {
            Chrome.Text(drawList, Chrome.Snap(pos), ImGui.GetColorU32(Semantic.TextTertiary), "MASTER");
        }

        var rowTop = pos.Y + ImGui.GetTextLineHeight() + Metrics.Xs;

        ImGui.SetCursorScreenPos(new Vector2(pos.X, rowTop));
        Mixer.PeakMeter("##v2meterMaster", new Vector2(width, MathF.Round(10f * Metrics.Scale)),
            status.OutputPeak, vertical: false);

        ImGui.SetCursorScreenPos(new Vector2(pos.X, rowTop + MathF.Round(18f * Metrics.Scale)));
        var normalised = Math.Clamp(status.MasterVolume / 2f, 0f, 1f);
        if (Mixer.Crossfader("##v2master", ref normalised, new Vector2(width, MathF.Round(26f * Metrics.Scale)),
                Semantic.Primary, Semantic.Primary, out _))
        {
            client.Send(MessageType.SetMasterVolume, new SetMasterVolumeCommand { Volume = normalised * 2f });
        }
    }

    private static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0)
            seconds = 0;
        var span = TimeSpan.FromSeconds(seconds);
        return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
    }
}
