using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EchoMix.Plugin.UI.Controls;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI;

/// The multi-host lobby's roster/promote panel - not an independent floating window but a slide-out drawer
/// permanently docked to DjDeckWindow's own right edge, so a lead can see and use it *while still looking at
/// their own deck controls* mid-set (a 4th DjDeckWindow ViewMode would force leaving the deck view just to
/// see who else is in the lobby).
public sealed class HostLobbyWindow : Window
{
    private const float ResizeLerpSpeed = 10f;
    private const float ExpandedWidth = 260f;

    private const float VisibleWidthThreshold = 20f;

    private readonly Plugin plugin;
    private float currentWidth;

    private int themeColorCount;

    public bool IsExpanded { get; set; }

    private float Scale => Math.Clamp(plugin.Configuration.UiScale, 0.75f, 1.5f);

    public HostLobbyWindow(Plugin plugin) : base("Host Lobby###echomix-hostlobby")
    {
        this.plugin = plugin;
    }

    /// Reads DjDeckWindow's current position/size fresh every frame (set at the top of its own Draw(), which
    /// WindowSystem always runs first since it was added to the system first) rather than caching them, so
    /// this drawer tracks the main window live even while that one is itself mid-animation (resizing,
    /// minimizing) or being dragged - matching exactly, frame for frame, rather than lagging a step behind.
    public override void PreDraw()
    {
        themeColorCount = Theme.Push();

        var anchor = plugin.DjDeckWindow.WindowScreenPosition;
        var mainSize = plugin.DjDeckWindow.CurrentWindowSize;

        var dt = ImGui.GetIO().DeltaTime;
        var targetWidth = IsExpanded ? ExpandedWidth * Scale : 0f;
        currentWidth = UiHelpers.Lerp(currentWidth, targetWidth, ResizeLerpSpeed, dt);

        Position = anchor + new Vector2(mainSize.X, 0f);
        PositionCondition = ImGuiCond.Always;
        Size = new Vector2(MathF.Max(1f, currentWidth), mainSize.Y) * UiHelpers.WindowSizeCompensation;
        SizeCondition = ImGuiCond.Always;

        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse;

        if (currentWidth < VisibleWidthThreshold)
            Flags |= ImGuiWindowFlags.NoBackground;
    }

    public override void PostDraw() => Theme.Pop(themeColorCount);

