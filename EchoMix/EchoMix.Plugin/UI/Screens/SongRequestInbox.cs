using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.Ipc;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Incoming song requests, waiting to be accepted onto a deck or declined.
public sealed class SongRequestInbox
{
    private readonly Plugin plugin;

    public SongRequestInbox(Plugin plugin) => this.plugin = plugin;

    public void Draw() => DrawRequested();

    private void DrawRequested()
    {
        var client = plugin.AudioHostClient;
        var requests = client.LatestPendingSongRequests;

        Surfaces.SectionHeader(requests.Count == 1 ? "1 Request" : $"{requests.Count} Requests");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        if (requests.Count == 0)
        {
            Surfaces.RowText(
                "Nothing waiting. Listeners send these from their own Listen screen while your show is on.",
                Semantic.TextTertiary);
            Surfaces.EndPanel();
            return;
        }

        var rowWidth = Surfaces.BeginRowAligned();
        foreach (var request in requests)
            DrawRequestRow(client, request, rowWidth);
        Surfaces.EndRowAligned();

        Surfaces.EndPanel();
    }

    private void DrawRequestRow(AudioHostClient client, PendingSongRequestDto request, float width)
    {
        var rowHeight = MathF.Round(Metrics.ControlXl + Metrics.Md);
        var pos = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        var buttonWidth = MathF.Round(Metrics.Xxxl);
        var buttonHeight = MathF.Round(Metrics.ControlSm);
        var buttonsWidth = (buttonWidth * 2f) + Metrics.Sm + Metrics.ControlSm + Metrics.Sm;

        var hitWidth = MathF.Max(Metrics.Xxl, width - buttonsWidth - Metrics.Md);
        ImGui.InvisibleButton($"##request{request.RequestId}", new Vector2(hitWidth, rowHeight));
        if (ImGui.IsItemHovered())
            drawList.AddRectFilled(pos, pos + new Vector2(width, rowHeight),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.04f)), Metrics.RadiusSoft);
        var textRoom = MathF.Max(Metrics.Xxl, width - buttonsWidth - (Metrics.Lg * 2f));

        var title = $"{request.FileName}  ({UiHelpers.FormatClock(request.DurationSeconds)})";
        var by = request.IsFromCoHost
            ? $"{request.RequesterName} (co-host)"
            : request.RequesterName;

        var titleSize = TypeScale.Measure(TypeScale.Body, title);
        var bySize = TypeScale.Measure(TypeScale.Caption, by);
        var block = titleSize.Y + Metrics.Xs + bySize.Y;
        var top = pos.Y + ((rowHeight - block) * 0.5f);

        using (TypeScale.Body())
            Chrome.Text(drawList, new Vector2(pos.X + Metrics.Md, top),
                ImGui.GetColorU32(Semantic.TextPrimary), UiHelpers.TruncateToWidth(title, textRoom));

        using (TypeScale.Caption())
            Chrome.Text(drawList, new Vector2(pos.X + Metrics.Md, top + titleSize.Y + Metrics.Xs),
                ImGui.GetColorU32(request.IsFromCoHost ? Semantic.DeckA : Semantic.TextTertiary),
                UiHelpers.TruncateToWidth(by, textRoom));

        var buttonY = pos.Y + ((rowHeight - buttonHeight) * 0.5f);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + width - buttonsWidth, buttonY));

        if (Fields.DeckButton($"##acceptA{request.RequestId}", "A", Semantic.DeckA, buttonWidth, buttonHeight))
            client.Send(MessageType.AcceptSongRequest, new AcceptSongRequestCommand { RequestId = request.RequestId, Deck = DeckId.A });

        ImGui.SameLine(0f, Metrics.Sm);
        if (Fields.DeckButton($"##acceptB{request.RequestId}", "B", Semantic.DeckB, buttonWidth, buttonHeight))
            client.Send(MessageType.AcceptSongRequest, new AcceptSongRequestCommand { RequestId = request.RequestId, Deck = DeckId.B });

        ImGui.SameLine(0f, Metrics.Sm);
        if (DeclineButton($"##decline{request.RequestId}", buttonHeight))
            client.Send(MessageType.DeclineSongRequest, new DeclineSongRequestCommand { RequestId = request.RequestId });

        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + rowHeight));
    }

    private static bool DeclineButton(string id, float size)
    {
        var pos = Chrome.Snap(ImGui.GetCursorScreenPos());
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        var centre = Chrome.Snap(pos + new Vector2(size * 0.5f, size * 0.5f));

        if (hovered)
            drawList.AddCircleFilled(centre, size * 0.5f,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Danger, 0.2f)));

        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.Times, centre,
                ImGui.GetColorU32(hovered ? Semantic.Danger : Semantic.TextTertiary));

        if (hovered)
            Tip.Hovered("Decline", "Drops the request without playing it.");

        return clicked;
    }

}
