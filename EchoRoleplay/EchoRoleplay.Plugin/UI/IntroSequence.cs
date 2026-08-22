using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoRoleplay.UI;

/// The first thing anybody sees: a stage curtain parting on the wordmark, lit from above.
public sealed class IntroSequence
{
    private const string Wordmark = "ECHOROLEPLAY";

    /// How long the curtain hangs closed before it moves.
    private const float CurtainDelay = 0.45f;

    /// How long the parting takes.
    private const float CurtainSeconds = 1.10f;

    /// Curtain open, wordmark revealed.
    private const float RevealSeconds = CurtainDelay + CurtainSeconds;

    /// The wordmark sits lit, with the light breathing.
    private const float HoldSeconds = 1.35f;

    /// Everything fades as the window starts to grow.
    private const float FadeSeconds = 0.55f;

    private const float TotalSeconds = RevealSeconds + HoldSeconds + FadeSeconds;

    /// Vertical folds drawn into each curtain panel.
    private const int FoldsPerPanel = 7;

    /// How far the closed curtain sways, in design pixels, and how fast.
    private const float SwayPixels = 1.6f;
    private const float SwaySpeed = 2.1f;

    /// Strips making up the light beam.
    private const int BeamStrips = 22;

    /// The most a single frame may advance the sequence.
    private const float MaximumFrameStep = 1f / 30f;

    /// How long before a click will skip it.
    private const float SkippableAfter = 0.4f;

    private float elapsed;
    private bool skipped;

    /// Whether the sequence has run its course.
    public bool Finished => skipped || elapsed >= TotalSeconds;

    /// How faded the whole thing is, for the window to match its own chrome to.
    public float Alpha => Finished
        ? 0f
        : 1f - Eased(Math.Clamp((elapsed - RevealSeconds - HoldSeconds) / FadeSeconds, 0f, 1f));

    /// How far the curtain has drawn back, nought to one.
    private float CurtainProgress => Eased(Math.Clamp((elapsed - CurtainDelay) / CurtainSeconds, 0f, 1f));

