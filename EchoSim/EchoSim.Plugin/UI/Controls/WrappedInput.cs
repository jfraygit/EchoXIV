using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;

namespace EchoSim.UI.Controls;

/// A multi-line text box that word-wraps.
public static class WrappedInput
{
    /// Room the wrap leaves for the frame padding and a scrollbar that may or may not be there.
    private static float Inset => (ImGui.GetStyle().FramePadding.X * 2) + ImGui.GetStyle().ScrollbarSize + 4f;

    /// Width the current callback is wrapping to.
    private static float wrapWidth;

    /// ImGui's InputTextMultiline, wrapped to the width of the box.
    public static bool Multiline(string id, ref string text, int maxLength, Vector2 size)
    {
        wrapWidth = size.X - Inset;

        return ImGui.InputTextMultiline(
            id, ref text, maxLength, size, ImGuiInputTextFlags.CallbackEdit, OnEdit);
    }

    /// BufDirty IS NOT OPTIONAL.
    private static int OnEdit(ref ImGuiInputTextCallbackData data)
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

    /// Rendered width of one word, decoded from the buffer's own bytes.
    private static float Measure(ReadOnlySpan<byte> word)
        => word.IsEmpty ? 0f : ImGui.CalcTextSize(Encoding.UTF8.GetString(word)).X;

    /// Takes the soft breaks back out, leaving the ones the player typed.
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

    /// The width Unfold needs, for a box that was drawn at size.
    public static float WidthFor(Vector2 size) => size.X - Inset;
}
