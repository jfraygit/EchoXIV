using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EchoSim.UI;

public static class UiHelpers
{
    /// The body font size every pixel constant in this UI was written against.
    public const float DesignFontSize = 17f;

    private static float scale = 1f;

    /// How much bigger the player's text is than the layout assumes, and therefore how much bigger every
    /// hand-placed pixel in this UI needs to be.
    public static float Scale => scale;

    /// Samples Scale for this frame.
    public static void SampleScale()
    {
        try
        {
            var size = ImGui.GetFontSize();
            scale = size > 0f ? Math.Clamp(size / DesignFontSize, 1f, 3f) : 1f;
        }
        catch
        {
            scale = 1f;
        }
    }

    /// A design-unit length in real pixels.
    public static float S(float px) => px * scale;

    /// A design-unit size in real pixels.
    public static Vector2 S(Vector2 size) => size * scale;

    /// Frame-rate independent exponential ease toward a target value.
    public static float Lerp(float current, float target, float speed, float deltaTime)
    {
        var t = 1f - MathF.Exp(-speed * deltaTime);
        return current + ((target - current) * t);
    }

    /// Fast out of the gate, settling gently onto the final value.
    public static float EaseOutCubic(float t)
    {
        var clamped = Math.Clamp(t, 0f, 1f);
        var inverse = 1f - clamped;
        return 1f - (inverse * inverse * inverse);
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

    /// Cancels Dalamud's automatic multiplication of Window.Size by Global Font Scale, so the on-screen size
    /// matches what was requested.
    public static float WindowSizeCompensation
    {
        get
        {
            try
            {
                var globalScale = ImGui.GetIO().FontGlobalScale;
                return globalScale > 0f ? 1f / globalScale : 1f;
            }
            catch
            {
                return 1f;
            }
        }
    }

    /// A triangle with rounded corners.
    public static void AddRoundedTriangle(
        ImDrawListPtr drawList, Vector2 a, Vector2 b, Vector2 c, float rounding, uint colour)
    {
        var points = new[] { a, b, c };

        for (var i = 0; i < 3; i++)
        {
            var current = points[i];
            var previous = points[(i + 2) % 3];
            var next = points[(i + 1) % 3];

            var toPrevious = Vector2.Normalize(previous - current);
            var toNext = Vector2.Normalize(next - current);

            var limit = MathF.Min(Vector2.Distance(current, previous), Vector2.Distance(current, next)) * 0.5f;
            var radius = MathF.Min(rounding, limit);

            drawList.PathLineTo(current + (toPrevious * radius));
            drawList.PathBezierQuadraticCurveTo(current, current + (toNext * radius), 0);
        }

        drawList.PathFillConvex(colour);
    }

    /// An up or down trend arrow: a lit, rounded triangle with a halo and a soft drop shadow.
    public static void DrawTrendArrow(
        ImDrawListPtr drawList, Vector2 centre, float width, float height, bool up, Vector4 colour, float appear = 1f)
    {
        var eased = EaseOutCubic(appear);

        var travel = (1f - eased) * 5f;
        var origin = centre + new Vector2(0f, up ? travel : -travel);

        var apexY = up ? origin.Y - (height / 2f) : origin.Y + (height / 2f);
        var baseY = up ? origin.Y + (height / 2f) : origin.Y - (height / 2f);

        var apex = new Vector2(origin.X, apexY);
        var left = new Vector2(origin.X - (width / 2f), baseY);
        var right = new Vector2(origin.X + (width / 2f), baseY);

        var rounding = MathF.Max(1.2f, width * 0.22f);
        var alpha = colour.W * eased;

        AddRoundedTriangle(
            drawList,
            apex + new Vector2(0f, 1f), left + new Vector2(0f, 1f), right + new Vector2(0f, 1f),
            rounding, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.28f * alpha)));

        AddRoundedTriangle(drawList, apex, left, right, rounding, ImGui.GetColorU32(colour with { W = alpha }));
    }

    /// Centres the cursor horizontally for content of the given width.
    public static void CenterCursorX(float contentWidth)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (avail - contentWidth) / 2f));
    }

    /// How wide a tooltip may get before it wraps, in characters of the current font.
    private const float TooltipWidthInChars = 52f;

    /// A tooltip that wraps instead of running off the screen.
    public static void WrappedTooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * TooltipWidthInChars);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }
}
