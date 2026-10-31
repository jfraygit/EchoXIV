using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI;

/// EchoGlam's palette and its ImGui style stack.
public static class Theme
{
    /// THE GROUND, AND IT IS NEARLY BLACK AS OF 2.0.
    public static readonly Vector4 Background = new(0.026f, 0.022f, 0.034f, 1f);

    /// A surface on the ground.
    public static readonly Vector4 Panel = new(0.052f, 0.044f, 0.068f, 1f);

    public static readonly Vector4 Text = new(0.94f, 0.93f, 0.96f, 1f);

    /// The resting edge.
    public static readonly Vector4 Border = new(0.42f, 0.38f, 0.50f, 0.22f);

    /// Muted text for labels, units and secondary detail.
    public static readonly Vector4 TextDim = new(0.64f, 0.62f, 0.70f, 1f);

    public static readonly Vector4 TextDisabled = new(0.40f, 0.38f, 0.46f, 1f);

    /// Orchid.
    public static readonly Vector4 DefaultAccent = new(0.90f, 0.44f, 0.76f, 1f);

    /// EchoGlam's accent.
    public static Vector4 Accent { get; private set; } = DefaultAccent;

    public static Vector4 AccentHover { get; private set; } = Vector4.Lerp(DefaultAccent, Vector4.One, 0.3f);

    public static Vector4 AccentActive { get; private set; } =
        new(DefaultAccent.X * 0.7f, DefaultAccent.Y * 0.7f, DefaultAccent.Z * 0.7f, 1f);

    /// The accent at low alpha, for card tints and fills.
    public static Vector4 AccentSoft { get; private set; } =
        new(DefaultAccent.X, DefaultAccent.Y, DefaultAccent.Z, 0.35f);


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

    /// Success - a submission accepted, a look applied.
    public static readonly Vector4 Good = new(0.44f, 0.88f, 0.52f, 1f);

    /// The favourite mark.
    public static readonly Vector4 Favourite = new(0.98f, 0.78f, 0.32f, 1f);

    /// The vote mark.
    public static readonly Vector4 Vote = new(0.96f, 0.40f, 0.48f, 1f);

    /// The panel colour nudged toward the accent, for surfaces that need to read as lit.
    public static Vector4 Tinted(float amount) => Vector4.Lerp(Panel, Accent, amount);

    /// What one Push put on the stacks, so Pop can take exactly that off again.
    public readonly record struct ThemePush(int Colours, int Vars);

    public static ThemePush Push()
    {
        var count = 0;
        var vars = 0;
        Color(ImGuiCol.WindowBg, Background, ref count);
        Color(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f), ref count);
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

        Var(
            ImGuiStyleVar.WindowPadding,
            Vector2.Max(ImGui.GetStyle().WindowPadding, new Vector2(8f, 8f) * scale),
            ref vars);

        Var(ImGuiStyleVar.WindowRounding, 8f * scale, ref vars);
        Var(ImGuiStyleVar.FrameRounding, Rad.Ctl, ref vars);
        Var(ImGuiStyleVar.GrabRounding, Rad.Ctl, ref vars);
        Var(ImGuiStyleVar.ChildRounding, Rad.Ctl, ref vars);
        Var(ImGuiStyleVar.PopupRounding, Rad.Ctl, ref vars);
        Var(ImGuiStyleVar.PopupBorderSize, 1.5f * scale, ref vars);

        return new ThemePush(count, vars);
    }

    public static void Pop(ThemePush pushed)
    {
        ImGui.PopStyleVar(pushed.Vars);
        ImGui.PopStyleColor(pushed.Colours);
    }

    private static void Color(ImGuiCol slot, Vector4 value, ref int count)
    {
        ImGui.PushStyleColor(slot, value);
        count++;
    }

    private static void Var(ImGuiStyleVar slot, float value, ref int count)
    {
        ImGui.PushStyleVar(slot, value);
        count++;
    }

    private static void Var(ImGuiStyleVar slot, Vector2 value, ref int count)
    {
        ImGui.PushStyleVar(slot, value);
        count++;
    }

    /// Corner radius for cards.
    public static float CardRounding => Rad.Card;



    /// A section heading: small tracked capitals, an accent tick, and a rule running to the edge.
    public static float HeaderRun(string label, float tracking = 1.6f)
    {
        var scale = UiHelpers.Scale;

        return TickWidth(scale) + (HeaderGapDesign * scale)
            + Foundation.Text.TrackedWidth(label.ToUpperInvariant(), tracking)
            + (tracking * scale) + (RuleGapDesign * scale);
    }

    private static float TickWidth(float scale) => 3f * scale;

    /// Between the tick and the label, and between the label and the rule.
    private const float HeaderGapDesign = 8f;

    private const float RuleGapDesign = 10f;

    public static void SectionHeader(string label, float tracking = 1.6f, float? ruleWidth = null)
    {
        var text = label.ToUpperInvariant();
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();

        var scale = UiHelpers.Scale;
        var tickWidth = TickWidth(scale);

        var tickTop = origin.Y + (lineHeight * 0.18f);
        drawList.AddRectFilled(
            new Vector2(origin.X, tickTop),
            new Vector2(origin.X + tickWidth, tickTop + (lineHeight * 0.64f)),
            ImGui.GetColorU32(Accent), 1.5f * scale);

        var labelStart = origin.X + tickWidth + (HeaderGapDesign * scale);
        var labelRoom = MathF.Max(0f, (origin.X + (ruleWidth ?? Layout.Inner)) - labelStart);

        var run = Foundation.Text.Tracked(
            drawList, new Vector2(labelStart, origin.Y), text, Col.U32(TextDim), tracking, labelRoom);

        var ruleStart = labelStart + run + (tracking * scale) + (RuleGapDesign * scale);
        var ruleEnd = origin.X + (ruleWidth ?? Layout.Inner);
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

}
