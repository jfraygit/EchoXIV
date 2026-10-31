using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EchoGlam.UI.Foundation;

/// A small capsule label: a status, a category, a count.
public static class Pill
{
    public enum Tone
    {
        /// The default: a fact with no weight attached.
        Neutral,

        /// Belongs to this plugin's own vocabulary - new, featured, selected.
        Accent,

        Info,
        Success,
        Caution,
        Danger,
    }

    private static Vector4 Colour(Tone tone) => tone switch
    {
        Tone.Accent => Theme.Accent,
        Tone.Info => Col.Info,
        Tone.Success => Col.Success,
        Tone.Caution => Col.Caution,
        Tone.Danger => Col.Danger,
        _ => Col.TextDim,
    };

    /// Height of a pill, which is a caption line plus its padding.
    public static float Height => (Text.CaptionSize * 1.15f) + (Sp.Xs * 2f);

    /// What Draw will take up.
    public static float Measure(string label, bool icon = false)
    {
        float width;
        using (Text.PushCaption())
            width = Text.Width(label);

        return width + (Sp.Md * 2f) + (icon ? Height * 0.8f : 0f);
    }

    /// Draws a pill at the cursor and advances past it.
    public static float Draw(string label, Tone tone = Tone.Neutral, string? tooltip = null, FontAwesomeIcon? icon = null)
    {
        var width = Paint(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), label, tone, icon);

        ImGui.Dummy(new Vector2(width, Height));

        if (tooltip is not null && ImGui.IsItemHovered())
            UiHelpers.WrappedTooltip(tooltip);

        return width;
    }

    /// Draws a pill at an explicit screen position and TOUCHES NOTHING ELSE.
    public static float DrawAt(
        Vector2 position, string label, Tone tone = Tone.Neutral, string? tooltip = null,
        string? footnote = null)
    {
        var width = Paint(ImGui.GetWindowDrawList(), position, label, tone);

        if (tooltip is not null && ImGui.IsMouseHoveringRect(position, position + new Vector2(width, Height)))
            UiHelpers.WrappedTooltip(tooltip, footnote);

        return width;
    }

    /// The pixels, and nothing else.
    public static float Paint(
        ImDrawListPtr drawList, Vector2 origin, string label, Tone tone = Tone.Neutral, FontAwesomeIcon? icon = null)
    {
        var width = Measure(label, icon is not null);
        var height = Height;
        var colour = Colour(tone);
        var rounding = Rad.Pill(height);

        drawList.AddRectFilled(
            origin, origin + new Vector2(width, height), Col.U32(colour with { W = 0.14f }), rounding);

        drawList.AddRect(
            origin, origin + new Vector2(width, height),
            Col.U32(colour with { W = 0.45f }), rounding, ImDrawFlags.None, Stroke.Hair);

        var textX = origin.X + Sp.Md;

        if (icon is { } glyph)
        {
            using (Text.PushIcon())
            {
                UiHelpers.DrawScaledIcon(
                    drawList, glyph,
                    new Vector2(textX + (height * 0.3f), origin.Y + (height / 2f)),
                    Col.U32(colour));
            }

            textX += height * 0.8f;
        }

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(textX, origin.Y + ((height - (Text.CaptionSize * 1.15f)) / 2f)),
                Col.U32(colour),
                label);
        }

        return width;
    }
}
