using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EchoRoleplay.UI;

/// Shared drawing odds and ends, ported from EchoMix, EchoSim, EchoNav and EchoGlam so the suite animates and
/// renders the same way.
public static class UiHelpers
{
    /// Frame-rate independent exponential ease toward a target value.
    public static float Lerp(float current, float target, float speed, float deltaTime)
    {
        var t = 1f - MathF.Exp(-speed * deltaTime);
        return current + ((target - current) * t);
    }

    /// Icons read as cramped at the font's natural size; this gives them breathing room.
    public const float IconDrawScale = 0.82f;

    /// Draws one icon glyph centred on its actual ink bounds rather than the advance-width box.
    public static void DrawScaledIcon(ImDrawListPtr drawList, FontAwesomeIcon icon, Vector2 center, uint color)
    {
        var glyph = icon.ToIconString();
        var font = ImGui.GetFont();
        var drawFontSize = ImGui.GetFontSize() * IconDrawScale;

        var glyphInfo = ImGui.FindGlyph(font, glyph[0]);
        if (!glyphInfo.IsNull && font.FontSize > 0f)
        {
            var inkScale = drawFontSize / font.FontSize;
            var inkMin = new Vector2(glyphInfo.X0, glyphInfo.Y0) * inkScale;
            var inkMax = new Vector2(glyphInfo.X1, glyphInfo.Y1) * inkScale;
            drawList.AddText(font, drawFontSize, center - ((inkMin + inkMax) / 2f), color, glyph, 0f);
            return;
        }

        var scaledSize = ImGui.CalcTextSize(glyph) * IconDrawScale;
        drawList.AddText(font, drawFontSize, center - (scaledSize / 2f), color, glyph, 0f);
    }

    /// Draws one icon glyph at an explicit pixel size, centred on its ink bounds.
    public static void DrawIconSized(
        ImDrawListPtr drawList, FontAwesomeIcon icon, Vector2 center, float fontSize, uint color)
    {
        var glyph = icon.ToIconString();
        var font = ImGui.GetFont();
        var current = ImGui.GetFontSize();

        if (fontSize <= 0f || current <= 0f)
            return;

        var glyphInfo = ImGui.FindGlyph(font, glyph[0]);

        if (!glyphInfo.IsNull && glyphInfo.AdvanceX > 0f)
        {
            var advancePixels = ImGui.CalcTextSize(glyph).X;
            var unitsToPixels = advancePixels / glyphInfo.AdvanceX;
            var inkScale = unitsToPixels * (fontSize / current);

            var inkMin = new Vector2(glyphInfo.X0, glyphInfo.Y0) * inkScale;
            var inkMax = new Vector2(glyphInfo.X1, glyphInfo.Y1) * inkScale;
            drawList.AddText(font, fontSize, center - ((inkMin + inkMax) / 2f), color, glyph, 0f);
            return;
        }

        var measured = ImGui.CalcTextSize(glyph) * (fontSize / current);
        drawList.AddText(font, fontSize, center - (measured / 2f), color, glyph, 0f);
    }


    /// How far below a line of text's top edge its capitals are visually centred.
    public static float TextInkCentre()
    {
        var font = ImGui.GetFont();
        var glyph = ImGui.FindGlyph(font, 'X');

        if (glyph.IsNull || font.FontSize <= 0f)
            return ImGui.GetTextLineHeight() / 2f;

        return (glyph.Y0 + glyph.Y1) / 2f * (ImGui.GetFontSize() / font.FontSize);
    }

    /// The size Dalamud's default font is at 100%.
    private const float BaseFontSize = 17f;

    /// How much bigger everything drawn by hand has to be for the user's UI scale.
    public static float Scale { get; private set; } = 1f;

    /// Reads the scale for this frame.
    public static void SampleScale()
    {
        var fontSize = ImGui.GetFontSize();
        Scale = fontSize > 0f ? Math.Clamp(fontSize / BaseFontSize, 1f, 3f) : 1f;
    }

