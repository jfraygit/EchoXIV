using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using EchoRoleplay.Game;
using EchoRoleplay.Shared;
using EchoRoleplay.UI.Controls;

namespace EchoRoleplay.UI;

/// Writing one status: pick an icon, name it, say what it means, decide how long it lasts.
public sealed class StatusEditor
{
    private const string PopupId = "##statuseditor";

    /// The dialog's size in design units.
    private static readonly Vector2 WindowSize = new(430f, 560f);

    /// Ready-made durations, in minutes.
    private static readonly (string Label, int Minutes)[] Durations =
    [
        ("Until Removed", 0),
        ("1 Hour", 60),
        ("2 Hours", 120),
        ("4 Hours", 240),
        ("Today", 720),
    ];

    /// The status being edited, or null when this is a new one.
    private RoleplayStatus? editing;

    private uint iconId;
    private string label = string.Empty;
    private string detail = string.Empty;
    private int durationMinutes;
    private string iconQuery = string.Empty;

    /// Set on the frame Open is called, so the popup is opened from inside the draw that owns the id stack
    /// rather than from a button handler that may be nested elsewhere.
    private bool openRequested;

    /// Raised with the finished status when the player commits.
    public event Action<RoleplayStatus>? Committed;

    public void OpenForNew()
    {
        editing = null;
        iconId = 0;
        label = string.Empty;
        detail = string.Empty;
        durationMinutes = 0;
        iconQuery = string.Empty;
        openRequested = true;
    }

    public void OpenForEdit(RoleplayStatus status)
    {
        editing = status;
        iconId = status.IconId;
        label = status.Label;
        detail = status.Detail;

        durationMinutes = 0;
        iconQuery = string.Empty;
        openRequested = true;
    }

    /// Draws the dialog if it is open.
    public void Draw(IconCatalogue icons)
    {
        if (openRequested)
        {
            ImGui.OpenPopup(PopupId);
            openRequested = false;
        }

        var scale = UiHelpers.Scale;

        ImGui.SetNextWindowSize(WindowSize * scale, ImGuiCond.Always);

        var viewport = ImGuiHelpers.MainViewport;
        ImGui.SetNextWindowPos(
            viewport.Pos + (viewport.Size / 2f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar;

        if (!ImGui.BeginPopup(PopupId, flags))
            return;

        ImGui.TextColored(Theme.Accent, editing is null ? "New Status" : "Edit Status");
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawNameRow(icons, scale);

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        Theme.SectionHeader("Icon");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        DrawIconPicker(icons, scale);

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        DrawDuration(scale);

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawButtons(scale);

        ImGui.EndPopup();
    }

    /// The chosen icon beside the two text fields, so the thing being named is visible while it is being
    /// named.
    private void DrawNameRow(IconCatalogue icons, float scale)
    {
        var box = 36f * scale;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(origin, origin + new Vector2(box, box),
            ImGui.GetColorU32(Theme.Tinted(0.10f)), 6f * scale);

        DrawIcon(drawList, icons, iconId, origin, box);

        drawList.AddRect(origin, origin + new Vector2(box, box),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.6f)),
            6f * scale, ImDrawFlags.None, 1.2f * scale);

        ImGui.Dummy(new Vector2(box, box));
        ImGui.SameLine(0f, 10f * scale);

        ImGui.BeginGroup();

        var width = ImGui.GetContentRegionAvail().X;

        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##label", "Name", ref label, ProfileLimits.StatusLabel);

        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##detail", "Detail (Optional)", ref detail, ProfileLimits.StatusDetail);

        ImGui.EndGroup();

        ImGui.TextColored(
            label.Length >= ProfileLimits.StatusLabel ? Theme.Warning : Theme.TextDim,
            $"{label.Length}/{ProfileLimits.StatusLabel} Name   {detail.Length}/{ProfileLimits.StatusDetail} Detail");
    }

    /// Draws one game icon filling a box, or nothing while it loads.
    private static void DrawIcon(ImDrawListPtr drawList, IconCatalogue icons, uint id, Vector2 min, float box)
    {
        var inset = box * 0.06f;
        icons.Draw(drawList, id, min + new Vector2(inset, inset), box - (inset * 2f), 0xFFFFFFFF);
    }

