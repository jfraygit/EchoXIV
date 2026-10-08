using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Everything about being in someone else's show, as a two-column master/detail.
public sealed class ListenScreen
{
    private enum Category
    {
        Listener,
        GuestDj,
    }

    private static readonly CategoryList.Item[] Categories =
    {
        new(nameof(Category.Listener), "Listener", FontAwesomeIcon.Headphones, "Join a show and listen"),
        new(nameof(Category.GuestDj), "Guest DJ", FontAwesomeIcon.SlidersH, "Take a deck in a show"),
    };

    private readonly Plugin plugin;
    private readonly CategoryList categoryList = new();
    private readonly FileDialogManager requestFileDialog = new();
    private Category selected = Category.Listener;

    public ListenScreen(Plugin plugin) => this.plugin = plugin;

    private Configuration Config => plugin.Configuration;

    private State.EchoMixEditState Edit => plugin.EditState;

    /// The request picker's dialog, pumped by the shell.
    public void DrawDialogs() => requestFileDialog.Draw();

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var listWidth = CategoryList.DefaultWidth;
        var origin = ImGui.GetCursorScreenPos();

        var picked = categoryList.Draw("##listenCat", origin, new Vector2(listWidth, avail.Y),
            Categories, selected.ToString());
        if (Enum.TryParse<Category>(picked, out var next))
            selected = next;

        ImGui.SetCursorScreenPos(origin + new Vector2(listWidth + Metrics.Xxl, 0f));
        var detailWidth = MathF.Max(1f, avail.X - listWidth - Metrics.Xxl);

        Surfaces.ReserveScrollbar = true;

        ImGui.BeginChild("##listenDetail", new Vector2(detailWidth, avail.Y), false);
        if (selected == Category.Listener)
            DrawListenerDetail();
        else
            DrawGuestDjDetail();
        ImGui.EndChild();

