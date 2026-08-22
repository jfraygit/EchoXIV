using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI;

/// The first thing anybody sees: the wordmark assembling itself, a scatter of sparkles, and then the window
/// opening into the plugin proper.
public sealed class IntroSequence
{
    /// Letters rise and fade in, staggered.
    private const float RevealSeconds = 1.15f;

    /// The wordmark sits with the sparkles going.
    private const float HoldSeconds = 1.35f;

    /// Everything fades as the window starts to grow.
    private const float FadeSeconds = 0.55f;

    private const float TotalSeconds = RevealSeconds + HoldSeconds + FadeSeconds;

    /// How far apart the letters start, and how long each takes.
    private const float LetterStagger = 0.07f;
    private const float LetterDuration = 0.42f;

    private const string Wordmark = "ECHOGLAM";

    /// Fixed rather than random, so the sequence is the same every time it is played.
    private static readonly (float X, float Y, float Size, float Phase, float Delay)[] Sparkles =
    [
        (-0.46f, -0.34f, 1.00f, 0.0f, 0.45f),
        (0.41f, -0.42f, 0.72f, 1.7f, 0.60f),
        (0.52f, 0.30f, 0.95f, 3.1f, 0.72f),
        (-0.55f, 0.26f, 0.66f, 2.2f, 0.85f),
        (0.14f, -0.52f, 0.58f, 4.4f, 0.95f),
        (-0.18f, 0.50f, 0.80f, 5.0f, 1.05f),
        (0.66f, -0.10f, 0.52f, 0.9f, 1.15f),
        (-0.68f, -0.04f, 0.62f, 2.7f, 1.25f),
        (0.29f, 0.46f, 0.48f, 3.8f, 1.35f),
        (-0.30f, -0.50f, 0.55f, 1.2f, 1.45f),
        (0.02f, 0.60f, 0.42f, 4.9f, 1.55f),
        (-0.62f, 0.44f, 0.45f, 0.4f, 1.65f),
    ];

    /// The most a single frame may advance the sequence.
    private const float MaximumFrameStep = 1f / 30f;

    /// How long before a click will skip it.
    private const float SkippableAfter = 0.4f;

    private float elapsed;
    private bool skipped;

    /// Whether the sequence has run its course.
    public bool Finished => skipped || elapsed >= TotalSeconds;

    /// How faded the whole thing is, for the window to match its own chrome to.
    public float Alpha => Finished ? 0f : 1f - Eased(Math.Clamp((elapsed - RevealSeconds - HoldSeconds) / FadeSeconds, 0f, 1f));

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

        DrawGlow(drawList, centre, size, alpha);
        DrawWordmark(drawList, fonts, centre, alpha);
        DrawSparkles(drawList, centre, size, alpha);
        DrawBorder(drawList, min, max, scale, alpha);

        drawList.PopClipRect();

        if (elapsed > SkippableAfter
            && ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
            && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            skipped = true;
    }

    /// A soft accent bloom behind the wordmark, growing as the letters land.
    private void DrawGlow(ImDrawListPtr drawList, Vector2 centre, Vector2 size, float alpha)
    {
        var progress = Eased(Math.Clamp(elapsed / RevealSeconds, 0f, 1f));
        var radius = MathF.Min(size.X, size.Y) * (0.28f + (0.22f * progress));

        for (var i = 8; i >= 1; i--)
        {
            var t = i / 8f;
            drawList.AddCircleFilled(
                centre, radius * t,
                ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.030f * progress * alpha)),
                48);
        }
    }

    /// The wordmark, set a letter at a time.
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

        for (var i = 0; i < Wordmark.Length; i++)
        {
            var glyph = Wordmark[i].ToString();
            var width = ImGui.CalcTextSize(glyph).X;

            var progress = Math.Clamp((elapsed - (i * LetterStagger)) / LetterDuration, 0f, 1f);
            var eased = Eased(progress);

            if (progress > 0f)
            {
                var drop = (1f - eased) * 14f * scale;
                var letterAlpha = eased * alpha;

                drawList.AddText(
                    new Vector2(x + 1.5f * scale, baseline + drop + (1.5f * scale)),
                    ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f * letterAlpha)), glyph);

                drawList.AddText(
                    new Vector2(x, baseline + drop),
                    ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, letterAlpha)), glyph);
            }

            x += width + tracking;
        }

        var ruleProgress = Eased(Math.Clamp((elapsed - (LetterStagger * Wordmark.Length)) / 0.5f, 0f, 1f));
        if (ruleProgress <= 0f)
            return;

        var half = (total / 2f) * ruleProgress;
        var ruleY = baseline + fontSize + (7f * scale);

        drawList.AddRectFilledMultiColor(
            new Vector2(centre.X - half, ruleY),
            new Vector2(centre.X, ruleY + (2f * scale)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0f)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, alpha)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, alpha)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0f)));

        drawList.AddRectFilledMultiColor(
            new Vector2(centre.X, ruleY),
            new Vector2(centre.X + half, ruleY + (2f * scale)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, alpha)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0f)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0f)),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, alpha)));
    }

    private void DrawSparkles(ImDrawListPtr drawList, Vector2 centre, Vector2 size, float alpha)
    {
        var scale = UiHelpers.Scale;
        var spread = new Vector2(size.X * 0.44f, size.Y * 0.40f);

        foreach (var (x, y, sparkleSize, phase, delay) in Sparkles)
        {
            if (elapsed < delay)
                continue;

            var age = elapsed - delay;

            var arrival = Eased(Math.Clamp(age / 0.35f, 0f, 1f));
            var twinkle = 0.55f + (0.45f * MathF.Sin((age * 2.4f) + phase));
            var drift = MathF.Sin((age * 1.1f) + phase) * 3f * scale;

            var position = centre + new Vector2(x * spread.X, (y * spread.Y) + drift);
            DrawSparkle(drawList, position, sparkleSize * 9f * scale, arrival * twinkle * alpha);
        }
    }

    /// A four-pointed star: two tapered spindles crossed, with a bright core.
    private static void DrawSparkle(ImDrawListPtr drawList, Vector2 centre, float size, float alpha)
    {
        if (alpha <= 0.01f)
            return;

        var waist = size * 0.20f;
        var colour = ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, alpha));

        drawList.AddQuadFilled(
            new Vector2(centre.X, centre.Y - size),
            new Vector2(centre.X + waist, centre.Y),
            new Vector2(centre.X, centre.Y + size),
            new Vector2(centre.X - waist, centre.Y),
            colour);

        drawList.AddQuadFilled(
            new Vector2(centre.X - size, centre.Y),
            new Vector2(centre.X, centre.Y - waist),
            new Vector2(centre.X + size, centre.Y),
            new Vector2(centre.X, centre.Y + waist),
            colour);

        drawList.AddCircleFilled(
            centre, size * 0.17f, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha * 0.85f)));
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
