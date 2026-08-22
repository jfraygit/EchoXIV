using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EchoGlam.UI;

/// Shared drawing odds and ends, ported from EchoMix, EchoSim and EchoNav so the suite animates and renders
/// the same way.
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

    /// A multiline text box that word-wraps, which ImGui's own does not.
    public static bool WrappingInputTextMultiline(
        string id, ref string text, int maxLength, Vector2 size, float wrapWidth)
    {
        wrapTarget = wrapWidth;

        return ImGui.InputTextMultiline(
            id, ref text, maxLength, size, ImGuiInputTextFlags.CallbackEdit, WrapCallback);
    }

    private static float wrapTarget;

    private static unsafe int WrapCallback(ref ImGuiInputTextCallbackData data)
    {
        var span = data.BufTextSpan;
        if (span.Length == 0 || wrapTarget <= 0f)
            return 0;

        var current = System.Text.Encoding.UTF8.GetString(span);
        var wrapped = Rewrap(current, wrapTarget);

        if (wrapped == current)
            return 0;

        var bytes = System.Text.Encoding.UTF8.GetBytes(wrapped);

        if (bytes.Length != span.Length)
            return 0;

        bytes.CopyTo(span);

        data.BufDirty = 1;

        return 0;
    }

    /// Removes the line breaks the wrapper itself put in, and leaves the ones somebody typed.
    public static string Unwrap(string text, float maxWidth)
    {
        if (text.Length == 0 || maxWidth <= 0f)
            return text;

        var chars = text.ToCharArray();
        var lineWidth = 0f;

        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] != '\n')
            {
                lineWidth += Advance(chars[i]);
                continue;
            }

            var wordWidth = 0f;
            for (var j = i + 1; j < chars.Length && chars[j] != ' ' && chars[j] != '\n'; j++)
                wordWidth += Advance(chars[j]);

            if (lineWidth + Advance(' ') + wordWidth <= maxWidth)
            {
                lineWidth = 0f;
                continue;
            }

            chars[i] = ' ';
            lineWidth += Advance(' ');
        }

        return new string(chars);
    }

    /// Lays text out to a width, keeping any breaks already in it.
    public static string Wrap(string text, float maxWidth)
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
                lineWidth = 0f;
                lastSpace = -1;
                continue;
            }

            lineWidth += Advance(chars[i]);

            if (chars[i] == ' ')
                lastSpace = i;

            if (lineWidth <= maxWidth || lastSpace < 0)
                continue;

            chars[lastSpace] = '\n';

            lineWidth = 0f;
            for (var j = lastSpace + 1; j <= i; j++)
                lineWidth += Advance(chars[j]);

            lastSpace = -1;
        }

        return new string(chars);
    }

    /// Unwrap then wrap: the whole round trip, idempotent at a given width and correct after the box is
    /// resized.
    public static string Rewrap(string text, float maxWidth) => Wrap(Unwrap(text, maxWidth), maxWidth);

    /// One character's advance.
    private static float Advance(char c) => ImGui.CalcTextSize(c.ToString()).X;

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

    /// A small colour chip that stays legible whatever colour it is.
    public static bool SearchField(
        string id, string hint, ref string text, float width, float height,
        Dalamud.Interface.ManagedFontAtlas.IFontHandle iconFont)
    {
        var scale = Scale;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var iconRoom = 30f * scale;
        var clearRoom = string.IsNullOrEmpty(text) ? 8f * scale : 26f * scale;
        var padY = MathF.Max(0f, (height - ImGui.GetTextLineHeight()) / 2f);

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, height / 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(iconRoom, padY));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.2f * scale);

        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Panel);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Lerp(Theme.Panel, Theme.Accent, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Lerp(Theme.Panel, Theme.Accent, 0.14f));
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Border);

        ImGui.SetNextItemWidth(width);
        var changed = ImGui.InputTextWithHint(id, hint, ref text, 60);
        var active = ImGui.IsItemActive();

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(3);

        if (active)
        {
            drawList.AddRect(
                origin, origin + new Vector2(width, height),
                ImGui.GetColorU32(Theme.Accent with { W = 0.8f }), height / 2f, ImDrawFlags.None, 1.4f * scale);
        }

        using (iconFont.PushSafe())
        {
            DrawScaledIcon(
                drawList, FontAwesomeIcon.Search,
                new Vector2(origin.X + (iconRoom / 2f) + (2f * scale), origin.Y + (height / 2f)),
                ImGui.GetColorU32(active ? Theme.Accent : Theme.TextDisabled));
        }

        if (string.IsNullOrEmpty(text))
            return changed;

        var clearCentre = new Vector2(origin.X + width - (clearRoom / 2f) - (4f * scale), origin.Y + (height / 2f));
        var clearRadius = 9f * scale;
        var overClear = ImGui.IsMouseHoveringRect(
            clearCentre - new Vector2(clearRadius, clearRadius),
            clearCentre + new Vector2(clearRadius, clearRadius));

        using (iconFont.PushSafe())
        {
            DrawScaledIcon(
                drawList, FontAwesomeIcon.TimesCircle, clearCentre,
                ImGui.GetColorU32(overClear ? Theme.Text : Theme.TextDisabled));
        }

        if (overClear && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            text = string.Empty;
            return true;
        }

        if (overClear)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return changed;
    }

    public static void DrawSwatch(ImDrawListPtr drawList, Vector2 min, float size, Vector4 colour, Vector4? ring = null)
    {
        var scale = Scale;
        var max = min + new Vector2(size, size);
        var rounding = size * 0.3f;

        var inset = MathF.Min(MathF.Max(1.2f, size * 0.10f), 2.5f * scale);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)), rounding);

        drawList.AddRectFilled(
            min + new Vector2(inset, inset), max - new Vector2(inset, inset),
            ImGui.GetColorU32(colour), rounding * 0.6f);

        drawList.AddRect(
            min, max, ImGui.GetColorU32(ring ?? new Vector4(1f, 1f, 1f, 0.55f)),
            rounding, ImDrawFlags.None, (ring is null ? 1.2f : 1.8f) * scale);
    }

    /// The vote and favourite counts on a glamour card, right-aligned in the row.
    public static float DrawGlamourCounts(
        ImDrawListPtr drawList, Dalamud.Interface.ManagedFontAtlas.IFontHandle iconFont,
        Vector2 origin, float width, int votes, bool voted, int favourites, bool favourited)
    {
        var lineHeight = ImGui.GetTextLineHeight();
        var centre = origin.Y + (lineHeight / 2f);

        var voteText = votes.ToString();
        var favouriteText = favourites.ToString();

        var voteWidth = ImGui.CalcTextSize(voteText).X;
        var favouriteWidth = ImGui.CalcTextSize(favouriteText).X;

        var markWidth = lineHeight * 0.9f;
        var markGap = 4f * Scale;
        var pairGap = 10f * Scale;

        var used = markWidth + markGap + voteWidth + pairGap + markWidth + markGap + favouriteWidth;
        var x = origin.X + width - used;

        using (iconFont.PushSafe())
        {
            DrawScaledIcon(
                drawList, Dalamud.Interface.FontAwesomeIcon.Heart,
                new Vector2(x + (markWidth / 2f), centre),
                ImGui.GetColorU32(voted ? Theme.Vote : Theme.TextDisabled));
        }

        drawList.AddText(new Vector2(x + markWidth + markGap, origin.Y), ImGui.GetColorU32(Theme.TextDim), voteText);

        var favouriteX = x + markWidth + markGap + voteWidth + pairGap;

        using (iconFont.PushSafe())
        {
            DrawScaledIcon(
                drawList, Dalamud.Interface.FontAwesomeIcon.Star,
                new Vector2(favouriteX + (markWidth / 2f), centre),
                ImGui.GetColorU32(favourited ? Theme.Favourite : Theme.TextDisabled));
        }

        drawList.AddText(
            new Vector2(favouriteX + markWidth + markGap, origin.Y),
            ImGui.GetColorU32(Theme.TextDim), favouriteText);

        return used;
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
