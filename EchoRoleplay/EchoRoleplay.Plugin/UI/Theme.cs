using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoRoleplay.UI;

/// The shared Echo suite theme: the same dark look EchoMix, EchoSim, EchoNav and EchoGlam use, so plugins in
/// the suite read as siblings rather than unrelated tools.
public static class Theme
{
    public static readonly Vector4 Background = new(0.07f, 0.07f, 0.09f, 1f);
    public static readonly Vector4 Panel = new(0.11f, 0.11f, 0.14f, 1f);
    public static readonly Vector4 Text = new(0.92f, 0.92f, 0.95f, 1f);
    public static readonly Vector4 Border = new(0.25f, 0.25f, 0.3f, 0.5f);

    /// Muted text for labels, units and secondary detail.
    public static readonly Vector4 TextDim = new(0.62f, 0.62f, 0.70f, 1f);

    public static readonly Vector4 TextDisabled = new(0.40f, 0.40f, 0.46f, 1f);

    /// Periwinkle.
    public static readonly Vector4 DefaultAccent = new(0.49f, 0.55f, 0.97f, 1f);

    /// EchoRoleplay's accent.
    public static Vector4 Accent { get; private set; } = DefaultAccent;

    public static Vector4 AccentHover { get; private set; } = Vector4.Lerp(DefaultAccent, Vector4.One, 0.3f);

    public static Vector4 AccentActive { get; private set; } =
        new(DefaultAccent.X * 0.7f, DefaultAccent.Y * 0.7f, DefaultAccent.Z * 0.7f, 1f);

    /// The accent at low alpha, for card tints and fills.
    public static Vector4 AccentSoft { get; private set; } =
        new(DefaultAccent.X, DefaultAccent.Y, DefaultAccent.Z, 0.35f);

    /// Ready-made accents, so changing the look doesn't require a colour wheel.
    public static readonly (string Name, Vector4 Colour)[] Presets =
    [
        ("Periwinkle", DefaultAccent),
        ("Iris", new Vector4(0.68f, 0.50f, 0.96f, 1f)),
        ("Cobalt", new Vector4(0.36f, 0.66f, 0.98f, 1f)),
        ("Ember", new Vector4(0.96f, 0.52f, 0.40f, 1f)),
        ("Sage", new Vector4(0.52f, 0.82f, 0.66f, 1f)),
        ("Rose", new Vector4(0.98f, 0.46f, 0.62f, 1f)),
    ];

