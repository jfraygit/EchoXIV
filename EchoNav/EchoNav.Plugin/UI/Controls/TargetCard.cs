using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using EchoNav.Game;

namespace EchoNav.UI.Controls;

/// One encounter, drawn as a single clickable card.
public static class TargetCard
{
    private const float HeightDesign = 62f;
    private const float IconSizeDesign = 34f;
    private const float PaddingDesign = 10f;

    /// How tall a card is.
    private static float Height
    {
        get
        {
            var scale = UiHelpers.Scale;
            var text = (ImGui.GetTextLineHeight() * 2f) + (PaddingDesign * 2f * scale);
            return MathF.Max(HeightDesign * scale, text);
        }
    }

    /// Draws the card.
    public static bool Draw(
        string id, NavTarget target, float distance, EncounterArt art, bool isCurrentDestination)
    {
        var scale = UiHelpers.Scale;
        var padding = PaddingDesign * scale;
        var iconSize = IconSizeDesign * scale;
        var height = Height;

        var width = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(width, height);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var hue = target.Kind == NavTargetKind.CriticalEncounter ? Theme.EncounterHue : Theme.FateHue;
        var accent = isCurrentDestination ? Theme.Accent : hue;

        DrawBackground(drawList, origin, size, accent, hovered, isCurrentDestination);

        var iconTexture = art.Icon(target);
        var iconCentre = new Vector2(origin.X + padding + (iconSize / 2f), origin.Y + (height / 2f));

        if (iconTexture != null)
        {
            var half = new Vector2(iconSize / 2f);
            drawList.AddImage(iconTexture.Handle, iconCentre - half, iconCentre + half);
        }
        else
        {
            drawList.AddCircleFilled(iconCentre, iconSize * 0.28f, ImGui.GetColorU32(hue), 24);
        }

        DrawText(
            drawList, target.Name, Detail(target), $"{distance:F0}y", target.Progress,
            origin, height, origin.X + size.X - padding, accent,
            target.IsEngageable ? accent : Theme.TextDim);

        return clicked;
    }

    /// The same card, for something that isn't an encounter - Return to Base Camp, which has no table behind
    /// it and no map icon of its own, so it takes a glyph instead.
    public static bool DrawAction(
        string id, string title, string detail, string trailing,
        FontAwesomeIcon glyph, IFontHandle iconFont, Vector4 accent, bool isCurrentDestination)
    {
        var scale = UiHelpers.Scale;
        var padding = PaddingDesign * scale;
        var iconSize = IconSizeDesign * scale;
        var height = Height;

        var width = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(width, height);
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        DrawBackground(drawList, origin, size, accent, hovered, isCurrentDestination);

        var iconCentre = new Vector2(origin.X + padding + (iconSize / 2f), origin.Y + (height / 2f));
        using (iconFont.PushSafe())
            UiHelpers.DrawScaledIcon(drawList, glyph, iconCentre, ImGui.GetColorU32(accent));

        DrawText(
            drawList, title, detail, trailing, 0, origin, height, origin.X + size.X - padding, accent,
            Theme.TextDim);

        return clicked;
    }

    private static void DrawBackground(
        ImDrawListPtr drawList, Vector2 origin, Vector2 size, Vector4 accent, bool hovered, bool current)
    {
        var scale = UiHelpers.Scale;

        var body = current ? Vector4.Lerp(Theme.Panel, accent, 0.14f)
            : hovered ? Vector4.Lerp(Theme.Panel, accent, 0.09f)
            : Theme.Panel;

        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(body), Theme.CardRounding);

        var stripe = new Vector2(origin.X + (3.5f * scale), origin.Y + (8f * scale));
        drawList.AddRectFilled(
            stripe, new Vector2(stripe.X + (3f * scale), origin.Y + size.Y - (8f * scale)),
            ImGui.GetColorU32(accent), 2f * scale);

        var edge = current ? 0.75f : hovered ? 0.45f : 0.22f;
        drawList.AddRect(
            origin, origin + size,
            ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, edge)),
            Theme.CardRounding, ImDrawFlags.None, (current ? 1.6f : 1.1f) * scale);
    }

    private static void DrawText(
        ImDrawListPtr drawList, string title, string detail, string trailing, byte progress,
        Vector2 origin, float height, float textRight, Vector4 accent, Vector4 detailColour)
    {
        var scale = UiHelpers.Scale;
        var padding = PaddingDesign * scale;
        var left = origin.X + padding + (IconSizeDesign * scale) + padding;

        var lineHeight = ImGui.GetTextLineHeight();

        var trailingWidth = trailing.Length > 0 ? ImGui.CalcTextSize(trailing).X : 0f;
        var nameLimit = textRight - trailingWidth - (12f * scale) - left;

        var nameTop = origin.Y + (height / 2f) - lineHeight - (2f * scale);
        drawList.AddText(new Vector2(left, nameTop), ImGui.GetColorU32(Theme.Text), Truncate(title, nameLimit));

        if (trailing.Length > 0)
        {
            drawList.AddText(
                new Vector2(textRight - trailingWidth, nameTop), ImGui.GetColorU32(Theme.TextDim), trailing);
        }

        var detailTop = origin.Y + (height / 2f) + (2f * scale);
        drawList.AddText(
            new Vector2(left, detailTop),
            ImGui.GetColorU32(detailColour),
            Truncate(detail, textRight - left));

        if (progress > 0)
        {
            var barLeft = left + ImGui.CalcTextSize(detail).X + (10f * scale);
            var barRight = textRight;
            if (barRight > barLeft + (24f * scale))
                DrawProgress(drawList, progress / 100f, barLeft, barRight, detailTop, accent);
        }
    }

    /// The second line: what it is, how it's doing, and how long is left.
    private static string Detail(NavTarget target)
    {
        var parts = new System.Collections.Generic.List<string> { target.TypeLabel };

        if (!string.IsNullOrEmpty(target.StateLabel))
            parts.Add(target.StateLabel);

        if (target.SecondsRemaining > 0)
        {
            var clock = $"{target.SecondsRemaining / 60}:{target.SecondsRemaining % 60:D2}";

            parts.Add(target.IsStaging ? $"starts in {clock}" : $"{clock} left");
        }

        if (target.MaxParticipants > 0)
            parts.Add($"{target.Participants}/{target.MaxParticipants}");

        return string.Join("  -  ", parts);
    }

    private static void DrawProgress(
        ImDrawListPtr drawList, float fraction, float left, float right, float top, Vector4 accent)
    {
        var barHeight = 5f * UiHelpers.Scale;
        var centre = top + (ImGui.GetTextLineHeight() / 2f) - (barHeight / 2f);
        var trackMin = new Vector2(left, centre);
        var trackMax = new Vector2(right, centre + barHeight);
        var rounding = barHeight / 2f;

        drawList.AddRectFilled(trackMin, trackMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.32f)), rounding);

        var clamped = Math.Clamp(fraction, 0f, 1f);
        if (clamped > 0.001f)
        {
            var fillMax = new Vector2(left + ((right - left) * clamped), trackMax.Y);
            drawList.AddRectFilled(trackMin, fillMax, ImGui.GetColorU32(accent), rounding);
        }
    }

    /// Shortens text to fit, ending in an ellipsis rather than being cut mid-glyph.
    private static string Truncate(string text, float maxWidth)
    {
        if (maxWidth <= 0f || ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        var trimmed = text;
        while (trimmed.Length > 1 && ImGui.CalcTextSize($"{trimmed}...").X > maxWidth)
            trimmed = trimmed[..^1];

        return $"{trimmed}...";
    }
}