    public override void Draw()
    {
        if (currentWidth < VisibleWidthThreshold)
            return;

        ImGui.SetWindowFontScale(Scale);

        DrawAccentBorder();

        var broadcast = plugin.AudioHostClient.LatestStatus.Broadcast;

        if (!broadcast.IsLive)
        {
            ImGui.TextDisabled("Not currently live.");
            return;
        }

        ImGui.TextColored(Theme.CyanAccent, $"Room {broadcast.RoomCode}");
        ImGui.TextDisabled($"{broadcast.HostRoster.Count} DJ(s) connected.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var leadEntry = broadcast.HostRoster.FirstOrDefault(h => h.HostId == broadcast.LeadHostId);
        var coHosts = broadcast.HostRoster.Where(h => h.HostId != broadcast.LeadHostId).OrderBy(h => h.ConnectedAtUtc).ToList();

        DrawSectionLabel("ON STAGE");
        if (leadEntry != null)
            DrawRosterRow(leadEntry, broadcast, isLead: true);
        else
            ImGui.TextDisabled("Nobody's live right now.");

        if (!broadcast.IsLead && leadEntry != null)
        {
            ImGui.Spacing();
            DrawLeadUpNext(broadcast);
        }

        ImGui.Spacing();
        ImGui.Spacing();

        DrawSectionLabel("CO-HOSTS");
        if (coHosts.Count == 0)
        {
            ImGui.TextDisabled("Nobody else has joined yet.");
        }
        else
        {
            foreach (var entry in coHosts)
                DrawRosterRow(entry, broadcast, isLead: false);
        }

        ImGui.Spacing();
        ImGui.Spacing();

        DrawSectionLabel($"LISTENERS ({broadcast.ListenerRoster.Count})");
        if (broadcast.ListenerRoster.Count == 0)
        {
            ImGui.TextDisabled("Nobody's listening yet.");
        }
        else
        {
            foreach (var entry in broadcast.ListenerRoster.OrderBy(l => l.ConnectedAtUtc))
                DrawListenerRow(entry);
        }

        if (broadcast.HostRoster.Count > 1)
        {
            ImGui.Spacing();
            DrawSectionLabel("MONITOR");
            ImGui.TextColored(Theme.NeutralAccent, broadcast.IsLead ? "You're on stage." : $"Monitoring {leadEntry?.DjName ?? "the lead"}.");
            ImGui.Spacing();

            var monitorVolume = broadcast.MonitorVolume;
            var faderSize = new Vector2(MathF.Min(180f, ImGui.GetContentRegionAvail().X), 32f) * Scale;
            var centerX = (ImGui.GetContentRegionAvail().X - faderSize.X) / 2f;
            if (centerX > 0f)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerX);

            if (HorizontalFader.Draw("##monitorVolume", ref monitorVolume, 0f, 1.5f, 1f, faderSize, Theme.NeutralAccent, Theme.NeutralAccent, out _, midValue: 1f))
                plugin.AudioHostClient.Send(MessageType.SetMonitorVolume, new SetMonitorVolumeCommand { Volume = monitorVolume });

            var labelWidth = ImGui.CalcTextSize("Monitor Volume").X;
            var labelCenterX = (ImGui.GetContentRegionAvail().X - labelWidth) / 2f;
            if (labelCenterX > 0f)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + labelCenterX);
            ImGui.TextDisabled("Monitor Volume");
        }
    }

    /// An accent highlight on the top/right/bottom edges only - the left edge is the seam against
    /// DjDeckWindow's own right edge and is deliberately left plain so the two read as one continuous panel
    /// there.
    private void DrawAccentBorder()
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(Theme.OrangeAccent);

        var thickness = 3f * Scale;

        var topLeft = pos;
        var topRight = new Vector2(pos.X + size.X, pos.Y);
        var bottomLeft = new Vector2(pos.X, pos.Y + size.Y);
        var bottomRight = pos + size;

        drawList.PushClipRect(pos - new Vector2(thickness, thickness), pos + size + new Vector2(thickness, thickness), false);
        drawList.AddLine(topLeft, topRight, color, thickness);
        drawList.AddLine(topRight, bottomRight, color, thickness);
        drawList.AddLine(bottomLeft, bottomRight, color, thickness);
        drawList.PopClipRect();
    }

    private static void DrawSectionLabel(string label)
    {
        ImGui.TextColored(Theme.NeutralAccent, label);
        ImGui.Separator();
        ImGui.Spacing();
    }

    /// One DJ's row: name + character, and - only on a co-host's row, and only while drawn for the current
    /// lead themselves - a "Give Stage" button that hands lead to them.
    private void DrawRosterRow(HostRosterEntryDto entry, BroadcastStatusMessage broadcast, bool isLead)
    {
        ImGui.PushID(entry.HostId.ToString());

        var canPromote = !isLead && broadcast.IsLead;

        ImGui.BeginGroup();
        ImGui.TextColored(isLead ? Theme.OrangeAccent : Theme.Text, entry.DjName);
        ImGui.TextDisabled(string.IsNullOrEmpty(entry.CharacterName) ? " " : entry.CharacterName);
        ImGui.EndGroup();

        if (canPromote)
        {
            var buttonSize = new Vector2(100, 26) * Scale;
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - buttonSize.X + ImGui.GetCursorPosX());
            if (PanelButton.Draw("##giveStage", null, null, "Give Stage", buttonSize, Theme.NeutralAccent))
                plugin.AudioHostClient.Send(MessageType.PromoteHost, new PromoteHostCommand { TargetHostId = entry.HostId });
        }

        ImGui.Spacing();
        ImGui.PopID();
    }

    /// One listener's row - just their character name, since (unlike a HostRosterEntryDto) a listener has no
    /// separate display name and nothing here is ever actionable the way a co-host's "Give Stage" button is.
    private static void DrawListenerRow(ListenerRosterEntryDto entry)
    {
        ImGui.PushID(entry.ListenerId.ToString());
        ImGui.TextColored(Theme.Text, string.IsNullOrEmpty(entry.CharacterName) ? "Unknown" : entry.CharacterName);
        ImGui.Spacing();
        ImGui.PopID();
    }

    /// Read-only up-next readout for whoever's currently lead, mirrored in over the relay (see
    /// BroadcastStatusMessage.LeadQueueA/B's own doc comment) - what's actually playing right now shows on
    /// the co-host's own decks instead (see DjDeckWindow), not here.
    private void DrawLeadUpNext(BroadcastStatusMessage broadcast)
    {
        var upNextA = broadcast.LeadQueueA?.Tracks ?? new List<TrackDto>();
        var upNextB = broadcast.IsHostSpotifyModeActive ? new List<TrackDto>() : broadcast.LeadQueueB?.Tracks ?? new List<TrackDto>();
        if (upNextA.Count == 0 && upNextB.Count == 0)
            return;

        DrawSectionLabel("UP NEXT");
        DrawUpNextList("A", upNextA);
        DrawUpNextList("B", upNextB);
    }

    private static void DrawUpNextList(string deckLabel, List<TrackDto> tracks)
    {
        if (tracks.Count == 0)
            return;

        ImGui.TextDisabled($"Deck {deckLabel}:");
        foreach (var track in tracks)
            ImGui.TextDisabled($"  {track.Title}");
    }
}
