using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoRoleplay.Game;
using EchoRoleplay.Shared;

namespace EchoRoleplay.UI;

/// The pieces a character sheet is built out of: a labelled box, a labelled picker, and a paragraph box.
public static class ProfileFields
{
    /// The portrait for a profile being drawn, or null.
    public static Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? Portrait(
        Plugin plugin, RoleplayProfile? profile, string characterKey)
    {
        if (characterKey.Length == 0)
            return null;

        if (profile is not null && plugin.Profiles.ById(profile.Id) is not null)
            return plugin.Portraits.Own(profile.Id);

        var stamp = plugin.Relay.PortraitStamp(characterKey);

        return stamp.Length == 0 ? null : plugin.Portraits.Remote(plugin.Relay.CardIdFor(characterKey), stamp);
    }

    /// How close to the cap a box gets before it says so, as a fraction.
    private const float CountFrom = 0.8f;

    /// Gap between columns in a field row.
    public static float ColumnGap => 12f * UiHelpers.Scale;

    /// How wide each cell is when a row is split into columns.
    public static float ColumnWidth(int columns)
    {
        var available = Theme.ContentWidth;
        var gaps = ColumnGap * (columns - 1);
        return MathF.Max(60f * UiHelpers.Scale, (available - gaps) / columns);
    }

    /// How many columns of at least minimum design units fit.
    public static int ColumnsThatFit(int most, float minimum)
    {
        var available = Theme.ContentWidth;
        var scaled = minimum * UiHelpers.Scale;
        var gap = ColumnGap;

        for (var columns = most; columns > 1; columns--)
        {
            if ((available - (gap * (columns - 1))) / columns >= scaled)
                return columns;
        }

        return 1;
    }

    /// Vertical gap between one row of fields and the next.
    private static float RowSpacing => 6f * UiHelpers.Scale;

    /// Places a cell at an offset from a row's left edge, for rows whose columns are not all the same width.
    public static void At(Vector2 rowOrigin, float offsetX) =>
        ImGui.SetCursorScreenPos(new Vector2(rowOrigin.X + offsetX, rowOrigin.Y));

    /// A row of fields, placed at computed coordinates rather than with SameLine.
    public struct FieldRow
    {
        private Vector2 origin;
        private float lowest;
        private int placed;

        /// How many cells fit across.
        public int Columns { get; private init; }

        /// How wide each cell is.
        public float Width { get; private init; }

        /// Starts a row at the cursor, with as many columns of at least minimumCell design units as will fit.
        public static FieldRow Begin(int most, float minimumCell)
        {
            var columns = ColumnsThatFit(most, minimumCell);
            var start = ImGui.GetCursorScreenPos();

            return new FieldRow
            {
                Columns = columns,
                Width = ColumnWidth(columns),
                origin = start,
                lowest = start.Y,
                placed = 0,
            };
        }

        /// Finishes the cell just drawn and places the next one, wrapping to a new line when the row is full.
        public void Next()
        {
            lowest = MathF.Max(lowest, ImGui.GetCursorScreenPos().Y);
            placed++;

            var column = placed % Columns;

            if (column == 0)
            {
                origin = new Vector2(origin.X, lowest + RowSpacing);
                lowest = origin.Y;
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X + (column * (Width + ColumnGap)), origin.Y));
        }

