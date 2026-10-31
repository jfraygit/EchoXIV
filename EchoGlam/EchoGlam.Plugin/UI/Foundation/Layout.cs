using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// The rectangle a surface gave its contents: where it starts, how wide it is, and how much of that the
/// padding has already taken.
public readonly record struct Box(Vector2 Min, float Width, Vector2 Pad)
{
    /// The width available INSIDE the padding.
    public float Inner => MathF.Max(0f, Width - (Pad.X * 2f));

    public float InnerLeft => Min.X + Pad.X;

    public float InnerRight => Min.X + Width - Pad.X;

    /// One of n equal columns with a gutter between them.
    public float Split(int n, float gutter) => n <= 0 ? 0f : (Inner - (gutter * (n - 1))) / n;

    /// The same box with different padding - for a surface that insets its contents further.
    public Box WithPad(Vector2 pad) => this with { Pad = pad };
}

public static class Layout
{
    /// THE STACK IS WHY ANY OF THIS WORKS.
    private static readonly List<Box> Boxes = [];

    /// The surface being drawn into.
    public static Box Here =>
        Boxes.Count > 0
            ? Boxes[^1]
            : new Box(ImGui.GetCursorScreenPos(), ImGui.GetContentRegionAvail().X, Vector2.Zero);

    /// Shorthand for Here.Inner, which is the overwhelmingly common use.
    public static float Inner => Here.Inner;

    internal static void Push(Box box) => Boxes.Add(box);

    internal static void Pop()
    {
        if (Boxes.Count > 0)
            Boxes.RemoveAt(Boxes.Count - 1);
    }

    /// Dropped at the top of each frame, for the same reason LayerStack.Reset is.
    internal static void Reset() => Boxes.Clear();

    /// How many columns of at least min fit in inner, and how wide each one is.
    public static (int Columns, float Width) Fit(float inner, float min, float gutter)
    {
        if (inner <= 0f || min <= 0f)
            return (1, MathF.Max(inner, 0f));

        var columns = (int)((inner + gutter) / (min + gutter));
        columns = Math.Max(1, columns);

        return (columns, (inner - (gutter * (columns - 1))) / columns);
    }

    /// Fit against the current surface's inner width.
    public static (int Columns, float Width) Fit(float min, float gutter) => Fit(Inner, min, gutter);

    /// Lays n items of the given width out in a row, returning the gap that spreads any remainder evenly
    /// rather than leaving it all at the right-hand end.
    public static float Spread(float inner, int count, float itemWidth, float minGutter)
    {
        if (count <= 1)
            return minGutter;

        var slack = inner - (itemWidth * count);
        return MathF.Max(minGutter, slack / (count - 1));
    }
}
