using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Shell;

/// The first thing anybody sees when EchoMix opens: two platters spinning up, the wordmark set a letter at a
/// time under a crossfader sweep, and a 2.0 badge landing on the beat.
public sealed class IntroSequence
{

    /// Platters spin up and slide together.
    private const float SpinSeconds = 1.0f;

    /// Letters rise and set, staggered left to right.
    private const float LetterStagger = 0.075f;
    private const float LetterDuration = 0.45f;
    private const float LettersStartAt = 0.55f;

    /// The crossfader highlight travels across the wordmark.
    private const float SweepStartAt = 1.0f;
    private const float SweepSeconds = 0.9f;

    /// The 2.0 badge drops in with a small overshoot.
    private const float BadgeStartAt = 1.6f;
    private const float BadgeSeconds = 0.55f;

    private const float HoldUntil = 2.85f;
    private const float FadeSeconds = 0.55f;
    private const float TotalSeconds = HoldUntil + FadeSeconds;

    private const string Wordmark = "ECHOMIX";
    private const string Version = "2.0";

    /// Two frames' worth at sixty.
    private const float MaximumFrameStep = 1f / 30f;

    /// A plugin is enabled BY CLICKING, and that click is often still being released on the frame this first
    /// draws - without a moment's grace the intro skips itself on the way in.
    private const float SkippableAfter = 0.4f;

    /// How far the cursor may travel between press and release and still count as a click rather than a drag,
    /// in design pixels.
    private const float DragSlop = 5f;

    /// Fixed, not random: an intro whose spectrum differs every run is one you cannot adjust by looking at
    /// it.
    private static readonly (float Height, float Speed, float Phase)[] Bars = BuildBars();

    private float elapsed;
    private bool skipped;

    public bool Finished => skipped || elapsed >= TotalSeconds;

    /// How faded the whole thing is, for the host window to match its own chrome to.
    public float Alpha => Finished
        ? 0f
        : 1f - Ease(Math.Clamp((elapsed - HoldUntil) / FadeSeconds, 0f, 1f));

    /// Replays from the top.
    public void Restart()
    {
        elapsed = 0f;
        skipped = false;
    }

