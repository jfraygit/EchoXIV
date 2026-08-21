using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EchoNav.UI;

/// Shared drawing odds and ends, ported from EchoMix and EchoSim so the three plugins animate and render the
/// same way.
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


    private const byte Space = 0x20;
    private const byte Newline = 0x0A;

    /// Wrap width for the box being drawn, in pixels.
    private static float inputWrapWidth;

    /// Held rather than built per call, so handing it to ImGui doesn't allocate a delegate on every frame the
    /// popup is open.
    private static readonly ImGui.ImGuiInputTextCallbackDelegate WrapCallback = OnInputEdit;

    /// What one box needs remembered between keystrokes so that a line break the user typed can be told apart
    /// from one this put in.
    private sealed class WrapState
    {
        /// The buffer exactly as this last left it.
        public byte[] Previous = [];

        public int PreviousLength;

        /// Byte offsets of the breaks the user typed.
        public readonly List<int> HardBreaks = [];

        /// The text with this pass's own breaks taken back out and the user's left in.
        public string Logical = string.Empty;
    }

    private static readonly Dictionary<string, WrapState> WrapStates = [];

    /// The box currently being drawn.
    private static WrapState? activeWrapState;

    /// A multiline text box whose text wraps at the edge.
    public static bool WrappingInput(string id, ref string text, int maxLength, Vector2 size)
    {
        if (!WrapStates.TryGetValue(id, out var state))
            WrapStates[id] = state = new WrapState();

        activeWrapState = state;

        var style = ImGui.GetStyle();

        inputWrapWidth = size.X - (style.FramePadding.X * 2f) - style.ScrollbarSize;

        return ImGui.InputTextMultiline(
            id, ref text, maxLength, size, ImGuiInputTextFlags.CallbackEdit, WrapCallback);
    }

    /// The text as it was typed: this pass's own line breaks taken out, the user's kept.
    public static string Unwrapped(string id, string wrapped) =>
        WrapStates.TryGetValue(id, out var state) && state.Logical.Length > 0 ? state.Logical : wrapped;

    private static int OnInputEdit(ref ImGuiInputTextCallbackData data)
    {
        if (activeWrapState is not { } state)
            return 0;

        if (Rewrap(data.BufTextSpan, inputWrapWidth, state))
            data.BufDirty = 1;

        return 0;
    }

    /// Rewraps ImGui's live buffer in place.
    private static bool Rewrap(Span<byte> buffer, float maxWidth, WrapState state)
    {
        if (maxWidth <= 0f)
            return false;

        var original = ArrayPool<byte>.Shared.Rent(Math.Max(1, buffer.Length));

        try
        {
            buffer.CopyTo(original);

            TrackTypedBreaks(buffer, state);
            FoldOwnBreaks(buffer, state.HardBreaks);

            state.Logical = Encoding.UTF8.GetString(buffer);

            InsertBreaks(buffer, maxWidth, state.HardBreaks);
            Remember(buffer, state);

            return !buffer.SequenceEqual(original.AsSpan(0, buffer.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(original);
        }
    }

    /// Carries the user's own line breaks across an edit, and spots a newly typed one.
    private static void TrackTypedBreaks(ReadOnlySpan<byte> current, WrapState state)
    {
        var previous = state.Previous.AsSpan(0, state.PreviousLength);

        var prefix = 0;
        while (prefix < previous.Length && prefix < current.Length && previous[prefix] == current[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < previous.Length - prefix
               && suffix < current.Length - prefix
               && previous[previous.Length - 1 - suffix] == current[current.Length - 1 - suffix])
            suffix++;

        var delta = current.Length - previous.Length;
        var tailStart = previous.Length - suffix;

        for (var i = state.HardBreaks.Count - 1; i >= 0; i--)
        {
            var at = state.HardBreaks[i];

            if (at < prefix)
                continue;

            if (at >= tailStart)
                state.HardBreaks[i] = at + delta;
            else
                state.HardBreaks.RemoveAt(i);
        }

        for (var i = prefix; i < current.Length - suffix; i++)
        {
            if (current[i] == Newline)
                state.HardBreaks.Add(i);
        }

        state.HardBreaks.Sort();
    }

    /// Turns this pass's own breaks back into the spaces they were made from, leaving the user's alone.
    private static void FoldOwnBreaks(Span<byte> buffer, List<int> hardBreaks)
    {
        var next = 0;

        for (var i = 0; i < buffer.Length; i++)
        {
            if (next < hardBreaks.Count && hardBreaks[next] == i)
            {
                next++;
                continue;
            }

            if (buffer[i] == Newline)
                buffer[i] = Space;
        }
    }

    /// Walks the text and swaps a space for a newline wherever the line would otherwise run past the edge.
    private static void InsertBreaks(Span<byte> buffer, float maxWidth, List<int> hardBreaks)
    {
        var font = ImGui.GetFont();
        var fontScale = font.FontSize > 0f ? ImGui.GetFontSize() / font.FontSize : 1f;

        var lineWidth = 0f;
        var lastSpace = -1;
        var index = 0;
        var nextHard = 0;

        while (index < buffer.Length)
        {
            if (nextHard < hardBreaks.Count && hardBreaks[nextHard] == index)
            {
                nextHard++;
                index++;
                lineWidth = 0f;
                lastSpace = -1;
                continue;
            }

            var start = index;
            var codepoint = NextCodepoint(buffer, ref index);

            if (buffer[start] == Space)
                lastSpace = start;

            lineWidth += Advance(font, codepoint) * fontScale;

            if (lineWidth <= maxWidth || lastSpace < 0 || lastSpace == start)
                continue;

            buffer[lastSpace] = Newline;

            lineWidth = 0f;
            for (var i = lastSpace + 1; i < index;)
                lineWidth += Advance(font, NextCodepoint(buffer, ref i)) * fontScale;

            lastSpace = -1;
        }
    }

    /// Keeps the finished buffer, which is what the next edit gets located against.
    private static void Remember(ReadOnlySpan<byte> buffer, WrapState state)
    {
        if (state.Previous.Length < buffer.Length)
            state.Previous = new byte[Math.Max(buffer.Length, 256)];

        buffer.CopyTo(state.Previous);
        state.PreviousLength = buffer.Length;
    }

    /// Reads one UTF-8 character and advances past it.
    private static int NextCodepoint(ReadOnlySpan<byte> buffer, ref int index)
    {
        var first = buffer[index];

        if (first < 0x80)
        {
            index++;
            return first;
        }

        int length, codepoint;

        if ((first & 0xE0) == 0xC0) { length = 2; codepoint = first & 0x1F; }
        else if ((first & 0xF0) == 0xE0) { length = 3; codepoint = first & 0x0F; }
        else if ((first & 0xF8) == 0xF0) { length = 4; codepoint = first & 0x07; }
        else { index++; return 0xFFFD; }

        if (index + length > buffer.Length)
        {
            index = buffer.Length;
            return 0xFFFD;
        }

        for (var i = 1; i < length; i++)
            codepoint = (codepoint << 6) | (buffer[index + i] & 0x3F);

        index += length;
        return codepoint;
    }

    /// How wide one character is in the current font, before the font's own scaling.
    private static float Advance(ImFontPtr font, int codepoint)
    {
        if (codepoint > char.MaxValue)
            return font.FontSize;

        var glyph = ImGui.FindGlyph(font, (char)codepoint);
        return glyph.IsNull ? font.FontSize * 0.5f : glyph.AdvanceX;
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

    /// The plugin's name set as a wordmark: tracked capitals in the accent, with a shadow and an underline.
    public static float DrawWordmark(
        string text, float startX, float rowTop, float availableWidth, float reservedRight, out float leftScreenX)
    {
        var scale = Scale;
        var letterSpacing = 6f * scale;

        var drawList = ImGui.GetWindowDrawList();
        var fontSize = ImGui.GetFontSize();

        Span<float> widths = stackalloc float[text.Length];
        var totalWidth = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            widths[i] = ImGui.CalcTextSize(text[i].ToString()).X;
            totalWidth += widths[i];
        }

        totalWidth += letterSpacing * (text.Length - 1);

        var ideal = MathF.Max(0f, (availableWidth - totalWidth) / 2f);
        var limit = MathF.Max(0f, availableWidth - reservedRight - totalWidth);
        ImGui.SetCursorPos(new Vector2(startX + MathF.Min(ideal, limit), rowTop));

        var origin = ImGui.GetCursorScreenPos();
        var x = origin.X;
        leftScreenX = origin.X;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i].ToString();
            drawList.AddText(
                new Vector2(x, origin.Y + (2f * scale)), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)), ch);
            drawList.AddText(new Vector2(x, origin.Y), ImGui.GetColorU32(Theme.Accent), ch);
            x += widths[i] + letterSpacing;
        }

        var underlineY = origin.Y + fontSize + (3f * scale);
        drawList.AddLine(
            new Vector2(origin.X, underlineY),
            new Vector2(origin.X + totalWidth, underlineY),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.7f)),
            2f * scale);

        return fontSize + (7f * scale);
    }
}
