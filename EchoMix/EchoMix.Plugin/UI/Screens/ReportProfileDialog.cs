using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Controls;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Screens;

/// Report This Profile, rebuilt on the 2.0 token layer.
public sealed class ReportProfileDialog
{
    private const string PopupId = "##v2reportProfile";

    private readonly Plugin plugin;

    public ReportProfileDialog(Plugin plugin) => this.plugin = plugin;

    /// Resets the form and opens it.
    public void Open()
    {
        plugin.EditState.DjProfileReportReasonBuffer = string.Empty;
        plugin.DjDeckWindow.ClearDjProfileReportResult();
        ImGui.OpenPopup(PopupId);
    }

    public void Draw(string profileId)
    {
        var width = MathF.Round(380f * Metrics.Scale);

        using var popupStyle = Sty.Popup();
        using var padding = Sty.New().Var(ImGuiStyleVar.WindowPadding, new Vector2(Metrics.Xl, Metrics.Xl));

        var windowPos = plugin.DjDeckWindow.WindowScreenPosition;
        var windowSize = plugin.DjDeckWindow.CurrentWindowSize;
        ImGui.SetNextWindowPos(windowPos + (windowSize * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));

        if (!ImGui.BeginPopup(PopupId, Sty.PopupFlags))
            return;

        using var detached = Surfaces.Detach();

        using (TypeScale.Heading())
            ImGui.TextColored(Semantic.TextPrimary, "Report This Profile");

        using (TypeScale.Caption())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.TextTertiary);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextWrapped(
                "Sends this listing and your character name to the developer. "
                + "Please only use this for genuine abuse.");
            ImGui.PopTextWrapPos();
        }

        Fields.Divider();

        var sending = plugin.DjDeckWindow.DjProfileReportSending;
        var result = plugin.DjDeckWindow.DjProfileReportResult;

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextSecondary, "What's Wrong?");

        Surfaces.Gap(Metrics.Xs);

        var reason = plugin.EditState.DjProfileReportReasonBuffer;
        Fields.Multiline("##v2reportProfileReason", ref reason, 500,
            width, MathF.Round(110f * Metrics.Scale), out var wrapWidth);
        plugin.EditState.DjProfileReportReasonBuffer = reason;

        Surfaces.Gap(Metrics.Lg);

        var buttonWidth = MathF.Round((width - Metrics.Md) * 0.5f);

        if (Fields.Button(sending ? "Sending..." : "Send Report", Fields.ButtonStyle.Primary,
                buttonWidth, enabled: !sending, icon: FontAwesomeIcon.Flag, idSuffix: "v2report"))
        {
            if (string.IsNullOrWhiteSpace(plugin.EditState.DjProfileReportReasonBuffer))
            {
                plugin.DjDeckWindow.RejectDjProfileReport("Please describe what's wrong first.");
            }
            else
            {
                plugin.DjDeckWindow.SubmitDjProfileReport(
                    profileId,
                    WrappedInput.Unfold(plugin.EditState.DjProfileReportReasonBuffer, wrapWidth));
            }
        }

        ImGui.SameLine(0f, Metrics.Md);

        if (Fields.Button("Close", Fields.ButtonStyle.Secondary, buttonWidth, idSuffix: "v2report"))
            ImGui.CloseCurrentPopup();

        if (result != null)
        {
            Surfaces.Gap(Metrics.Md);

            using (TypeScale.Body())
            {
                ImGui.TextColored(
                    result.Accepted ? Semantic.Success : Semantic.Danger,
                    result.Accepted
                        ? "Reported. Thank you."
                        : $"Couldn't send: {result.Error ?? "unknown error"}");
            }
        }

        ImGui.EndPopup();
    }
}
