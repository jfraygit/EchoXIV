using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Controls.V2;

/// The mixer instrument controls: transport buttons, knobs, faders, crossfader, seek bar and peak meter.
public static class Mixer
{
    private static readonly Dictionary<string, float> Hovers = new();
    private static readonly Dictionary<string, float> Lits = new();
    private static readonly Dictionary<string, float> Meters = new();
    private static readonly Dictionary<string, DragAnchor> Drags = new();

    /// Value and cursor position captured at press.
    private readonly record struct DragAnchor(float Value, Vector2 Mouse);

    private static float Ease(string key, bool on, float speed)
    {
        var current = Lits.TryGetValue(key, out var v) ? v : (on ? 1f : 0f);
        current = Motion.Approach(current, on ? 1f : 0f, speed);
        Lits[key] = current;
        return current;
    }

    private static float Hover(string key, bool hovered)
    {
        var current = Hovers.TryGetValue(key, out var v) ? v : 0f;
        current = Motion.Approach(current, hovered ? 1f : 0f, Motion.SpeedInstant);
        Hovers[key] = current;
        return current;
    }

    /// The value this control last drove locally, held until the engine's status catches up.
    private static readonly Dictionary<string, float> Pendings = new();

    /// In-flight animated return to default.
    private readonly record struct ResetAnim(float From, float To, double StartedAt);

    private const double ResetSeconds = 0.22;
    private static readonly Dictionary<string, ResetAnim> Resets = new();

    /// Reconciles the engine's reported value with anything this control is currently driving - an in-flight
    /// reset animation first, then a sent value the engine hasn't echoed yet.
    private static float Reconcile(string id, float reported, float range, out bool changed)
    {
        changed = false;

        if (Resets.TryGetValue(id, out var anim))
        {
            var t = (float)Math.Clamp((ImGui.GetTime() - anim.StartedAt) / ResetSeconds, 0d, 1d);
            var value = anim.From + ((anim.To - anim.From) * Motion.EaseOutCubic(t));

            if (t >= 1f)
            {
                Resets.Remove(id);
                value = anim.To;
            }

            Pendings[id] = value;
            changed = true;
            return value;
        }

        if (Pendings.TryGetValue(id, out var pending))
        {
            var tolerance = MathF.Max(0.0005f, MathF.Abs(range) * 0.01f);
            if (MathF.Abs(reported - pending) < tolerance)
                Pendings.Remove(id);
            else
                return pending;
        }

        return reported;
    }

    private static void HoldPending(string id, float value) => Pendings[id] = value;

    /// Double-click or right-click on a draggable control means "back to default", animated.
    private static bool ResetRequested(string id, bool hovered, float current, float target)
    {
        if (!hovered)
            return false;

        if (!ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            return false;

        Drags.Remove(id);
        Resets[id] = new ResetAnim(current, target, ImGui.GetTime());
        return true;
    }

    /// A transport button: rounded square, raised surface, accent ring when primary or toggled on.
    public static bool TransportButton(
        string id,
        FontAwesomeIcon icon,
        string tooltip,
        string? tooltipBody,
        float size,
        Vector4 accent,
        bool primary = false,
        bool toggledOn = false,
        bool enabled = true)
    {
        var box = MathF.Round(size);
        var min = Chrome.Snap(ImGui.GetCursorScreenPos());
        var max = min + new Vector2(box, box);

        var clicked = ImGui.InvisibleButton(id, new Vector2(box, box)) && enabled;
        var hovered = ImGui.IsItemHovered() && enabled;
        var held = ImGui.IsItemActive() && enabled;

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var hover = Hover(id, hovered);
        var lit = Ease($"{id}#lit", toggledOn || primary, Motion.SpeedFast);

        var press = held ? MathF.Round(1.5f * Metrics.Scale) : 0f;
        var faceMin = min + new Vector2(0f, press);
        var faceMax = max + new Vector2(0f, press);

        var drawList = ImGui.GetWindowDrawList();
        if (!held)
            Elevation.DrawShadow(drawList, min, max, Metrics.RadiusSoft, Elevation.ShadowSpec.Low);

        var face = Vector4.Lerp(Elevation.Raised, Semantic.Alpha(accent, 0.22f), lit);
        if (hover > 0f)
            face = Vector4.Lerp(face, Semantic.Lift(face, 0.12f), hover);

        drawList.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(face), Metrics.RadiusSoft);

        var ringAlpha = enabled ? 0.35f + (lit * 0.55f) + (hover * 0.2f) : 0.15f;
        drawList.AddRect(faceMin, faceMax, ImGui.GetColorU32(Semantic.Alpha(accent, MathF.Min(1f, ringAlpha))),
            Metrics.RadiusSoft, ImDrawFlags.None, MathF.Max(1f, Metrics.Hairline));

        var iconColor = enabled
            ? Vector4.Lerp(Semantic.TextSecondary, accent, MathF.Max(lit, hover))
            : Semantic.TextDisabled;

        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, icon,
                Chrome.Snap(faceMin + new Vector2(box * 0.5f, box * 0.5f)),
                ImGui.GetColorU32(iconColor));

