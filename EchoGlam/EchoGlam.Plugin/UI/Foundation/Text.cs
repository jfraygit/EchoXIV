using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// The type scale, and the measuring that goes with it.
public static class Text
{
    /// One big thing per screen - a detail title, the intro wordmark.
    public const float DisplayRatio = 1.65f;

    /// Pane titles, card headings, the wordmark.
    public const float TitleRatio = 1.3125f;

    /// Metadata, stat labels, pill text, timestamps, tracked small capitals.
    public const float CaptionRatio = 0.8125f;

    private static Fonts? fonts;

    /// Hands the type scale its font handles.
    internal static void Use(Fonts handles) => fonts = handles;

    /// The user's body font size, sampled once per frame.
    public static float BodySize { get; private set; } = 16f;

    /// Reads the body size for this frame.
    public static void Sample()
    {
        var size = ImGui.GetFontSize();
        if (size > 0f)
            BodySize = size;
    }

    public static float DisplaySize => BodySize * DisplayRatio;
    public static float TitleSize => BodySize * TitleRatio;
    public static float CaptionSize => BodySize * CaptionRatio;

    /// Pushes the title font, or nothing if the atlas is mid-rebuild.
    public static IDisposable? PushDisplay() => fonts?.Display.PushSafe();

    public static IDisposable? PushTitle() => fonts?.Header.PushSafe();

    public static IDisposable? PushCaption() => fonts?.Caption.PushSafe();

    public static IDisposable? PushIcon() => fonts?.Icon.PushSafe();


    /// Widths, cached for the current font size.
    private static readonly Dictionary<string, float> Widths = new(StringComparer.Ordinal);

    private static float cachedAt = -1f;

    /// Capped because the item picker measures item names, and there are tens of thousands of those.
    private const int CacheCap = 4096;

    public static float Width(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        var size = ImGui.GetFontSize();
        if (size != cachedAt)
        {
            Widths.Clear();
            cachedAt = size;
        }

        if (Widths.TryGetValue(text, out var cached))
            return cached;

        if (Widths.Count >= CacheCap)
            Widths.Clear();

        var width = ImGui.CalcTextSize(text).X;
        Widths[text] = width;
        return width;
    }

    /// Clears the cache.
    public static void Invalidate()
    {
        Widths.Clear();
        Clamps.Clear();
        cachedAt = -1f;
    }

    /// Shortens text to fit, ending in an ellipsis.
    public static string Truncate(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f || Width(text) <= maxWidth)
            return text;

        const string ellipsis = "...";
        var room = maxWidth - Width(ellipsis);
        if (room <= 0f)
            return ellipsis;

        var low = 0;
        var high = text.Length - 1;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X <= room)
                low = mid;
            else
                high = mid - 1;
        }

        if (low > 0 && char.IsHighSurrogate(text[low - 1]))
            low--;

        return low <= 0 ? ellipsis : string.Concat(text.AsSpan(0, low), ellipsis);
    }

    /// Clamps wrapped text to a number of lines, ending in an ellipsis on a word boundary.
    public static string ClampLines(string text, float width, int lines, out bool cut)
    {
        if (string.IsNullOrEmpty(text) || width <= 0f || lines <= 0)
        {
            cut = lines <= 0 && !string.IsNullOrEmpty(text);
            return lines <= 0 ? string.Empty : text;
        }

        var key = (text, width, lines);

        if (!Clamps.TryGetValue(key, out var found))
        {
            if (Clamps.Count >= ClampCap)
                Clamps.Clear();

            found = Clamp(text, width, lines);
            Clamps[key] = found;
        }

        cut = found.Cut;
        return found.Text;
    }

    /// Far smaller than the width cache: these are paragraphs, and a screen holds a handful.
    private const int ClampCap = 64;

    private static readonly Dictionary<(string Text, float Width, int Lines), (string Text, bool Cut)> Clamps = [];

    private static (string Text, bool Cut) Clamp(string text, float width, int lines)
    {
        var budget = ImGui.GetTextLineHeight() * (lines + 0.5f);

        if (ImGui.CalcTextSize(text, false, width).Y <= budget)
            return (text, false);

        const string ellipsis = "...";

        var low = 0;
        var high = text.Length;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;

            if (ImGui.CalcTextSize(text[..mid] + ellipsis, false, width).Y <= budget)
                low = mid;
            else
                high = mid - 1;
        }

        var space = text.LastIndexOf(' ', Math.Max(0, low - 1));

        if (space > low - 24 && space > 0)
            low = space;

        return (text[..low].TrimEnd() + ellipsis, true);
    }


    /// Draws text with letter spacing, a glyph at a time.
    public static float Tracked(
        ImDrawListPtr drawList, Vector2 pos, string text, uint colour, float trackingDesign,
        float maxWidth = 0f)
    {
        if (maxWidth > 0f)
            text = TruncateTracked(text, maxWidth, trackingDesign);

        var tracking = trackingDesign * UiHelpers.Scale;
        var x = pos.X;

        foreach (var character in text)
        {
            var glyph = character.ToString();
            drawList.AddText(new Vector2(MathF.Round(x), MathF.Round(pos.Y)), colour, glyph);
            x += Width(glyph) + tracking;
        }

        return MathF.Max(0f, x - pos.X - tracking);
    }


    /// Shortens tracked text to fit, measuring WITH the tracking.
    public static string TruncateTracked(string text, float maxWidth, float trackingDesign)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            return text;

        if (TrackedWidth(text, trackingDesign) <= maxWidth)
            return text;

        const string ellipsis = "...";
        var room = maxWidth - TrackedWidth(ellipsis, trackingDesign);

        if (room <= 0f)
            return string.Empty;

        var low = 0;
        var high = text.Length - 1;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (TrackedWidth(text[..mid], trackingDesign) <= room)
                low = mid;
            else
                high = mid - 1;
        }

        if (low > 0 && char.IsHighSurrogate(text[low - 1]))
            low--;

        return low <= 0 ? ellipsis : string.Concat(text.AsSpan(0, low), ellipsis);
    }

    /// What Tracked will take up, without drawing it.
    public static float TrackedWidth(string text, float trackingDesign)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        var tracking = trackingDesign * UiHelpers.Scale;
        var total = 0f;

        foreach (var character in text)
            total += Width(character.ToString()) + tracking;

        return MathF.Max(0f, total - tracking);
    }
}
