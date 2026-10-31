using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI.Shell;

/// What was pressed in the window's top-right corner, if anything.
public enum ChromeAction
{
    None,
    Lock,
    Settings,
    Minimise,
    Close,
}

/// Settings, minimise and close, at the window's top right.
public static class WindowChrome
{
    /// THE LOCK IS BACK UP HERE, and the argument that moved it away is worth keeping rather than quietly
    /// deleting.
    private const int Count = 4;

    private static readonly string[] Tips = ["Lock", "Settings", "Minimise", "Close"];

    /// How big the MARK is inside its hit box, as a fraction of it.
    private const float MarkFraction = 0.5f;

    public static float Height => Metrics.TouchMin;

    /// How much width the row takes, so a heading beside it knows where to stop.
    public static float Width => (Metrics.TouchMin * Count) + (Sp.Sm * (Count - 1));

    /// Draws the row with its right edge at rightEdge, and returns what was pressed.
    public static ChromeAction Draw(
        Vector2 rightEdge, bool settingsOpen, bool locked, bool unreadNotes)
    {
        var drawList = ImGui.GetWindowDrawList();
        var size = Metrics.TouchMin;
        var side = size * MarkFraction;
        var pressed = ChromeAction.None;
        var x = rightEdge.X - Width;

        var weight = Stroke.Edge;

        for (var i = 0; i < Count; i++)
        {
            var min = new Vector2(x, rightEdge.Y);
            var centre = min + new Vector2(size / 2f, size / 2f);

            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton($"##chrome{i}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                pressed = (ChromeAction)(i + 1);

            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                UiHelpers.WrappedTooltip(i switch
                {
                    0 => locked
                        ? "Window pinned. Click to let it move again."
                        : "Pin the window so working in it cannot nudge it around.",

                    1 when settingsOpen => "Back",
                    1 when unreadNotes => "Settings\n\nThere are release notes you have not read.",

                    _ => Tips[i],
                });
            }

            var active = (i == 1 && settingsOpen) || (i == 0 && locked);
            var lit = MathF.Max(Motion.Toward($"chrome{i}", hovered, 20f), active ? 1f : 0f);

            var tone = i == Count - 1 ? Col.Danger : Theme.Accent;

            if (lit > 0.01f)
            {
                var half = new Vector2(side / 2f, side / 2f);

                Elev.Glow(
                    drawList, centre - half, centre + half, tone, 0.34f * lit, side / 2f, 7f);
            }

            Paint(
                drawList, i, centre, side, weight, locked,
                Col.U32(Vector4.Lerp(Col.TextDim, tone, lit)));

            if (i == 1 && unreadNotes && !settingsOpen)
            {
                var pip = Sp.Px(DotDesign);

                var at = centre + new Vector2((side / 2f) + pip, -(side / 2f) - pip);

                drawList.AddCircleFilled(at, pip * 1.9f, Col.U32(Col.Window), 16);
                drawList.AddCircleFilled(at, pip, Col.U32(Theme.Accent), 16);
            }

            x += size + Sp.Sm;
        }

        return pressed;
    }

    /// Radius of the changelog pip, in design units.
    private const float DotDesign = 2.4f;

    /// One mark, inside a square of side centred on c.
    private static void Paint(
        ImDrawListPtr drawList, int which, Vector2 c, float side, float weight, bool locked, uint ink)
    {
        var half = side / 2f;

        switch (which)
        {
            case 0:
            {
                var bodyTop = c.Y - (half * 0.08f);
                var bodyHalf = half * 0.62f;

                drawList.AddRect(
                    new Vector2(c.X - bodyHalf, bodyTop),
                    new Vector2(c.X + bodyHalf, c.Y + (half * 0.86f)),
                    ink, weight * 1.4f, ImDrawFlags.None, weight);

                var radius = half * 0.40f;
                var shift = locked ? 0f : half * 0.30f;
                var rise = locked ? 0f : half * 0.12f;

                drawList.PathArcTo(
                    new Vector2(c.X + shift, bodyTop - rise), radius, MathF.PI, MathF.PI * 2f, 16);

                drawList.PathStroke(ink, ImDrawFlags.None, weight);

                break;
            }

            case 1:
            {
                var top = c.Y - (half * 0.44f);
                var bottom = c.Y + (half * 0.44f);

                var knob = MathF.Max(weight * 1.6f, 1.7f * UiHelpers.Scale);

                drawList.AddLine(new Vector2(c.X - half, top), new Vector2(c.X + half, top), ink, weight);
                drawList.AddLine(new Vector2(c.X - half, bottom), new Vector2(c.X + half, bottom), ink, weight);

                drawList.AddCircleFilled(new Vector2(c.X - (half * 0.32f), top), knob, ink, 12);
                drawList.AddCircleFilled(new Vector2(c.X + (half * 0.32f), bottom), knob, ink, 12);

                break;
            }

            case 2:
                drawList.AddLine(new Vector2(c.X - half, c.Y), new Vector2(c.X + half, c.Y), ink, weight);
                break;

            default:
            {
                var reach = half * 0.78f;

                drawList.AddLine(
                    new Vector2(c.X - reach, c.Y - reach), new Vector2(c.X + reach, c.Y + reach), ink, weight);

                drawList.AddLine(
                    new Vector2(c.X - reach, c.Y + reach), new Vector2(c.X + reach, c.Y - reach), ink, weight);

                break;
            }
        }
    }
}