        Surfaces.ReserveScrollbar = false;
    }


    private void DrawListenerDetail()
    {
        var broadcast = plugin.AudioHostClient.LatestStatus.Broadcast;

        if (broadcast.IsListening)
        {
            DrawNowPlaying(broadcast);
            Surfaces.Gap(Metrics.Xxl);
            DrawYourVolume();
            Surfaces.Gap(Metrics.Xxl);
            DrawSongRequest(broadcast);
            return;
        }

        DrawListenerJoin(broadcast);
        Surfaces.Gap(Metrics.Xxl);
        DrawAutoJoin();
    }

    private void DrawListenerJoin(BroadcastStatusMessage broadcast)
    {
        Surfaces.SectionHeader("Join a Show");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        if (broadcast.IsLive)
        {
            Surfaces.RowText(
                "You're on the decks right now. End or leave your show from Broadcast before listening to another.",
                Semantic.TextTertiary);
            Surfaces.EndPanel();
            return;
        }

        if (Config.ListenerAutoJoinNearbyShows)
        {
            Surfaces.RowText(
                "Auto-join is on, so EchoMix picks a show for you. Turn it off below to enter a room code by hand.",
                Semantic.TextTertiary);
            Surfaces.EndPanel();
            return;
        }

        TextField("##v2ConnectRoomCode", "Room Code", ref Edit.ConnectRoomCodeBuffer, 24,
            "The code the DJ gave you.", required: true, hint: "Room code");

        TextField("##v2ConnectPassword", "Password", ref Edit.ConnectPasswordBuffer, 48,
            "Only needed if the DJ set one. A publicly-listed show never asks for one.",
            password: true, hint: "If the show has one", divider: false);

        var roomCode = Edit.ConnectRoomCodeBuffer.Trim();
        var canConnect = !string.IsNullOrWhiteSpace(roomCode);

        Surfaces.Gap(Metrics.Sm);

        if (Fields.Button("Connect", Fields.ButtonStyle.Primary, enabled: canConnect,
                icon: FontAwesomeIcon.SignInAlt)
            && canConnect)
        {
            Config.LastRoomCode = roomCode;
            Config.Save();

            plugin.AudioHostClient.Send(MessageType.ConnectToRemote, new ConnectToRemoteCommand
            {
                RoomCode = roomCode,
                Password = Edit.ConnectPasswordBuffer,
                CharacterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty,
            });
        }

        if (!canConnect)
        {
            Surfaces.Gap(Metrics.Md);
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.Warning, "Enter a room code first.");
        }

        if (!string.IsNullOrEmpty(broadcast.ListenError))
        {
            Surfaces.Gap(Metrics.Md);
            using (TypeScale.Body())
                ImGui.TextColored(Semantic.Danger, broadcast.ListenError);
        }

        Surfaces.EndPanel();
    }

    private void DrawAutoJoin()
    {
        Surfaces.SectionHeader("Auto-Join");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var autoJoin = Config.ListenerAutoJoinNearbyShows;
        if (Fields.Switch("##v2AutoJoin", "Join Nearby Shows", ref autoJoin,
                "Beta. Connects you automatically when a public proximity show starts near you."))
        {
            Config.ListenerAutoJoinNearbyShows = autoJoin;
            Config.Save();
        }

        if (!autoJoin)
        {
            Surfaces.EndPanel();
            return;
        }

        Fields.Divider();

        Surfaces.RowText(plugin.DjDeckWindow.AutoJoinStatusText(), Semantic.TextTertiary);

        Surfaces.EndPanel();
    }

    private void DrawNowPlaying(BroadcastStatusMessage broadcast)
    {
        Surfaces.SectionHeader("Now Playing");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var rowHeight = ImGui.GetTextLineHeight();

        var pulse = 0.55f + (Motion.Pulse(0.6f) * 0.45f);
        drawList.AddCircleFilled(
            Chrome.Snap(new Vector2(pos.X + (5f * Metrics.Scale), pos.Y + (rowHeight * 0.5f))),
            5f * Metrics.Scale, ImGui.GetColorU32(Semantic.Alpha(Semantic.Live, pulse)));

        ImGui.Dummy(new Vector2(16f * Metrics.Scale, 0f));
        ImGui.SameLine();

        using (TypeScale.Heading())
            ImGui.TextColored(Semantic.TextPrimary, broadcast.HostDjName ?? "Live");

        if (broadcast.LiveSinceUtc is { } since)
        {
            ImGui.SameLine(0f, Metrics.Lg);
            using (TypeScale.Caption())
            {
                var elapsed = DateTime.UtcNow - since;
                ImGui.TextColored(Semantic.TextTertiary, elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
                    : $"{elapsed.Minutes}m {elapsed.Seconds}s");
            }
        }

        var autoJoined = broadcast.RoomCode != null
            && broadcast.RoomCode == plugin.AutoJoinTracker.AutoJoinedRoomCode;

        using (TypeScale.Caption())
        {
            ImGui.TextColored(Semantic.TextTertiary, broadcast.IsProximityAudio
                ? autoJoined
                    ? "Auto-joined. Proximity Audio - your volume follows your distance from the DJ."
                    : "Proximity Audio - your volume follows your distance from the DJ."
                : autoJoined
                    ? "Auto-joined. Global Audio."
                    : "Global Audio.");
        }

        if (broadcast.IsListenerReconnecting)
        {
            Surfaces.Gap(Metrics.Sm);
            using (TypeScale.Body())
                ImGui.TextColored(Semantic.Warning, "Reconnecting...");
        }

        Surfaces.Gap(Metrics.Lg);

        DrawDecks(broadcast);

        Surfaces.Gap(Metrics.Lg);
        Surfaces.BeginRowAligned();

        if (Fields.Button("Leave Show", Fields.ButtonStyle.Danger, icon: FontAwesomeIcon.SignOutAlt))
        {
            plugin.NotifyManualDisconnectRequested();
            plugin.AudioHostClient.Send(MessageType.DisconnectFromRemote, new object());
        }

        Surfaces.EndRowAligned();
        Surfaces.EndPanel();
    }

    /// What the DJ is actually playing, per deck.
    private void DrawDecks(BroadcastStatusMessage broadcast)
    {
        var hasA = !string.IsNullOrWhiteSpace(broadcast.NowPlayingTitleA);

        var hasB = !broadcast.IsHostSpotifyModeActive && !string.IsNullOrWhiteSpace(broadcast.NowPlayingTitleB);

        if (!hasA && !hasB)
        {
            using (TypeScale.Body())
            {
                ImGui.TextColored(Semantic.TextTertiary, broadcast.IsListenerReconnecting
                    ? "Waiting for the stream to come back..."
                    : "Nothing playing right now - the DJ hasn't started a track yet.");
            }

            return;
        }

        if (hasA)
        {
            DrawDeckStrip("A", broadcast.NowPlayingTitleA!, broadcast.NowPlayingPositionSecondsA,
                broadcast.NowPlayingDurationSecondsA, plugin.AudioHostClient.LatestListenSpectrumA,
                Semantic.DeckA, broadcast.IsHostSpotifyModeActive);
        }

        if (hasA && hasB)
            Surfaces.Gap(Metrics.Md);

        if (hasB)
        {
            DrawDeckStrip("B", broadcast.NowPlayingTitleB!, broadcast.NowPlayingPositionSecondsB,
                broadcast.NowPlayingDurationSecondsB, plugin.AudioHostClient.LatestListenSpectrumB,
                Semantic.DeckB, false);
        }
    }

    /// One deck: spectrum as the backdrop, title and deck letter over it, progress along the base.
    private void DrawDeckStrip(
        string letter, string title, double position, double duration, float[] spectrum, Vector4 accent, bool spotify)
    {
        var width = Surfaces.ContentWidth;
        var height = MathF.Round(72f * Metrics.Scale);
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var max = Chrome.Snap(origin + new Vector2(width, height));
        var drawList = ImGui.GetWindowDrawList();

        Elevation.DrawSurface(drawList, origin, max, Elevation.Sunken, Metrics.RadiusSoft,
            Elevation.ShadowSpec.None, topEdge: false, Semantic.Alpha(accent, 0.25f));

        var trackLane = MathF.Round(Metrics.Lg);

        var visHeight = MathF.Max(Metrics.Xxl, height - trackLane - (Metrics.Xs * 2f));
        var visPos = new Vector2(origin.X + Metrics.Xs, origin.Y + Metrics.Xs);

        ImGui.SetCursorScreenPos(visPos);
        drawList.PushClipRect(visPos, new Vector2(max.X - Metrics.Xs, visPos.Y + visHeight), true);
        VisualizerWidget.Draw($"##v2listenVis{letter}", spectrum,
            new Vector2(width - (Metrics.Xs * 2f), visHeight),
            Semantic.Alpha(accent, 0.5f),
            (VisualizerWidget.Style)Config.ListenerVisualizerStyle,
            Config.ListenerVisualizerSensitivity);
        drawList.PopClipRect();

        drawList.AddRectFilledMultiColor(
            new Vector2(origin.X, origin.Y + (height * 0.25f)),
            new Vector2(max.X, visPos.Y + visHeight),
            0x00000000u, 0x00000000u,
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.72f)),
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.72f)));

        var chip = MathF.Round(Metrics.ControlXs);
        var chipMin = Chrome.Snap(new Vector2(origin.X + Metrics.Md, origin.Y + Metrics.Md));
        drawList.AddRectFilled(chipMin, chipMin + new Vector2(chip, chip),
            ImGui.GetColorU32(Semantic.Alpha(accent, 0.22f)), Metrics.RadiusSharp);
        drawList.AddRect(chipMin, chipMin + new Vector2(chip, chip),
            ImGui.GetColorU32(Semantic.Alpha(accent, 0.6f)), Metrics.RadiusSharp, ImDrawFlags.None, Metrics.Hairline);

        using (TypeScale.Caption())
        {
            var size = ImGui.CalcTextSize(letter);
            Chrome.Text(drawList,
                Chrome.CenterY(chipMin.X + ((chip - size.X) * 0.5f), chipMin.Y, chip, size.Y),
                ImGui.GetColorU32(accent), letter);
        }

        var clock = duration > 0
            ? $"{UiHelpers.FormatClock(position)} / {UiHelpers.FormatClock(duration)}"
            : UiHelpers.FormatClock(position);

        float clockWidth;
        using (TypeScale.Caption())
            clockWidth = ImGui.CalcTextSize(clock).X;

        var textLeft = chipMin.X + chip + Metrics.Md;
        var textRoom = MathF.Max(Metrics.Xxl, width - (textLeft - origin.X) - clockWidth - (Metrics.Lg * 2f));

        using (TypeScale.Body())
            Chrome.Text(drawList, Chrome.CenterY(textLeft, chipMin.Y, chip, ImGui.GetTextLineHeight()),
                ImGui.GetColorU32(Semantic.TextPrimary), UiHelpers.TruncateToWidth(title, textRoom));

        using (TypeScale.Caption())
            Chrome.Text(drawList,
                Chrome.CenterY(max.X - clockWidth - Metrics.Md, chipMin.Y, chip, ImGui.GetTextLineHeight()),
                ImGui.GetColorU32(Semantic.TextSecondary), clock);

        var trackY = max.Y - (trackLane * 0.5f);
        var trackLeft = origin.X + Metrics.Md;
        var trackRight = max.X - Metrics.Md;
        var thickness = MathF.Max(2f, 2f * Metrics.Scale);

        drawList.AddLine(Chrome.Snap(new Vector2(trackLeft, trackY)), Chrome.Snap(new Vector2(trackRight, trackY)),
            ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.12f)), thickness);

        if (duration > 0)
        {
            var progress = Math.Clamp((float)(position / duration), 0f, 1f);
            var filled = trackLeft + ((trackRight - trackLeft) * progress);
            drawList.AddLine(Chrome.Snap(new Vector2(trackLeft, trackY)), Chrome.Snap(new Vector2(filled, trackY)),
                ImGui.GetColorU32(accent), thickness);
        }
        else if (spotify)
        {
            using (TypeScale.Caption())
                Chrome.Text(drawList, new Vector2(trackLeft, trackY - ImGui.GetTextLineHeight() - Metrics.Xs),
                    ImGui.GetColorU32(Semantic.TextDisabled), "Live from Spotify");
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawYourVolume()
    {
        Surfaces.SectionHeader("Your Volume");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var volume = Config.ListenerVolume * 100f;
        if (Fields.Slider("##v2ListenerVolume", "Volume", ref volume, 0f, 150f, "{0:F0}%", 100f,
                "Only affects what you hear. The DJ's mix is untouched."))
        {
            Config.ListenerVolume = volume / 100f;
            Config.Save();
        }

        Surfaces.EndPanel();
    }

    private void DrawSongRequest(BroadcastStatusMessage broadcast)
    {
        Surfaces.SectionHeader("Request a Song");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        Surfaces.RowText(
            "Send the DJ a track from your own PC. They can play it or decline it.",
            Semantic.TextTertiary);

        Surfaces.Gap(Metrics.Lg);

        var cooldown = broadcast.SongRequestCooldownSecondsRemaining;
        var onCooldown = cooldown > 0.05f;

        Surfaces.BeginRowAligned();
        if (Fields.Button(onCooldown ? $"Wait {(int)MathF.Ceiling(cooldown)}s" : "Choose a File",
                Fields.ButtonStyle.Secondary, enabled: !onCooldown, icon: FontAwesomeIcon.Upload)
            && !onCooldown)
        {
            OpenRequestPicker();
        }
        Surfaces.EndRowAligned();

        if (!string.IsNullOrEmpty(broadcast.SongRequestError))
        {
            Surfaces.Gap(Metrics.Md);
            Surfaces.RowText(broadcast.SongRequestError, Semantic.Danger);
        }

        Surfaces.EndPanel();
    }


    private void DrawGuestDjDetail()
    {
        var broadcast = plugin.AudioHostClient.LatestStatus.Broadcast;

        Surfaces.SectionHeader("Join the Decks");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        if (broadcast.IsLive)
        {
            var guest = !broadcast.IsLead;

            Surfaces.RowText(
                guest
                    ? $"You're on the decks in {broadcast.RoomCode}. The room, your co-hosts and the mixer are all on Broadcast and Mixer."
                    : "You're already live. See Broadcast.",
                Semantic.TextTertiary);

            if (guest)
            {
                Surfaces.Gap(Metrics.Lg);
                if (Fields.Button("Leave the Decks", Fields.ButtonStyle.Danger,
                        icon: FontAwesomeIcon.SignOutAlt, idSuffix: "guestLeave"))
                {
                    plugin.AudioHostClient.Send(MessageType.StopBroadcast, new object());
                }

                Surfaces.Gap(Metrics.Sm);
                using (TypeScale.Caption())
                    ImGui.TextColored(Semantic.TextTertiary,
                        "Leaves your seat. The show keeps going for everyone else.");
            }

            Surfaces.EndPanel();
            return;
        }

        if (broadcast.IsListening)
        {
            Surfaces.RowText(
                "You're listening to a show. Leave it first - you can't be in the audience and on the decks at once.",
                Semantic.TextTertiary);
            Surfaces.EndPanel();
            return;
        }

        Surfaces.RowText(
            "Join another DJ's room as a co-host and take one of the decks. You need the host password, "
            + "which is different from the one listeners use.",
            Semantic.TextTertiary);

        Surfaces.Gap(Metrics.Sm);
        Fields.Divider();

        var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

        TextField("##v2DjRoomCode", "Room Code", ref Edit.ConnectRoomCodeBuffer, 24,
            "The same room code a listener would use.", required: true, hint: "Room code");

        TextField("##v2DjHostPassword", "Host Password", ref Edit.ConnectHostPasswordBuffer, 48,
            "The co-host password, not the listener one. The room's DJ sets it when they go live.",
            required: true, password: true, hint: "Host password");

        TextField("##v2DjName", "Your DJ Name", ref Edit.ConnectDjNameBuffer, 48,
            "What the room sees you as.",
            hint: string.IsNullOrEmpty(characterName) ? "Your DJ name" : characterName,
            divider: false);

        var roomCode = Edit.ConnectRoomCodeBuffer.Trim();
        var blocker = string.IsNullOrWhiteSpace(roomCode)
            ? "Enter a room code first."
            : string.IsNullOrWhiteSpace(Edit.ConnectHostPasswordBuffer)
                ? "The host password is required."
                : null;

        Surfaces.Gap(Metrics.Sm);

        if (Fields.Button("Join as DJ", Fields.ButtonStyle.Primary, enabled: blocker == null,
                icon: FontAwesomeIcon.SignInAlt)
            && blocker == null)
        {
            Config.LastRoomCode = roomCode;
            Config.Save();

            plugin.AudioHostClient.Send(MessageType.JoinAsHost, new JoinAsHostCommand
            {
                RoomCode = roomCode,
                HostPassword = Edit.ConnectHostPasswordBuffer,
                DjName = string.IsNullOrWhiteSpace(Edit.ConnectDjNameBuffer)
                    ? characterName
                    : Edit.ConnectDjNameBuffer.Trim(),
                CharacterName = characterName,
            });
        }

        if (blocker != null)
        {
            Surfaces.Gap(Metrics.Md);
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.Warning, blocker);
        }

        if (!string.IsNullOrEmpty(broadcast.BroadcastError))
        {
            Surfaces.Gap(Metrics.Md);
            using (TypeScale.Body())
                ImGui.TextColored(Semantic.Danger, broadcast.BroadcastError);
        }

        Surfaces.EndPanel();
    }


    private void OpenRequestPicker() =>
        requestFileDialog.OpenFileDialog(
            "Pick a song to request",
            "Audio files{.mp3,.wav,.wma,.aac,.m4a,.flac,.ogg}",
            (success, paths) =>
            {
                if (!success || paths.Count == 0)
                    return;

                plugin.AudioHostClient.Send(MessageType.RequestSong, new RequestSongCommand
                {
                    SourceFilePath = paths[0],
                    RequesterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "Unknown",
                });
            },
            1,
            null,
            false);

    /// Stacked form field - see BroadcastScreen.TextField for why a form stacks rather than using the
    /// settings-row layout.
    private void TextField(
        string id,
        string label,
        ref string value,
        int maxLength,
        string? helper = null,
        bool required = false,
        bool password = false,
        string hint = "",
        bool divider = true)
    {
        var fullWidth = Surfaces.ContentWidth;

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextPrimary, label);

        ImGui.SameLine(0f, Metrics.Md);
        using (TypeScale.Caption())
        {
            ImGui.TextColored(
                required ? Semantic.Alpha(Semantic.Warning, 0.9f) : Semantic.TextDisabled,
                required ? "Required" : "Optional");
        }

        Surfaces.Gap(Metrics.Xs);
        Fields.TextInput(id, ref value, maxLength, fullWidth - Metrics.Lg, hint, password);

        if (!string.IsNullOrEmpty(helper))
        {
            Surfaces.Gap(Metrics.Xs);
            using (TypeScale.Caption())
            {
                using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.TextTertiary);
                ImGui.TextWrapped(helper);
            }
        }

        if (divider)
        {
            Surfaces.Gap(Metrics.Sm);
            Fields.Divider();
            return;
        }

        Surfaces.Gap(Metrics.Lg);
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
