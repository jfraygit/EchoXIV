using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoSim.Game;

namespace EchoSim.UI.Controls;

/// The plugin's dropdown: a dark body with an accent ring, a chevron that flips when open, and a list whose
/// rows light up rather than filling with a flat selection block.
public static class EchoCombo
{
    private const float Rounding = 6f;

    /// Style pushes held open across the popup, popped by End.
    private static int pushedStyles;

    private static int pushedColours;

    public static bool Begin(string id, string preview, float width = 0f, string? tooltip = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        var height = ImGui.GetFrameHeight();
        var size = new Vector2(width > 0 ? width : ImGui.GetContentRegionAvail().X, height);

        var popupId = $"{id}_popup";
        var wasOpen = ImGui.IsPopupOpen(popupId);

        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        if (hovered && !string.IsNullOrEmpty(tooltip))
            ImGui.SetTooltip(tooltip);

        var min = pos;
        var max = pos + size;

        var body = wasOpen
            ? Theme.Tinted(0.16f)
            : hovered
                ? Theme.Tinted(0.11f)
                : Theme.Tinted(0.05f);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(body), Rounding);

        var ringAlpha = wasOpen ? 1f : hovered ? 0.8f : 0.45f;
        drawList.AddRect(min, max,
            ImGui.GetColorU32(Theme.Accent with { W = ringAlpha }), Rounding, ImDrawFlags.None, 1.3f);

        var padX = UiHelpers.S(10f);
        var chevronBox = UiHelpers.S(22f);
        var textClip = new Vector4(min.X + padX, min.Y, max.X - chevronBox, max.Y);
        drawList.PushClipRect(new Vector2(textClip.X, textClip.Y), new Vector2(textClip.Z, textClip.W), true);
        drawList.AddText(
            new Vector2(min.X + padX, min.Y + ((height - ImGui.GetTextLineHeight()) / 2f)),
            ImGui.GetColorU32(string.IsNullOrEmpty(preview) ? Theme.TextDisabled : Theme.Text),
            string.IsNullOrEmpty(preview) ? "-" : preview);
        drawList.PopClipRect();

        var chevronCentre = new Vector2(max.X - (chevronBox / 2f), min.Y + (height / 2f));
        UiHelpers.DrawTrendArrow(
            drawList, chevronCentre, UiHelpers.S(9f), UiHelpers.S(6f),
            up: wasOpen,
            colour: hovered || wasOpen ? Theme.Accent : Theme.TextDim);

        if (clicked)
            ImGui.OpenPopup(popupId);

        ImGui.SetNextWindowPos(new Vector2(min.X, max.Y + 4f));

        ImGui.SetNextWindowSizeConstraints(
            new Vector2(size.X, 0f),
            new Vector2(MathF.Max(size.X, UiHelpers.S(560f)), UiHelpers.S(420f)));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f, 6f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1.3f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Background);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Accent with { W = 0.7f });

        if (ImGui.BeginPopup(popupId))
        {
            pushedStyles = 3;
            pushedColours = 2;
            return true;
        }

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
        return false;
    }

    public static void End()
    {
        ImGui.EndPopup();
        ImGui.PopStyleColor(pushedColours);
        ImGui.PopStyleVar(pushedStyles);
        pushedStyles = 0;
        pushedColours = 0;
    }

    /// One row.
    public static bool Item(string label, bool selected, uint iconId = 0)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        var icon = iconId != 0 ? GameData.Icon(iconId) : null;
        var iconSize = icon is not null ? ImGui.GetTextLineHeight() + 4f : 0f;
        var height = MathF.Max(ImGui.GetTextLineHeight() + 8f, iconSize + 4f);

        const float padX = 10f;
        var needed = padX + (icon is not null ? iconSize + 7f : 0f) + ImGui.CalcTextSize(label).X + padX;
        var width = MathF.Max(ImGui.GetContentRegionAvail().X, needed);

        var clicked = ImGui.InvisibleButton($"##item_{label}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();

        if (hovered || selected)
        {
            drawList.AddRectFilled(pos, pos + new Vector2(width, height),
                ImGui.GetColorU32(Theme.Accent with { W = hovered ? 0.18f : 0.10f }), 4f);
        }

        if (selected)
        {
            drawList.AddRectFilled(
                new Vector2(pos.X + 1f, pos.Y + 3f),
                new Vector2(pos.X + 3.5f, pos.Y + height - 3f),
                ImGui.GetColorU32(Theme.Accent), 2f);
        }

        var textX = pos.X + padX;

        if (icon is not null)
        {
            drawList.AddImage(icon.Handle,
                new Vector2(textX, pos.Y + ((height - iconSize) / 2f)),
                new Vector2(textX + iconSize, pos.Y + ((height + iconSize) / 2f)));
            textX += iconSize + 7f;
        }

        drawList.AddText(
            new Vector2(textX, pos.Y + ((height - ImGui.GetTextLineHeight()) / 2f)),
            ImGui.GetColorU32(selected ? Theme.Accent : hovered ? Theme.Text : Theme.TextDim),
            label);

        if (clicked)
            ImGui.CloseCurrentPopup();

        return clicked;
    }
}
