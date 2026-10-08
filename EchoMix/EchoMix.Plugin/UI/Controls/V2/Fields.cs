using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Controls.V2;

/// Form controls for 2.0 screens: a shared settings row plus a switch and a slider.
public static class Fields
{
    private static readonly Dictionary<string, float> SwitchPositions = new();
    private static readonly Dictionary<string, float> RowHovers = new();

    /// Track geometry frozen at the moment a slider drag starts.
    private readonly record struct DragState(float TrackX, float TrackWidth);

    private static readonly Dictionary<string, DragState> SliderDrags = new();

    /// Where a double-clicked slider is easing back to, per id.
    private static readonly Dictionary<string, float> SliderResets = new();
    private static readonly Dictionary<string, float> SegmentMarkers = new();
    private static readonly Dictionary<string, float> ButtonHovers = new();

    public readonly record struct Row(
        Vector2 ControlMin,
        float ControlWidth,
        float ControlHeight,
        bool Hovered,
        bool Clicked,
        Vector2 NextCursor);

    /// Opens a labelled settings row.
    public static Row BeginRow(
        string id,
        string label,
        string? helper,
        float controlWidth,
        string? tooltip = null,
        bool rowClickable = true)
    {
        var fullWidth = Surfaces.ContentWidth;
        var origin = ImGui.GetCursorScreenPos();
        var controlHeight = MathF.Round(Metrics.ControlMd);

        var textWidth = MathF.Max(Metrics.Xxl, fullWidth - controlWidth - (Metrics.Lg * 3f));

        var labelSize = TypeScale.Measure(TypeScale.Body, label);

        string[] helperLines;
        using (TypeScale.Caption())
        {
            helperLines = string.IsNullOrEmpty(helper)
                ? Array.Empty<string>()
                : UiHelpers.WrapToWidth(helper, textWidth, 2);
        }

        var helperLineHeight = helperLines.Length == 0
            ? 0f
            : TypeScale.Measure(TypeScale.Caption, helperLines[0]).Y;

        var textHeight = labelSize.Y
            + (helperLines.Length > 0 ? Metrics.Xs + (helperLines.Length * helperLineHeight) : 0f);
        var height = MathF.Round(MathF.Max(controlHeight + (Metrics.Md * 2f), textHeight + (Metrics.Md * 2f)));

        var hitWidth = MathF.Max(Metrics.Xxl, rowClickable ? fullWidth : textWidth + Metrics.Lg);
        var clicked = ImGui.InvisibleButton($"{id}##row", new Vector2(hitWidth, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered && rowClickable)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var hover = RowHovers.TryGetValue(id, out var h) ? h : 0f;
        hover = Motion.Approach(hover, hovered ? 1f : 0f, Motion.SpeedInstant);
        RowHovers[id] = hover;

        var drawList = ImGui.GetWindowDrawList();
        if (hover > 0.01f)
        {
            drawList.AddRectFilled(
                Chrome.Snap(origin),
                Chrome.Snap(origin + new Vector2(fullWidth, height)),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.04f * hover)),
                Metrics.RadiusSoft);
        }

        var textTop = origin.Y + ((height - textHeight) * 0.5f);

        using (TypeScale.Body())
            Chrome.Text(drawList, new Vector2(origin.X + Metrics.Lg, textTop),
                ImGui.GetColorU32(Semantic.TextPrimary), label);

        using (TypeScale.Caption())
        {
            for (var i = 0; i < helperLines.Length; i++)
            {
                Chrome.Text(drawList,
                    new Vector2(origin.X + Metrics.Lg,
                        textTop + labelSize.Y + Metrics.Xs + (i * helperLineHeight)),
                    ImGui.GetColorU32(Semantic.TextTertiary), helperLines[i]);
            }
        }

        if (tooltip != null)
            Tip.Hovered(label, tooltip);

        var controlMin = Chrome.Snap(new Vector2(
            origin.X + fullWidth - Metrics.Lg - controlWidth,
            origin.Y + ((height - controlHeight) * 0.5f)));

