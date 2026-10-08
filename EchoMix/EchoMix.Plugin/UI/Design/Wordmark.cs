using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Design;

/// ECHOMIX, letter-spaced and graded across the user's own two deck accents.
public static class Wordmark
{
    public const string Text = "ECHOMIX";

    /// Letter spacing as a fraction of the ambient font size, so the mark keeps its proportions at every type
    /// role rather than looking cramped at Display and loose at Title.
    private const float SpacingRatio = 0.17f;

    /// Total drawn width under the currently-pushed font.
    public static float Measure()
    {
        var spacing = ImGui.GetFontSize() * SpacingRatio;
        var width = 0f;

        for (var i = 0; i < Text.Length; i++)
            width += ImGui.CalcTextSize(Text[i].ToString()).X + (i < Text.Length - 1 ? spacing : 0f);

        return width;
    }

    /// Draws the mark with its top-left at `pos`.
    public static float Draw(ImDrawListPtr drawList, Vector2 pos, float alpha = 1f)
    {
        var spacing = ImGui.GetFontSize() * SpacingRatio;
        var x = pos.X;

        for (var i = 0; i < Text.Length; i++)
        {
            var glyph = Text[i].ToString();
            var t = Text.Length > 1 ? i / (float)(Text.Length - 1) : 0f;

            var color = Chrome.DeckGradient(t);

            Chrome.Text(drawList, new Vector2(x, pos.Y + 1f),
                ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.45f * alpha)), glyph);
            Chrome.Text(drawList, new Vector2(x, pos.Y),
                ImGui.GetColorU32(Semantic.Alpha(color, alpha)), glyph);

            x += ImGui.CalcTextSize(glyph).X + spacing;
        }

        return MathF.Max(0f, x - pos.X - spacing);
    }
}
