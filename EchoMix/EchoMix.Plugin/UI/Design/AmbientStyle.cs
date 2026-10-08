using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Design;

/// Dalamud's own geometry style values, captured once before the 2.0 look overrides them.
public static class AmbientStyle
{
    public static bool Captured { get; private set; }

    public static Vector2 WindowPadding { get; private set; } = new(8f, 8f);
    public static Vector2 FramePadding { get; private set; } = new(4f, 3f);
    public static Vector2 ItemSpacing { get; private set; } = new(8f, 4f);
    public static Vector2 ItemInnerSpacing { get; private set; } = new(4f, 4f);
    public static Vector2 CellPadding { get; private set; } = new(4f, 2f);
    public static float IndentSpacing { get; private set; } = 21f;
    public static float ScrollbarSize { get; private set; } = 14f;

    /// Call at the very top of a frame, before any style push.
    public static void CaptureOnce()
    {
        if (Captured)
            return;

        var style = ImGui.GetStyle();
        WindowPadding = style.WindowPadding;
        FramePadding = style.FramePadding;
        ItemSpacing = style.ItemSpacing;
        ItemInnerSpacing = style.ItemInnerSpacing;
        CellPadding = style.CellPadding;
        IndentSpacing = style.IndentSpacing;
        ScrollbarSize = style.ScrollbarSize;
        Captured = true;
    }

    /// Puts the captured geometry back for the duration of the scope, so a 1.0 body laid out against
    /// Dalamud's spacing still gets Dalamud's spacing.
    public static Sty Restore()
        => Sty.New()
            .Var(ImGuiStyleVar.WindowPadding, WindowPadding)
            .Var(ImGuiStyleVar.FramePadding, FramePadding)
            .Var(ImGuiStyleVar.ItemSpacing, ItemSpacing)
            .Var(ImGuiStyleVar.ItemInnerSpacing, ItemInnerSpacing)
            .Var(ImGuiStyleVar.CellPadding, CellPadding)
            .Var(ImGuiStyleVar.IndentSpacing, IndentSpacing)
            .Var(ImGuiStyleVar.ScrollbarSize, ScrollbarSize);
}
