using System;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI;

/// A multiline text box that word-wraps, which ImGui's own does not - it scrolls sideways off the edge
/// instead, and there's no flag for it anywhere in ImGuiInputTextFlags.
public static class WrappedInput
{
    /// Room the wrap leaves for the frame padding and a scrollbar that may or may not be there - assumed
    /// present rather than measured, since it appears exactly when the text outgrows the box, which is
    /// exactly when wrapping starts mattering; a wrap computed against its absence puts the last word of each
    /// line underneath it.
    private static float Inset => (ImGui.GetStyle().FramePadding.X * 2) + ImGui.GetStyle().ScrollbarSize + 4f;

    /// Width the current callback is wrapping to - static because the callback runs inside the InputText call
    /// below and nothing else can be drawing at the same time (ImGui is single-threaded and this isn't
    /// re-entrant).
    private static float wrapWidth;

    /// ImGui's InputTextMultiline, wrapped to the width of the box.
    public static bool Multiline(string id, ref string text, int maxLength, Vector2 size)
    {
        wrapWidth = size.X - Inset;
        return ImGui.InputTextMultiline(id, ref text, maxLength, size, ImGuiInputTextFlags.CallbackEdit, OnEdit);
    }

    private static unsafe int OnEdit(ref ImGuiInputTextCallbackData data)
    {
        if (Rewrap(data.BufTextSpan, wrapWidth))
            data.BufDirty = 1;

        return 0;
    }

    /// Lays the buffer out to width, in place.
    private static bool Rewrap(Span<byte> buffer, float width)
    {
        if (width <= 0)
            return false;

        var space = ImGui.CalcTextSize(" ").X;
        var changed = false;
        var line = 0f;
        var start = 0;
        var separator = -1;
        for (var i = 0; i <= buffer.Length; i++)
        {
            var end = i == buffer.Length ? i : -1;
            if (end < 0)
            {
                if (buffer[i] is not ((byte)' ' or (byte)'\n'))
                    continue;

                end = i;
            }

            var wordWidth = Measure(buffer[start..end]);
            var fits = line <= 0 || line + space + wordWidth <= width;

            if (separator >= 0)
            {
                var hard = buffer[separator] == (byte)'\n' && fits;
                var wanted = hard || !fits ? (byte)'\n' : (byte)' ';

                if (buffer[separator] != wanted)
                {
                    buffer[separator] = wanted;
                    changed = true;
                }

                line = wanted == (byte)'\n' ? wordWidth : line + space + wordWidth;
            }
            else
            {
                line = wordWidth;
            }

            separator = i;
            start = i + 1;
        }

        return changed;
    }

    private static float Measure(ReadOnlySpan<byte> word) =>
        word.IsEmpty ? 0f : ImGui.CalcTextSize(Encoding.UTF8.GetString(word)).X;

    /// Same word-scan and the same hard/soft rule as Rewrap, but for a plain string instead of ImGui's live
    /// buffer, and building a new string rather than mutating in place - for laying out a value that's
    /// arriving from outside (a saved bio) rather than being typed, since InputText only wraps its own edits,
    /// not a value assigned into it.
    public static string Fold(string text, float width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
            return text;

        var bytes = Encoding.UTF8.GetBytes(text);
        var space = ImGui.CalcTextSize(" ").X;
        var line = 0f;
        var start = 0;
        var separator = -1;

        for (var i = 0; i <= bytes.Length; i++)
        {
            if (i < bytes.Length && bytes[i] is not ((byte)' ' or (byte)'\n'))
                continue;

            var wordWidth = Measure(bytes.AsSpan(start, i - start));
            var fits = line <= 0 || line + space + wordWidth <= width;

            if (separator >= 0)
            {
                var hard = bytes[separator] == (byte)'\n' && fits;
                var wanted = hard || !fits ? (byte)'\n' : (byte)' ';
                bytes[separator] = wanted;
                line = wanted == (byte)'\n' ? wordWidth : line + space + wordWidth;
            }
            else
            {
                line = wordWidth;
            }

            separator = i;
            start = i + 1;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// Takes the soft breaks back out, leaving the ones the player typed - a wrap is laid out for one box's
    /// width and is wrong anywhere else.
    public static string Unfold(string text, float width)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('\n'))
            return text;

        var bytes = Encoding.UTF8.GetBytes(text);
        var space = ImGui.CalcTextSize(" ").X;
        var line = 0f;
        var start = 0;
        var separator = -1;

        for (var i = 0; i <= bytes.Length; i++)
        {
            if (i < bytes.Length && bytes[i] is not ((byte)' ' or (byte)'\n'))
                continue;

            var wordWidth = Measure(bytes.AsSpan(start, i - start));
            var fits = line <= 0 || line + space + wordWidth <= width;

            if (separator >= 0)
            {
                var hard = bytes[separator] == (byte)'\n' && fits;
                if (!hard && bytes[separator] == (byte)'\n')
                    bytes[separator] = (byte)' ';

                line = hard ? wordWidth : line + space + wordWidth;
            }
            else
            {
                line = wordWidth;
            }

            separator = i;
            start = i + 1;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// The width Fold/Unfold need, for a box drawn at size.
    public static float WidthFor(Vector2 size) => size.X - Inset;
}
