using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using EchoGlam.Shared;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Controls;

/// Choosing tags from a vocabulary too large to print.
public sealed class TagPicker
{
    public enum Mode
    {
        /// The publish form: several tags, up to the board's cap.
        Many,

        /// The gallery's filter: one tag, or none.
        One,
    }

    private readonly string id;
    private readonly Mode mode;

    private string search = string.Empty;

    /// Which band the dialog is showing.
    private int band;

    /// The vocabulary the bands below were built from, by reference.
    private string[] grouped = [];
    private List<GalleryTags.Group> bands = [];

    public TagPicker(string id, Mode mode)
    {
        this.id = id;
        this.mode = mode;
    }

    private string PopupId => $"##tagpicker{id}";


    /// One chip: a tag name in a capsule.
    public static float ChipHeight => MathF.Max(Metrics.TouchMin, (Text.CaptionSize * 1.15f) + (Sp.Sm * 2f));

    /// Room a chip needs.
    private static float ChipWidth(string label, bool removable = false)
    {
        float text;
        using (Text.PushCaption())
            text = Text.Width(label);

        return text + (Sp.Lg * 2f) + (removable ? Sp.Lg : 0f);
    }

    /// Height of the field the form draws.
    public static float FieldHeight => MathF.Max(Metrics.Field, ChipHeight + (Sp.Xs * 2f));

    /// A chip, drawn and hit-tested.
    private bool Chip(
        Vector2 min, string label, bool chosen, bool enabled, string? tooltip, bool removable = false)
    {
        var drawList = ImGui.GetWindowDrawList();
        var size = new Vector2(ChipWidth(label, removable), ChipHeight);
        var rounding = Rad.Pill(ChipHeight);

        var cursor = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(min);

        var clicked = ImGui.InvisibleButton($"##chip{id}{label}", size, ImGuiButtonFlags.MouseButtonLeft) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();

        ImGui.SetCursorScreenPos(cursor);

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (!string.IsNullOrEmpty(tooltip))
                UiHelpers.WrappedTooltip(tooltip);
        }

        var warm = Motion.Toward($"chip{id}{label}", hovered, 22f);
        var max = min + size;

        var body = !enabled
            ? Col.Surface with { W = 0.5f }
            : chosen
                ? Vector4.Lerp(Theme.Accent with { W = 0.20f }, Theme.Accent with { W = 0.32f }, warm)
                : Vector4.Lerp(Col.Raised, Col.RaisedHigh, warm);

        drawList.AddRectFilled(min, max, Col.U32(body), rounding);

        var edge = !enabled
            ? Col.EdgeRest with { W = 0.25f }
            : chosen
                ? Vector4.Lerp(Theme.Accent with { W = 0.7f }, Theme.Accent, warm)
                : Vector4.Lerp(Col.EdgeRest with { W = 0.6f }, Col.EdgeHover, warm);

        drawList.AddRect(min, max, Col.U32(edge), rounding, ImDrawFlags.None, Stroke.Hair);

        var tint = !enabled
            ? Col.TextMuted with { W = 0.5f }
            : chosen
                ? Vector4.Lerp(Theme.Accent, Vector4.One, 0.25f)
                : Vector4.Lerp(Col.TextDim, Col.Text, warm);

        using (Text.PushCaption())
        {
            var text = Text.Width(label);
            var room = size.X - (removable ? Sp.Lg : 0f);

            drawList.AddText(
                new Vector2(min.X + ((room - text) / 2f), min.Y + ((ChipHeight - Text.CaptionSize) / 2f)),
                Col.U32(tint), label);
        }

        if (removable && warm > 0.01f)
        {
            using (Text.PushIcon())
            {
                UiHelpers.DrawScaledIcon(
                    drawList, FontAwesomeIcon.Times,
                    new Vector2(max.X - Sp.Lg, min.Y + (ChipHeight / 2f)),
                    Col.U32(Col.Danger with { W = warm }));
            }
        }

