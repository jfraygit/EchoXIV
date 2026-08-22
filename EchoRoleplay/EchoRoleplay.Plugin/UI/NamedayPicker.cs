using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoRoleplay.UI;

/// Pick a real date; store an Eorzean one.
public sealed class NamedayPicker
{
    /// Suns in every moon.
    public const int SunsPerMoon = 32;

    private static readonly string[] Months =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    ];

    private static readonly int[] MonthLengths = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    private static readonly string[] Moons =
    [
        "First Astral Moon",
        "First Umbral Moon",
        "Second Astral Moon",
        "Second Umbral Moon",
        "Third Astral Moon",
        "Third Umbral Moon",
        "Fourth Astral Moon",
        "Fourth Umbral Moon",
        "Fifth Astral Moon",
        "Fifth Umbral Moon",
        "Sixth Astral Moon",
        "Sixth Umbral Moon",
    ];

    private const string PopupId = "##namedaypicker";

    /// Which moon the grid is showing.
    private int shownMoon;

    private int chosenMoon = -1;
    private int chosenSun = -1;

    private bool openRequested;

    /// Draws the field, and the calendar when it is open.
    public bool Draw(string label, ref string value, float width, string? tooltip = null)
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

        var pressed = ImGui.InvisibleButton("##namedayfield", size, ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var rounding = ImGui.GetStyle().FrameRounding;

        drawList.AddRectFilled(
            origin, origin + size,
            ImGui.GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg), rounding);

        var padding = ImGui.GetStyle().FramePadding;
        var empty = string.IsNullOrWhiteSpace(value);

        drawList.AddText(
            origin + padding,
            ImGui.GetColorU32(empty ? Theme.TextDisabled : Theme.Text),
            empty ? "Pick A Nameday" : UiHelpers.Truncate(value, width - (padding.X * 2f) - (16f * scale)));

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
            Parse(value, out chosenMoon, out chosenSun);
            shownMoon = chosenMoon < 0 ? 0 : chosenMoon;
            openRequested = true;
        }

        if (openRequested)
        {
            ImGui.OpenPopup(PopupId);
            openRequested = false;
        }

        if (ImGui.BeginPopup(PopupId, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize))
        {
            changed = DrawCalendar(ref value, scale);
            ImGui.EndPopup();
        }

        ImGui.EndGroup();
        return changed;
    }

    private bool DrawCalendar(ref string value, float scale)
    {
        var changed = false;
        var cell = 30f * scale;
        var gap = 3f * scale;

        const int columns = 8;

        var gridWidth = (cell * columns) + (gap * (columns - 1));

        DrawMonthStepper(gridWidth, scale);

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var days = MonthLengths[shownMoon];

        for (var sun = 1; sun <= days; sun++)
        {
            var index = sun - 1;
            var column = index % columns;
            var rowIndex = index / columns;

            var cellOrigin = new Vector2(
                origin.X + (column * (cell + gap)), origin.Y + (rowIndex * (cell + gap)));

            ImGui.SetCursorScreenPos(cellOrigin);

            var chosen = shownMoon == chosenMoon && sun == chosenSun;

            if (ImGui.InvisibleButton($"##sun{sun}", new Vector2(cell, cell), ImGuiButtonFlags.MouseButtonLeft))
            {
                chosenMoon = shownMoon;
                chosenSun = sun;
                value = Format(chosenSun, chosenMoon);
                changed = true;
                ImGui.CloseCurrentPopup();
            }

            var hovered = ImGui.IsItemHovered();

            if (chosen || hovered)
            {
                drawList.AddRectFilled(
                    cellOrigin, cellOrigin + new Vector2(cell, cell),
                    ImGui.GetColorU32(chosen ? Theme.Accent : Theme.Tinted(0.18f)), 5f * scale);
            }

            var text = sun.ToString(CultureInfo.InvariantCulture);
            var textSize = ImGui.CalcTextSize(text);

            drawList.AddText(
                cellOrigin + ((new Vector2(cell, cell) - textSize) / 2f),
                ImGui.GetColorU32(chosen ? Theme.Background : hovered ? Theme.Text : Theme.TextDim),
                text);
        }

        var rows = (days + columns - 1) / columns;

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(gridWidth, (rows * cell) + ((rows - 1) * gap)));

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var previewSun = chosenMoon == shownMoon && chosenSun > 0 ? chosenSun : 1;

        ImGui.TextColored(Theme.TextDim, Format(previewSun, shownMoon));

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (Controls.EchoButton.Draw("##clearnameday", "Clear", new Vector2(0f, 24f * scale)))
        {
            value = string.Empty;
            chosenMoon = -1;
            chosenSun = -1;
            changed = true;
            ImGui.CloseCurrentPopup();
        }

        return changed;
    }

    private void DrawMonthStepper(float width, float scale)
    {
        var button = 22f * scale;
        var origin = ImGui.GetCursorScreenPos();

        if (Controls.EchoButton.Draw("##prevmoon", "<", new Vector2(button, button)))
            shownMoon = (shownMoon + Months.Length - 1) % Months.Length;

        var name = Months[shownMoon];
        var nameSize = ImGui.CalcTextSize(name);

        ImGui.GetWindowDrawList().AddText(
            new Vector2(
                origin.X + ((width - nameSize.X) / 2f),
                origin.Y + ((button - nameSize.Y) / 2f)),
            ImGui.GetColorU32(Theme.Accent), name);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - button, origin.Y));

        if (Controls.EchoButton.Draw("##nextmoon", ">", new Vector2(button, button)))
            shownMoon = (shownMoon + 1) % Months.Length;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + button));
        ImGui.Dummy(new Vector2(width, 0f));
    }

    /// A real date as an Eorzean one - "14th Sun, Second Astral Moon" for the 14th of March.
    public static string Format(int day, int month)
    {
        if (day < 1 || day > SunsPerMoon || month < 0 || month >= Moons.Length)
            return string.Empty;

        return $"{day}{Ordinal(day)} Sun, {Moons[month]}";
    }

    /// Reads a nameday back into a moon and a sun, so reopening the picker lands where the value already
    /// points.
    public static void Parse(string value, out int moon, out int sun)
    {
        moon = -1;
        sun = -1;

        if (string.IsNullOrWhiteSpace(value))
            return;

        for (var i = 0; i < Moons.Length; i++)
        {
            if (value.Contains(Moons[i], StringComparison.OrdinalIgnoreCase))
            {
                moon = i;
                break;
            }
        }

        var start = -1;

        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsDigit(value[i]))
            {
                if (start >= 0)
                    break;

                continue;
            }

            if (start < 0)
                start = i;
        }

        if (start < 0)
            return;

        var end = start;
        while (end < value.Length && char.IsDigit(value[end]))
            end++;

        if (int.TryParse(value[start..end], out var parsed) && parsed >= 1 && parsed <= SunsPerMoon)
            sun = parsed;
    }

    private static string Ordinal(int value) => (value % 100) switch
    {
        11 or 12 or 13 => "th",
        _ => (value % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        },
    };
}
