using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoSim.UI;

/// The shared Echo suite theme: the same dark/neon look EchoMix uses, so plugins in the suite read as
/// siblings rather than unrelated tools.
public static class Theme
{
    public static readonly Vector4 Background = new(0.07f, 0.07f, 0.09f, 1f);
    public static readonly Vector4 Panel = new(0.11f, 0.11f, 0.14f, 1f);
    public static readonly Vector4 Text = new(0.92f, 0.92f, 0.95f, 1f);
    public static readonly Vector4 Border = new(0.25f, 0.25f, 0.3f, 0.5f);

    /// Muted text for labels, units and secondary detail.
    public static readonly Vector4 TextDim = new(0.62f, 0.62f, 0.70f, 1f);

    public static readonly Vector4 TextDisabled = new(0.40f, 0.40f, 0.46f, 1f);

    /// EchoSim's single accent.
    public static Vector4 Accent { get; private set; } = DefaultAccent;

    public static readonly Vector4 DefaultAccent = new(0.98f, 0.72f, 0.22f, 1f);

    public static Vector4 AccentHover { get; private set; } = Vector4.Lerp(DefaultAccent, Vector4.One, 0.3f);

    public static Vector4 AccentActive { get; private set; } = new(DefaultAccent.X * 0.7f, DefaultAccent.Y * 0.7f, DefaultAccent.Z * 0.7f, 1f);

    /// The accent at low alpha, for card tints and plot fills.
    public static Vector4 AccentSoft { get; private set; } = new(DefaultAccent.X, DefaultAccent.Y, DefaultAccent.Z, 0.35f);

    /// A few ready-made accents, so changing the look doesn't require using a colour wheel.
    public static readonly (string Name, Vector4 Colour)[] Presets =
    [
        ("Amber", DefaultAccent),
        ("Crystal", new Vector4(0.30f, 0.72f, 0.98f, 1f)),
        ("Signal", new Vector4(0.24f, 0.88f, 0.60f, 1f)),
        ("Ember", new Vector4(0.96f, 0.42f, 0.30f, 1f)),
        ("Orchid", new Vector4(0.72f, 0.48f, 0.98f, 1f)),
        ("Rose", new Vector4(0.96f, 0.45f, 0.62f, 1f)),
    ];

    /// Re-tints the plugin, re-deriving every shade from the new accent.
    public static void ApplyAccent(Vector4 accent)
    {
        Accent = new Vector4(accent.X, accent.Y, accent.Z, 1f);
        AccentHover = Vector4.Lerp(Accent, Vector4.One, 0.3f);
        AccentActive = new Vector4(Accent.X * 0.7f, Accent.Y * 0.7f, Accent.Z * 0.7f, 1f);
        AccentSoft = new Vector4(Accent.X, Accent.Y, Accent.Z, 0.35f);
    }

    /// Warning/attention colour.
    public static readonly Vector4 Warning = new(0.95f, 0.42f, 0.35f, 1f);

    /// Improvement colour, paired with Warning for regressions.
    public static readonly Vector4 Positive = new(0.36f, 0.85f, 0.52f, 1f);

