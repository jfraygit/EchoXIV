using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Design;

/// Tooltips for the 2.0 look.
public static class Tip
{
    /// Maximum text width before wrapping.
    private const float WrapWidth = 300f;

    /// Shows a tooltip for the item just drawn, if it's hovered.
    public static void Hovered(string title, string? body = null, Vector4? accent = null)
    {
        if (ImGui.IsItemHovered())
            Show(title, body, accent);
    }

    /// Shows a tooltip unconditionally.
    public static void Show(string title, string? body = null, Vector4? accent = null)
    {
        using var style = Sty.New()
            .Var(ImGuiStyleVar.WindowPadding, new Vector2(Metrics.Lg, Metrics.Md))
            .Var(ImGuiStyleVar.WindowRounding, Metrics.RadiusSoft)
            .Var(ImGuiStyleVar.WindowBorderSize, 0f)
            .Var(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Metrics.Sm))
            .Col(ImGuiCol.PopupBg, Elevation.Overlay);

        ImGui.BeginTooltip();

        var origin = ImGui.GetCursorScreenPos();

        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + (WrapWidth * Metrics.Scale));

        using (TypeScale.Heading())
        using (Sty.New().Col(ImGuiCol.Text, Semantic.TextPrimary))
            ImGui.TextUnformatted(title);

        if (!string.IsNullOrEmpty(body))
        {
            using (TypeScale.Body())
            using (Sty.New().Col(ImGuiCol.Text, Semantic.TextSecondary))
                ImGui.TextWrapped(body);
        }

        ImGui.PopTextWrapPos();

        if (accent is { } accentColor)
        {
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetWindowPos();
            var max = min + ImGui.GetWindowSize();
            var width = 3f * Metrics.Scale;

            drawList.PushClipRect(min - new Vector2(width, width), max + new Vector2(width, width), false);
            drawList.AddRectFilled(
                new Vector2(min.X, min.Y),
                new Vector2(min.X + width, max.Y),
                ImGui.GetColorU32(accentColor),
                Metrics.RadiusSoft,
                ImDrawFlags.RoundCornersLeft);
            drawList.PopClipRect();
        }

        DrawEdge();
        ImGui.EndTooltip();
    }

    /// Hairline border plus lit top edge on the tooltip window itself.
    private static void DrawEdge()
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var rounding = Metrics.RadiusSoft;

        drawList.PushClipRect(min - new Vector2(2f, 2f), max + new Vector2(2f, 2f), false);
        drawList.AddRect(min, max, ImGui.GetColorU32(Elevation.LineStrong), rounding, ImDrawFlags.None, Metrics.Hairline);
        drawList.PopClipRect();
    }
}