    public void Draw(Fonts fonts)
    {
        elapsed += MathF.Min(ImGui.GetIO().DeltaTime, MaximumFrameStep);

        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        var centre = origin + (size / 2f);
        var alpha = Alpha;

        drawList.PushClipRectFullScreen();

        var inset = MathF.Max(1f, Metrics.Hairline);
        var min = origin + new Vector2(inset, inset);
        var max = origin + size - new Vector2(inset, inset);

        drawList.AddRectFilled(min, max,
            ImGui.GetColorU32(Semantic.Alpha(Elevation.Base, alpha)), Metrics.RadiusPanel);

        DrawBloom(drawList, centre, size, alpha);
        DrawPlatters(drawList, centre, size, alpha);
        DrawSpectrum(drawList, origin, size, alpha);
        DrawWordmark(drawList, fonts, centre, alpha);

        drawList.AddRect(min, max,
            ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.35f * alpha)),
            Metrics.RadiusPanel, ImDrawFlags.None, MathF.Max(1.5f, 1.5f * Metrics.Scale));

        drawList.PopClipRect();

        if (elapsed > SkippableAfter
            && ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
            && ImGui.IsMouseReleased(ImGuiMouseButton.Left)
            && ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Length() <= DragSlop * Metrics.Scale)
        {
            skipped = true;
        }
    }

    /// A soft bloom behind everything, throbbing on a four-to-the-floor beat.
    private void DrawBloom(ImDrawListPtr drawList, Vector2 centre, Vector2 size, float alpha)
    {
        var progress = Ease(Math.Clamp(elapsed / SpinSeconds, 0f, 1f));

        const float beat = 0.5f;
        var intoBeat = (elapsed % beat) / beat;
        var kick = MathF.Pow(1f - intoBeat, 3f);

        var radius = MathF.Min(size.X, size.Y) * (0.3f + (0.2f * progress) + (0.04f * kick));

        for (var i = 8; i >= 1; i--)
        {
            var t = i / 8f;
            drawList.AddCircleFilled(centre, radius * t,
                ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.028f * progress * alpha)), 48);
        }
    }

    /// Two platters, one per deck, spinning up and sliding together.
    private void DrawPlatters(ImDrawListPtr drawList, Vector2 centre, Vector2 size, float alpha)
    {
        var progress = Ease(Math.Clamp(elapsed / SpinSeconds, 0f, 1f));
        var radius = MathF.Min(size.X, size.Y) * 0.22f;

        var spread = (1f - progress) * size.X * 0.22f;

        var spin = (elapsed * 7f) - (progress * elapsed * 3.5f);

        DrawPlatter(drawList, centre - new Vector2(spread, 0f), radius, spin, Semantic.DeckA, alpha * progress);
        DrawPlatter(drawList, centre + new Vector2(spread, 0f), radius, -spin, Semantic.DeckB, alpha * progress);
    }

    /// No spindle at the centre, deliberately.
    private static void DrawPlatter(ImDrawListPtr drawList, Vector2 centre, float radius, float spin, Vector4 accent, float alpha)
    {
        drawList.AddCircle(centre, radius, ImGui.GetColorU32(Semantic.Alpha(accent, 0.22f * alpha)),
            64, MathF.Max(1f, Metrics.Scale));

        const int segments = 48;
        const int litSegments = 30;

        for (var i = 0; i < litSegments; i++)
        {
            var t0 = spin + (MathF.Tau * i / segments);
            var t1 = spin + (MathF.Tau * (i + 1) / segments);

            var lead = 1f - (i / (float)litSegments);
            var a = alpha * 0.9f * lead * lead;

            drawList.AddLine(
                centre + new Vector2(MathF.Cos(t0), MathF.Sin(t0)) * radius,
                centre + new Vector2(MathF.Cos(t1), MathF.Sin(t1)) * radius,
                ImGui.GetColorU32(Semantic.Alpha(accent, a)),
                MathF.Max(1.5f, 2f * Metrics.Scale));
        }
    }

    /// A spectrum along the base, because this is a DJ deck and that is what it looks like.
    private void DrawSpectrum(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float alpha)
    {
        var rise = Ease(Math.Clamp((elapsed - 0.3f) / 0.8f, 0f, 1f));
        if (rise <= 0f)
            return;

        var floorY = origin.Y + size.Y - (Metrics.Xxl * 0.5f);
        var maxHeight = size.Y * 0.16f;
        var width = size.X - (Metrics.Xxl * 2f);
        var barWidth = width / (Bars.Length * 1.6f);
        var stride = width / Bars.Length;

        for (var i = 0; i < Bars.Length; i++)
        {
            var (height, speed, phase) = Bars[i];

            var wave = MathF.Abs(MathF.Sin((elapsed * speed) + phase));
            var h = maxHeight * height * (0.25f + (0.75f * wave)) * rise;

            var x = origin.X + Metrics.Xxl + (i * stride);

            var tint = Vector4.Lerp(Semantic.DeckA, Semantic.DeckB, i / (float)(Bars.Length - 1));

            drawList.AddRectFilled(
                new Vector2(x, floorY - h),
                new Vector2(x + barWidth, floorY),
                ImGui.GetColorU32(Semantic.Alpha(tint, 0.5f * alpha)),
                barWidth * 0.3f);
        }
    }

    /// The wordmark, set a letter at a time, with the crossfader sweep over it and the 2.0 badge landing
    /// beside it.
    private void DrawWordmark(ImDrawListPtr drawList, Fonts fonts, Vector2 centre, float alpha)
    {
        var sweep = Math.Clamp((elapsed - SweepStartAt) / SweepSeconds, 0f, 1f);

        float fontSize;
        float total;
        float baseline;

        using (fonts.HeaderLarge.PushSafe())
        {
            fontSize = ImGui.GetFontSize();
            var tracking = MathF.Round(6f * Metrics.Scale);

            total = 0f;
            foreach (var character in Wordmark)
                total += ImGui.CalcTextSize(character.ToString()).X + tracking;

            total = MathF.Max(0f, total - tracking);

            var x = MathF.Round(centre.X - (total / 2f));
            baseline = MathF.Round(centre.Y - (fontSize / 2f));

            var sweepX = x - (total * 0.25f) + (total * 1.5f * Ease(sweep));

            for (var i = 0; i < Wordmark.Length; i++)
            {
                var glyph = Wordmark[i].ToString();
                var glyphWidth = ImGui.CalcTextSize(glyph).X;

                var progress = Math.Clamp((elapsed - LettersStartAt - (i * LetterStagger)) / LetterDuration, 0f, 1f);
                if (progress <= 0f)
                {
                    x += glyphWidth + tracking;
                    continue;
                }

                var eased = Ease(progress);
                var drop = (1f - eased) * 18f * Metrics.Scale;
                var letterAlpha = eased * alpha;

                var tint = Vector4.Lerp(Semantic.DeckA, Semantic.DeckB, i / (float)(Wordmark.Length - 1));

                var distance = MathF.Abs((x + (glyphWidth * 0.5f)) - sweepX);
                var lit = sweep > 0f && sweep < 1f
                    ? MathF.Max(0f, 1f - (distance / (total * 0.22f)))
                    : 0f;

                var colour = Vector4.Lerp(tint, Vector4.One, lit * 0.75f);

                Chrome.Text(drawList, new Vector2(x + (2f * Metrics.Scale), baseline + drop + (2f * Metrics.Scale)),
                    ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.55f * letterAlpha)), glyph);

                Chrome.Text(drawList, new Vector2(x, baseline + drop),
                    ImGui.GetColorU32(Semantic.Alpha(colour, letterAlpha)), glyph);

                x += glyphWidth + tracking;
            }
        }

        DrawVersionBadge(drawList, fonts, centre, total, baseline, fontSize, alpha);
    }

    /// The 2.0 badge, dropping in under the wordmark with a small overshoot.
    private void DrawVersionBadge(
        ImDrawListPtr drawList, Fonts fonts, Vector2 centre, float wordmarkWidth, float baseline, float fontSize, float alpha)
    {
        var progress = Math.Clamp((elapsed - BadgeStartAt) / BadgeSeconds, 0f, 1f);
        if (progress <= 0f)
            return;

        var settle = Overshoot(progress);

        using var font = fonts.Header.PushSafe();

        var measured = ImGui.CalcTextSize(Version);
        var textWidth = measured.X;
        var textHeight = measured.Y;

        var padX = Metrics.Lg;
        var height = MathF.Round(textHeight + (Metrics.Sm * 2f));
        var width = MathF.Round(textWidth + (padX * 2f));

        var restY = baseline + fontSize + (Metrics.Lg * 1.5f);
        var y = restY - ((1f - settle) * 24f * Metrics.Scale);

        var min = Chrome.Snap(new Vector2(centre.X - (width / 2f), y));
        var max = Chrome.Snap(min + new Vector2(width, height));
        var a = alpha * Math.Clamp(progress * 2f, 0f, 1f);

        drawList.AddRectFilled(min, max,
            ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.9f * a)), Metrics.Pill(height));

        Chrome.Text(drawList,
            Chrome.CenterY(min.X + padX, min.Y, height, textHeight),
            ImGui.GetColorU32(Semantic.Alpha(Semantic.TextOnAccent, a)), Version);

        var ruleProgress = Ease(Math.Clamp((progress - 0.5f) / 0.5f, 0f, 1f));
        if (ruleProgress <= 0f)
            return;

        var ruleY = min.Y + (height / 2f);
        var reach = (wordmarkWidth / 2f) * ruleProgress;
        var gap = (width / 2f) + Metrics.Lg;
        var line = ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.35f * a));

        if (reach <= gap)
            return;

        drawList.AddLine(new Vector2(centre.X - reach, ruleY), new Vector2(centre.X - gap, ruleY), line, 1f);
        drawList.AddLine(new Vector2(centre.X + gap, ruleY), new Vector2(centre.X + reach, ruleY), line, 1f);
    }

    private static (float Height, float Speed, float Phase)[] BuildBars()
    {
        const int count = 24;
        var bars = new (float, float, float)[count];

        for (var i = 0; i < count; i++)
        {
            var t = i / (float)(count - 1);
            var hump = MathF.Sin(t * MathF.PI);
            bars[i] = (
                0.35f + (0.65f * hump),
                4f + (((i * 37) % 11) * 0.45f),
                ((i * 53) % 31) * 0.21f);
        }

        return bars;
    }

    /// Smoothstep.
    private static float Ease(float t) => t * t * (3f - (2f * t));

    /// Eases past the target and comes back - a landing with weight behind it.
    private static float Overshoot(float t)
    {
        const float tension = 1.7f;
        var p = t - 1f;
        return (p * p * (((tension + 1f) * p) + tension)) + 1f;
    }
}
