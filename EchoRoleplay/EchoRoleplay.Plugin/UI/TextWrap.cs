using System;

namespace EchoRoleplay.UI;

/// Folding a paragraph to a width by swapping spaces for newlines, and taking it back out.
public static class TextWrap
{
    /// Folds text to a width, preserving breaks the player typed.
    public static string Wrap(string text, float maxWidth, Func<char, float> measure)
    {
        if (text.Length == 0 || maxWidth <= 0f)
            return text;

        var chars = text.ToCharArray();
        var lineWidth = 0f;
        var lastSpace = -1;

        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '\n')
            {
                if (WasTyped(chars, i, lineWidth, maxWidth, measure))
                {
                    lineWidth = 0f;
                    lastSpace = -1;
                    continue;
                }

                chars[i] = ' ';
            }

            lineWidth += measure(chars[i]);

            if (chars[i] == ' ')
                lastSpace = i;

            if (lineWidth <= maxWidth || lastSpace < 0)
                continue;

            chars[lastSpace] = '\n';

            lineWidth = 0f;
            for (var j = lastSpace + 1; j <= i; j++)
                lineWidth += measure(chars[j]);

            lastSpace = -1;
        }

        return new string(chars);
    }

    /// Takes out the breaks this inserted, leaving the ones the player typed.
    public static string Unwrap(string text, float maxWidth, Func<char, float> measure)
    {
        if (text.Length == 0 || maxWidth <= 0f)
            return text;

        var chars = text.ToCharArray();
        var lineWidth = 0f;

        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '\n')
            {
                if (WasTyped(chars, i, lineWidth, maxWidth, measure))
                {
                    lineWidth = 0f;
                    continue;
                }

                chars[i] = ' ';
            }

            lineWidth += measure(chars[i]);
        }

        return new string(chars);
    }

    /// Whether the newline at index is one the player typed.
    private static bool WasTyped(char[] chars, int index, float lineWidth, float maxWidth, Func<char, float> measure)
    {
        var wordWidth = 0f;

        for (var j = index + 1; j < chars.Length; j++)
        {
            if (chars[j] is ' ' or '\n')
                break;

            wordWidth += measure(chars[j]);
        }

        if (wordWidth <= 0f)
            return true;

        return lineWidth + measure(' ') + wordWidth <= maxWidth;
    }
}