        /// Puts the layout cursor back at the left edge, below the row.
        public void End()
        {
            lowest = MathF.Max(lowest, ImGui.GetCursorScreenPos().Y);
            ImGui.SetCursorScreenPos(new Vector2(origin.X, placed % Columns == 0 ? origin.Y : lowest));
        }
    }

    /// A single-line field: a dim label, a box under it, and a count once it is nearly full.
    public static bool Text(
        string id, string label, ref string value, int limit, float width, string? hint = null,
        string? tooltip = null, bool titleCase = false)
    {
        ImGui.BeginGroup();

        DrawLabel(label, value.Length, limit, width, tooltip);

        ImGui.SetNextItemWidth(width);
        var changed = string.IsNullOrEmpty(hint)
            ? ImGui.InputText(id, ref value, limit)
            : ImGui.InputTextWithHint(id, hint, ref value, limit);

        if (titleCase && ImGui.IsItemDeactivatedAfterEdit())
        {
            var tidied = TitleCase(value);

            if (!string.Equals(tidied, value, StringComparison.Ordinal))
            {
                value = tidied;
                changed = true;
            }
        }

        Underline();

        ImGui.EndGroup();
        return changed;
    }

    /// Capitalises a name the way a name is capitalised, and leaves alone anything that looks deliberate.
    public static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var built = new System.Text.StringBuilder(value.Length);
        var start = 0;

        while (start < value.Length)
        {
            var end = value.IndexOf(' ', start);

            if (end < 0)
                end = value.Length;

            var word = value.AsSpan(start, end - start);

            if (word.Length == 0 || ContainsUpper(word))
            {
                built.Append(word);
            }
            else
            {
                var fresh = true;

                foreach (var c in word)
                {
                    built.Append(fresh ? char.ToUpperInvariant(c) : c);
                    fresh = c is '\'' or '-' or '’';
                }
            }

            if (end < value.Length)
                built.Append(' ');

            start = end + 1;
        }

        return built.ToString();
    }

    private static bool ContainsUpper(ReadOnlySpan<char> word)
    {
        foreach (var c in word)
        {
            if (char.IsUpper(c))
                return true;
        }

        return false;
    }

    /// An accent rule along the bottom of the box just submitted, lit when it has focus and faint under the
    /// pointer.
    private static void Underline() =>
        UnderlineAt(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.IsItemActive(), ImGui.IsItemHovered());

    private static void UnderlineAt(Vector2 min, Vector2 max, bool active, bool hovered)
    {
        if (!active && !hovered)
            return;

        var scale = UiHelpers.Scale;
        var inset = ImGui.GetStyle().FrameRounding;

        var colour = new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, active ? 1f : 0.35f);

        ImGui.GetWindowDrawList().AddLine(
            new Vector2(min.X + inset, max.Y - (1f * scale)),
            new Vector2(max.X - inset, max.Y - (1f * scale)),
            ImGui.GetColorU32(colour), (active ? 2f : 1.5f) * scale);
    }

    /// A rounded pill that toggles.
    public static float ChipWidth(string label) =>
        ImGui.CalcTextSize(label).X + (11f * UiHelpers.Scale * 2f);

    public static bool Chip(string id, string label, bool selected, string? tooltip = null)
    {
        var scale = UiHelpers.Scale;
        var padding = new Vector2(11f, 4f) * scale;

        var textSize = ImGui.CalcTextSize(label);
        var size = new Vector2(textSize.X + (padding.X * 2f), textSize.Y + (padding.Y * 2f));

        var origin = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size, ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        if (hovered && !string.IsNullOrEmpty(tooltip))
            UiHelpers.WrappedTooltip(tooltip);

        var drawList = ImGui.GetWindowDrawList();
        var max = origin + size;

        var rounding = size.Y / 2f;

        var body = selected
            ? new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.88f)
            : hovered
                ? Theme.Tinted(0.16f)
                : Theme.Tinted(0.05f);

        drawList.AddRectFilled(origin, max, ImGui.GetColorU32(body), rounding);

        if (!selected)
        {
            drawList.AddRect(
                origin, max,
                ImGui.GetColorU32(hovered
                    ? new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.55f)
                    : new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0.8f)),
                rounding, ImDrawFlags.None, 1f * scale);
        }

        var textColour = selected ? Theme.Background : hovered ? Theme.Text : Theme.TextDim;

        drawList.AddText(origin + padding, ImGui.GetColorU32(textColour), label);

        return clicked;
    }

    /// Air inside a state pill, around its label.
    private static Vector2 PillPadding => new Vector2(10f, 3f) * UiHelpers.Scale;

    /// How tall a state pill draws.
    public static float StatePillHeight => ImGui.GetTextLineHeight() + (PillPadding.Y * 2f);

    /// A small filled pill that says something rather than doing anything - a state, not a control.
    public static void StatePill(string label, Vector4 colour)
    {
        var scale = UiHelpers.Scale;
        var padding = PillPadding;
        var dot = 4f * scale;
        var dotGap = 7f * scale;

        var textSize = ImGui.CalcTextSize(label);
        var size = new Vector2(
            textSize.X + (padding.X * 2f) + dot + dotGap + dot, textSize.Y + (padding.Y * 2f));

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var rounding = size.Y / 2f;

        drawList.AddRectFilled(
            origin, origin + size,
            ImGui.GetColorU32(new Vector4(colour.X, colour.Y, colour.Z, 0.14f)), rounding);

        drawList.AddRect(
            origin, origin + size,
            ImGui.GetColorU32(new Vector4(colour.X, colour.Y, colour.Z, 0.45f)),
            rounding, ImDrawFlags.None, 1f * scale);

        drawList.AddCircleFilled(
            new Vector2(origin.X + padding.X + dot, origin.Y + padding.Y + UiHelpers.TextInkCentre()), dot,
            ImGui.GetColorU32(colour));

        drawList.AddText(
            new Vector2(origin.X + padding.X + (dot * 2f) + dotGap, origin.Y + padding.Y),
            ImGui.GetColorU32(colour), label);

        ImGui.Dummy(size);
    }

    /// One control holding several exclusive choices, with a highlight that slides between them.
    public static Vector2 SegmentedSize(IReadOnlyList<string> labels)
    {
        var scale = UiHelpers.Scale;
        var padding = new Vector2(14f, 6f) * scale;
        var inset = 3f * scale;
        var total = 0f;

        foreach (var label in labels)
            total += ImGui.CalcTextSize(label).X + (padding.X * 2f);

        return new Vector2(
            total + (inset * 2f),
            ImGui.GetTextLineHeight() + (padding.Y * 2f) + (inset * 2f));
    }

    public static int Segmented(
        string id, IReadOnlyList<string> labels, int selected, ref Vector2 slide,
        Func<int, bool>? marked = null)
    {
        var scale = UiHelpers.Scale;
        var padding = new Vector2(14f, 6f) * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        var height = lineHeight + (padding.Y * 2f);

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var widths = new float[labels.Count];
        var total = 0f;

        for (var i = 0; i < labels.Count; i++)
        {
            widths[i] = ImGui.CalcTextSize(labels[i]).X + (padding.X * 2f);
            total += widths[i];
        }

        var inset = 3f * scale;
        var trackHeight = height + (inset * 2f);

        drawList.AddRectFilled(
            origin, new Vector2(origin.X + total + (inset * 2f), origin.Y + trackHeight),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.22f)), trackHeight / 2f);

        var chosen = selected;
        var target = Vector2.Zero;
        var x = origin.X + inset;

        for (var i = 0; i < labels.Count; i++)
        {
            var width = widths[i];

            ImGui.SetCursorScreenPos(new Vector2(x, origin.Y + inset));
            if (ImGui.InvisibleButton($"{id}##seg{i}", new Vector2(width, height), ImGuiButtonFlags.MouseButtonLeft))
                chosen = i;

            if (i == selected)
                target = new Vector2(x, width);

            x += width;
        }

        slide = slide.Y <= 0f
            ? target
            : new Vector2(
                UiHelpers.Lerp(slide.X, target.X, 20f, ImGui.GetIO().DeltaTime),
                UiHelpers.Lerp(slide.Y, target.Y, 20f, ImGui.GetIO().DeltaTime));

        drawList.AddRectFilled(
            new Vector2(slide.X, origin.Y + inset),
            new Vector2(slide.X + slide.Y, origin.Y + inset + height),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.92f)),
            height / 2f);

        x = origin.X + inset;

        for (var i = 0; i < labels.Count; i++)
        {
            var textSize = ImGui.CalcTextSize(labels[i]);
            var centre = new Vector2(x + (widths[i] / 2f), origin.Y + inset + (height / 2f));

            drawList.AddText(
                centre - (textSize / 2f),
                ImGui.GetColorU32(i == selected ? Theme.Background : Theme.TextDim),
                labels[i]);

            if (marked is not null && marked(i))
            {
                drawList.AddCircleFilled(
                    new Vector2(x + widths[i] - (9f * scale), origin.Y + inset + (8f * scale)),
                    2.5f * scale,
                    ImGui.GetColorU32(i == selected ? Theme.Background : Theme.Accent));
            }

            x += widths[i];
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(total + (inset * 2f), trackHeight));

        return chosen;
    }

    /// A paragraph box that wraps and still lets Enter make a break.
    public static bool Paragraph(string id, ref string value, int limit, Vector2 size, string? label = null)
    {
        if (label is not null)
            DrawLabel(label, value.Length, limit, size.X, null);

        return UiHelpers.WrappingMultiline(id, ref value, limit, size);
    }

    /// A picker over one of the game's own sheets, with a "not set" entry at the top.
    public static bool Choice(
        string id, string label, IReadOnlyList<GameChoice> choices, ref uint selected, float width,
        string noneLabel = "Not Set")
    {
        ImGui.BeginGroup();
        DrawLabel(label, 0, 0, width, null);

        var current = GameLists.NameOf(choices, selected);
        var changed = false;

        ImGui.SetNextItemWidth(width);

        var open = ImGui.BeginCombo(id, current.Length > 0 ? current : noneLabel);

        var box = (Min: ImGui.GetItemRectMin(), Max: ImGui.GetItemRectMax());
        var hovered = ImGui.IsItemHovered();

        if (open)
        {
            if (ImGui.Selectable(noneLabel, selected == 0))
            {
                selected = 0;
                changed = true;
            }

            foreach (var choice in choices)
            {
                if (!ImGui.Selectable($"{choice.Name}##{choice.Id}", choice.Id == selected))
                    continue;

                selected = choice.Id;
                changed = true;
            }

            ImGui.EndCombo();
        }

        UnderlineAt(box.Min, box.Max, open, hovered);

        ImGui.EndGroup();
        return changed;
    }

    /// The label row: the name on the left, and the count on the right once the box is nearly full.
    private static void DrawLabel(string label, int length, int limit, float width, string? tooltip)
    {
        var origin = ImGui.GetCursorScreenPos();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        if (tooltip is not null && ImGui.IsItemHovered())
            UiHelpers.WrappedTooltip(tooltip);

        if (limit <= 0 || length < limit * CountFrom)
            return;

        var count = $"{length}/{limit}";
        var countWidth = ImGui.CalcTextSize(count).X;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddText(
            new Vector2(origin.X + width - countWidth, origin.Y),
            ImGui.GetColorU32(length >= limit ? Theme.Warning : Theme.TextDim),
            count);
    }
}
