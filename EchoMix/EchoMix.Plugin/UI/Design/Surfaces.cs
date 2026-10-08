using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Design;

/// The container primitives every 2.0 screen is built out of: panels, cards and section headers.
public static class Surfaces
{
    private static Vector2 panelStart;
    private static float panelWidth;
    private static bool panelOpen;
    private static Vector4 panelFill;
    private static Elevation.ShadowSpec panelShadow;
    private static Vector4? panelBorder;

    /// Opens a self-measuring panel.
    public static bool ReserveScrollbar { get; set; }

    /// Width a panel or section header takes when it isn't given one - the content region, less the
    /// scrollbar's width when ReserveScrollbar is set and the scrollbar isn't already showing (in which case
    /// the content region has already lost it).
    public static float AutoWidth()
    {
        var avail = ImGui.GetContentRegionAvail().X;

        if (!ReserveScrollbar || ImGui.GetScrollMaxY() > 0f)
            return avail;

        return MathF.Max(0f, avail - ImGui.GetStyle().ScrollbarSize);
    }

    /// The usable width inside the open panel: what a row, divider or indented block has to work with.
    public static float ContentWidth => panelOpen
        ? MathF.Max(0f, panelWidth - (Metrics.PanelPadding.X * 2f))
        : AutoWidth();

    /// What ContentWidth will be for a panel of this outer width, before it has been opened.
    public static float ContentWidthFor(float outerWidth)
        => MathF.Max(0f, outerWidth - (Metrics.PanelPadding.X * 2f));

    /// Suspends the open panel for the duration of a popup opened from inside one.
    public static DetachedPanel Detach()
    {
        var scope = new DetachedPanel(panelOpen, panelWidth);
        panelOpen = false;
        return scope;
    }

    public readonly struct DetachedPanel : IDisposable
    {
        private readonly bool wasOpen;
        private readonly float width;

        internal DetachedPanel(bool wasOpen, float width)
        {
            this.wasOpen = wasOpen;
            this.width = width;
        }

        public void Dispose()
        {
            panelOpen = wasOpen;
            panelWidth = width;
        }
    }

    public static void BeginPanel(
        float width = 0f,
        Vector4? fill = null,
        Elevation.ShadowSpec? shadow = null,
        Vector4? border = null)
    {
        if (panelOpen)
            throw new InvalidOperationException("Surfaces.BeginPanel does not nest - close the open panel first.");

        panelOpen = true;
        panelFill = fill ?? Elevation.Surface;
        panelShadow = shadow ?? Elevation.ShadowSpec.Low;
        panelBorder = border;
        panelWidth = width > 0f ? width : AutoWidth();
        panelStart = ImGui.GetCursorScreenPos();

        ImGui.GetWindowDrawList().ChannelsSplit(2);
        ImGui.GetWindowDrawList().ChannelsSetCurrent(1);

        var padding = Metrics.PanelPadding;
        ImGui.SetCursorScreenPos(panelStart + padding);
        ImGui.BeginGroup();

        var windowX = ImGui.GetWindowPos().X;
        ImGui.PushTextWrapPos(panelStart.X - windowX + panelWidth - padding.X);
    }

    public static void EndPanel()
    {
        if (!panelOpen)
            throw new InvalidOperationException("Surfaces.EndPanel called without a matching BeginPanel.");

        ImGui.PopTextWrapPos();
        ImGui.EndGroup();

        var padding = Metrics.PanelPadding;
        var contentHeight = ImGui.GetItemRectSize().Y;
        var size = new Vector2(panelWidth, contentHeight + (padding.Y * 2f));

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        Elevation.DrawSurface(
            drawList,
            panelStart,
            panelStart + size,
            panelFill,
            Metrics.RadiusCard,
            panelShadow,
            topEdge: true,
            panelBorder);
        drawList.ChannelsMerge();

        ImGui.SetCursorScreenPos(panelStart);
        ImGui.Dummy(size);

        panelOpen = false;
        panelBorder = null;
    }

    /// A fixed-size card.
    public static void BeginCard(
        string id,
        Vector2 size,
        Vector4? fill = null,
        Elevation.ShadowSpec? shadow = null,
        float glow = 0f,
        Vector4? glowColor = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var accent = glowColor ?? Semantic.Primary;

        Elevation.DrawSurface(
            drawList,
            pos,
            pos + size,
            fill ?? Elevation.Raised,
            Metrics.RadiusCard,
            shadow ?? Elevation.ShadowSpec.Low,
            topEdge: true,
            border: glow > 0.01f ? null : Elevation.Line);

        if (glow > 0.01f)
        {
            drawList.AddRect(pos, pos + size, ImGui.GetColorU32(Semantic.Alpha(accent, glow * 0.22f)),
                Metrics.RadiusCard, ImDrawFlags.None, 4f * Metrics.Scale);
            drawList.AddRect(pos, pos + size, ImGui.GetColorU32(Semantic.Alpha(accent, glow * 0.85f)),
                Metrics.RadiusCard, ImDrawFlags.None, Metrics.Hairline);
        }

        using var _ = Sty.New()
            .Col(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f))
            .Col(ImGuiCol.Border, new Vector4(0f, 0f, 0f, 0f));
        ImGui.BeginChild(id, size, false, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + Metrics.PanelPadding);
        ImGui.BeginGroup();
    }

    public static void EndCard()
    {
        ImGui.EndGroup();
        ImGui.EndChild();
    }

    /// A section label with a rule running to the right edge - the 2.0 replacement for 1.0's all-caps
    /// TextDisabled headings, which carried no structure, just a colour change.
    public static void SectionHeader(string label, Vector4? accent = null, float width = 0f)
    {
        var color = accent ?? Semantic.TextSecondary;
        var start = ImGui.GetCursorScreenPos();
        var textSize = TypeScale.Measure(TypeScale.Heading, label);

        TypeScale.Text(TypeScale.Heading, color, label);

        var drawList = ImGui.GetWindowDrawList();
        var lineY = start.Y + (textSize.Y * 0.5f);
        var lineStart = new Vector2(start.X + textSize.X + Metrics.Lg, lineY);
        var lineEnd = new Vector2(start.X + (width > 0f ? width : AutoWidth()), lineY);
        if (lineEnd.X > lineStart.X)
            drawList.AddLine(lineStart, lineEnd, ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);
    }

    /// Vertical rhythm helper.
    public static void Gap(float amount) => ImGui.Dummy(new Vector2(0f, amount));

    /// Loose wrapped text inside a panel that also contains Fields rows.
    public static void RowText(string text, Vector4 color)
    {
        ImGui.Indent(Metrics.Lg);

        using (TypeScale.Caption())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, color);
            ImGui.TextWrapped(text);
        }

        ImGui.Unindent(Metrics.Lg);
    }

    /// Indents a block of loose content to line up with the panel's rows - the non-text counterpart of
    /// RowText, for an image or a button cluster.
    public static float BeginRowAligned()
    {
        var width = ContentWidth - (Metrics.Lg * 2f);
        ImGui.Indent(Metrics.Lg);
        return width;
    }

    public static void EndRowAligned() => ImGui.Unindent(Metrics.Lg);
}
