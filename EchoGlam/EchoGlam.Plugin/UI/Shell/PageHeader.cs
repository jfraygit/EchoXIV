using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Shell;

/// The band at the top of every page: what this page is, and the controls that belong to it.
public static class PageHeader
{
    /// Height of the band, in real pixels.
    public static float Height => MathF.Max(Metrics.RowTall, WindowChrome.Height) + Sp.Lg;

    /// The band with nothing in it but the window's own three marks.
    public static float BareHeight => WindowChrome.Height + Sp.Md;

    /// The row the band's content sits on.
    public static float BandRow(bool bare) =>
        bare ? WindowChrome.Height : MathF.Max(Metrics.RowTall, WindowChrome.Height);

    /// Where the band's hairline falls, measured from the content's top edge.
    public static float RuleOffset(bool bare) => BandRow(bare) + (Sp.Md / 2f);

    /// Draws the band and leaves the cursor below it.
    public static Vector2 Draw(string subtitle, float width, float actionWidth = 0f, Vector4? tone = null)
    {
        var bare = string.IsNullOrEmpty(subtitle) && actionWidth <= 0f;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var rowHeight = BandRow(bare);

        var actionsRight = origin.X + width - WindowChrome.Width - Sp.Xl;
        var actionsLeft = actionsRight - actionWidth;

        using (Text.PushCaption())
        {
            var room = MathF.Max(0f, (actionWidth > 0f ? actionsLeft : actionsRight) - origin.X - Sp.Lg);

            drawList.AddText(
                new Vector2(origin.X, origin.Y + ((rowHeight - Text.CaptionSize) / 2f)),
                Col.U32(tone ?? Col.TextDim),
                Text.Truncate(subtitle, room));
        }

        var ruleY = origin.Y + rowHeight + (Sp.Md / 2f);

        drawList.AddRectFilled(
            new Vector2(origin.X, ruleY),
            new Vector2(origin.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        ImGui.Dummy(new Vector2(width, bare ? BareHeight : Height));

        return new Vector2(actionsLeft, origin.Y + ((rowHeight - Metrics.Row) / 2f));
    }
}
