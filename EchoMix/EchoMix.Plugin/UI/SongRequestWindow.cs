using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using EchoMix.Plugin.UI.Controls;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI;

/// The Listener view's "Request a Song" panel - a slide-out drawer docked to DjDeckWindow's own right edge,
/// the same technique HostLobbyWindow already uses for the DJ side's multi-host roster (see that class's own
/// doc comment for why a drawer rather than a 4th ViewMode).
public sealed class SongRequestWindow : Window
{
    private const float ResizeLerpSpeed = 10f;
    private const float ExpandedWidth = 260f;
    private const float VisibleWidthThreshold = 20f;

    private readonly Plugin plugin;
    private readonly FileDialogManager fileDialogManager = new();
    private float currentWidth;

    private int themeColorCount;

    public bool IsExpanded { get; set; }

    private float Scale => Math.Clamp(plugin.Configuration.UiScale, 0.75f, 1.5f);

    public SongRequestWindow(Plugin plugin) : base("Song Request###echomix-songrequest")
    {
        this.plugin = plugin;
    }

    /// Identical anchoring to HostLobbyWindow.PreDraw - see that method's own doc comment for the full
    /// reasoning (draw-order-guaranteed-fresh WindowScreenPosition/CurrentWindowSize).
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
        fileDialogManager.Draw();

        var broadcast = plugin.AudioHostClient.LatestStatus.Broadcast;

        ImGui.TextColored(Theme.OrangeAccent, "Request a Song");
        ImGui.TextDisabled($"Playing for {broadcast.HostDjName}");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var cooldown = broadcast.SongRequestCooldownSecondsRemaining;
        var onCooldown = cooldown > 0.05f;

        ImGui.TextWrapped("Pick a song from your own PC to send the DJ - they can play it or decline it.");
        ImGui.Spacing();

        if (onCooldown)
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

        var buttonLabel = onCooldown ? $"Wait {(int)MathF.Ceiling(cooldown)}s" : "Choose a File...";
        if (PanelButton.Draw("##pickSongRequest", plugin.Fonts.Icon, FontAwesomeIcon.Upload, buttonLabel, new Vector2(ExpandedWidth - 40f, 32f) * Scale, Theme.OrangeAccent)
            && !onCooldown)
        {
            OpenRequestDialog();
        }

        if (onCooldown)
            ImGui.PopStyleVar();

        if (onCooldown)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.OrangeAccent, $"You can request again in {(int)MathF.Ceiling(cooldown)}s.");
        }

        if (!string.IsNullOrEmpty(broadcast.SongRequestError))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(broadcast.SongRequestError);
        }
    }

    private void OpenRequestDialog()
    {
        fileDialogManager.OpenFileDialog(
            "Pick a song to request",
            "Audio files{.mp3,.wav,.wma,.aac,.m4a,.flac,.ogg}",
            (success, paths) =>
            {
                if (!success || paths.Count == 0)
                    return;

                var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "Unknown";
                plugin.AudioHostClient.Send(MessageType.RequestSong, new RequestSongCommand
                {
                    SourceFilePath = paths[0],
                    RequesterName = characterName,
                });
            },
            1,
            null,
            false);
    }

    /// Same technique as HostLobbyWindow.DrawAccentBorder - the left edge is the seam against DjDeckWindow's
    /// own right edge and is deliberately left plain so the two read as one continuous panel there.
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
}
