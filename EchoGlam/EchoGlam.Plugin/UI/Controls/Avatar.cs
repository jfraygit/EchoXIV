using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoGlam.Game;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Controls;

/// Somebody's picture, as a disc.
public static class Avatar
{
    /// How many rings the halo is built from, and how far apart they sit in design pixels.
    private const int Rings = 3;

    private const float RingStep = 2.2f;

    /// How far the halo carries beyond the rim.
    public static float Halo => Rings * RingStep * UiHelpers.Scale;

    /// Draws the disc.
    public static bool Draw(
        ImDrawListPtr drawList, GalleryImages? images, string? url, string? name,
        Vector2 min, float size, bool interactive = false)
    {
        var scale = UiHelpers.Scale;
        var radius = size / 2f;
        var centre = min + new Vector2(radius, radius);

        for (var i = Rings; i >= 1; i--)
        {
            drawList.AddCircleFilled(
                centre, radius + (i * RingStep * scale), Col.U32(Theme.Accent with { W = 0.045f }));
        }

        drawList.AddCircleFilled(centre, radius, Col.U32(Theme.Accent with { W = 0.22f }));

        var picture = images is null || string.IsNullOrEmpty(url)
            ? null
            : images.Get(url, (int)MathF.Ceiling(size * 2f));

        if (picture != null)
        {
            var (uv0, uv1) = CentreCrop(picture.Width, picture.Height);

            drawList.AddImageRounded(
                picture.Handle, centre - new Vector2(radius, radius), centre + new Vector2(radius, radius),
                uv0, uv1, Col.U32(Vector4.One), radius);
        }
        else
        {
            var initial = string.IsNullOrWhiteSpace(name)
                ? "?"
                : name.Trim()[..1].ToUpperInvariant();

            drawList.AddCircleFilled(
                centre - new Vector2(0f, size * 0.12f), size * 0.42f,
                Col.U32(Theme.Accent with { W = 0.10f }));

            using (size >= 64f * scale ? Text.PushDisplay() : Text.PushTitle())
            {
                var glyph = ImGui.CalcTextSize(initial);
                drawList.AddText(centre - (glyph / 2f), Col.U32(Theme.Accent), initial);
            }
        }

        drawList.AddCircle(centre, radius, Col.U32(Theme.Accent with { W = 0.65f }), 0, 1.5f * scale);

        if (!interactive)
            return false;

        if (!ImGui.IsMouseHoveringRect(centre - new Vector2(radius, radius), centre + new Vector2(radius, radius)))
            return false;

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return ImGui.IsMouseClicked(ImGuiMouseButton.Left);
    }

    /// UVs that fill a square with the middle of a picture of any shape.
    public static (Vector2 Uv0, Vector2 Uv1) CentreCrop(int textureWidth, int textureHeight)
    {
        if (textureWidth <= 0 || textureHeight <= 0)
            return (Vector2.Zero, Vector2.One);

        var aspect = (float)textureWidth / textureHeight;

        if (aspect > 1f)
        {
            var half = 1f / aspect / 2f;
            return (new Vector2(0.5f - half, 0f), new Vector2(0.5f + half, 1f));
        }

        if (aspect < 1f)
        {
            var half = aspect / 2f;
            return (new Vector2(0f, 0.5f - half), new Vector2(1f, 0.5f + half));
        }

        return (Vector2.Zero, Vector2.One);
    }

    /// UVs that fill a rectangle of any shape with the largest centred piece of a source that matches it -
    /// CSS background-size:cover, as texture coordinates.
    public static (Vector2 Uv0, Vector2 Uv1) Cover(
        int textureWidth, int textureHeight, float width, float height)
    {
        if (textureWidth <= 0 || textureHeight <= 0 || width <= 0f || height <= 0f)
            return (Vector2.Zero, Vector2.One);

        var wanted = width / height;
        var actual = (float)textureWidth / textureHeight;

        var uv0 = Vector2.Zero;
        var uv1 = Vector2.One;

        if (actual > wanted)
        {
            var keep = wanted / actual;
            uv0.X = (1f - keep) / 2f;
            uv1.X = uv0.X + keep;
        }
        else if (actual < wanted)
        {
            var keep = actual / wanted;
            uv0.Y = (1f - keep) / 2f;
            uv1.Y = uv0.Y + keep;
        }

        return (uv0, uv1);
    }
}
