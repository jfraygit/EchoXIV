using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Shell;

/// The window's ground.
public static class Backdrop
{
    /// Where the vertical fall of light ends, as a fraction of the window's height.
    private const float FallFraction = 0.72f;

    /// Paints the ground into the current window, edge to edge.
    public static void Draw(Vector2 min, Vector2 max, float rounding)
    {
        var drawList = ImGui.GetWindowDrawList();
        var size = max - min;

        if (size.X <= 0f || size.Y <= 0f)
            return;

        var top = Col.U32(Vector4.Lerp(Theme.Background, Vector4.One, 0.045f));
        var bottom = Col.U32(Theme.Background);
        var fall = min.Y + (size.Y * FallFraction);

        drawList.AddRectFilled(
            min, new Vector2(max.X, min.Y + rounding), top, rounding, ImDrawFlags.RoundCornersTop);

        drawList.AddRectFilledMultiColor(
            new Vector2(min.X, min.Y + rounding), new Vector2(max.X, fall), top, top, bottom, bottom);

        drawList.AddRectFilled(
            new Vector2(min.X, fall), max, bottom, rounding, ImDrawFlags.RoundCornersBottom);

        drawList.PushClipRect(min, max, true);

        var centre = new Vector2(min.X + (size.X * 0.10f), min.Y - (size.Y * 0.04f));
        var reach = size.X * 0.42f;

        for (var ring = Rings; ring >= 1; ring--)
        {
            drawList.AddCircleFilled(
                centre,
                reach * (ring / (float)Rings),
                Col.U32(Theme.Accent with { W = 0.011f }),
                40);
        }

        drawList.PopClipRect();
    }

    /// Enough for the steps to be invisible, few enough to be free.
    private const int Rings = 8;
}
