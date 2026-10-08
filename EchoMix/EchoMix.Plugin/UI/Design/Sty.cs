using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Design;

/// Scoped ImGui style pushes.
public struct Sty : IDisposable
{
    private int colors;
    private int vars;

    /// Starts an empty scope.
    public static Sty New() => default;

    public Sty Col(ImGuiCol slot, Vector4 color)
    {
        ImGui.PushStyleColor(slot, color);
        colors++;
        return this;
    }

    public Sty Var(ImGuiStyleVar slot, float value)
    {
        ImGui.PushStyleVar(slot, value);
        vars++;
        return this;
    }

    public Sty Var(ImGuiStyleVar slot, Vector2 value)
    {
        ImGui.PushStyleVar(slot, value);
        vars++;
        return this;
    }

    /// Pushes only when `condition` holds, so a conditional style doesn't force the caller to branch around
    /// the whole using statement (the thing that produces mismatched pops).
    public Sty ColIf(bool condition, ImGuiCol slot, Vector4 color)
        => condition ? Col(slot, color) : this;

    public Sty VarIf(bool condition, ImGuiStyleVar slot, float value)
        => condition ? Var(slot, value) : this;

    /// The whole-window baseline for the 2.0 look: surfaces, text, native chrome and the geometry style vars
    /// 1.0's Theme.Push deliberately left alone.
    public static Sty Shell()
        => New()
            .Col(ImGuiCol.WindowBg, Elevation.Base)
            .Col(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f))
            .Col(ImGuiCol.PopupBg, Elevation.Overlay)
            .Col(ImGuiCol.Border, Elevation.Line)
            .Col(ImGuiCol.Text, Semantic.TextPrimary)
            .Col(ImGuiCol.TextDisabled, Semantic.TextDisabled)
            .Col(ImGuiCol.FrameBg, Elevation.Sunken)
            .Col(ImGuiCol.FrameBgHovered, Semantic.Alpha(Semantic.Primary, 0.14f))
            .Col(ImGuiCol.FrameBgActive, Semantic.Alpha(Semantic.Primary, 0.22f))
            .Col(ImGuiCol.Button, Semantic.Alpha(Semantic.Primary, 0.16f))
            .Col(ImGuiCol.ButtonHovered, Semantic.Alpha(Semantic.Primary, 0.26f))
            .Col(ImGuiCol.ButtonActive, Semantic.Alpha(Semantic.Primary, 0.36f))
            .Col(ImGuiCol.Header, Semantic.Alpha(Semantic.Primary, 0.18f))
            .Col(ImGuiCol.HeaderHovered, Semantic.Alpha(Semantic.Primary, 0.28f))
            .Col(ImGuiCol.HeaderActive, Semantic.Alpha(Semantic.Primary, 0.38f))
            .Col(ImGuiCol.SliderGrab, Semantic.Primary)
            .Col(ImGuiCol.SliderGrabActive, Semantic.PrimaryHover)
            .Col(ImGuiCol.CheckMark, Semantic.Primary)
            .Col(ImGuiCol.Separator, Elevation.Line)
            .Col(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0f))
            .Col(ImGuiCol.ScrollbarGrab, Semantic.Alpha(Semantic.TextPrimary, 0.14f))
            .Col(ImGuiCol.ScrollbarGrabHovered, Semantic.Alpha(Semantic.TextPrimary, 0.22f))
            .Col(ImGuiCol.ScrollbarGrabActive, Semantic.Alpha(Semantic.TextPrimary, 0.3f))
            .Col(ImGuiCol.TitleBg, Elevation.Base)
            .Col(ImGuiCol.TitleBgActive, Elevation.Base)
            .Var(ImGuiStyleVar.WindowRounding, Metrics.RadiusPanel)
            .Var(ImGuiStyleVar.WindowBorderSize, 0f)
            .Var(ImGuiStyleVar.ChildRounding, Metrics.RadiusCard)
            .Var(ImGuiStyleVar.FrameRounding, Metrics.RadiusSoft)
            .Var(ImGuiStyleVar.GrabRounding, Metrics.RadiusSoft)
            .Var(ImGuiStyleVar.PopupRounding, Metrics.RadiusCard)
            .Var(ImGuiStyleVar.FrameBorderSize, 0f)
            .Var(ImGuiStyleVar.WindowPadding, new Vector2(0f, 0f))
            .Var(ImGuiStyleVar.FramePadding, new Vector2(Metrics.Lg, Metrics.Md))
            .Var(ImGuiStyleVar.ItemSpacing, new Vector2(Metrics.Md, Metrics.Md))
            .Var(ImGuiStyleVar.ItemInnerSpacing, new Vector2(Metrics.Sm, Metrics.Sm))
            .Var(ImGuiStyleVar.CellPadding, new Vector2(Metrics.Md, Metrics.Sm))
            .Var(ImGuiStyleVar.IndentSpacing, Metrics.Xl)
            .Var(ImGuiStyleVar.ScrollbarSize, 10f * Metrics.Scale)
            .Var(ImGuiStyleVar.ScrollbarRounding, Metrics.RadiusSharp);

    /// Window flags every popup this plugin opens must carry.
    public const ImGuiWindowFlags PopupFlags = ImGuiWindowFlags.NoMove;

    /// Styling for a popup that ImGui itself owns and draws - a colour picker, a native combo, a context
    /// menu.
    public static Sty Popup()
        => New()
            .Var(ImGuiStyleVar.PopupBorderSize, MathF.Max(1f, Metrics.Hairline))
            .Var(ImGuiStyleVar.WindowBorderSize, MathF.Max(1f, Metrics.Hairline))
            .Var(ImGuiStyleVar.PopupRounding, Metrics.RadiusCard)
            .Var(ImGuiStyleVar.WindowPadding, new Vector2(Metrics.Lg, Metrics.Lg))
            .Var(ImGuiStyleVar.FrameRounding, Metrics.RadiusSoft)
            .Var(ImGuiStyleVar.ItemSpacing, new Vector2(Metrics.Md, Metrics.Md))
            .Col(ImGuiCol.PopupBg, Elevation.Overlay)
            .Col(ImGuiCol.Border, Elevation.LineStrong)
            .Col(ImGuiCol.FrameBg, Elevation.Sunken);

    public void Dispose()
    {
        if (vars > 0)
            ImGui.PopStyleVar(vars);
        if (colors > 0)
            ImGui.PopStyleColor(colors);
    }
}
