using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Screens;

/// Report a Bug, rebuilt on the 2.0 token layer.
public sealed class ReportBugDialog
{
    private const string PopupId = "##v2reportBug";

    private readonly Plugin plugin;

    public ReportBugDialog(Plugin plugin) => this.plugin = plugin;

    /// Resets the form and opens it.
    public void Open()
    {
        plugin.EditState.ReportBugDescriptionBuffer = string.Empty;
        plugin.EditState.ReportBugDiscordNameBuffer =
            plugin.Configuration.LastReportBugDiscordName ?? string.Empty;

        plugin.DjDeckWindow.ClearReportBugResult();
        ImGui.OpenPopup(PopupId);
    }

    public void Draw()
    {
        var width = MathF.Round(420f * Metrics.Scale);

        using var popupStyle = Sty.Popup();
        using var sizeConstraint = Sty.New().Var(ImGuiStyleVar.WindowPadding, new Vector2(Metrics.Xl, Metrics.Xl));

        var windowPos = plugin.DjDeckWindow.WindowScreenPosition;
        var windowSize = plugin.DjDeckWindow.CurrentWindowSize;
        ImGui.SetNextWindowPos(windowPos + (windowSize * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));

        if (!ImGui.BeginPopup(PopupId))
            return;

        using var detached = Surfaces.Detach();

        using (TypeScale.Heading())
            ImGui.TextColored(Semantic.TextPrimary, "Report a Bug");

        using (TypeScale.Caption())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.TextTertiary);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextWrapped(
                "Sends your AudioHost session log, plugin version and current status. "
                + "Nothing else on this PC.");
            ImGui.PopTextWrapPos();
        }

        Fields.Divider();

        var sending = plugin.DjDeckWindow.ReportBugSending;
        var result = plugin.DjDeckWindow.ReportBugResult;

        FieldLabel("What Happened?", "Optional, but it helps a lot.");

        var description = plugin.EditState.ReportBugDescriptionBuffer;
        Fields.Multiline("##v2reportBugDescription", ref description, 1000,
            width, MathF.Round(110f * Metrics.Scale), out var wrapWidth);
        plugin.EditState.ReportBugDescriptionBuffer = description;

        Surfaces.Gap(Metrics.Md);
        FieldLabel("Discord Name", "Optional. Only so I can reply.");

        var discord = plugin.EditState.ReportBugDiscordNameBuffer;
        Fields.TextInput("##v2reportBugDiscord", ref discord, 64, width, "e.g. username");
        plugin.EditState.ReportBugDiscordNameBuffer = discord;

        Surfaces.Gap(Metrics.Lg);

        var buttonWidth = MathF.Round((width - Metrics.Md) * 0.5f);

        if (Fields.Button(sending ? "Sending..." : "Send Report", Fields.ButtonStyle.Primary,
                buttonWidth, enabled: !sending, icon: FontAwesomeIcon.PaperPlane, idSuffix: "v2bug"))
        {
            plugin.DjDeckWindow.SubmitBugReport(
                WrappedInput.Unfold(plugin.EditState.ReportBugDescriptionBuffer, wrapWidth),
                plugin.EditState.ReportBugDiscordNameBuffer);
        }

        ImGui.SameLine(0f, Metrics.Md);

        if (Fields.Button("Close", Fields.ButtonStyle.Secondary, buttonWidth, idSuffix: "v2bug"))
            ImGui.CloseCurrentPopup();

        if (result != null)
        {
            Surfaces.Gap(Metrics.Md);

            using (TypeScale.Body())
            {
                ImGui.TextColored(
                    result.Success ? Semantic.Success : Semantic.Danger,
                    result.Success
                        ? "Sent. Thank you!"
                        : $"Couldn't send: {result.Error ?? "unknown error"}");
            }
        }

        ImGui.EndPopup();
    }

    /// A field label and its own one-line note.
    private static void FieldLabel(string label, string note)
    {
        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextSecondary, label);

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, note);

        Surfaces.Gap(Metrics.Xs);
    }
}