    public static readonly Vector4 RoleTank = new(0.36f, 0.62f, 0.95f, 1f);
    public static readonly Vector4 RoleHealer = new(0.35f, 0.82f, 0.50f, 1f);
    public static readonly Vector4 RoleDps = new(0.88f, 0.38f, 0.38f, 1f);

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
        Color(ImGuiCol.PlotHistogram, Accent, ref count);
        Color(ImGuiCol.PlotLines, Accent, ref count);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1.5f);

        return count;
    }

    public static void Pop(int colorCount)
    {
        ImGui.PopStyleVar(StyleVarCount);
        ImGui.PopStyleColor(colorCount);
    }

    /// Must match the number of PushStyleVar calls in Push.
    private const int StyleVarCount = 6;

    private static void Color(ImGuiCol slot, Vector4 value, ref int count)
    {
        ImGui.PushStyleColor(slot, value);
        count++;
    }

    private static float CardRounding => UiHelpers.S(12f);

    /// Draws a layered drop shadow and rounded panel at the cursor, then opens a transparent child so normal
    /// layout works on top of it.
    public static void BeginCard(string id, Vector2 size, Vector4? accent = null, Vector4? gradientTint = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        for (var i = 3; i >= 1; i--)
        {
            var offset = new Vector2(0, i * 2f);
            drawList.AddRectFilled(pos + offset, pos + size + offset, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.05f * i)), CardRounding);
        }

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Panel), CardRounding);

        if (gradientTint is { } tint)
        {
            var topColor = ImGui.GetColorU32(Vector4.Lerp(Panel, tint, 0.24f));
            var panelU32 = ImGui.GetColorU32(Panel);
            var fadeBottom = pos.Y + System.MathF.Min(size.Y * 0.55f, size.Y);

            drawList.AddRectFilled(pos, pos + new Vector2(size.X, CardRounding), topColor, CardRounding, ImDrawFlags.RoundCornersTop);
            drawList.AddRectFilledMultiColor(
                pos + new Vector2(0f, CardRounding), new Vector2(pos.X + size.X, fadeBottom),
                topColor, topColor, panelU32, panelU32);
        }

        var borderColor = accent is { } a ? new Vector4(a.X, a.Y, a.Z, 0.55f) : Border;
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(borderColor), CardRounding, ImDrawFlags.None, accent is null ? 1.2f : 1.6f);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
        ImGui.BeginChild(id, size, false, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(UiHelpers.S(14f), UiHelpers.S(12f)));
        ImGui.BeginGroup();
    }

    public static void EndCard()
    {
        ImGui.EndGroup();
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    /// Table chrome: banded rows, hairline separators, and no vertical rules.
    public static int PushTableStyle()
    {
        var count = 0;

        Color(ImGuiCol.TableRowBg, new Vector4(0f, 0f, 0f, 0f), ref count);
        Color(ImGuiCol.TableRowBgAlt, new Vector4(1f, 1f, 1f, 0.022f), ref count);
        Color(ImGuiCol.TableHeaderBg, new Vector4(0f, 0f, 0f, 0.22f), ref count);
        Color(ImGuiCol.TableBorderLight, new Vector4(Border.X, Border.Y, Border.Z, 0.28f), ref count);
        Color(ImGuiCol.TableBorderStrong, new Vector4(Accent.X, Accent.Y, Accent.Z, 0.35f), ref count);

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8f, 5f));

        return count;
    }

    public static void PopTableStyle(int colorCount)
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(colorCount);
    }

    /// A proportion bar, for a share-of-total column.
    public static void ShareBar(float fraction, float width, string label)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();

        var labelSize = ImGui.CalcTextSize(label);
        var barWidth = MathF.Max(24f, width - labelSize.X - 8f);

        var barHeight = UiHelpers.S(7f);
        var barTop = origin.Y + ((lineHeight - barHeight) / 2f);
        var trackMin = new Vector2(origin.X, barTop);
        var trackMax = new Vector2(origin.X + barWidth, barTop + barHeight);
        var rounding = barHeight / 2f;

        drawList.AddRectFilled(trackMin, trackMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.30f)), rounding);

        var clamped = Math.Clamp(fraction, 0f, 1f);
        if (clamped > 0.001f)
        {
            var fillMax = new Vector2(origin.X + (barWidth * clamped), trackMax.Y);
            drawList.AddRectFilled(trackMin, fillMax, ImGui.GetColorU32(Accent), rounding);
        }

        drawList.AddText(
            new Vector2(trackMax.X + 8f, origin.Y),
            ImGui.GetColorU32(TextDim),
            label);

        ImGui.Dummy(new Vector2(width, lineHeight));
    }

    /// A dim label above a bright value, the suite's standard readout pairing.
    public static void Stat(string label, string value, Vector4? valueColor = null)
    {
        ImGui.TextColored(TextDim, label);
        ImGui.TextColored(valueColor ?? Text, value);
    }

    /// A section heading: small tracked capitals, an accent tick, and a rule running to the edge.
    public static void SectionHeader(string label, float tracking = 1.6f, float? ruleWidth = null)
    {
        var text = label.ToUpperInvariant();
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();

        const float tickWidth = 3f;
        var tickTop = origin.Y + (lineHeight * 0.18f);
        drawList.AddRectFilled(
            new Vector2(origin.X, tickTop),
            new Vector2(origin.X + tickWidth, tickTop + (lineHeight * 0.64f)),
            ImGui.GetColorU32(Accent), 1.5f);

        var x = origin.X + tickWidth + 8f;
        var colour = ImGui.GetColorU32(TextDim);

        foreach (var character in text)
        {
            var glyph = character.ToString();
            drawList.AddText(new Vector2(x, origin.Y), colour, glyph);
            x += ImGui.CalcTextSize(glyph).X + tracking;
        }

        var ruleStart = x + 10f;
        var ruleEnd = origin.X + (ruleWidth ?? ImGui.GetContentRegionAvail().X);
        if (ruleEnd > ruleStart + 12f)
        {
            var y = origin.Y + (lineHeight / 2f);
            drawList.AddRectFilledMultiColor(
                new Vector2(ruleStart, y), new Vector2(ruleEnd, y + 1f),
                ImGui.GetColorU32(new Vector4(Border.X, Border.Y, Border.Z, 0.75f)),
                ImGui.GetColorU32(new Vector4(Border.X, Border.Y, Border.Z, 0f)),
                ImGui.GetColorU32(new Vector4(Border.X, Border.Y, Border.Z, 0f)),
                ImGui.GetColorU32(new Vector4(Border.X, Border.Y, Border.Z, 0.75f)));
        }

        ImGui.Dummy(new Vector2(0, lineHeight));
    }
}