    /// Draws the sequence into the whole of the window it is called from.
    public void Draw(Fonts fonts)
    {
        elapsed += MathF.Min(ImGui.GetIO().DeltaTime, MaximumFrameStep);

        var scale = UiHelpers.Scale;
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        var centre = origin + (size / 2f);
        var alpha = Alpha;

        drawList.PushClipRectFullScreen();

        var inset = 1.5f * scale;
        var min = origin + new Vector2(inset, inset);
        var max = origin + size - new Vector2(inset, inset);

        drawList.AddRectFilled(
            min, max, ImGui.GetColorU32(new Vector4(Theme.Background.X, Theme.Background.Y, Theme.Background.Z, alpha)),
            10f * scale);

        DrawBeam(drawList, min, max, alpha);
        DrawLightPool(drawList, centre, size, alpha);
        DrawWordmark(drawList, fonts, centre, alpha);
        DrawCurtain(drawList, min, max, scale, alpha);
        DrawBorder(drawList, min, max, scale, alpha);

        drawList.PopClipRect();

        if (elapsed > SkippableAfter
            && ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
            && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            skipped = true;
    }

    /// A spotlight beam falling from above the wordmark.
    private void DrawBeam(ImDrawListPtr drawList, Vector2 min, Vector2 max, float alpha)
    {
        var lit = CurtainProgress * alpha;
        if (lit <= 0.01f)
            return;

        var width = max.X - min.X;
        var height = max.Y - min.Y;
        var centreX = (min.X + max.X) / 2f;

        var topWidth = width * 0.12f;
        var bottomWidth = width * 0.62f;
        var bottom = min.Y + (height * 0.78f);
        var stripHeight = (bottom - min.Y) / BeamStrips;

        for (var i = 0; i < BeamStrips; i++)
        {
            var t = i / (float)(BeamStrips - 1);
            var half = (topWidth + ((bottomWidth - topWidth) * t)) / 2f;
            var y = min.Y + (i * stripHeight);

            var strength = (1f - t) * 0.055f * lit;

            drawList.AddRectFilled(
                new Vector2(centreX - half, y),
                new Vector2(centreX + half, y + stripHeight + 1f),
                ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, strength)));
        }
    }

    /// The pool of light the wordmark stands in.
    private void DrawLightPool(ImDrawListPtr drawList, Vector2 centre, Vector2 size, float alpha)
    {
        var lit = CurtainProgress * alpha;
        if (lit <= 0.01f)
            return;

        var breath = 1f + (0.035f * MathF.Sin(elapsed * 1.6f));

        var poolWidth = size.X * 0.66f * breath;
        var poolHeight = size.Y * 0.34f * breath;

        for (var i = 6; i >= 1; i--)
        {
            var t = i / 6f;
            var half = new Vector2(poolWidth * t / 2f, poolHeight * t / 2f);
            var poolMin = centre - half;
            var poolMax = centre + half;

            drawList.AddRectFilled(
                poolMin, poolMax,
                ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.035f * lit)),
                half.Y);
        }
    }

    /// The wordmark, set whole.
    private void DrawWordmark(ImDrawListPtr drawList, Fonts fonts, Vector2 centre, float alpha)
    {
        using var _ = fonts.Header.PushSafe();

        var scale = UiHelpers.Scale;
        var tracking = UiHelpers.WordmarkTracking;
        var fontSize = ImGui.GetFontSize();

        var total = 0f;
        foreach (var character in Wordmark)
            total += ImGui.CalcTextSize(character.ToString()).X + tracking;

        total = MathF.Max(0f, total - tracking);

        var x = centre.X - (total / 2f);
        var baseline = centre.Y - (fontSize / 2f);

        foreach (var character in Wordmark)
        {
            var glyph = character.ToString();

            drawList.AddText(
                new Vector2(x + (1.5f * scale), baseline + (1.5f * scale)),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f * alpha)), glyph);

            drawList.AddText(
                new Vector2(x, baseline),
                ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, alpha)), glyph);

            x += ImGui.CalcTextSize(glyph).X + tracking;
        }

        var ruleAlpha = Eased(Math.Clamp((elapsed - RevealSeconds + 0.25f) / 0.45f, 0f, 1f)) * alpha;
        if (ruleAlpha <= 0.01f)
            return;

        var half = total / 2f;
        var ruleY = baseline + fontSize + (7f * scale);

        drawList.AddRectFilledMultiColor(
            new Vector2(centre.X - half, ruleY),
            new Vector2(centre.X, ruleY + (2f * scale)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0f)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, ruleAlpha)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, ruleAlpha)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0f)));

        drawList.AddRectFilledMultiColor(
            new Vector2(centre.X, ruleY),
            new Vector2(centre.X + half, ruleY + (2f * scale)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, ruleAlpha)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0f)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0f)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, ruleAlpha)));
    }

    /// The two curtain panels, drawn over everything else and parting to reveal it.
    private void DrawCurtain(ImDrawListPtr drawList, Vector2 min, Vector2 max, float scale, float alpha)
    {
        var open = CurtainProgress;
        if (open >= 0.999f)
            return;

        var width = max.X - min.X;
        var halfWidth = width / 2f;

        var sway = open <= 0f ? MathF.Sin(elapsed * SwaySpeed) * SwayPixels * scale : 0f;

        var panelWidth = MathF.Max(0f, (halfWidth * (1f - open)) + sway);
        if (panelWidth <= 0.5f)
            return;

        DrawPanel(drawList, min, max, panelWidth, scale, alpha, leftHand: true);
        DrawPanel(drawList, min, max, panelWidth, scale, alpha, leftHand: false);

        if (open > 0f)
        {
            var gapHalf = halfWidth * open;
            drawList.AddRectFilledMultiColor(
                new Vector2(min.X + halfWidth - gapHalf, min.Y),
                new Vector2(min.X + halfWidth + gapHalf, max.Y),
                ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.10f * alpha)),
                ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0f)),
                ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0f)),
                ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.10f * alpha)));
        }
    }

    /// One panel: a dark accent-tinted body with vertical folds shaded into it, and a bright leading edge
    /// where it meets its partner.
    private static void DrawPanel(
        ImDrawListPtr drawList, Vector2 min, Vector2 max, float panelWidth, float scale, float alpha, bool leftHand)
    {
        var panelMin = leftHand ? min : new Vector2(max.X - panelWidth, min.Y);
        var panelMax = leftHand ? new Vector2(min.X + panelWidth, max.Y) : max;

        var body = Vector4.Lerp(Theme.Background, Theme.Accent, 0.16f);
        drawList.AddRectFilled(
            panelMin, panelMax, ImGui.GetColorU32(new Vector4(body.X, body.Y, body.Z, alpha)),
            10f * scale,
            leftHand ? ImDrawFlags.RoundCornersLeft : ImDrawFlags.RoundCornersRight);

        var foldWidth = panelWidth / FoldsPerPanel;

        for (var i = 0; i < FoldsPerPanel; i++)
        {
            var x = panelMin.X + (i * foldWidth);

            var shade = i % 2 == 0 ? 0.16f : 0.05f;

            drawList.AddRectFilledMultiColor(
                new Vector2(x, panelMin.Y),
                new Vector2(x + foldWidth, panelMax.Y),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, shade * alpha)),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f)),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f)),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, shade * alpha)));
        }

        var edgeX = leftHand ? panelMax.X : panelMin.X;
        drawList.AddRectFilled(
            new Vector2(edgeX - (1.5f * scale), panelMin.Y),
            new Vector2(edgeX + (1.5f * scale), panelMax.Y),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.55f * alpha)));
    }

    /// The accent frame, drawn on as the sequence starts.
    private void DrawBorder(ImDrawListPtr drawList, Vector2 min, Vector2 max, float scale, float alpha)
    {
        var progress = Eased(Math.Clamp(elapsed / 0.7f, 0f, 1f));

        drawList.AddRect(
            min, max,
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.8f * progress * alpha)),
            10f * scale, ImDrawFlags.None, 2f * scale);
    }

    /// Ease-out cubic.
    private static float Eased(float t) => 1f - MathF.Pow(1f - t, 3f);
}
