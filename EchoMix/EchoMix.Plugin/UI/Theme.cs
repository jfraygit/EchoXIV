using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI;

/// Small dark/neon theme applied around each window's Draw() so the plugin doesn't inherit the default ImGui
/// grey look.
public static class Theme
{
    public static readonly Vector4 Background = new(0.07f, 0.07f, 0.09f, 1f);
    public static readonly Vector4 Panel = new(0.11f, 0.11f, 0.14f, 1f);

    public static readonly Vector4 Accent = new(0.55f, 0.35f, 0.95f, 1f);
    public static readonly Vector4 AccentHover = new(0.65f, 0.45f, 1f, 1f);
    public static readonly Vector4 AccentActive = new(0.45f, 0.25f, 0.85f, 1f);

    public static readonly Vector4 Text = new(0.92f, 0.92f, 0.95f, 1f);

    public static readonly Vector4 FieldBg = new(0.045f, 0.045f, 0.055f, 1f);

    public static Vector4 CyanAccent = new(0.25f, 0.85f, 0.95f, 1f);
    public static Vector4 OrangeAccent = new(1f, 0.6f, 0.15f, 1f);
    public static readonly Vector4 Border = new(0.25f, 0.25f, 0.3f, 0.5f);

    public static readonly Vector4 FixedCyan = new(0.25f, 0.85f, 0.95f, 1f);
    public static readonly Vector4 FixedOrange = new(1f, 0.6f, 0.15f, 1f);

    public static Vector4 NeutralAccent = Vector4.Lerp(CyanAccent, OrangeAccent, 0.5f);
    public static Vector4 NeutralAccentHover = Vector4.Lerp(NeutralAccent, Vector4.One, 0.3f);
    public static Vector4 NeutralAccentActive = new(NeutralAccent.X * 0.7f, NeutralAccent.Y * 0.7f, NeutralAccent.Z * 0.7f, 1f);

    /// Applies a user's custom Deck A/Deck B/blend colors, re-deriving NeutralAccentHover/
    /// NeutralAccentActive with the exact same formulas their own field initializers use above.
    public static void ApplyCustomColors(Vector4 deckA, Vector4 deckB, Vector4 blend)
    {
        CyanAccent = deckA;
        OrangeAccent = deckB;
        NeutralAccent = blend;
        NeutralAccentHover = Vector4.Lerp(NeutralAccent, Vector4.One, 0.3f);
        NeutralAccentActive = new Vector4(NeutralAccent.X * 0.7f, NeutralAccent.Y * 0.7f, NeutralAccent.Z * 0.7f, 1f);
    }

    public static int Push()
    {
        var count = 0;
        Color(ImGuiCol.WindowBg, Background, ref count);
        Color(ImGuiCol.ChildBg, Panel, ref count);
        Color(ImGuiCol.PopupBg, Panel, ref count);
        Color(ImGuiCol.Text, Text, ref count);
        Color(ImGuiCol.Button, NeutralAccent, ref count);
        Color(ImGuiCol.ButtonHovered, NeutralAccentHover, ref count);
        Color(ImGuiCol.ButtonActive, NeutralAccentActive, ref count);
        Color(ImGuiCol.FrameBg, FieldBg, ref count);
        Color(ImGuiCol.FrameBgHovered, NeutralAccentHover, ref count);
        Color(ImGuiCol.FrameBgActive, NeutralAccentActive, ref count);
        Color(ImGuiCol.SliderGrab, CyanAccent, ref count);
        Color(ImGuiCol.SliderGrabActive, NeutralAccentHover, ref count);
        Color(ImGuiCol.CheckMark, CyanAccent, ref count);
        Color(ImGuiCol.Header, NeutralAccent, ref count);
        Color(ImGuiCol.HeaderHovered, NeutralAccentHover, ref count);
        Color(ImGuiCol.HeaderActive, NeutralAccentActive, ref count);
        Color(ImGuiCol.Tab, Panel, ref count);
        Color(ImGuiCol.TabHovered, NeutralAccentHover, ref count);
        Color(ImGuiCol.TabActive, NeutralAccent, ref count);
        Color(ImGuiCol.TitleBg, Background, ref count);
        Color(ImGuiCol.TitleBgActive, Panel, ref count);
        Color(ImGuiCol.Border, Border, ref count);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);

