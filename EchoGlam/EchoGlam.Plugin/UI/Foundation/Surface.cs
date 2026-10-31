using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// What a surface is made of.
public enum SurfaceKind
{
    /// Grouping only: padding and a width, nothing drawn.
    Flat,

    /// A block inside a pane.
    Raised,

    /// A card.
    Card,

    /// A recess: an image well, a list's bed.
    Well,
}

/// One open surface.
public readonly struct SurfaceScope : IDisposable
{
    internal SurfaceScope(Box box, SurfaceKind kind, Vector4? accent, int depth, float? height)
    {
        Box = box;
        this.kind = kind;
        this.accent = accent;
        this.depth = depth;
        this.height = height;
    }

    /// The rectangle this surface gave its contents.
    public Box Box { get; }

    private readonly SurfaceKind kind;
    private readonly Vector4? accent;
    private readonly int depth;
    private readonly float? height;

    public void Dispose() => Surface.End(Box, kind, accent, depth, height);
}

/// A card that takes its height from what goes inside it, at any nesting depth.
public static class Surface
{
    /// Default inner padding, in design pixels.
    public const float PadDesign = 14f;

    public static SurfaceScope Begin(
        float width,
        SurfaceKind kind = SurfaceKind.Card,
        Vector4? accent = null,
        float? height = null,
        float? padding = null)
    {
        var scale = UiHelpers.Scale;
        var pad = padding is { } p
            ? new Vector2(p * scale, (p - 2f) * scale)
            : new Vector2(PadDesign * scale, (PadDesign - 2f) * scale);

        var origin = ImGui.GetCursorScreenPos();

        var localX = ImGui.GetCursorPosX();

        var depth = LayerStack.EnterSurface();

        ImGui.SetCursorScreenPos(origin + pad);
        ImGui.BeginGroup();

        ImGui.PushTextWrapPos(localX + width - pad.X);

        var box = new Box(origin, width, pad);
        Layout.Push(box);

        return new SurfaceScope(box, kind, accent, depth, height);
    }

    internal static void End(Box box, SurfaceKind kind, Vector4? accent, int depth, float? height)
    {
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        Layout.Pop();

        var measured = MathF.Max(ImGui.GetItemRectMax().Y - box.Min.Y, 0f) + box.Pad.Y;
        var size = new Vector2(box.Width, height ?? measured);

        LayerStack.UseDecor(depth);
        Paint(box.Min, size, kind, accent);
        LayerStack.ExitSurface();

        ImGui.SetCursorScreenPos(box.Min);
        ImGui.Dummy(size);
    }

    /// THE ONE FUNCTION THAT DECIDES WHAT EVERY SURFACE IN THE PLUGIN IS MADE OF.
    public static void Paint(
        Vector2 min, Vector2 size, SurfaceKind kind, Vector4? accent = null, bool inside = false)
    {
        if (kind == SurfaceKind.Flat)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var max = min + size;

        var rounding = kind == SurfaceKind.Card ? Rad.Panel : Rad.Ctl;

        if (kind == SurfaceKind.Well)
        {
            drawList.AddRectFilled(min, max, Col.U32(Col.Sunken), rounding);

            drawList.AddLine(
                new Vector2(min.X + rounding, min.Y + (Stroke.Hair / 2f)),
                new Vector2(max.X - rounding, min.Y + (Stroke.Hair / 2f)),
                Col.U32(new Vector4(0f, 0f, 0f, 0.45f)), Stroke.Hair);

            return;
        }

        var focal = accent is { } tint ? tint : (Vector4?)null;

        if (focal is { } glow && !inside)
            Elev.Glow(drawList, min, max, glow, 0.165f, rounding, 10.2f);

        Elev.Shadow(drawList, min, max, kind == SurfaceKind.Card ? Elev.Card : Elev.Block, rounding);

        var body = focal is { } tinted
            ? Vector4.Lerp(Col.Surface, tinted, 0.055f)
            : kind == SurfaceKind.Raised ? Col.Raised : Col.Surface;

        drawList.AddRectFilled(min, max, Col.U32(body), rounding);

        drawList.AddLine(
            new Vector2(min.X + rounding, min.Y + (Stroke.Hair / 2f)),
            new Vector2(max.X - rounding, min.Y + (Stroke.Hair / 2f)),
            Col.U32(new Vector4(1f, 1f, 1f, focal is null ? 0.07f : 0.12f)),
            Stroke.Hair);

        if (focal is not { } lit)
        {
            drawList.AddRect(
                min, max, Col.U32(Col.EdgeRest), rounding, ImDrawFlags.None, Stroke.Hair);

            return;
        }

        if (inside)
        {
            drawList.AddRect(
                min, max, Col.U32(lit with { W = 0.85f }), rounding, ImDrawFlags.None, Stroke.Edge);

            return;
        }

        for (var ring = 2; ring >= 0; ring--)
        {
            var spread = ring * 1.5f * UiHelpers.Scale;

            drawList.AddRect(
                min - new Vector2(spread, spread),
                max + new Vector2(spread, spread),
                Col.U32(lit with { W = ring == 0 ? 0.85f : 0.07f }),
                rounding + spread,
                ImDrawFlags.None,
                ring == 0 ? Stroke.Edge : Stroke.Hair);
        }
    }
}