    /// A multiline text box that word-wraps and still lets Enter make a break.
    public static bool WrappingMultiline(string id, ref string text, int maxLength, Vector2 size)
    {
        wrapWidth = WrapWidthFor(size.X);

        return ImGui.InputTextMultiline(
            id, ref text, maxLength, size, ImGuiInputTextFlags.CallbackEdit, WrapCallback);
    }

    /// Wraps text ready to be shown in a box of this width.
    public static string Wrap(string text, float boxWidth) =>
        TextWrap.Wrap(text, WrapWidthFor(boxWidth), MeasureChar);

    /// Takes this box's soft breaks back out, for saving or sending.
    public static string Unwrap(string text, float width) =>
        TextWrap.Unwrap(text, WrapWidthFor(width), MeasureChar);

    /// How much room the text really has inside a box of this width.
    private static float WrapWidthFor(float boxWidth) =>
        boxWidth - (ImGui.GetStyle().FramePadding.X * 2f) - (6f * Scale);

    private static float wrapWidth;

    private static float MeasureChar(char c) => ImGui.CalcTextSize(c.ToString()).X;

    private static int WrapCallback(ref ImGuiInputTextCallbackData data)
    {
        var span = data.BufTextSpan;
        if (span.Length == 0 || wrapWidth <= 0f)
            return 0;

        var text = System.Text.Encoding.UTF8.GetString(span);
        var wrapped = TextWrap.Wrap(text, wrapWidth, MeasureChar);

        if (string.Equals(text, wrapped, StringComparison.Ordinal))
            return 0;

        var bytes = System.Text.Encoding.UTF8.GetBytes(wrapped);
        if (bytes.Length != span.Length)
            return 0;

        bytes.CopyTo(span);

        data.BufDirty = 1;
        return 0;
    }

    /// Shortens text to fit a width, ending in an ellipsis rather than being cut off mid-glyph at the window
    /// edge.
    public static string Truncate(string text, float maxWidth)
    {
        if (maxWidth <= 0f || ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        var trimmed = text;
        while (trimmed.Length > 1 && ImGui.CalcTextSize($"{trimmed}...").X > maxWidth)
            trimmed = trimmed[..^1];

        return $"{trimmed}...";
    }

    /// A tooltip that wraps instead of running off the screen edge.
    public static void WrappedTooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    /// Letter spacing for the wordmark.
    public static float WordmarkTracking => 6f * Scale;

    /// How much room the wordmark needs, without drawing it.
    public static Vector2 MeasureWordmark(string text)
    {
        var tracking = WordmarkTracking;
        var width = 0f;

        foreach (var character in text)
            width += ImGui.CalcTextSize(character.ToString()).X + tracking;

        width = MathF.Max(0f, width - tracking);

        return new Vector2(width, ImGui.GetFontSize() + (7f * Scale));
    }

    /// The plugin's name set as a wordmark: tracked capitals in the accent, with a shadow and an underline.
    public static void DrawWordmark(string text, Vector2 screenPos)
    {
        var scale = Scale;
        var tracking = WordmarkTracking;

        var drawList = ImGui.GetWindowDrawList();
        var fontSize = ImGui.GetFontSize();

        var x = screenPos.X;

        foreach (var character in text)
        {
            var glyph = character.ToString();
            drawList.AddText(
                new Vector2(x, screenPos.Y + (2f * scale)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)), glyph);
            drawList.AddText(new Vector2(x, screenPos.Y), ImGui.GetColorU32(Theme.Accent), glyph);
            x += ImGui.CalcTextSize(glyph).X + tracking;
        }

        var underlineY = screenPos.Y + fontSize + (3f * scale);
        drawList.AddLine(
            new Vector2(screenPos.X, underlineY),
            new Vector2(MathF.Max(screenPos.X, x - tracking), underlineY),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.7f)),
            2f * scale);
    }
}
