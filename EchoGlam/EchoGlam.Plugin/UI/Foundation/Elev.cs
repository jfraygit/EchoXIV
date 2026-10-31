using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// Drop shadows, as four levels instead of four copies.
public static class Elev
{
    /// Nothing.
    public const int None = 0;

    /// A block inside a pane.
    public const int Block = 1;

    /// A card.
    public const int Card = 2;

    /// Lifted off the page: a hovered card, a popup, anything floating over content.
    public const int Float = 3;

    private readonly record struct Step(int Layers, float Offset, float Spread, float Alpha);

    /// Indexed by level.
    private static readonly Step[] Steps =
    [
        new(0, 0f, 0f, 0f),
        new(3, 1.5f, 0f, 0.045f),
        new(3, 2f, 0f, 0.05f),
        new(4, 2f, 0.8f, 0.045f),
    ];

    /// Paints a shadow beneath the rectangle.
    public static void Shadow(
        ImDrawListPtr drawList, Vector2 min, Vector2 max, int level, float rounding, float lift = 1f)
    {
        if (level <= None || level >= Steps.Length)
            return;

        var step = Steps[level];
        var scale = UiHelpers.Scale;

        for (var i = step.Layers; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * step.Offset * scale * lift);
            var spread = (i - 1) * step.Spread * scale * lift;
            var wide = new Vector2(spread, 0f);

            drawList.AddRectFilled(
                min + offset - wide,
                max + offset + wide,
                Col.U32(new Vector4(0f, 0f, 0f, step.Alpha * i)),
                rounding);
        }
    }

    /// How many rectangles a glow is built from.
    private const int Rings = 8;

    /// A glow outside the shape rather than beneath it: the primary button's halo, a selection underline's
    /// soft copy.
    public static void Glow(
        ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector4 colour, float alpha, float rounding,
        float reachDesign = 7.2f)
    {
        if (alpha <= 0f || reachDesign <= 0f)
            return;

        var step = reachDesign * UiHelpers.Scale / Rings;
        var each = colour with { W = alpha / Rings };

        for (var i = Rings; i >= 1; i--)
        {
            var spread = i * step;

            drawList.AddRectFilled(
                min - new Vector2(spread, spread * 0.4f),
                max + new Vector2(spread, spread),
                Col.U32(each),
                rounding + spread);
        }
    }
}
