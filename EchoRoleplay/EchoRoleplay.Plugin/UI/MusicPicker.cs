using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoRoleplay.Game;
using EchoRoleplay.UI.Controls;

namespace EchoRoleplay.UI;

/// Choosing a track out of several hundred.
public sealed class MusicPicker
{
    private const string PopupId = "##musicpicker";

    private string query = string.Empty;
    private bool openRequested;

    /// Draws the field, and the list when it is open.
    public bool Draw(string label, ref uint selected, float width, MusicCatalogue music, string? tooltip = null)
    {
        var scale = UiHelpers.Scale;
        var changed = false;

        ImGui.BeginGroup();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        if (tooltip is not null && ImGui.IsItemHovered())
            UiHelpers.WrappedTooltip(tooltip);

        var origin = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        var size = new Vector2(width, height);

        var pressed = ImGui.InvisibleButton("##musicfield", size, ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var rounding = ImGui.GetStyle().FrameRounding;

        drawList.AddRectFilled(
            origin, origin + size,
            ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), rounding);

        var name = music.NameOf(selected);
        var empty = name.Length == 0;

        var padding = ImGui.GetStyle().FramePadding;

        drawList.AddText(
            origin + padding,
            ImGui.GetColorU32(empty ? Theme.TextDisabled : Theme.Text),
            empty ? "No Theme" : UiHelpers.Truncate(name, width - (padding.X * 2f)));

        if (hovered)
        {
            drawList.AddLine(
                new Vector2(origin.X + rounding, origin.Y + height - (1f * scale)),
                new Vector2(origin.X + width - rounding, origin.Y + height - (1f * scale)),
                ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.35f)),
                1.5f * scale);
        }

        if (pressed)
        {
            query = string.Empty;
            openRequested = true;
        }

        if (openRequested)
        {
            ImGui.OpenPopup(PopupId);
            openRequested = false;
        }

        if (ImGui.BeginPopup(PopupId, ImGuiWindowFlags.NoSavedSettings))
        {
            changed = DrawList(ref selected, music, scale);
            ImGui.EndPopup();
        }

        ImGui.EndGroup();
        return changed;
    }

    private bool DrawList(ref uint selected, MusicCatalogue music, float scale)
    {
        var changed = false;
        var width = 300f * scale;

        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##musicsearch", "Search The Game's Music", ref query, 48);

        ImGui.Dummy(new Vector2(0f, 4f * scale));

        if (EchoButton.Draw("##notheme", "No Theme", new Vector2(0f, 22f * scale)))
        {
            selected = 0;
            changed = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.Dummy(new Vector2(0f, 4f * scale));

        var matches = music.Search(query).ToArray();
        var rowHeight = ImGui.GetTextLineHeightWithSpacing();

        if (ImGui.BeginChild("##musiclist", new Vector2(width, 260f * scale), true))
        {
            var scrollY = ImGui.GetScrollY();
            var viewHeight = ImGui.GetWindowHeight();

            var first = Math.Max(0, (int)(scrollY / rowHeight) - 1);
            var last = Math.Min(matches.Length, (int)((scrollY + viewHeight) / rowHeight) + 1);

            if (first > 0)
                ImGui.Dummy(new Vector2(0f, first * rowHeight));

            for (var i = first; i < last; i++)
            {
                var track = matches[i];

                if (!ImGui.Selectable($"{track.Name}##track{track.Id}", track.Id == selected))
                    continue;

                selected = track.Id;
                changed = true;
                ImGui.CloseCurrentPopup();
            }

            if (last < matches.Length)
                ImGui.Dummy(new Vector2(0f, (matches.Length - last) * rowHeight));
        }

        ImGui.EndChild();

        ImGui.TextColored(Theme.TextDim,
            query.Length > 0 ? $"{matches.Length} Matching" : $"{music.All.Count} Tracks");

        return changed;
    }
}