        return count;
    }

    public static void Pop(int colorCount)
    {
        ImGui.PopStyleVar(6);
        ImGui.PopStyleColor(colorCount);
    }

    private static void Color(ImGuiCol slot, Vector4 value, ref int count)
    {
        ImGui.PushStyleColor(slot, value);
        count++;
    }

    private const float CardRounding = 12f;

    /// Draws a layered drop shadow + rounded panel behind the cursor position, then opens a transparent child
    /// so normal ImGui layout still works on top of it.
    public static void BeginCard(string id, Vector2 size, float glow = 0f, Vector4? glowColor = null, float fontScale = 1f, Vector4? gradientTint = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        for (var i = 3; i >= 1; i--)
        {
            var offset = new Vector2(0, i * 2f);
            var alpha = 0.05f * i;
            drawList.AddRectFilled(pos + offset, pos + size + offset, ImGui.GetColorU32(new Vector4(0, 0, 0, alpha)), CardRounding);
        }

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Panel), CardRounding);

        if (gradientTint is { } tint)
        {
            var topColor = Vector4.Lerp(Panel, tint, 0.24f);
            var topColorU32 = ImGui.GetColorU32(topColor);
            var panelU32 = ImGui.GetColorU32(Panel);
            var fadeBottom = pos.Y + MathF.Min(size.Y * 0.55f, size.Y);

            drawList.AddRectFilled(pos, pos + new Vector2(size.X, CardRounding), topColorU32, CardRounding, ImDrawFlags.RoundCornersTop);
            drawList.AddRectFilledMultiColor(
                pos + new Vector2(0f, CardRounding), new Vector2(pos.X + size.X, fadeBottom),
                topColorU32, topColorU32, panelU32, panelU32);
        }

        if (glow > 0.01f)
        {
            var baseColor = glowColor ?? CyanAccent;
            var litColor = new Vector4(baseColor.X, baseColor.Y, baseColor.Z, glow * 0.9f);
            drawList.AddRect(pos, pos + size, ImGui.GetColorU32(litColor), CardRounding, ImDrawFlags.None, 1.5f + (glow * 2f));

            DrawChasingHighlight(drawList, pos, size, glow);
        }
        else
        {
            drawList.AddRect(pos, pos + size, ImGui.GetColorU32(Border), CardRounding, ImDrawFlags.None, 1.2f);
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
        ImGui.BeginChild(id, size, false, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetWindowFontScale(fontScale);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(14, 12));
        ImGui.BeginGroup();
    }

    public static void EndCard()
    {
        ImGui.EndGroup();
        ImGui.EndChild();
        ImGui.PopStyleColor(2);
    }

    /// A little white comet travels around the card's border while `glow` is lit up (i.e.
    private static void DrawChasingHighlight(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float glow)
    {
        const int trailDots = 7;
        const float trailSpacing = 0.018f;
        const float loopsPerSecond = 0.35f;

        var r = MathF.Min(CardRounding, MathF.Min(size.X, size.Y) / 2f);
        var headT = (float)(ImGui.GetTime() * loopsPerSecond % 1.0);

        for (var i = 0; i < trailDots; i++)
        {
            var t = headT - (i * trailSpacing);
            var trailAlpha = glow * MathF.Pow(1f - (i / (float)trailDots), 1.5f);
            var dotPos = RoundedRectPerimeterPoint(pos, size, r, t);
            var dotRadius = MathF.Max(1f, 3.5f - (i * 0.3f));

            if (i == 0)
                drawList.AddCircleFilled(dotPos, dotRadius * 2f, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, trailAlpha * 0.35f)));
            drawList.AddCircleFilled(dotPos, dotRadius, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, trailAlpha)));
        }
    }

    /// Maps t (wrapped to [0,1)) to a point walking clockwise around a rounded rect's perimeter, starting at
    /// the top edge's left end.
    internal static Vector2 RoundedRectPerimeterPoint(Vector2 pos, Vector2 size, float r, float t)
    {
        var straightX = size.X - (2f * r);
        var straightY = size.Y - (2f * r);
        var arcLen = r * MathF.PI / 2f;
        var perimeter = (2f * straightX) + (2f * straightY) + (4f * arcLen);

        var d = (((t % 1f) + 1f) % 1f) * perimeter;

        if (d < straightX)
            return new Vector2(pos.X + r + d, pos.Y);
        d -= straightX;

        if (d < arcLen)
        {
            var a = (-MathF.PI / 2f) + (d / arcLen * (MathF.PI / 2f));
            var c = new Vector2(pos.X + size.X - r, pos.Y + r);
            return c + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * r);
        }
        d -= arcLen;

        if (d < straightY)
            return new Vector2(pos.X + size.X, pos.Y + r + d);
        d -= straightY;

        if (d < arcLen)
        {
            var a = d / arcLen * (MathF.PI / 2f);
            var c = new Vector2(pos.X + size.X - r, pos.Y + size.Y - r);
            return c + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * r);
        }
        d -= arcLen;

        if (d < straightX)
            return new Vector2(pos.X + size.X - r - d, pos.Y + size.Y);
        d -= straightX;

        if (d < arcLen)
        {
            var a = (MathF.PI / 2f) + (d / arcLen * (MathF.PI / 2f));
            var c = new Vector2(pos.X + r, pos.Y + size.Y - r);
            return c + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * r);
        }
        d -= arcLen;

        if (d < straightY)
            return new Vector2(pos.X, pos.Y + size.Y - r - d);
        d -= straightY;

        var aLast = MathF.PI + (d / arcLen * (MathF.PI / 2f));
        var cLast = new Vector2(pos.X + r, pos.Y + r);
        return cLast + (new Vector2(MathF.Cos(aLast), MathF.Sin(aLast)) * r);
    }
}