        return new Row(
            controlMin,
            controlWidth,
            controlHeight,
            hovered,
            clicked,
            new Vector2(origin.X, origin.Y + height));
    }

    /// Restores the layout cursor to just below the row.
    public static void EndRow(Row row) => ImGui.SetCursorScreenPos(row.NextCursor);

    /// An animated on/off switch.
    public static bool Switch(string id, string label, ref bool value, string? helper = null, string? tooltip = null)
    {
        var width = MathF.Round(Metrics.ControlXl);
        var height = MathF.Round(Metrics.ControlSm * 0.8f);

        var row = BeginRow(id, label, helper, width, tooltip);

        var position = SwitchPositions.TryGetValue(id, out var p) ? p : (value ? 1f : 0f);
        position = Motion.Approach(position, value ? 1f : 0f, Motion.SpeedFast);
        SwitchPositions[id] = position;

        var min = Chrome.Snap(new Vector2(row.ControlMin.X, row.ControlMin.Y + ((row.ControlHeight - height) * 0.5f)));
        var max = min + new Vector2(width, height);
        var radius = height * 0.5f;

        var off = Semantic.Alpha(Vector4.Zero, 0.38f);
        var track = Vector4.Lerp(off, Semantic.Primary, position);
        if (row.Hovered)
            track = Semantic.Lift(track, 0.08f);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(track), radius);
        drawList.AddRect(min, max,
            ImGui.GetColorU32(Vector4.Lerp(Elevation.Line, Semantic.Alpha(Semantic.Primary, 0.6f), position)),
            radius, ImDrawFlags.None, Metrics.Hairline);

        var knobRadius = radius - (2f * Metrics.Scale);
        var knobX = min.X + radius + ((width - (radius * 2f)) * position);
        var knobCentre = Chrome.Snap(new Vector2(knobX, min.Y + radius));

        drawList.AddCircleFilled(knobCentre + new Vector2(0f, 1f), knobRadius,
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.3f)));
        drawList.AddCircleFilled(knobCentre, knobRadius,
            ImGui.GetColorU32(Vector4.Lerp(Semantic.TextSecondary, Semantic.TextOnAccent, position)));

        EndRow(row);

        if (!row.Clicked)
            return false;

        value = !value;
        return true;
    }

    private static float SliderTrackWidth => MathF.Round(200f * Metrics.Scale);

    private static float SliderReadoutWidth => MathF.Round(56f * Metrics.Scale);

    /// The narrowest a Slider row can be without its label and its track overlapping.
    public static float MeasureSliderRow(string label, float? trackWidth = null)
    {
        var control = (trackWidth is { } w ? MathF.Round(w) : SliderTrackWidth) + SliderReadoutWidth + Metrics.Lg;
        var text = MathF.Max(Metrics.Xxl, TypeScale.Measure(TypeScale.Body, label).X);
        return MathF.Round(control + text + (Metrics.Lg * 3f));
    }

    /// A slider with an animated grab and a value readout.
    public static bool Slider(
        string id,
        string label,
        ref float value,
        float min,
        float max,
        string format,
        float? resetTo = null,
        string? helper = null,
        float? trackWidth = null)
    {
        var sliderWidth = trackWidth is { } w ? MathF.Round(w) : SliderTrackWidth;
        var readoutWidth = SliderReadoutWidth;
        var tooltip = resetTo.HasValue ? "Double-click to reset to the default." : null;

        var row = BeginRow(id, label, helper, sliderWidth + readoutWidth + Metrics.Lg, tooltip, rowClickable: false);

        var trackHeight = MathF.Round(5f * Metrics.Scale);
        var trackMin = Chrome.Snap(new Vector2(row.ControlMin.X, row.ControlMin.Y + ((row.ControlHeight - trackHeight) * 0.5f)));
        var trackMax = trackMin + new Vector2(sliderWidth, trackHeight);
        var centreY = trackMin.Y + (trackHeight * 0.5f);

        ImGui.SetCursorScreenPos(new Vector2(trackMin.X, row.ControlMin.Y));
        ImGui.InvisibleButton($"{id}##track", new Vector2(sliderWidth, row.ControlHeight));

        var hovered = ImGui.IsItemHovered();
        var hasDrag = SliderDrags.TryGetValue(id, out var drag);

        if (ImGui.IsItemActivated())
        {
            drag = new DragState(trackMin.X, sliderWidth);
            SliderDrags[id] = drag;
            hasDrag = true;
        }

        if (hasDrag && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            SliderDrags.Remove(id);
            hasDrag = false;
        }

        var changed = false;
        if (hasDrag)
        {
            var t = Math.Clamp((ImGui.GetIO().MousePos.X - drag.TrackX) / MathF.Max(1f, drag.TrackWidth), 0f, 1f);
            var next = min + (t * (max - min));
            if (MathF.Abs(next - value) > 0.0001f)
            {
                value = next;
                changed = true;
            }
        }

        if (resetTo is { } reset && hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            SliderDrags.Remove(id);
            SliderResets[id] = reset;
        }

        if (SliderResets.TryGetValue(id, out var resetTarget))
        {
            if (SliderDrags.ContainsKey(id))
            {
                SliderResets.Remove(id);
            }
            else
            {
                value = Motion.Approach(value, resetTarget, Motion.SpeedFast);
                changed = true;

                if (MathF.Abs(value - resetTarget) <= MathF.Max(0.0005f, (max - min) * 0.002f))
                {
                    value = resetTarget;
                    SliderResets.Remove(id);
                }
            }
        }

        var fraction = Math.Clamp((value - min) / MathF.Max(0.0001f, max - min), 0f, 1f);
        var fillMax = new Vector2(trackMin.X + (sliderWidth * fraction), trackMax.Y);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(trackMin, trackMax, ImGui.GetColorU32(Elevation.Sunken), trackHeight * 0.5f);
        drawList.AddRect(trackMin, trackMax, ImGui.GetColorU32(Elevation.Line), trackHeight * 0.5f,
            ImDrawFlags.None, Metrics.Hairline);
        if (fraction > 0f)
            drawList.AddRectFilled(trackMin, fillMax, ImGui.GetColorU32(Semantic.Primary), trackHeight * 0.5f);

        var grabRadius = MathF.Round((hasDrag ? 9f : hovered ? 8f : 7f) * Metrics.Scale);
        var grabCentre = Chrome.Snap(new Vector2(fillMax.X, centreY));

        if (hasDrag || hovered)
            drawList.AddCircleFilled(grabCentre, grabRadius + (4f * Metrics.Scale),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, hasDrag ? 0.22f : 0.14f)));

        drawList.AddCircleFilled(grabCentre + new Vector2(0f, 1f), grabRadius,
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.35f)));
        drawList.AddCircleFilled(grabCentre, grabRadius, ImGui.GetColorU32(Elevation.Raised));
        drawList.AddCircle(grabCentre, grabRadius, ImGui.GetColorU32(Semantic.Primary), 0,
            MathF.Max(1.5f, 2f * Metrics.Scale));

        using (TypeScale.Caption())
        {
            var readout = string.Format(format, value);
            var size = ImGui.CalcTextSize(readout);
            Chrome.Text(drawList,
                Chrome.CenterY(trackMax.X + Metrics.Lg, row.ControlMin.Y, row.ControlHeight, size.Y),
                ImGui.GetColorU32(Semantic.TextSecondary), readout);
        }

        EndRow(row);
        return changed;
    }

    /// A dropdown built from scratch rather than ImGui.Combo.
    public static bool Dropdown(
        string id,
        string label,
        ref int selected,
        IReadOnlyList<string> options,
        string? helper = null,
        float? controlWidth = null)
    {
        var boxWidth = MathF.Round(controlWidth ?? (220f * Metrics.Scale));
        var row = BeginRow(id, label, helper, boxWidth, rowClickable: false);

        ImGui.SetCursorScreenPos(row.ControlMin);
        var changed = DropdownInline(id, ref selected, options, boxWidth, row.ControlHeight);

        EndRow(row);
        return changed;
    }

    /// The dropdown box on its own, drawn at the cursor rather than in a row's control slot.
    public static bool DropdownInline(
        string id,
        ref int selected,
        IReadOnlyList<string> options,
        float width,
        float? height = null,
        string placeholder = "")
    {
        const int maxVisibleRows = 10;

        var boxWidth = MathF.Round(width);
        var boxHeight = MathF.Round(height ?? Metrics.ControlMd);

        var boxMin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var boxMax = boxMin + new Vector2(boxWidth, boxHeight);

        var opened = ImGui.InvisibleButton($"{id}##box", new Vector2(boxWidth, boxHeight));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var popupId = $"{id}##popup";
        if (opened)
            ImGui.OpenPopup(popupId);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(boxMin, boxMax,
            ImGui.GetColorU32(hovered ? Elevation.Overlay : Elevation.Raised), Metrics.RadiusSoft);
        drawList.AddRect(boxMin, boxMax,
            ImGui.GetColorU32(hovered ? Semantic.Alpha(Semantic.Primary, 0.5f) : Elevation.LineStrong),
            Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline);

        var hasSelection = selected >= 0 && selected < options.Count;
        var current = hasSelection ? options[selected] : placeholder;
        var chevronRoom = Metrics.Xxl;

        using (TypeScale.Body())
        {
            var shown = UiHelpers.TruncateToWidth(current, boxWidth - chevronRoom - Metrics.Lg);
            var size = ImGui.CalcTextSize(shown);
            Chrome.Text(drawList,
                Chrome.CenterY(boxMin.X + Metrics.Lg, boxMin.Y, boxHeight, size.Y),
                ImGui.GetColorU32(hasSelection ? Semantic.TextPrimary : Semantic.TextTertiary), shown);
        }

        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.ChevronDown,
                Chrome.Snap(new Vector2(boxMax.X - (chevronRoom * 0.5f), boxMin.Y + (boxHeight * 0.5f))),
                ImGui.GetColorU32(hovered ? Semantic.Primary : Semantic.TextTertiary));

        var changed = false;
        var optionHeight = MathF.Round(Metrics.ControlMd);
        var padding = Metrics.Md;

        var visibleRows = Math.Min(options.Count, maxVisibleRows);
        var popupHeight = (visibleRows * optionHeight) + (padding * 2f);

        var widest = boxWidth;
        using (TypeScale.Body())
        {
            foreach (var option in options)
                widest = MathF.Max(widest, ImGui.CalcTextSize(option).X);
        }

        var maxPopupWidth = MathF.Round(460f * Metrics.Scale);
        var scrollbarRoom = options.Count > maxVisibleRows ? ImGui.GetStyle().ScrollbarSize : 0f;
        var popupWidth = MathF.Min(maxPopupWidth, widest + (padding * 2f) + Metrics.Lg + scrollbarRoom);

        var viewport = ImGui.GetMainViewport();
        var popupX = MathF.Min(boxMin.X, viewport.WorkPos.X + viewport.WorkSize.X - popupWidth - Metrics.Md);

        ImGui.SetNextWindowPos(new Vector2(MathF.Max(viewport.WorkPos.X, popupX), boxMax.Y + Metrics.Xs));
        ImGui.SetNextWindowSize(new Vector2(popupWidth, popupHeight));

        using var popupStyle = Sty.New()
            .Var(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding))
            .Var(ImGuiStyleVar.WindowRounding, Metrics.RadiusSoft)
            .Var(ImGuiStyleVar.WindowBorderSize, 0f)
            .Var(ImGuiStyleVar.ItemSpacing, Vector2.Zero)
            .Col(ImGuiCol.PopupBg, Elevation.Overlay);

        if (!ImGui.BeginPopup(popupId, Sty.PopupFlags))
            return false;

        var popupDrawList = ImGui.GetWindowDrawList();
        var popupMin = ImGui.GetWindowPos();
        var popupMax = popupMin + ImGui.GetWindowSize();

        for (var i = 0; i < options.Count; i++)
        {
            var optionPos = ImGui.GetCursorScreenPos();
            var optionWidth = ImGui.GetContentRegionAvail().X;

            if (ImGui.InvisibleButton($"##opt{i}", new Vector2(optionWidth, optionHeight)))
            {
                selected = i;
                changed = true;
                ImGui.CloseCurrentPopup();
            }

            var optionHovered = ImGui.IsItemHovered();
            var isSelected = i == selected;

            if (optionHovered || isSelected)
            {
                var inset = MathF.Max(1f, Metrics.Xs * 0.5f);
                popupDrawList.AddRectFilled(
                    Chrome.Snap(optionPos + new Vector2(0f, inset)),
                    Chrome.Snap(optionPos + new Vector2(optionWidth, optionHeight - inset)),
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, isSelected ? 0.22f : 0.12f)),
                    Metrics.RadiusSoft);
            }

            using (TypeScale.Body())
            {
                var shown = UiHelpers.TruncateToWidth(options[i], optionWidth - (Metrics.Md * 2f));
                var size = ImGui.CalcTextSize(shown);
                Chrome.Text(popupDrawList,
                    Chrome.CenterY(optionPos.X + Metrics.Md, optionPos.Y, optionHeight, size.Y),
                    ImGui.GetColorU32(isSelected ? Semantic.TextPrimary : Semantic.TextSecondary),
                    shown);
            }
        }

        popupDrawList.AddRect(popupMin, popupMax, ImGui.GetColorU32(Elevation.LineStrong),
            Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline);

        ImGui.EndPopup();
        return changed;
    }

    /// A text input styled to read as a field.
    public static bool TextInput(
        string id,
        ref string value,
        int maxLength,
        float width,
        string hint = "",
        bool password = false)
    {
        using var style = Sty.New()
            .Var(ImGuiStyleVar.FrameRounding, Metrics.RadiusSoft)
            .Var(ImGuiStyleVar.FrameBorderSize, MathF.Max(1f, Metrics.Hairline))
            .Var(ImGuiStyleVar.FramePadding, new Vector2(Metrics.Lg, Metrics.Md))
            .Col(ImGuiCol.FrameBg, Vector4.Lerp(Elevation.Sunken, Elevation.Surface, 0.35f))
            .Col(ImGuiCol.FrameBgHovered, Vector4.Lerp(Elevation.Sunken, Elevation.Surface, 0.55f))
            .Col(ImGuiCol.FrameBgActive, Vector4.Lerp(Elevation.Sunken, Elevation.Surface, 0.55f))
            .Col(ImGuiCol.Border, Elevation.LineStrong)
            .Col(ImGuiCol.Text, Semantic.TextPrimary)
            .Col(ImGuiCol.TextDisabled, Semantic.TextDisabled);

        ImGui.SetNextItemWidth(width);

        var flags = password ? ImGuiInputTextFlags.Password : ImGuiInputTextFlags.None;
        var changed = ImGui.InputTextWithHint(id, hint, ref value, maxLength, flags);

        if (ImGui.IsItemActive())
        {
            ImGui.GetWindowDrawList().AddRect(
                ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.8f)),
                Metrics.RadiusSoft, ImDrawFlags.None, MathF.Max(1f, Metrics.Hairline));
        }

        return changed;
    }

    /// A text input mounted in a settings row - label and helper on the left, field on the right.
    public static bool TextRow(
        string id,
        string label,
        ref string value,
        int maxLength,
        string? helper = null,
        string hint = "",
        bool password = false,
        string? tooltip = null,
        float? controlWidth = null)
    {
        var width = MathF.Round(controlWidth ?? (200f * Metrics.Scale));
        var row = BeginRow(id, label, helper, width, tooltip, rowClickable: false);

        var frameHeight = ImGui.GetTextLineHeight() + (Metrics.Md * 2f);
        ImGui.SetCursorScreenPos(Chrome.Snap(new Vector2(
            row.ControlMin.X,
            row.ControlMin.Y + ((row.ControlHeight - frameHeight) * 0.5f))));

        var changed = TextInput(id, ref value, maxLength, width, hint, password);

        EndRow(row);
        return changed;
    }

    /// A word-wrapping multiline text box, styled as a field.
    public static bool Multiline(
        string id,
        ref string value,
        int maxLength,
        float width,
        float height,
        out float wrapWidth,
        bool foldPending = false)
    {
        using var style = Sty.New()
            .Var(ImGuiStyleVar.FrameRounding, Metrics.RadiusSoft)
            .Var(ImGuiStyleVar.FrameBorderSize, MathF.Max(1f, Metrics.Hairline))
            .Var(ImGuiStyleVar.FramePadding, new Vector2(Metrics.Lg, Metrics.Md))
            .Col(ImGuiCol.FrameBg, Vector4.Lerp(Elevation.Sunken, Elevation.Surface, 0.35f))
            .Col(ImGuiCol.FrameBgHovered, Vector4.Lerp(Elevation.Sunken, Elevation.Surface, 0.55f))
            .Col(ImGuiCol.FrameBgActive, Vector4.Lerp(Elevation.Sunken, Elevation.Surface, 0.55f))
            .Col(ImGuiCol.Border, Elevation.LineStrong)
            .Col(ImGuiCol.Text, Semantic.TextPrimary)
            .Col(ImGuiCol.TextDisabled, Semantic.TextDisabled);

        var size = new Vector2(MathF.Round(width), MathF.Round(height));

        wrapWidth = WrappedInput.WidthFor(size);

        if (foldPending)
            value = WrappedInput.Fold(value, wrapWidth);

        var changed = WrappedInput.Multiline(id, ref value, maxLength, size);

        if (ImGui.IsItemActive())
        {
            ImGui.GetWindowDrawList().AddRect(
                ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.8f)),
                Metrics.RadiusSoft, ImDrawFlags.None, MathF.Max(1f, Metrics.Hairline));
        }

        return changed;
    }

    /// An editable list of freeform tags, rendered as removable pills.
    public static bool TagChips(
        string id,
        List<string> items,
        ref string entryBuffer,
        string hint,
        string emptyHint,
        float width,
        Vector4 accent,
        Func<string, string>? transform = null)
    {
        var changed = false;
        var addWidth = MathF.Round(Metrics.Xxxl * 2.2f);
        var inputWidth = MathF.Max(Metrics.Xxxl, width - addWidth - Metrics.Md);
        var frameHeight = MathF.Round(ImGui.GetTextLineHeight() + (Metrics.Md * 2f));

        TextInput($"{id}Entry", ref entryBuffer, 40, inputWidth, hint);
        var submitted = ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter, false);

        ImGui.SameLine(0f, Metrics.Md);
        var canAdd = !string.IsNullOrWhiteSpace(entryBuffer);
        var clicked = Button("Add", ButtonStyle.Secondary, addWidth, canAdd, height: frameHeight, idSuffix: id);

        if ((clicked || submitted) && canAdd)
        {
            var value = entryBuffer.Trim();
            if (transform != null)
                value = transform(value);

            var already = false;
            foreach (var existing in items)
            {
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                {
                    already = true;
                    break;
                }
            }

            if (!already)
            {
                items.Add(value);
                changed = true;
            }

            entryBuffer = string.Empty;
        }

        Surfaces.Gap(Metrics.Md);

        if (items.Count == 0)
        {
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextTertiary, emptyHint);

            return changed;
        }

        var drawList = ImGui.GetWindowDrawList();
        var padX = Metrics.Lg;
        var gap = Metrics.Md;
        var closeRoom = Metrics.Xl;
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var chipHeight = MathF.Round(Metrics.ControlSm);

        var x = 0f;
        var y = 0f;
        int? removeAt = null;

        for (var i = 0; i < items.Count; i++)
        {
            float labelWidth;
            using (TypeScale.Caption())
                labelWidth = ImGui.CalcTextSize(items[i]).X;

            var chipWidth = MathF.Round(labelWidth + (padX * 2f) + closeRoom);

            if (x > 0f && x + chipWidth > width)
            {
                x = 0f;
                y += chipHeight + gap;
            }

            var pos = Chrome.Snap(new Vector2(origin.X + x, origin.Y + y));
            var box = new Vector2(chipWidth, chipHeight);

            ImGui.SetCursorScreenPos(pos);
            var pressed = ImGui.InvisibleButton($"{id}chip{i}", box);
            var hovered = ImGui.IsItemHovered();

            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            drawList.AddRectFilled(pos, pos + box,
                ImGui.GetColorU32(Semantic.Alpha(accent, hovered ? 0.3f : 0.18f)), Metrics.Pill(chipHeight));
            drawList.AddRect(pos, pos + box,
                ImGui.GetColorU32(Semantic.Alpha(accent, hovered ? 0.7f : 0.35f)),
                Metrics.Pill(chipHeight), ImDrawFlags.None, Metrics.Hairline);

            using (TypeScale.Caption())
                Chrome.Text(drawList,
                    Chrome.CenterY(pos.X + padX, pos.Y, chipHeight, ImGui.GetTextLineHeight()),
                    ImGui.GetColorU32(accent), items[i]);

            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.Times,
                    Chrome.Snap(new Vector2(pos.X + chipWidth - (closeRoom * 0.5f) - (padX * 0.5f), pos.Y + (chipHeight * 0.5f))),
                    ImGui.GetColorU32(hovered ? Semantic.Danger : Semantic.Alpha(accent, 0.7f)));

            if (hovered)
                Tip.Hovered(items[i], "Click to remove.");

            if (pressed)
                removeAt = i;

            x += chipWidth + gap;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, y + chipHeight));

        if (removeAt is { } index)
        {
            items.RemoveAt(index);
            changed = true;
        }

        return changed;
    }

    /// A colour swatch in a settings row, opening ImGui's own picker.
    public static bool ColorRow(string id, string label, ref Vector3 value, string? helper = null)
    {
        var swatchWidth = MathF.Round(Metrics.ControlXl);

        var row = BeginRow(id, label, helper, swatchWidth,
            "Click the swatch to open the colour picker.", rowClickable: false);

        ImGui.SetCursorScreenPos(row.ControlMin);
        ImGui.SetNextItemWidth(swatchWidth);

        bool changed;
        using (Sty.Popup())
            changed = ImGui.ColorEdit3(id, ref value, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel);

        ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
            ImGui.GetColorU32(Elevation.LineStrong), Metrics.RadiusSoft, ImDrawFlags.None,
            MathF.Max(1f, Metrics.Hairline));

        EndRow(row);
        return changed;
    }

    /// A segmented control: two to four mutually exclusive choices in one pill.
    public static bool Segmented(
        string id,
        ref int selected,
        IReadOnlyList<string> options,
        float width,
        float? height = null)
    {
        if (options.Count == 0)
            return false;

        var boxHeight = MathF.Round(height ?? Metrics.ControlMd);
        var boxWidth = MathF.Round(width);
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var segmentWidth = boxWidth / options.Count;

        var clicked = ImGui.InvisibleButton($"{id}##seg", new Vector2(boxWidth, boxHeight));
        var hovered = ImGui.IsItemHovered();

        var hoverIndex = -1;
        if (hovered)
        {
            hoverIndex = Math.Clamp(
                (int)((ImGui.GetIO().MousePos.X - origin.X) / MathF.Max(1f, segmentWidth)),
                0, options.Count - 1);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var changed = false;
        if (clicked && hoverIndex >= 0 && hoverIndex != selected)
        {
            selected = hoverIndex;
            changed = true;
        }

        var marker = SegmentMarkers.TryGetValue(id, out var m) ? m : selected;
        marker = Motion.Approach(marker, selected, Motion.SpeedFast);
        SegmentMarkers[id] = marker;

        var drawList = ImGui.GetWindowDrawList();
        var max = origin + new Vector2(boxWidth, boxHeight);
        var radius = Metrics.Pill(boxHeight);

        drawList.AddRectFilled(origin, max, ImGui.GetColorU32(Elevation.Sunken), radius);
        drawList.AddRect(origin, max, ImGui.GetColorU32(Elevation.Line), radius,
            ImDrawFlags.None, Metrics.Hairline);

        var inset = MathF.Round(2f * Metrics.Scale);
        var markerMin = Chrome.Snap(new Vector2(origin.X + inset + (marker * segmentWidth), origin.Y + inset));
        var markerMax = Chrome.Snap(new Vector2(markerMin.X + segmentWidth - (inset * 2f), max.Y - inset));
        drawList.AddRectFilled(markerMin, markerMax, ImGui.GetColorU32(Semantic.Primary),
            Metrics.Pill(boxHeight - (inset * 2f)));

        for (var i = 0; i < options.Count; i++)
        {
            var lit = Math.Clamp(1f - MathF.Abs(marker - i), 0f, 1f);
            var resting = i == hoverIndex ? Semantic.TextPrimary : Semantic.TextSecondary;

            using (TypeScale.Body())
            {
                var shown = UiHelpers.TruncateToWidth(options[i], segmentWidth - (Metrics.Md * 2f));
                var size = ImGui.CalcTextSize(shown);
                Chrome.Text(drawList,
                    Chrome.CenterY(
                        origin.X + (segmentWidth * i) + ((segmentWidth - size.X) * 0.5f),
                        origin.Y, boxHeight, size.Y),
                    ImGui.GetColorU32(Vector4.Lerp(resting, Semantic.TextOnAccent, lit)), shown);
            }
        }

        return changed;
    }

    /// The width a segmented control needs for none of its labels to be clipped.
    public static float MeasureSegmented(IReadOnlyList<string> options)
    {
        var widest = 0f;

        using (TypeScale.Body())
        {
            foreach (var option in options)
                widest = MathF.Max(widest, ImGui.CalcTextSize(option).X);
        }

        return MathF.Round((widest + (Metrics.Md * 2f) + Metrics.Lg) * options.Count);
    }

    /// A segmented control mounted in a settings row.
    public static bool SegmentedRow(
        string id,
        string label,
        ref int selected,
        IReadOnlyList<string> options,
        string? helper = null,
        string? tooltip = null,
        float? controlWidth = null)
    {
        var width = MathF.Round(controlWidth ?? MeasureSegmented(options));
        var row = BeginRow(id, label, helper, width, tooltip, rowClickable: false);

        ImGui.SetCursorScreenPos(row.ControlMin);
        var changed = Segmented(id, ref selected, options, width, row.ControlHeight);

        EndRow(row);
        return changed;
    }

    /// The emphasis a button carries.
    public enum ButtonStyle
    {
        Primary,
        Secondary,
        Danger,

        /// A toggle that is currently ON - Liked, Following.
        Toggled,
    }

    /// An action button.
    public static bool Button(
        string label,
        ButtonStyle style = ButtonStyle.Secondary,
        float width = 0f,
        bool enabled = true,
        FontAwesomeIcon? icon = null,
        float? height = null,
        string? idSuffix = null,
        Vector4? toggleAccent = null)
    {
        var key = idSuffix == null ? label : $"{label}#{idSuffix}";
        var boxHeight = MathF.Round(height ?? Metrics.ControlLg);

        float textWidth;
        using (TypeScale.Body())
            textWidth = ImGui.CalcTextSize(label).X;

        var iconRoom = icon.HasValue ? Metrics.Xl + Metrics.Md : 0f;
        var boxWidth = width > 0f
            ? MathF.Round(width)
            : MathF.Round(textWidth + iconRoom + (Metrics.Xxl * 2f));

        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var pressed = ImGui.InvisibleButton($"##btn_{key}", new Vector2(boxWidth, boxHeight)) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();
        var held = hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var hover = ButtonHovers.TryGetValue(key, out var h) ? h : 0f;
        hover = Motion.Approach(hover, held ? 1f : hovered ? 0.5f : 0f, Motion.SpeedFast);
        ButtonHovers[key] = hover;

        var accent = toggleAccent ?? Semantic.Primary;

        var (baseFill, baseBorder, baseText) = style switch
        {
            ButtonStyle.Primary => (Semantic.Primary, Semantic.Alpha(Semantic.Primary, 0f), Semantic.TextOnAccent),
            ButtonStyle.Danger => (Semantic.Alpha(Semantic.Danger, 0.16f), Semantic.Alpha(Semantic.Danger, 0.55f), Semantic.Danger),
            ButtonStyle.Toggled => (Semantic.Alpha(accent, 0.2f), Semantic.Alpha(accent, 0.7f), accent),
            _ => (Elevation.Raised, Elevation.LineStrong, Semantic.TextPrimary),
        };

        Vector4 fill, border, text;
        if (!enabled)
        {
            fill = Elevation.Sunken;
            border = Elevation.Line;
            text = Semantic.TextDisabled;
        }
        else
        {
            fill = style switch
            {
                ButtonStyle.Primary => Semantic.Lift(baseFill, 0.14f * hover),
                ButtonStyle.Danger => Semantic.Alpha(Semantic.Danger, 0.16f + (0.18f * hover)),
                ButtonStyle.Toggled => Semantic.Alpha(accent, 0.2f + (0.16f * hover)),
                _ => Vector4.Lerp(baseFill, Elevation.Overlay, hover),
            };
            border = style switch
            {
                ButtonStyle.Primary => baseBorder,
                ButtonStyle.Danger => Semantic.Alpha(Semantic.Danger, 0.55f + (0.35f * hover)),
                ButtonStyle.Toggled => Semantic.Alpha(accent, 0.7f + (0.3f * hover)),
                _ => Vector4.Lerp(baseBorder, Semantic.Alpha(Semantic.Primary, 0.6f), hover),
            };
            text = baseText;
        }

        var sink = MathF.Round(hover > 0.75f ? 1f * Metrics.Scale : 0f);
        var min = origin + new Vector2(0f, sink);
        var max = min + new Vector2(boxWidth, boxHeight);
        var radius = Metrics.RadiusSoft;

        var drawList = ImGui.GetWindowDrawList();
        if (enabled)
        {
            drawList.AddRectFilled(
                origin + new Vector2(0f, 2f * Metrics.Scale),
                origin + new Vector2(boxWidth, boxHeight + (2f * Metrics.Scale)),
                ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.28f)), radius);
        }

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), radius);
        if (border.W > 0.001f)
            drawList.AddRect(min, max, ImGui.GetColorU32(border), radius, ImDrawFlags.None, Metrics.Hairline);

        var contentWidth = textWidth + iconRoom;
        var contentX = min.X + ((boxWidth - contentWidth) * 0.5f);

        if (icon is { } glyph)
        {
            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, glyph,
                    Chrome.Snap(new Vector2(contentX + (Metrics.Xl * 0.5f), min.Y + (boxHeight * 0.5f))),
                    ImGui.GetColorU32(text));

            contentX += iconRoom;
        }

        using (TypeScale.Body())
        {
            var size = ImGui.CalcTextSize(label);
            Chrome.Text(drawList, Chrome.CenterY(contentX, min.Y, boxHeight, size.Y),
                ImGui.GetColorU32(text), label);
        }

        return pressed;
    }

    /// An editable list of character names - the whitelist/blacklist shape.
    public static bool NameList(
        string id,
        List<string> items,
        ref string entryBuffer,
        string hint,
        string emptyHint,
        float width)
    {
        var changed = false;
        var addWidth = MathF.Round(Metrics.Xxxl * 2.2f);
        var inputWidth = MathF.Max(Metrics.Xxxl, width - addWidth - Metrics.Md);
        var frameHeight = MathF.Round(ImGui.GetTextLineHeight() + (Metrics.Md * 2f));

        TextInput($"{id}Entry", ref entryBuffer, 32, inputWidth, hint);

        ImGui.SameLine(0f, Metrics.Md);
        var canAdd = !string.IsNullOrWhiteSpace(entryBuffer);
        if (Button("Add", ButtonStyle.Secondary, addWidth, canAdd, height: frameHeight, idSuffix: id) && canAdd)
        {
            var name = entryBuffer.Trim();
            var already = false;
            foreach (var existing in items)
            {
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                {
                    already = true;
                    break;
                }
            }

            if (!already)
            {
                items.Add(name);
                changed = true;
            }

            entryBuffer = string.Empty;
        }

        Surfaces.Gap(Metrics.Md);

        if (items.Count == 0)
        {
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextTertiary, emptyHint);
            return changed;
        }

        var drawList = ImGui.GetWindowDrawList();
        var rowHeight = MathF.Round(Metrics.ControlSm);
        var closeSize = MathF.Round(Metrics.ControlXs);

        for (var i = items.Count - 1; i >= 0; i--)
        {
            var rowPos = Chrome.Snap(ImGui.GetCursorScreenPos());

            var hitWidth = MathF.Max(Metrics.Xxl, width - closeSize - (Metrics.Md * 2f));
            ImGui.InvisibleButton($"{id}row{i}", new Vector2(hitWidth, rowHeight));
            var rowHovered = ImGui.IsItemHovered();

            if (rowHovered)
                drawList.AddRectFilled(rowPos, rowPos + new Vector2(width, rowHeight),
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.05f)), Metrics.RadiusSoft);

            using (TypeScale.Body())
            {
                var shown = UiHelpers.TruncateToWidth(items[i], width - closeSize - (Metrics.Lg * 2f));
                var size = ImGui.CalcTextSize(shown);
                Chrome.Text(drawList, Chrome.CenterY(rowPos.X + Metrics.Md, rowPos.Y, rowHeight, size.Y),
                    ImGui.GetColorU32(Semantic.TextSecondary), shown);
            }

            var closeMin = Chrome.Snap(new Vector2(
                rowPos.X + width - closeSize - Metrics.Md,
                rowPos.Y + ((rowHeight - closeSize) * 0.5f)));

            ImGui.SetCursorScreenPos(closeMin);
            var remove = ImGui.InvisibleButton($"{id}del{i}", new Vector2(closeSize, closeSize));
            var closeHovered = ImGui.IsItemHovered();
            if (closeHovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            var closeCentre = Chrome.Snap(closeMin + new Vector2(closeSize * 0.5f, closeSize * 0.5f));
            if (closeHovered)
                drawList.AddCircleFilled(closeCentre, closeSize * 0.5f,
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.Danger, 0.2f)));

            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.Times, closeCentre,
                    ImGui.GetColorU32(closeHovered ? Semantic.Danger : Semantic.TextTertiary));

            ImGui.SetCursorScreenPos(new Vector2(rowPos.X, rowPos.Y + rowHeight));

            if (remove)
            {
                items.RemoveAt(i);
                changed = true;
            }
        }

        return changed;
    }

    /// A compact icon-only button.
    public static bool IconButton(
        string id,
        FontAwesomeIcon icon,
        float size,
        string tooltipTitle,
        string tooltipBody = "",
        bool enabled = true,
        bool danger = false)
    {
        var pos = Chrome.Snap(ImGui.GetCursorScreenPos());
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered() && enabled;

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        var centre = Chrome.Snap(pos + new Vector2(size * 0.5f, size * 0.5f));
        var tint = danger ? Semantic.Danger : Semantic.Primary;

        if (hovered)
            drawList.AddCircleFilled(centre, size * 0.5f, ImGui.GetColorU32(Semantic.Alpha(tint, 0.2f)));

        var color = !enabled
            ? Semantic.TextDisabled
            : hovered ? tint : Semantic.TextTertiary;

        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, icon, centre, ImGui.GetColorU32(color));

        if (hovered)
            Tip.Hovered(tooltipTitle, tooltipBody);

        return clicked && enabled;
    }

    /// A menu row for inside a popup, replacing ImGui.MenuItem.
    public static bool MenuRow(string label, float minWidth = 0f)
    {
        var height = MathF.Round(Metrics.ControlMd);

        float textWidth;
        using (TypeScale.Body())
            textWidth = ImGui.CalcTextSize(label).X;

        var width = MathF.Max(MathF.Max(minWidth, textWidth + (Metrics.Lg * 2f)), ImGui.GetContentRegionAvail().X);
        var pos = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##menu_{label}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        if (hovered)
            drawList.AddRectFilled(Chrome.Snap(pos), Chrome.Snap(pos + new Vector2(width, height)),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.18f)), Metrics.RadiusSoft);

        using (TypeScale.Body())
        {
            var size = ImGui.CalcTextSize(label);
            Chrome.Text(drawList, Chrome.CenterY(pos.X + Metrics.Lg, pos.Y, height, size.Y),
                ImGui.GetColorU32(hovered ? Semantic.TextPrimary : Semantic.TextSecondary), label);
        }

        return clicked;
    }

    /// A list's title, a rule, and room on the right for that list's own filters.
    public static ListHeader BeginListHeader(string title, float controlsWidth)
    {
        var width = Surfaces.ContentWidth;
        var drawList = ImGui.GetWindowDrawList();
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var height = MathF.Round(ImGui.GetTextLineHeight() + (Metrics.Md * 2f));

        var titleSize = TypeScale.Measure(TypeScale.Heading, title);
        using (TypeScale.Heading())
            Chrome.Text(drawList, Chrome.CenterY(origin.X, origin.Y, height, titleSize.Y),
                ImGui.GetColorU32(Semantic.TextSecondary), title);

        var ruleStart = origin.X + titleSize.X + Metrics.Lg;
        var ruleEnd = origin.X + width - controlsWidth - Metrics.Lg;
        if (ruleEnd > ruleStart)
        {
            var ruleY = origin.Y + (height * 0.5f);
            drawList.AddLine(Chrome.Snap(new Vector2(ruleStart, ruleY)), Chrome.Snap(new Vector2(ruleEnd, ruleY)),
                ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);
        }

        return new ListHeader(
            new Vector2(origin.X + width - controlsWidth, origin.Y),
            height,
            new Vector2(origin.X, origin.Y),
            width);
    }

    public static void EndListHeader(ListHeader header)
    {
        ImGui.SetCursorScreenPos(header.Origin);
        ImGui.Dummy(new Vector2(header.Width, header.Height));
    }

    public readonly record struct ListHeader(Vector2 ControlMin, float Height, Vector2 Origin, float Width);

    /// A small accent-tinted A/B button for sending a track to a deck.
    public static bool DeckButton(string id, string label, Vector4 accent, float width, float height, bool queued = false)
    {
        var pos = Chrome.Snap(ImGui.GetCursorScreenPos());
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var drawList = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(width, height);

        var fill = queued
            ? Semantic.Alpha(accent, hovered ? 1f : 0.85f)
            : Semantic.Alpha(accent, hovered ? 0.3f : 0.16f);

        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(fill), Metrics.RadiusSoft);
        drawList.AddRect(pos, max,
            ImGui.GetColorU32(Semantic.Alpha(accent, queued ? 1f : hovered ? 0.9f : 0.5f)),
            Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline);

        using (TypeScale.Body())
        {
            var size = ImGui.CalcTextSize(label);
            Chrome.Text(drawList,
                Chrome.CenterY(pos.X + ((width - size.X) * 0.5f), pos.Y, height, size.Y),
                ImGui.GetColorU32(queued ? Semantic.TextOnAccent : accent), label);
        }

        if (hovered)
        {
            Tip.Hovered($"Deck {label}",
                queued ? "Already queued. Click to add it again." : "Adds it to that deck's queue.",
                accent);
        }

        return clicked;
    }

    /// A divider between groups of rows inside a panel.
    public static void Divider()
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = Surfaces.ContentWidth;

        ImGui.Dummy(new Vector2(width, Metrics.Md));
        drawList.AddLine(
            Chrome.Snap(new Vector2(pos.X, pos.Y + (Metrics.Md * 0.5f))),
            Chrome.Snap(new Vector2(pos.X + width, pos.Y + (Metrics.Md * 0.5f))),
            ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);
    }
}