    /// How many icons fit per row is worked out from the width, so the grid reflows rather than clipping at a
    /// fixed column count.
    private void DrawIconPicker(IconCatalogue icons, float scale)
    {
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##iconsearch", "Search Icons", ref iconQuery, 40);

        ImGui.Dummy(new Vector2(0f, 4f * scale));

        var cell = 34f * scale;

        var style = ImGui.GetStyle();
        var line = ImGui.GetTextLineHeightWithSpacing();

        var reserved =
            line            + (8f * scale)            + line            + ImGui.GetFrameHeightWithSpacing()            + (10f * scale)            + (28f * scale)            + (style.ItemSpacing.Y * 3f)
            + style.WindowPadding.Y;

        var gridHeight = MathF.Max(90f * scale, ImGui.GetContentRegionAvail().Y - reserved);

        if (!ImGui.BeginChild("##icongrid", new Vector2(0f, gridHeight), true))
        {
            ImGui.EndChild();
            return;
        }

        icons.EnsureBuilt();

        var matches = icons.Search(iconQuery).ToArray();
        var width = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)(width / cell));
        var rows = (matches.Length + columns - 1) / columns;

        var scrollY = ImGui.GetScrollY();
        var viewHeight = ImGui.GetWindowHeight();
        var firstRow = Math.Max(0, (int)(scrollY / cell) - 1);
        var lastRow = Math.Min(rows, (int)((scrollY + viewHeight) / cell) + 1);

        if (firstRow > 0)
            ImGui.Dummy(new Vector2(0f, firstRow * cell));

        var drawList = ImGui.GetWindowDrawList();

        for (var row = firstRow; row < lastRow; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = (row * columns) + column;
                if (index >= matches.Length)
                    break;

                if (column > 0)
                    ImGui.SameLine(0f, 0f);

                var entry = matches[index];
                var origin = ImGui.GetCursorScreenPos();
                var selected = entry.IconId == iconId;

                if (ImGui.InvisibleButton($"##icon{entry.IconId}", new Vector2(cell, cell), ImGuiButtonFlags.MouseButtonLeft))
                    iconId = entry.IconId;

                var hovered = ImGui.IsItemHovered();

                if (hovered)
                    UiHelpers.WrappedTooltip(entry.Name);

                if (selected || hovered)
                {
                    drawList.AddRectFilled(
                        origin, origin + new Vector2(cell, cell),
                        ImGui.GetColorU32(Theme.Tinted(selected ? 0.40f : 0.18f)), 4f * scale);
                }

                DrawIcon(drawList, icons, entry.IconId, origin, cell);

                if (selected)
                {
                    drawList.AddRect(
                        origin, origin + new Vector2(cell, cell),
                        ImGui.GetColorU32(Theme.Accent), 4f * scale, ImDrawFlags.None, 1.5f * scale);
                }
            }
        }

        if (lastRow < rows)
            ImGui.Dummy(new Vector2(0f, (rows - lastRow) * cell));

        ImGui.EndChild();

        ImGui.TextColored(Theme.TextDim,
            iconQuery.Length > 0 ? $"{matches.Length} Matching" : $"{icons.All.Count} Icons");
    }

    private void DrawDuration(float scale)
    {
        ImGui.TextColored(Theme.TextDim, "Clears After");
        ImGui.SetNextItemWidth(MathF.Min(200f * scale, ImGui.GetContentRegionAvail().X));

        var current = Array.Find(Durations, d => d.Minutes == durationMinutes).Label ?? Durations[0].Label;

        if (!ImGui.BeginCombo("##duration", current))
            return;

        foreach (var (durationLabel, minutes) in Durations)
        {
            if (ImGui.Selectable(durationLabel, minutes == durationMinutes))
                durationMinutes = minutes;
        }

        ImGui.EndCombo();
    }

    private void DrawButtons(float scale)
    {
        var canSave = !string.IsNullOrWhiteSpace(label) && iconId != 0;

        if (EchoButton.Draw("##savestatus", editing is null ? "Add" : "Save",
                new Vector2(96f * scale, 28f * scale), enabled: canSave))
        {
            var status = editing ?? new RoleplayStatus();

            status.IconId = iconId;
            status.Label = label.Trim();
            status.Detail = detail.Trim();
            status.ExpiresUtc = durationMinutes == 0 ? null : DateTime.UtcNow.AddMinutes(durationMinutes);

            Committed?.Invoke(status);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0f, 8f * scale);

        if (EchoButton.Draw("##cancelstatus", "Cancel", new Vector2(96f * scale, 28f * scale)))
            ImGui.CloseCurrentPopup();

        if (canSave)
            return;

        ImGui.SameLine(0f, 10f * scale);
        ImGui.TextColored(
            Theme.TextDim,
            string.IsNullOrWhiteSpace(label) ? "Name Required" : "Icon Required");
    }
}