    public static void ApplyAccent(Vector4 accent)
    {
        Accent = new Vector4(accent.X, accent.Y, accent.Z, 1f);
        AccentHover = Vector4.Lerp(Accent, Vector4.One, 0.3f);
        AccentActive = new Vector4(Accent.X * 0.7f, Accent.Y * 0.7f, Accent.Z * 0.7f, 1f);
        AccentSoft = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.35f);
    }

    /// Attention colour.
    public static readonly Vector4 Warning = new(0.98f, 0.74f, 0.28f, 1f);

    /// Failure.
    public static readonly Vector4 Bad = new(0.95f, 0.42f, 0.42f, 1f);

    /// Success - a profile published, a character verified.
    public static readonly Vector4 Good = new(0.44f, 0.88f, 0.52f, 1f);


    /// The panel colour nudged toward the accent, for surfaces that need to read as lit.
    public static Vector4 Tinted(float amount) => Vector4.Lerp(Panel, Accent, amount);

    public static int Push()
    {
        var count = 0;
        Color(ImGuiCol.WindowBg, Background, ref count);
        Color(ImGuiCol.ChildBg, Panel, ref count);
        Color(ImGuiCol.PopupBg, Background, ref count);
        Color(ImGuiCol.Text, Text, ref count);


        Color(ImGuiCol.Button, Tinted(0.10f), ref count);
        Color(ImGuiCol.ButtonHovered, Tinted(0.20f), ref count);
        Color(ImGuiCol.ButtonActive, Tinted(0.32f), ref count);
        Color(ImGuiCol.FrameBg, Tinted(0.05f), ref count);
        Color(ImGuiCol.FrameBgHovered, Tinted(0.14f), ref count);
        Color(ImGuiCol.FrameBgActive, Tinted(0.22f), ref count);
        Color(ImGuiCol.SliderGrab, Accent, ref count);
        Color(ImGuiCol.SliderGrabActive, AccentHover, ref count);
        Color(ImGuiCol.CheckMark, Accent, ref count);
        Color(ImGuiCol.Header, Tinted(0.18f), ref count);
        Color(ImGuiCol.HeaderHovered, Tinted(0.26f), ref count);
        Color(ImGuiCol.HeaderActive, Tinted(0.34f), ref count);
        Color(ImGuiCol.Tab, Tinted(0.04f), ref count);
        Color(ImGuiCol.TabHovered, Tinted(0.18f), ref count);
        Color(ImGuiCol.TabActive, Tinted(0.30f), ref count);
        Color(ImGuiCol.TextDisabled, TextDisabled, ref count);
        Color(ImGuiCol.TitleBg, Background, ref count);
        Color(ImGuiCol.TitleBgActive, Panel, ref count);
        Color(ImGuiCol.Border, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.55f), ref count);
        Color(ImGuiCol.TableHeaderBg, Panel, ref count);
        Color(ImGuiCol.TableBorderLight, Border, ref count);
        Color(ImGuiCol.TableBorderStrong, Border, ref count);
        Color(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0.20f), ref count);
        Color(ImGuiCol.ScrollbarGrab, Tinted(0.22f), ref count);
        Color(ImGuiCol.ScrollbarGrabHovered, Tinted(0.32f), ref count);
        Color(ImGuiCol.ScrollbarGrabActive, Tinted(0.42f), ref count);

        Color(ImGuiCol.ResizeGrip, new Vector4(0f, 0f, 0f, 0f), ref count);
        Color(ImGuiCol.ResizeGripHovered, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.45f), ref count);
        Color(ImGuiCol.ResizeGripActive, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.80f), ref count);

        var scale = UiHelpers.Scale;

        ImGui.PushStyleVar(
            ImGuiStyleVar.WindowPadding,
            Vector2.Max(ImGui.GetStyle().WindowPadding, new Vector2(8f, 8f) * scale));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1.5f * scale);

        return count;
    }

    public static void Pop(int colorCount)
    {
        ImGui.PopStyleVar(StyleVarCount);
        ImGui.PopStyleColor(colorCount);
    }

    /// Must match the number of PushStyleVar calls in Push.
    private const int StyleVarCount = 7;

    private static void Color(ImGuiCol slot, Vector4 value, ref int count)
    {
        ImGui.PushStyleColor(slot, value);
        count++;
    }

    /// Corner radius for cards.
    public static float CardRounding => 12f * UiHelpers.Scale;

    /// Draws a layered drop shadow and rounded panel at the cursor, then opens a transparent child so normal
    /// layout works on top of it.
    public static void BeginCard(string id, Vector2 size, Vector4? accent = null, Vector4? gradientTint = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var scale = UiHelpers.Scale;

        for (var i = 3; i >= 1; i--)
        {
            var offset = new Vector2(0, i * 2f * scale);
            drawList.AddRectFilled(
                pos + offset, pos + size + offset,
                ImGui.GetColorU32(new Vector4(0, 0, 0, 0.05f * i)), CardRounding);
        }

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Panel), CardRounding);

        if (gradientTint is { } tint)
        {
            var topColor = ImGui.GetColorU32(Vector4.Lerp(Panel, tint, 0.24f));
            var panelU32 = ImGui.GetColorU32(Panel);
            var fadeBottom = pos.Y + MathF.Min(size.Y * 0.55f, size.Y);

            drawList.AddRectFilled(
                pos, pos + new Vector2(size.X, CardRounding), topColor, CardRounding, ImDrawFlags.RoundCornersTop);
            drawList.AddRectFilledMultiColor(
                pos + new Vector2(0f, CardRounding), new Vector2(pos.X + size.X, fadeBottom),
                topColor, topColor, panelU32, panelU32);
        }

        var borderColor = accent is { } a ? new Vector4(a.X, a.Y, a.Z, 0.55f) : Border;
        drawList.AddRect(
            pos, pos + size, ImGui.GetColorU32(borderColor), CardRounding, ImDrawFlags.None,
            (accent is null ? 1.2f : 1.6f) * scale);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
        ImGui.BeginChild(id, size, false, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + (new Vector2(14, 12) * scale));
        ImGui.BeginGroup();
    }

    public static void EndCard()
    {
        ImGui.EndGroup();
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }


    /// Air between a panel's edge and its contents.
    public static Vector2 PanelPadding => new Vector2(14f, 12f) * UiHelpers.Scale;

    public static float PanelRounding => 10f * UiHelpers.Scale;

    private readonly record struct PanelState(Vector2 Origin, float Width, Vector4? Accent, bool Gradient);

    private static readonly System.Collections.Generic.Stack<PanelState> OpenPanels = new();

    /// How wide the content region really is, allowing for any panel around it.
    public static float ContentWidth =>
        OpenPanels.Count > 0
            ? OpenPanels.Peek().Width - (PanelPadding.X * 2f)
            : ImGui.GetContentRegionAvail().X;

    /// Opens a panel that sizes itself to whatever is drawn into it.
    public static void BeginPanel(string id, Vector4? accent = null, bool gradient = false)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padding = PanelPadding;

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        OpenPanels.Push(new PanelState(origin, width, accent, gradient));

        ImGui.SetCursorScreenPos(origin + padding);
        ImGui.PushID(id);
        ImGui.BeginGroup();
    }

    public static void EndPanel()
    {
        var state = OpenPanels.Pop();

        ImGui.EndGroup();
        ImGui.PopID();

        var padding = PanelPadding;
        var scale = UiHelpers.Scale;
        var rounding = PanelRounding;

        var height = ImGui.GetItemRectSize().Y + (padding.Y * 2f);

        var min = state.Origin;
        var max = new Vector2(min.X + state.Width, min.Y + height);

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);

        for (var i = 3; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 1.6f * scale);
            drawList.AddRectFilled(
                min + offset, max + offset, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.045f * i)), rounding);
        }

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Panel), rounding);

        if (state.Gradient)
        {
            var top = ImGui.GetColorU32(Vector4.Lerp(Panel, Accent, 0.16f));
            var bottom = ImGui.GetColorU32(Panel);
            var fade = min.Y + MathF.Min(height, 74f * scale);

            drawList.AddRectFilled(
                min, new Vector2(max.X, min.Y + rounding), top, rounding, ImDrawFlags.RoundCornersTop);
            drawList.AddRectFilledMultiColor(
                new Vector2(min.X, min.Y + rounding), new Vector2(max.X, fade), top, top, bottom, bottom);
        }

        drawList.AddLine(
            new Vector2(min.X + rounding, min.Y + (1f * scale)),
            new Vector2(max.X - rounding, min.Y + (1f * scale)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), 1f * scale);

        var border = state.Accent is { } a
            ? new Vector4(a.X, a.Y, a.Z, 0.55f)
            : new Vector4(Border.X, Border.Y, Border.Z, 0.7f);

        drawList.AddRect(
            min, max, ImGui.GetColorU32(border), rounding, ImDrawFlags.None,
            (state.Accent is null ? 1f : 1.5f) * scale);

        drawList.ChannelsMerge();

        ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y));
    }

    /// A panel's own heading: tracked capitals with an accent tick, and no rule.
    public static void PanelHeader(string label)
    {
        var text = label.ToUpperInvariant();
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        var scale = UiHelpers.Scale;

        var tickWidth = 3f * scale;
        var tracking = 1.6f * scale;

        var tickTop = origin.Y + (lineHeight * 0.18f);
        drawList.AddRectFilled(
            new Vector2(origin.X, tickTop),
            new Vector2(origin.X + tickWidth, tickTop + (lineHeight * 0.64f)),
            ImGui.GetColorU32(Accent), 1.5f * scale);

        var x = origin.X + tickWidth + (8f * scale);
        var colour = ImGui.GetColorU32(TextDim);

        foreach (var character in text)
        {
            var glyph = character.ToString();
            drawList.AddText(new Vector2(x, origin.Y), colour, glyph);
            x += ImGui.CalcTextSize(glyph).X + tracking;
        }

        ImGui.Dummy(new Vector2(0f, lineHeight + (8f * scale)));
    }

    /// A hairline across a panel, for splitting one off from a footer.
    public static void PanelDivider(float width)
    {
        var scale = UiHelpers.Scale;
        var origin = ImGui.GetCursorScreenPos();

        var solid = ImGui.GetColorU32(new Vector4(Border.X, Border.Y, Border.Z, 0.65f));
        var clear = ImGui.GetColorU32(new Vector4(Border.X, Border.Y, Border.Z, 0f));
        var middle = width / 2f;

        ImGui.GetWindowDrawList().AddRectFilledMultiColor(
            origin, new Vector2(origin.X + middle, origin.Y + (1f * scale)), clear, solid, solid, clear);
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(
            new Vector2(origin.X + middle, origin.Y), new Vector2(origin.X + width, origin.Y + (1f * scale)),
            solid, clear, clear, solid);

        ImGui.Dummy(new Vector2(width, 1f * scale));
    }

    /// A section heading: small tracked capitals, an accent tick, and a rule running to the edge.
    public static void SectionHeader(string label, float tracking = 1.6f, float? ruleWidth = null)
    {
        var text = label.ToUpperInvariant();
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();

        var scale = UiHelpers.Scale;
        var tickWidth = 3f * scale;
        tracking *= scale;

        var tickTop = origin.Y + (lineHeight * 0.18f);
        drawList.AddRectFilled(
            new Vector2(origin.X, tickTop),
            new Vector2(origin.X + tickWidth, tickTop + (lineHeight * 0.64f)),
            ImGui.GetColorU32(Accent), 1.5f * scale);

        var x = origin.X + tickWidth + (8f * scale);
        var colour = ImGui.GetColorU32(TextDim);

        foreach (var character in text)
        {
            var glyph = character.ToString();
            drawList.AddText(new Vector2(x, origin.Y), colour, glyph);
            x += ImGui.CalcTextSize(glyph).X + tracking;
        }

        var ruleStart = x + (10f * scale);
        var ruleEnd = origin.X + (ruleWidth ?? ImGui.GetContentRegionAvail().X);
        if (ruleEnd > ruleStart + (12f * scale))
        {
            var y = origin.Y + (lineHeight / 2f);
            drawList.AddRectFilledMultiColor(
                new Vector2(ruleStart, y), new Vector2(ruleEnd, y + (1f * scale)),
                ImGui.GetColorU32(new Vector4(Border.X, Border.Y, Border.Z, 0.75f)),
                ImGui.GetColorU32(new Vector4(Border.X, Border.Y, Border.Z, 0f)),
                ImGui.GetColorU32(new Vector4(Border.X, Border.Y, Border.Z, 0f)),
                ImGui.GetColorU32(new Vector4(Border.X, Border.Y, Border.Z, 0.75f)));
        }

        ImGui.Dummy(new Vector2(0, lineHeight));
    }

    /// A dim label above a bright value, the suite's standard readout pairing.
    public static void Stat(string label, string value, Vector4? valueColor = null)
    {
        ImGui.TextColored(TextDim, label);
        ImGui.TextColored(valueColor ?? Text, value);
    }

    /// Table chrome: banded rows, hairline separators, no vertical rules.
    public static int PushTableStyle()
    {
        var count = 0;
        Color(ImGuiCol.TableRowBg, new Vector4(0f, 0f, 0f, 0f), ref count);
        Color(ImGuiCol.TableRowBgAlt, new Vector4(1f, 1f, 1f, 0.022f), ref count);
        Color(ImGuiCol.TableHeaderBg, new Vector4(0f, 0f, 0f, 0.22f), ref count);
        Color(ImGuiCol.TableBorderLight, new Vector4(Border.X, Border.Y, Border.Z, 0.28f), ref count);
        Color(ImGuiCol.TableBorderStrong, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.35f), ref count);

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8f, 5f) * UiHelpers.Scale);
        return count;
    }

    public static void PopTableStyle(int colorCount)
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(colorCount);
    }
}