        return clicked;
    }


    /// The row the form draws: what is chosen, and the way in.
    public bool Field(
        Vector2 min, float width, ISet<string> chosen, string[] vocabulary, int maximum,
        IFontHandle iconFont)
    {
        var drawList = ImGui.GetWindowDrawList();
        var height = FieldHeight;
        var max = min + new Vector2(width, height);

        drawList.AddRectFilled(min, max, Col.U32(Col.Sunken), Rad.Ctl);

        var openLabel = chosen.Count == 0 ? "Choose" : "Change";
        var openWidth = ChipWidth(openLabel);
        var openMin = new Vector2(max.X - openWidth - Sp.Md, min.Y + ((height - ChipHeight) / 2f));

        var changed = false;
        var x = min.X + Sp.Md;
        var room = openMin.X - Sp.Md;
        var chipTop = min.Y + ((height - ChipHeight) / 2f);

        if (chosen.Count == 0)
        {
            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(x + Sp.Xs, min.Y + ((height - Text.CaptionSize) / 2f)),
                    Col.U32(Col.TextMuted),
                    mode == Mode.One ? "Any tag" : "None yet");
            }

            x += Sp.Xs;
        }
        else
        {
            var order = vocabulary.Where(chosen.Contains).ToList();
            var shown = 0;

            for (var i = 0; i < order.Count; i++)
            {
                var label = order[i];
                var chipWidth = ChipWidth(label, removable: true);
                var rest = order.Count - i;

                var counter = rest > 1 ? Sp.Xs + ChipWidth($"+{rest - 1}") : 0f;

                if (shown > 0 && x + chipWidth + counter > room)
                    break;

                if (Chip(
                        new Vector2(x, chipTop), label, chosen: true, enabled: true,
                        tooltip: "Take this tag off.", removable: true))
                {
                    chosen.Remove(label);
                    changed = true;
                }

                x += chipWidth + Sp.Xs;
                shown++;
            }

            if (shown < order.Count)
            {
                var hidden = order.Count - shown;
                var label = $"+{hidden}";

                Chip(
                    new Vector2(x, chipTop), label, chosen: false, enabled: false,
                    tooltip: string.Join(", ", order.Skip(shown)));

                x += ChipWidth(label) + Sp.Xs;
            }
        }

        var gap = MathF.Max(0f, openMin.X - x);
        var over = false;

        if (gap > Sp.Md)
        {
            var cursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(x, min.Y));

            var hit = ImGui.InvisibleButton(
                $"##tagfield{id}", new Vector2(gap, height), ImGuiButtonFlags.MouseButtonLeft);

            over = ImGui.IsItemHovered();
            ImGui.SetCursorScreenPos(cursor);

            if (over)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (hit)
                ImGui.OpenPopup(PopupId);
        }

        drawList.AddRect(
            min, max, Col.U32(over ? Col.EdgeHover : Col.EdgeRest with { W = 0.6f }),
            Rad.Ctl, ImDrawFlags.None, Stroke.Hair);

        if (Chip(
                openMin, openLabel, chosen: false, enabled: true,
                tooltip: mode == Mode.One
                    ? "Pick a tag to filter the board by."
                    : $"Up to {maximum} tags. They are how people find this on the board."))
            ImGui.OpenPopup(PopupId);

        return Popup(chosen, vocabulary, maximum, iconFont) || changed;
    }


    /// Groups the vocabulary the relay actually served, cached against the array it came in.
    private List<GalleryTags.Group> Bands(string[] vocabulary)
    {
        if (ReferenceEquals(grouped, vocabulary) && bands.Count > 0)
            return bands;

        var built = new List<GalleryTags.Group>();

        foreach (var group in GalleryTags.Groups)
        {
            var present = group.Tags.Where(vocabulary.Contains).ToArray();

            if (present.Length > 0)
                built.Add(new GalleryTags.Group(group.Name, present));
        }

        var extra = vocabulary.Where(t => GalleryTags.GroupOf(t) is null).ToArray();

        if (extra.Length > 0)
            built.Add(new GalleryTags.Group("More", extra));

        grouped = vocabulary;
        bands = built;
        return bands;
    }

    private bool Popup(ISet<string> chosen, string[] vocabulary, int maximum, IFontHandle iconFont)
    {
        var scale = UiHelpers.Scale;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18f, 16f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rad.Panel);

        Modal.Centre();

        var open = ImGui.BeginPopup(PopupId);

        ImGui.PopStyleVar(2);

        if (!open)
            return false;

        var width = Modal.Width(ModalSize.Medium);
        var all = Bands(vocabulary);
        var changed = false;

        band = all.Count > 0 ? Math.Clamp(band, 0, all.Count - 1) : 0;

        ImGui.Dummy(new Vector2(width, 0f));


        var drawList = ImGui.GetWindowDrawList();
        var headOrigin = ImGui.GetCursorScreenPos();

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, headOrigin, mode == Mode.One ? "FILTER BY TAG" : "TAGS",
                Col.U32(Col.TextMuted), 1.8f);

            if (mode == Mode.Many)
            {
                var count = $"{chosen.Count} of {maximum}";

                drawList.AddText(
                    new Vector2(headOrigin.X + width - Text.Width(count), headOrigin.Y),
                    Col.U32(chosen.Count >= maximum ? Col.Caution : Col.TextMuted),
                    count);
            }
        }

        ImGui.Dummy(new Vector2(width, Text.CaptionSize * 1.6f));


        UiHelpers.SearchField(
            $"##tagsearch{id}", "Search tags...", ref search, width, Metrics.Row, iconFont);

        ImGui.Dummy(new Vector2(0f, Sp.Md));

        var searching = search.Trim().Length > 0;

        if (!searching)
        {
            var x = 0f;

            for (var i = 0; i < all.Count; i++)
            {
                var label = all[i].Name.ToUpperInvariant();
                var tabWidth = ChipWidth(label);

                if (x > 0f && x + Sp.Xs + tabWidth > width)
                    x = 0f;
                else if (x > 0f)
                    ImGui.SameLine(0f, Sp.Xs);

                if (Band(label, i == band))
                    band = i;

                x += tabWidth + Sp.Xs;
            }

            ImGui.Dummy(new Vector2(0f, Sp.Md));
        }


        var words = new List<string>();

        if (searching)
        {
            var needle = search.Trim();
            words.AddRange(vocabulary.Where(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }
        else if (all.Count > 0)
        {
            words.AddRange(all[band].Tags);
        }

        var listWidth = width - ImGui.GetStyle().ScrollbarSize - Sp.Xs;
        var listHeight = (6f * (ChipHeight + Sp.Xs)) - Sp.Xs;

        if (ImGui.BeginChild($"##tagwords{id}", new Vector2(width, listHeight), false))
        {
            var origin = ImGui.GetCursorScreenPos();
            var x = origin.X;
            var y = origin.Y;
            var atCap = mode == Mode.Many && chosen.Count >= maximum;

            if (words.Count == 0)
            {
                using (Text.PushCaption())
                {
                    ImGui.GetWindowDrawList().AddText(
                        origin + new Vector2(Sp.Xs, Sp.Sm), Col.U32(Col.TextMuted), "No tag by that name.");
                }
            }

            foreach (var tag in words)
            {
                var chipWidth = ChipWidth(tag);

                if (x > origin.X && x + chipWidth > origin.X + listWidth)
                {
                    x = origin.X;
                    y += ChipHeight + Sp.Xs;
                }

                var on = chosen.Contains(tag);

                if (Chip(
                        new Vector2(x, y), tag, on, enabled: on || !atCap,
                        tooltip: on
                            ? mode == Mode.One ? "Clear the filter." : "Take this tag off."
                            : atCap
                                ? $"That is already {maximum} tags. Take one off to add another."
                                : null))
                {
                    changed = true;

                    if (mode == Mode.One)
                    {
                        chosen.Clear();

                        if (!on)
                            chosen.Add(tag);

                        ImGui.CloseCurrentPopup();
                    }
                    else if (!chosen.Remove(tag))
                    {
                        chosen.Add(tag);
                    }
                }

                x += chipWidth + Sp.Xs;
            }

            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(listWidth, y - origin.Y + ChipHeight));
        }

        ImGui.EndChild();


        ImGui.Dummy(new Vector2(0f, Sp.Sm));

        var footOrigin = ImGui.GetCursorScreenPos();

        drawList.AddRectFilled(
            footOrigin,
            new Vector2(footOrigin.X + width, footOrigin.Y + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        ImGui.Dummy(new Vector2(width, Stroke.Hair + Sp.Lg));

        var doneWidth = EchoButton.ContentSize("Done").X;
        var clearWidth = EchoButton.ContentSize("Clear").X;
        var left = ImGui.GetCursorPosX();

        if (chosen.Count > 0)
        {
            if (EchoButton.Draw(
                    $"##tagclear{id}", "Clear", new Vector2(clearWidth, Metrics.Row),
                    accentOverride: Col.Caution,
                    tooltip: mode == Mode.One ? "Show every tag again." : "Take all of them off."))
            {
                chosen.Clear();
                changed = true;
            }

            ImGui.SameLine(0f, Sp.Sm);
        }

        ImGui.SetCursorPosX(left + MathF.Max(chosen.Count > 0 ? clearWidth + Sp.Sm : 0f, width - doneWidth));

        if (EchoButton.Draw($"##tagdone{id}", "Done", new Vector2(doneWidth, Metrics.Row), primary: true))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
        return changed;
    }

    /// One band heading in the dialog, as a tab.
    private bool Band(string label, bool active)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(ChipWidth(label), ChipHeight);

        var clicked = ImGui.InvisibleButton($"##band{id}{label}", size, ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var warm = Motion.Toward($"band{id}{label}", hovered || active, 22f);
        var tint = Col.U32(Vector4.Lerp(Col.TextMuted, active ? Theme.Accent : Col.Text, warm));

        using (Text.PushCaption())
        {
            var run = Text.TrackedWidth(label, 1.4f);
            var runLeft = min.X + ((size.X - run) / 2f);

            Text.Tracked(
                drawList, new Vector2(runLeft, min.Y + ((size.Y - Text.CaptionSize) / 2f)), label, tint, 1.4f);

            if (active)
            {
                var markY = min.Y + size.Y - Stroke.Mark;

                drawList.AddRectFilled(
                    new Vector2(runLeft, markY),
                    new Vector2(runLeft + run, markY + Stroke.Mark),
                    Col.U32(Theme.Accent), Rad.Chip);
            }
        }

        return clicked;
    }
}