        Tip.Hovered(tooltip, tooltipBody, accent);
        return clicked;
    }

    /// The moving cap shared by the channel faders and the crossfader.
    private static float CapEdgeInset => MathF.Round(Metrics.RadiusSoft);

    private static void DrawCap(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        Vector4 accent,
        float hover,
        bool horizontal)
    {
        var rounding = Metrics.RadiusSharp;

        Elevation.DrawShadow(drawList, min, max, rounding, Elevation.ShadowSpec.High);

        var top = Semantic.Lift(Elevation.Overlay, 0.22f + (hover * 0.12f));
        var bottom = Semantic.Sink(Elevation.Raised, 0.25f);

        drawList.AddRectFilled(min, new Vector2(max.X, min.Y + rounding), ImGui.GetColorU32(top),
            rounding, ImDrawFlags.RoundCornersTop);
        drawList.AddRectFilled(new Vector2(min.X, max.Y - rounding), max, ImGui.GetColorU32(bottom),
            rounding, ImDrawFlags.RoundCornersBottom);
        drawList.AddRectFilledMultiColor(
            new Vector2(min.X, min.Y + rounding),
            new Vector2(max.X, max.Y - rounding),
            ImGui.GetColorU32(top), ImGui.GetColorU32(top),
            ImGui.GetColorU32(bottom), ImGui.GetColorU32(bottom));

        drawList.AddLine(
            new Vector2(min.X + rounding, min.Y + 0.5f),
            new Vector2(max.X - rounding, min.Y + 0.5f),
            ImGui.GetColorU32(Semantic.Alpha(Vector4.One, 0.22f)), 1f);
        drawList.AddLine(
            new Vector2(min.X + rounding, max.Y - 0.5f),
            new Vector2(max.X - rounding, max.Y - 0.5f),
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.45f)), 1f);

        drawList.AddRect(min, max,
            ImGui.GetColorU32(Semantic.Alpha(accent, 0.55f + (hover * 0.35f))),
            rounding, ImDrawFlags.None, MathF.Max(1f, Metrics.Hairline));

        var centreX = MathF.Round((min.X + max.X) * 0.5f);
        var centreY = MathF.Round((min.Y + max.Y) * 0.5f);
        var step = MathF.Max(3f, 4f * Metrics.Scale);
        var dark = ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.5f));
        var light = ImGui.GetColorU32(Semantic.Alpha(Vector4.One, 0.14f));

        for (var i = -1; i <= 1; i++)
        {
            if (horizontal)
            {
                var x = MathF.Round(centreX + (i * step) - 0.5f);
                var y0 = min.Y + (5f * Metrics.Scale);
                var y1 = max.Y - (5f * Metrics.Scale);
                drawList.AddLine(new Vector2(x, y0), new Vector2(x, y1), dark, 1f);
                drawList.AddLine(new Vector2(x + 1f, y0), new Vector2(x + 1f, y1), light, 1f);
            }
            else
            {
                var y = MathF.Round(centreY + (i * step) - 0.5f);
                var x0 = min.X + (5f * Metrics.Scale);
                var x1 = max.X - (5f * Metrics.Scale);
                drawList.AddLine(new Vector2(x0, y), new Vector2(x1, y), dark, 1f);
                drawList.AddLine(new Vector2(x0, y + 1f), new Vector2(x1, y + 1f), light, 1f);
            }
        }
    }

    /// The primary play/pause control: a large filled circle in the deck's colour.
    public static bool PlayButton(string id, bool isPlaying, float size, Vector4 accent, string tooltip, string? tooltipBody)
    {
        var box = MathF.Round(size);
        var min = Chrome.Snap(ImGui.GetCursorScreenPos());
        var radius = box * 0.5f;
        var centre = Chrome.Snap(min + new Vector2(radius, radius));

        var clicked = ImGui.InvisibleButton(id, new Vector2(box, box));
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var hover = Hover(id, hovered);
        var playing = Ease($"{id}#playing", isPlaying, Motion.SpeedFast);
        var press = held ? MathF.Round(1.5f * Metrics.Scale) : 0f;
        var c = centre + new Vector2(0f, press);

        var drawList = ImGui.GetWindowDrawList();

        if (playing > 0.01f)
            drawList.AddCircle(c, radius + (4f * Metrics.Scale),
                ImGui.GetColorU32(Semantic.Alpha(accent, 0.45f * playing)),
                0, MathF.Max(1f, 1.5f * Metrics.Scale));

        if (!held)
            drawList.AddCircleFilled(c + new Vector2(0f, 2f * Metrics.Scale), radius,
                ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.4f)));

        var fill = Vector4.Lerp(accent, Semantic.Lift(accent, 0.18f), hover);
        drawList.AddCircleFilled(c, radius, ImGui.GetColorU32(fill));

        drawList.PathArcTo(c, radius - 1f, MathF.PI * 0.15f, MathF.PI * 0.85f, 28);
        drawList.PathStroke(ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.16f)),
            ImDrawFlags.None, MathF.Max(1f, 2f * Metrics.Scale));

        drawList.AddCircle(c, radius, ImGui.GetColorU32(Semantic.Alpha(Vector4.One, 0.16f + (hover * 0.16f))),
            0, MathF.Max(1f, Metrics.Hairline));

        var nudge = isPlaying ? Vector2.Zero : new Vector2(radius * 0.06f, 0f);

        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, isPlaying ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play,
                Chrome.Snap(c + nudge), ImGui.GetColorU32(Semantic.TextOnAccent), box * 0.38f);

        Tip.Hovered(tooltip, tooltipBody, accent);
        return clicked;
    }

    /// A labelled pill toggle for a mode, as opposed to TransportButton's square icon action.
    public static bool ToggleChip(
        string id,
        FontAwesomeIcon icon,
        string label,
        bool isOn,
        float height,
        Vector4 accent,
        string tooltip,
        string? tooltipBody = null)
    {
        var h = MathF.Round(height);
        var iconRoom = MathF.Round(h * 0.9f);

        float textWidth;
        using (TypeScale.Caption())
            textWidth = ImGui.CalcTextSize(label).X;

        var width = MathF.Round(iconRoom + textWidth + (Metrics.Md * 2f));
        var min = Chrome.Snap(ImGui.GetCursorScreenPos());
        var max = min + new Vector2(width, h);

        var clicked = ImGui.InvisibleButton(id, new Vector2(width, h));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var hover = Hover(id, hovered);
        var lit = Ease($"{id}#lit", isOn, Motion.SpeedFast);

        var drawList = ImGui.GetWindowDrawList();
        var fill = Vector4.Lerp(Elevation.Raised, Semantic.Alpha(accent, 0.25f), lit);
        if (hover > 0f)
            fill = Vector4.Lerp(fill, Semantic.Lift(fill, 0.1f), hover);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), h * 0.5f);
        drawList.AddRect(min, max,
            ImGui.GetColorU32(Vector4.Lerp(Elevation.Line, accent, MathF.Max(lit, hover * 0.5f))),
            h * 0.5f, ImDrawFlags.None, Metrics.Hairline);

        var content = Vector4.Lerp(Semantic.TextTertiary, Semantic.TextPrimary, MathF.Max(lit, hover));

        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, icon,
                Chrome.Snap(new Vector2(min.X + Metrics.Md + (iconRoom * 0.5f), min.Y + (h * 0.5f))),
                ImGui.GetColorU32(content), h * 0.5f);

        using (TypeScale.Caption())
        {
            var textSize = ImGui.CalcTextSize(label);
            Chrome.Text(drawList,
                Chrome.CenterY(min.X + Metrics.Md + iconRoom, min.Y, h, textSize.Y),
                ImGui.GetColorU32(content), label);
        }

        Tip.Hovered(tooltip, tooltipBody, accent);
        return clicked;
    }

    /// A sound pad.
    public static bool PadButton(
        string id,
        string label,
        bool hasSound,
        bool looping,
        Vector2 size,
        Vector4 accent,
        out bool middleClicked)
    {
        var min = Chrome.Snap(ImGui.GetCursorScreenPos());
        var max = min + size;

        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        middleClicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Middle);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var hover = Hover(id, hovered);
        var loop = Ease($"{id}#loop", looping, Motion.SpeedFast);

        var press = held ? MathF.Round(1.5f * Metrics.Scale) : 0f;
        var faceMin = min + new Vector2(0f, press);
        var faceMax = max + new Vector2(0f, press);

        var drawList = ImGui.GetWindowDrawList();

        if (!hasSound)
        {
            drawList.AddRectFilled(faceMin, faceMax,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.03f + (hover * 0.04f))), Metrics.RadiusSoft);
            drawList.AddRect(faceMin, faceMax,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.12f + (hover * 0.12f))),
                Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline);

            using (TypeScale.Icon())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.Plus,
                    Chrome.Snap(faceMin + (size * 0.5f)),
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.TextTertiary, 0.6f + (hover * 0.4f))));

            Tip.Hovered("Empty Pad", "Click to load a sound effect.");
            return clicked;
        }

        if (!held)
            Elevation.DrawShadow(drawList, min, max, Metrics.RadiusSoft, Elevation.ShadowSpec.Low);

        var face = Vector4.Lerp(Elevation.Raised, Semantic.Alpha(accent, 0.2f), loop);
        if (hover > 0f)
            face = Vector4.Lerp(face, Semantic.Lift(face, 0.12f), hover);

        drawList.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(face), Metrics.RadiusSoft);
        drawList.AddRect(faceMin, faceMax,
            ImGui.GetColorU32(Semantic.Alpha(accent, 0.4f + (loop * 0.5f) + (hover * 0.2f))),
            Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline);

        using (TypeScale.Caption())
        {
            var shown = UiHelpers.TruncateToWidth(label, size.X - (Metrics.Md * 2f));
            var textSize = ImGui.CalcTextSize(shown);
            Chrome.Text(drawList,
                Chrome.Snap(faceMin + new Vector2((size.X - textSize.X) * 0.5f, (size.Y - textSize.Y) * 0.5f)),
                ImGui.GetColorU32(Semantic.TextPrimary), shown);
        }

        if (loop > 0.01f)
            drawList.AddCircleFilled(
                Chrome.Snap(new Vector2(faceMax.X - (6f * Metrics.Scale), faceMin.Y + (6f * Metrics.Scale))),
                MathF.Max(1.5f, 2.5f * Metrics.Scale), ImGui.GetColorU32(Semantic.Alpha(accent, loop)));

        Tip.Hovered(label, looping ? "Looping. Middle-click to stop looping, right-click for options."
            : "Click to play. Middle-click to loop, right-click for options.", accent);

        return clicked;
    }

    /// Maps a value onto dial travel with the default pinned at half rotation (straight up).
    private static float ValueToFraction(float value, float min, float max, float defaultValue)
    {
        if (defaultValue <= min)
            return Math.Clamp((value - min) / MathF.Max(0.0001f, max - min), 0f, 1f);
        if (defaultValue >= max)
            return Math.Clamp((value - min) / MathF.Max(0.0001f, max - min), 0f, 1f);

        return value <= defaultValue
            ? 0.5f * Math.Clamp((value - min) / MathF.Max(0.0001f, defaultValue - min), 0f, 1f)
            : 0.5f + (0.5f * Math.Clamp((value - defaultValue) / MathF.Max(0.0001f, max - defaultValue), 0f, 1f));
    }

    private static float FractionToValue(float fraction, float min, float max, float defaultValue)
    {
        if (defaultValue <= min || defaultValue >= max)
            return min + (Math.Clamp(fraction, 0f, 1f) * (max - min));

        return fraction <= 0.5f
            ? min + ((fraction / 0.5f) * (defaultValue - min))
            : defaultValue + (((fraction - 0.5f) / 0.5f) * (max - defaultValue));
    }

    /// A rotary knob.
    public static bool Knob(
        string id,
        string label,
        ref float value,
        float min,
        float max,
        float defaultValue,
        float radius,
        Vector4 accent,
        string? readoutFormat = null)
    {
        const float startAngle = 3f * MathF.PI / 4f;
        const float sweep = 3f * MathF.PI / 2f;

        var r = MathF.Round(radius);
        var labelHeight = ImGui.GetTextLineHeight();
        var origin = ImGui.GetCursorScreenPos();
        var totalWidth = r * 2f;

        using (TypeScale.Caption())
        {
            var size = ImGui.CalcTextSize(label);
            Chrome.Text(ImGui.GetWindowDrawList(),
                new Vector2(origin.X + ((totalWidth - size.X) * 0.5f), origin.Y),
                ImGui.GetColorU32(Semantic.TextTertiary), label);
        }

        var dialTop = origin.Y + labelHeight + Metrics.Xs;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, dialTop));
        var changed = ImGui.InvisibleButton(id, new Vector2(totalWidth, r * 2f));
        changed = false;

        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        value = Reconcile(id, value, max - min, out changed);

        var fraction = ValueToFraction(value, min, max, defaultValue);

        if (ResetRequested(id, hovered, value, defaultValue))
        {
        }
        else
        {
            if (ImGui.IsItemActivated())
                Drags[id] = new DragAnchor(fraction, ImGui.GetIO().MousePos);

            if (Drags.TryGetValue(id, out var anchor))
            {
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    Drags.Remove(id);
                }
                else
                {
                    var dx = ImGui.GetIO().MousePos.X - anchor.Mouse.X;
                    var sensitivity = ImGui.GetIO().KeyShift ? 0.25f : 1f;
                    var nextFraction = Math.Clamp(anchor.Value + (dx / 180f * sensitivity), 0f, 1f);
                    var next = FractionToValue(nextFraction, min, max, defaultValue);

                    if (MathF.Abs(next - value) > 0.0001f)
                    {
                        value = next;
                        fraction = nextFraction;
                        HoldPending(id, value);
                        changed = true;
                    }
                }
            }
        }

        var hover = Hover(id, hovered);
        var centre = Chrome.Snap(new Vector2(origin.X + (totalWidth * 0.5f), dialTop + r));
        var angle = startAngle + (sweep * fraction);

        var drawList = ImGui.GetWindowDrawList();

        drawList.AddCircleFilled(centre, r, ImGui.GetColorU32(Elevation.Sunken));
        drawList.AddCircleFilled(centre + new Vector2(0f, 1f), r - (2f * Metrics.Scale),
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.3f)));

        var body = Vector4.Lerp(Elevation.Raised, Semantic.Lift(Elevation.Raised, 0.1f), hover);
        drawList.AddCircleFilled(centre, r - (2.5f * Metrics.Scale), ImGui.GetColorU32(body));

        var arcRadius = r - (1f * Metrics.Scale);
        var thickness = MathF.Max(1.5f, 2.5f * Metrics.Scale);

        drawList.PathArcTo(centre, arcRadius, startAngle, startAngle + sweep, 48);
        drawList.PathStroke(ImGui.GetColorU32(Elevation.Line), ImDrawFlags.None, thickness);

        var neutralAngle = startAngle + (sweep * 0.5f);
        if (MathF.Abs(fraction - 0.5f) > 0.002f)
        {
            drawList.PathArcTo(centre, arcRadius, MathF.Min(neutralAngle, angle), MathF.Max(neutralAngle, angle), 48);
            drawList.PathStroke(ImGui.GetColorU32(Semantic.Alpha(accent, 0.9f + (hover * 0.1f))),
                ImDrawFlags.None, thickness);
        }

        var neutralDir = new Vector2(MathF.Cos(neutralAngle), MathF.Sin(neutralAngle));
        drawList.AddLine(centre + (neutralDir * (arcRadius - thickness)), centre + (neutralDir * (arcRadius + thickness)),
            ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.3f)), 1f);

        var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        drawList.AddLine(centre + (dir * r * 0.35f), centre + (dir * (r - (5f * Metrics.Scale))),
            ImGui.GetColorU32(Vector4.Lerp(Semantic.TextSecondary, accent, 0.4f + (hover * 0.6f))),
            MathF.Max(1.5f, 2f * Metrics.Scale));

        if (readoutFormat != null && (hovered || Drags.ContainsKey(id)))
            Tip.Show(label, string.Format(readoutFormat, value), accent);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, dialTop + (r * 2f)));
        ImGui.Dummy(new Vector2(totalWidth, 0f));
        return changed;
    }

    /// A vertical channel fader.
    public static bool Fader(
        string id,
        ref float value,
        float min,
        float max,
        float defaultValue,
        Vector2 size,
        Vector4 accent,
        out bool activated)
    {
        var min2 = Chrome.Snap(ImGui.GetCursorScreenPos());
        var max2 = min2 + size;

        var clicked = ImGui.InvisibleButton(id, size);
        activated = ImGui.IsItemActivated();
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        var capHeight = MathF.Round(20f * Metrics.Scale);

        var travel = MathF.Max(1f, size.Y - capHeight - (CapEdgeInset * 2f));
        var changed = false;

        value = Reconcile(id, value, max - min, out changed);

        if (ResetRequested(id, hovered, value, defaultValue))
        {
        }
        else
        {
            if (activated)
                Drags[id] = new DragAnchor(value, ImGui.GetIO().MousePos);

            if (Drags.TryGetValue(id, out var anchor))
            {
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    Drags.Remove(id);
                }
                else
                {
                    var dy = anchor.Mouse.Y - ImGui.GetIO().MousePos.Y;
                    var next = Math.Clamp(anchor.Value + (dy / travel * (max - min)), min, max);
                    if (MathF.Abs(next - value) > 0.0001f)
                    {
                        value = next;
                        HoldPending(id, value);
                        changed = true;
                    }
                }
            }
        }

        var hover = Hover(id, hovered);
        var fraction = Math.Clamp((value - min) / MathF.Max(0.0001f, max - min), 0f, 1f);

        var drawList = ImGui.GetWindowDrawList();
        var centreX = MathF.Round(min2.X + (size.X * 0.5f));

        Elevation.DrawSurface(drawList, min2, max2, Elevation.Surface, Metrics.RadiusSoft,
            Elevation.ShadowSpec.None, topEdge: true, Elevation.Line);

        var channelWidth = MathF.Round(6f * Metrics.Scale);
        var channelMin = new Vector2(centreX - (channelWidth * 0.5f), min2.Y + (capHeight * 0.5f) + CapEdgeInset);
        var channelMax = new Vector2(centreX + (channelWidth * 0.5f), max2.Y - (capHeight * 0.5f) - CapEdgeInset);
        drawList.AddRectFilled(channelMin, channelMax, ImGui.GetColorU32(Elevation.Sunken), channelWidth * 0.5f);

        var capCentreY = channelMax.Y - ((channelMax.Y - channelMin.Y) * fraction);
        drawList.AddRectFilled(new Vector2(channelMin.X, capCentreY), channelMax,
            ImGui.GetColorU32(Semantic.Alpha(accent, 0.75f)), channelWidth * 0.5f);

        for (var i = 0; i <= 10; i++)
        {
            var y = MathF.Round(channelMin.Y + ((channelMax.Y - channelMin.Y) * (i / 10f)));
            var long_ = i % 5 == 0;
            var half = (long_ ? size.X * 0.3f : size.X * 0.18f);
            drawList.AddLine(new Vector2(centreX - half, y), new Vector2(centreX - (channelWidth * 0.5f) - 1f, y),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, long_ ? 0.18f : 0.09f)), 1f);
            drawList.AddLine(new Vector2(centreX + (channelWidth * 0.5f) + 1f, y), new Vector2(centreX + half, y),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, long_ ? 0.18f : 0.09f)), 1f);
        }

        var capWidth = MathF.Round(size.X - (6f * Metrics.Scale));
        var capMin = Chrome.Snap(new Vector2(centreX - (capWidth * 0.5f), capCentreY - (capHeight * 0.5f)));
        var capMax = capMin + new Vector2(capWidth, capHeight);

        DrawCap(drawList, capMin, capMax, accent, hover, horizontal: false);

        return changed;
    }

    /// The horizontal crossfader.
    public static bool Crossfader(
        string id,
        ref float value,
        Vector2 size,
        Vector4 leftAccent,
        Vector4 rightAccent,
        out bool activated)
    {
        var min = Chrome.Snap(ImGui.GetCursorScreenPos());
        var max = min + size;

        ImGui.InvisibleButton(id, size);
        activated = ImGui.IsItemActivated();
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        var capWidth = MathF.Round(26f * Metrics.Scale);

        var travel = MathF.Max(1f, size.X - capWidth - (CapEdgeInset * 2f));
        var changed = false;

        value = Reconcile(id, value, 1f, out changed);

        if (ResetRequested(id, hovered, value, 0.5f))
        {
        }
        else
        {
            if (activated)
                Drags[id] = new DragAnchor(value, ImGui.GetIO().MousePos);

            if (Drags.TryGetValue(id, out var anchor))
            {
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    Drags.Remove(id);
                }
                else
                {
                    var dx = ImGui.GetIO().MousePos.X - anchor.Mouse.X;
                    var next = Math.Clamp(anchor.Value + (dx / travel), 0f, 1f);
                    if (MathF.Abs(next - value) > 0.0001f)
                    {
                        value = next;
                        HoldPending(id, value);
                        changed = true;
                    }
                }
            }
        }

        var hover = Hover(id, hovered);
        var drawList = ImGui.GetWindowDrawList();
        var centreY = MathF.Round(min.Y + (size.Y * 0.5f));

        Elevation.DrawSurface(drawList, min, max, Elevation.Surface, Metrics.RadiusSoft,
            Elevation.ShadowSpec.None, topEdge: true, Elevation.Line);

        var channelHeight = MathF.Round(6f * Metrics.Scale);
        var channelMin = new Vector2(min.X + (capWidth * 0.5f) + CapEdgeInset, centreY - (channelHeight * 0.5f));
        var channelMax = new Vector2(max.X - (capWidth * 0.5f) - CapEdgeInset, centreY + (channelHeight * 0.5f));
        drawList.AddRectFilled(channelMin, channelMax, ImGui.GetColorU32(Elevation.Sunken), channelHeight * 0.5f);

        var centreX = MathF.Round((channelMin.X + channelMax.X) * 0.5f);
        drawList.AddLine(new Vector2(centreX, min.Y + (size.Y * 0.2f)), new Vector2(centreX, max.Y - (size.Y * 0.2f)),
            ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.22f)), MathF.Max(1f, Metrics.Hairline));

        var capCentreX = channelMin.X + ((channelMax.X - channelMin.X) * value);
        var capMin = Chrome.Snap(new Vector2(capCentreX - (capWidth * 0.5f), min.Y + (4f * Metrics.Scale)));
        var capMax = Chrome.Snap(new Vector2(capCentreX + (capWidth * 0.5f), max.Y - (4f * Metrics.Scale)));
        var capColor = Vector4.Lerp(leftAccent, rightAccent, value);

        DrawCap(drawList, capMin, capMax, capColor, hover, horizontal: true);

        return changed;
    }

    /// Track position.
    public static bool SeekBar(
        string id,
        ref float value,
        float duration,
        float? cueSeconds,
        Vector2 size,
        Vector4 accent,
        bool interactive = true)
    {
        var min = Chrome.Snap(ImGui.GetCursorScreenPos());
        var max = min + size;
        var radius = size.Y * 0.5f;

        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered() && interactive;
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var committed = false;
        if (interactive)
        {
            if (ImGui.IsItemActive())
            {
                var t = Math.Clamp((ImGui.GetIO().MousePos.X - min.X) / MathF.Max(1f, size.X), 0f, 1f);
                value = t * duration;
            }

            if (ImGui.IsItemDeactivated())
                committed = true;
        }

        var fraction = duration > 0.01f ? Math.Clamp(value / duration, 0f, 1f) : 0f;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Elevation.Sunken), radius);

        if (fraction > 0f)
        {
            var fillMax = new Vector2(min.X + (size.X * fraction), max.Y);
            drawList.AddRectFilled(min, fillMax, ImGui.GetColorU32(Semantic.Alpha(accent, 0.85f)), radius);
        }

        if (cueSeconds is { } cue && duration > 0.01f)
        {
            var cueX = MathF.Round(min.X + (size.X * Math.Clamp(cue / duration, 0f, 1f)));
            drawList.AddTriangleFilled(
                new Vector2(cueX, min.Y - (3f * Metrics.Scale)),
                new Vector2(cueX - (3f * Metrics.Scale), min.Y - (8f * Metrics.Scale)),
                new Vector2(cueX + (3f * Metrics.Scale), min.Y - (8f * Metrics.Scale)),
                ImGui.GetColorU32(Semantic.TextSecondary));
        }

        if (fraction > 0f || hovered)
        {
            var headX = MathF.Round(min.X + (size.X * fraction));
            var headCentre = new Vector2(headX, MathF.Round(min.Y + radius));
            drawList.AddCircleFilled(headCentre, radius * 1.9f, ImGui.GetColorU32(Semantic.Alpha(accent, 0.3f)));
            drawList.AddCircleFilled(headCentre, radius * 1.05f, ImGui.GetColorU32(Semantic.TextPrimary));
        }

        return committed;
    }

    /// A segmented peak meter with attack/release ballistics, so it reads like a meter rather than a bar that
    /// snaps.
    public static void PeakMeter(string id, Vector2 size, float peak, bool vertical = true)
    {
        var smoothed = Meters.TryGetValue(id, out var s) ? s : 0f;
        var target = Math.Clamp(peak * 1.4f, 0f, 1f);
        smoothed = Motion.Approach(smoothed, target, target > smoothed ? 40f : 6f);
        Meters[id] = smoothed;

        var min = Chrome.Snap(ImGui.GetCursorScreenPos());
        var max = min + size;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Elevation.Sunken), Metrics.RadiusSharp);

        const int segments = 18;
        var gap = MathF.Max(1f, 2f * Metrics.Scale);
        var span = (vertical ? size.Y : size.X) - (gap * (segments - 1));
        var segLength = span / segments;

        for (var i = 0; i < segments; i++)
        {
            var t = (i + 1) / (float)segments;
            var on = smoothed >= t - (0.5f / segments);

            var color = t < 0.6f
                ? Vector4.Lerp(Semantic.MeterOk, Semantic.MeterWarn, t / 0.6f)
                : Vector4.Lerp(Semantic.MeterWarn, Semantic.MeterClip, (t - 0.6f) / 0.4f);

            var alpha = on ? 1f : 0.12f;

            Vector2 segMin, segMax;
            if (vertical)
            {
                var y = max.Y - ((i + 1) * segLength) - (i * gap);
                segMin = new Vector2(min.X + 1f, MathF.Round(y));
                segMax = new Vector2(max.X - 1f, MathF.Round(y + segLength));
            }
            else
            {
                var x = min.X + (i * (segLength + gap));
                segMin = new Vector2(MathF.Round(x), min.Y + 1f);
                segMax = new Vector2(MathF.Round(x + segLength), max.Y - 1f);
            }

            drawList.AddRectFilled(segMin, segMax, ImGui.GetColorU32(Semantic.Alpha(color, alpha)), 1f);
        }
    }
}
