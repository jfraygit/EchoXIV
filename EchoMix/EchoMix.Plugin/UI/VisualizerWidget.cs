using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI;

/// Spectrum bars drawn on the window's draw list.
public static class VisualizerWidget
{
    public enum Style
    {
        Bars, SmoothLine, FilledArea, MirroredBars, MirroredFilledArea, Dots, Blocks,
        Waveform, Radial, Rings, Polygon, PeakBars, Waterfall, Embers, HeatStrip, OrbitDots, Skyline,
        PulseLine, Starfield, MatrixRain, Kaleidoscope, CometRide, Mesh, PyramidBars, RippleField, BounceBalls, Aurora,
    }

    private static readonly Dictionary<string, float[]> SmoothedByKey = new();
    private static readonly Dictionary<string, float[]> PeaksByKey = new();
    private static readonly Dictionary<string, List<float[]>> HistoryByKey = new();
    private static readonly Dictionary<string, List<Ember>> EmbersByKey = new();
    private static readonly Dictionary<string, float[]> RainByKey = new();
    private static readonly Dictionary<string, (float[] Y, float[] Velocity)> BallsByKey = new();

    private const int WaterfallHistoryLength = 40;

    private struct Ember
    {
        public float X;
        public float Y;
        public float Life;        public float Size;
        public Vector4 Color;
    }

    /// The "diameter" a circular style can safely use - a drop-in replacement for a plain MathF.Min(size.X,
    /// size.Y) that also accounts for a nonzero mirrorCenterOffset.
    private static float RadiusBudget(Vector2 size, float mirrorCenterOffset) =>
        MathF.Min(size.X, size.Y - (mirrorCenterOffset * 2f));

    /// Returns true the frame this widget is clicked - used by the Listener view's minimized box, where the
    /// whole visualizer doubles as the "restore" click target.
    public static bool Draw(string id, float[] spectrum, Vector2 size, Vector4? accent = null, Style style = Style.Bars, float reactivity = 1f, float mirrorCenterOffset = 0f)
    {
        var accentColor = accent ?? Theme.CyanAccent;
        var dt = ImGui.GetIO().DeltaTime;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##visualizer" + id, size);

        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(Theme.Panel), 8f);

        var bands = spectrum.Length;
        if (bands == 0)
            return clicked;

        if (!SmoothedByKey.TryGetValue(id, out var smoothed) || smoothed.Length != bands)
        {
            smoothed = new float[bands];
            SmoothedByKey[id] = smoothed;
        }

        for (var i = 0; i < bands; i++)
        {
            var bandT = bands > 1 ? i / (float)(bands - 1) : 0f;
            var baseSensitivity = 6f * (1f + (bandT * bandT * 9f));
            var target = Math.Clamp(spectrum[i] * baseSensitivity, 0f, 1f);

            if (reactivity != 1f)
                target = MathF.Pow(target, 1f / MathF.Max(0.05f, reactivity));

            var speed = smoothed[i] > target ? 8f : 24f;
            smoothed[i] = UiHelpers.Lerp(smoothed[i], target, speed, dt);
        }

        switch (style)
        {
            case Style.Bars:
                DrawBars(drawList, origin, size, smoothed, accentColor);
                break;
            case Style.SmoothLine:
                DrawSmoothLine(drawList, origin, size, smoothed, accentColor);
                break;
            case Style.FilledArea:
                DrawFilledArea(drawList, origin, size, smoothed, accentColor);
                break;
            case Style.MirroredBars:
                DrawMirroredBars(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.MirroredFilledArea:
                DrawMirroredFilledArea(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.Dots:
                DrawDots(drawList, origin, size, smoothed, accentColor);
                break;
            case Style.Blocks:
                DrawBlocks(drawList, origin, size, smoothed, accentColor);
                break;
            case Style.Waveform:
                DrawWaveform(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.Radial:
                DrawRadial(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.Rings:
                DrawRings(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.Polygon:
                DrawPolygon(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.PeakBars:
                DrawPeakBars(drawList, origin, size, smoothed, accentColor, id);
                break;
            case Style.Waterfall:
                DrawWaterfall(drawList, origin, size, smoothed, accentColor, id);
                break;
            case Style.Embers:
                DrawEmbers(drawList, origin, size, smoothed, accentColor, id);
                break;
            case Style.HeatStrip:
                DrawHeatStrip(drawList, origin, size, smoothed, accentColor);
                break;
            case Style.OrbitDots:
                DrawOrbitDots(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.Skyline:
                DrawSkyline(drawList, origin, size, smoothed, accentColor);
                break;
            case Style.PulseLine:
                DrawPulseLine(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.Starfield:
                DrawStarfield(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.MatrixRain:
                DrawMatrixRain(drawList, origin, size, smoothed, accentColor, id, mirrorCenterOffset);
                break;
            case Style.Kaleidoscope:
                DrawKaleidoscope(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.CometRide:
                DrawCometRide(drawList, origin, size, smoothed, accentColor);
                break;
            case Style.Mesh:
                DrawMesh(drawList, origin, size, smoothed, accentColor, mirrorCenterOffset);
                break;
            case Style.PyramidBars:
                DrawPyramidBars(drawList, origin, size, smoothed, accentColor);
                break;
            case Style.RippleField:
                DrawRippleField(drawList, origin, size, smoothed, accentColor);
                break;
            case Style.BounceBalls:
                DrawBounceBalls(drawList, origin, size, smoothed, accentColor, id);
                break;
            case Style.Aurora:
                DrawAurora(drawList, origin, size, smoothed, accentColor);
                break;
        }

        return clicked;
    }

    private static void DrawBars(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        var bands = smoothed.Length;
        const float gap = 3f;
        var barWidth = (size.X - (gap * (bands - 1))) / bands;
        var barRounding = Math.Min(4f, barWidth / 2f);

        for (var i = 0; i < bands; i++)
        {
            var barHeight = Math.Max(2f, smoothed[i] * size.Y);
            var x = origin.X + (i * (barWidth + gap));
            var top = new Vector2(x, origin.Y + size.Y - barHeight);
            var bottom = new Vector2(x + barWidth, origin.Y + size.Y);

            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);
            drawList.AddRectFilled(top, bottom, ImGui.GetColorU32(color), barRounding);

            var capTop = top;
            var capBottom = new Vector2(bottom.X, top.Y + Math.Min(3f, barHeight));
            drawList.AddRectFilled(capTop, capBottom, ImGui.GetColorU32(Vector4.Lerp(color, Vector4.One, 0.5f)), barRounding);
        }
    }

    /// A single flowing line through each band's height instead of discrete bars - one of the Listener view's
    /// customization options, for a calmer look than the deck screens' hardware-meter bars.
    private static void DrawSmoothLine(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        var bands = smoothed.Length;
        var points = new Vector2[bands];
        for (var i = 0; i < bands; i++)
        {
            var x = origin.X + ((i + 0.5f) / bands * size.X);
            var y = origin.Y + size.Y - (smoothed[i] * size.Y);
            points[i] = new Vector2(x, y);
        }

        var lineColor = ImGui.GetColorU32(accentColor);
        for (var i = 0; i < bands - 1; i++)
            drawList.AddLine(points[i], points[i + 1], lineColor, 2.5f);

        var dotColor = ImGui.GetColorU32(Vector4.Lerp(accentColor, Vector4.One, 0.5f));
        for (var i = 0; i < bands; i++)
            drawList.AddCircleFilled(points[i], 2.5f, dotColor);
    }

    /// Same curve as Smooth Line, but with the area beneath it filled - reads fuller/more energetic without
    /// needing new data.
    private static void DrawFilledArea(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        var bands = smoothed.Length;
        var points = new Vector2[bands];
        for (var i = 0; i < bands; i++)
        {
            var x = origin.X + ((i + 0.5f) / bands * size.X);
            var y = origin.Y + size.Y - (smoothed[i] * size.Y);
            points[i] = new Vector2(x, y);
        }

        var baseline = origin.Y + size.Y;
        var fillColor = ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.35f));
        for (var i = 0; i < bands - 1; i++)
        {
            drawList.AddQuadFilled(
                points[i], points[i + 1],
                new Vector2(points[i + 1].X, baseline), new Vector2(points[i].X, baseline),
                fillColor);
        }

        var lineColor = ImGui.GetColorU32(accentColor);
        for (var i = 0; i < bands - 1; i++)
            drawList.AddLine(points[i], points[i + 1], lineColor, 2.5f);
    }

    /// Bars extending symmetrically up and down from a center line instead of only upward from the bottom - a
    /// classic mirrored-EQ look.
    private static void DrawMirroredBars(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        const float gap = 3f;
        var barWidth = (size.X - (gap * (bands - 1))) / bands;
        var barRounding = Math.Min(4f, barWidth / 2f);
        var centerY = origin.Y + (size.Y / 2f) + mirrorCenterOffset;

        for (var i = 0; i < bands; i++)
        {
            var halfHeight = Math.Max(1f, smoothed[i] * size.Y / 2f);
            var x = origin.X + (i * (barWidth + gap));
            var top = new Vector2(x, centerY - halfHeight);
            var bottom = new Vector2(x + barWidth, centerY + halfHeight);

            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);
            drawList.AddRectFilled(top, bottom, ImGui.GetColorU32(color), barRounding);
        }
    }

    /// MirroredBars' shape, but filled/smooth like FilledArea instead of discrete blocks - drawn as one quad
    /// per segment between the mirrored top and bottom curves, same concave-safe approach as DrawFilledArea.
    private static void DrawMirroredFilledArea(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        var centerY = origin.Y + (size.Y / 2f) + mirrorCenterOffset;
        var topPoints = new Vector2[bands];
        var bottomPoints = new Vector2[bands];
        for (var i = 0; i < bands; i++)
        {
            var x = origin.X + ((i + 0.5f) / bands * size.X);
            var halfHeight = smoothed[i] * size.Y / 2f;
            topPoints[i] = new Vector2(x, centerY - halfHeight);
            bottomPoints[i] = new Vector2(x, centerY + halfHeight);
        }

        var fillColor = ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.35f));
        for (var i = 0; i < bands - 1; i++)
            drawList.AddQuadFilled(topPoints[i], topPoints[i + 1], bottomPoints[i + 1], bottomPoints[i], fillColor);

        var lineColor = ImGui.GetColorU32(accentColor);
        for (var i = 0; i < bands - 1; i++)
        {
            drawList.AddLine(topPoints[i], topPoints[i + 1], lineColor, 2f);
            drawList.AddLine(bottomPoints[i], bottomPoints[i + 1], lineColor, 2f);
        }
    }

    /// Just a dot per band, no connecting stroke - a calmer "particle field" look.
    private static void DrawDots(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        var bands = smoothed.Length;
        var dotColor = ImGui.GetColorU32(accentColor);
        var glowColor = ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.25f));

        for (var i = 0; i < bands; i++)
        {
            var x = origin.X + ((i + 0.5f) / bands * size.X);
            var y = origin.Y + size.Y - (smoothed[i] * size.Y);
            var radius = 2f + (smoothed[i] * 7f);
            drawList.AddCircleFilled(new Vector2(x, y), radius * 1.8f, glowColor);
            drawList.AddCircleFilled(new Vector2(x, y), radius, dotColor);
        }
    }

    /// Classic hardware VU meter look - each band is a stack of discrete rounded cells instead of one
    /// continuous bar, lit from the bottom up to the band's current amplitude.
    private static void DrawBlocks(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        const int cellCount = 12;
        const float cellGap = 2f;

        var bands = smoothed.Length;
        const float barGap = 3f;
        var barWidth = (size.X - (barGap * (bands - 1))) / bands;
        var barRounding = Math.Min(3f, barWidth / 2f);
        var cellHeight = (size.Y - (cellGap * (cellCount - 1))) / cellCount;

        for (var i = 0; i < bands; i++)
        {
            var x = origin.X + (i * (barWidth + barGap));
            var litCells = Math.Clamp((int)MathF.Ceiling(smoothed[i] * cellCount), 0, cellCount);

            for (var c = 0; c < cellCount; c++)
            {
                var cellBottom = origin.Y + size.Y - (c * (cellHeight + cellGap));
                var cellTop = cellBottom - cellHeight;
                var lit = c < litCells;
                var color = lit
                    ? Vector4.Lerp(accentColor, Theme.Accent, c / (float)cellCount)
                    : new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.08f);
                drawList.AddRectFilled(new Vector2(x, cellTop), new Vector2(x + barWidth, cellBottom), ImGui.GetColorU32(color), barRounding);
            }
        }
    }

    /// An oscilloscope-style trace instead of a spectrum shape - alternating bands swing above and below a
    /// center line rather than only rising from the bottom, closer to a classic waveform than an equalizer.
    private static void DrawWaveform(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        var centerY = origin.Y + (size.Y / 2f) + mirrorCenterOffset;
        var points = new Vector2[bands];
        for (var i = 0; i < bands; i++)
        {
            var x = origin.X + ((i + 0.5f) / bands * size.X);
            var sign = i % 2 == 0 ? 1f : -1f;
            points[i] = new Vector2(x, centerY - (sign * smoothed[i] * size.Y / 2f));
        }

        drawList.AddLine(new Vector2(origin.X, centerY), new Vector2(origin.X + size.X, centerY),
            ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.2f)), 1f);

        var lineColor = ImGui.GetColorU32(accentColor);
        for (var i = 0; i < bands - 1; i++)
            drawList.AddLine(points[i], points[i + 1], lineColor, 2.5f);
    }

    /// Spokes radiating outward from the widget's center rather than bars rising from its bottom edge - a
    /// circular EQ instead of a linear one.
    private static void DrawRadial(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        var center = origin + (size / 2f) + new Vector2(0f, mirrorCenterOffset);
        var budget = RadiusBudget(size, mirrorCenterOffset);
        var baseRadius = budget * 0.12f;
        var maxExtra = budget * 0.42f;

        for (var i = 0; i < bands; i++)
        {
            var angle = (i / (float)bands) * MathF.PI * 2f;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);
            var outer = center + (dir * (baseRadius + (smoothed[i] * maxExtra)));
            drawList.AddLine(center + (dir * baseRadius), outer, ImGui.GetColorU32(color), 2.5f);
            drawList.AddCircleFilled(outer, 2f, ImGui.GetColorU32(Vector4.Lerp(color, Vector4.One, 0.5f)));
        }
    }

    /// One concentric ring per band instead of one bar - radius is fixed per band (outermost ring is always
    /// the last band), only a ring's own thickness and brightness pulse with its amplitude.
    private static void DrawRings(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        var center = origin + (size / 2f) + new Vector2(0f, mirrorCenterOffset);
        var maxRadius = RadiusBudget(size, mirrorCenterOffset) / 2f;

        for (var i = 0; i < bands; i++)
        {
            var t = (i + 1) / (float)bands;
            var alpha = 0.15f + (smoothed[i] * 0.6f);
            var thickness = 1.5f + (smoothed[i] * 4f);
            var color = Vector4.Lerp(accentColor, Theme.Accent, t);
            drawList.AddCircle(center, t * maxRadius, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha)), 0, thickness);
        }
    }

    /// Every band placed around a circle and connected into one closed shape - a radar/ spider chart reading
    /// as a single pulsing blob instead of a row of independent bars.
    private static void DrawPolygon(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        if (bands < 3)
        {
            DrawBars(drawList, origin, size, smoothed, accentColor);
            return;
        }

        var center = origin + (size / 2f) + new Vector2(0f, mirrorCenterOffset);
        var maxRadius = RadiusBudget(size, mirrorCenterOffset) / 2f;
        var points = new Vector2[bands];
        for (var i = 0; i < bands; i++)
        {
            var angle = (i / (float)bands) * MathF.PI * 2f;
            var radius = maxRadius * (0.15f + (smoothed[i] * 0.85f));
            points[i] = center + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }

        var fillColor = ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.3f));
        for (var i = 0; i < bands; i++)
            drawList.AddTriangleFilled(center, points[i], points[(i + 1) % bands], fillColor);

        var lineColor = ImGui.GetColorU32(accentColor);
        for (var i = 0; i < bands; i++)
            drawList.AddLine(points[i], points[(i + 1) % bands], lineColor, 2f);
    }

    /// Bars, plus a thin bright cap that jumps to a new high instantly and falls slowly afterward - a classic
    /// hardware peak-hold meter, and the first style here with real memory across frames rather than being a
    /// pure function of this frame's spectrum alone.
    private static void DrawPeakBars(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, string id)
    {
        var bands = smoothed.Length;
        if (!PeaksByKey.TryGetValue(id, out var peaks) || peaks.Length != bands)
        {
            peaks = new float[bands];
            PeaksByKey[id] = peaks;
        }

        const float fallPerSecond = 0.6f;
        var dt = ImGui.GetIO().DeltaTime;
        for (var i = 0; i < bands; i++)
            peaks[i] = smoothed[i] >= peaks[i] ? smoothed[i] : MathF.Max(0f, peaks[i] - (fallPerSecond * dt));

        const float gap = 3f;
        var barWidth = (size.X - (gap * (bands - 1))) / bands;
        var barRounding = Math.Min(4f, barWidth / 2f);

        for (var i = 0; i < bands; i++)
        {
            var barHeight = Math.Max(2f, smoothed[i] * size.Y);
            var x = origin.X + (i * (barWidth + gap));
            var top = new Vector2(x, origin.Y + size.Y - barHeight);
            var bottom = new Vector2(x + barWidth, origin.Y + size.Y);

            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);
            drawList.AddRectFilled(top, bottom, ImGui.GetColorU32(color), barRounding);

            var peakY = origin.Y + size.Y - (peaks[i] * size.Y);
            var peakColor = ImGui.GetColorU32(Vector4.Lerp(color, Vector4.One, 0.7f));
            drawList.AddRectFilled(new Vector2(x, peakY - 1.5f), new Vector2(x + barWidth, peakY + 1.5f), peakColor, 1f);
        }
    }

    /// A scrolling spectrogram - every frame's spectrum becomes a new row that scrolls up and fades as it
    /// ages, so the shape on screen is recent history rather than only this instant.
    private static void DrawWaterfall(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, string id)
    {
        if (!HistoryByKey.TryGetValue(id, out var history))
        {
            history = new List<float[]>();
            HistoryByKey[id] = history;
        }

        history.Add((float[])smoothed.Clone());
        while (history.Count > WaterfallHistoryLength)
            history.RemoveAt(0);

        var bands = smoothed.Length;
        var rowHeight = size.Y / WaterfallHistoryLength;
        var colWidth = size.X / bands;

        for (var row = 0; row < history.Count; row++)
        {
            var frame = history[history.Count - 1 - row];            var y = origin.Y + size.Y - ((row + 1) * rowHeight);
            var age = row / (float)WaterfallHistoryLength;

            for (var col = 0; col < bands; col++)
            {
                var amplitude = frame[col];
                if (amplitude <= 0.02f)
                    continue;

                var x = origin.X + (col * colWidth);
                var color = Vector4.Lerp(accentColor, Theme.Accent, col / (float)bands);
                drawList.AddRectFilled(new Vector2(x, y), new Vector2(x + colWidth, y + rowHeight),
                    ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, amplitude * (1f - age))));
            }
        }
    }

    /// A loud band occasionally kicks off a small ember that rises and fades, instead of every band drawing
    /// something every frame - reads as ambient energy rather than a literal spectrum shape.
    private static void DrawEmbers(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, string id)
    {
        if (!EmbersByKey.TryGetValue(id, out var embers))
        {
            embers = new List<Ember>();
            EmbersByKey[id] = embers;
        }

        var bands = smoothed.Length;
        for (var i = 0; i < bands; i++)
        {
            if (smoothed[i] < 0.35f || Random.Shared.NextDouble() > smoothed[i] * 0.15)
                continue;

            var x = origin.X + ((i + 0.5f) / bands * size.X);
            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);
            embers.Add(new Ember { X = x, Y = origin.Y + size.Y, Life = 1f, Size = 2f + (smoothed[i] * 3f), Color = color });
        }

        var dt = ImGui.GetIO().DeltaTime;
        for (var i = embers.Count - 1; i >= 0; i--)
        {
            var ember = embers[i];
            ember.Y -= (20f + (ember.Size * 10f)) * dt;
            ember.Life -= dt * 0.6f;
            if (ember.Life <= 0f || ember.Y < origin.Y)
            {
                embers.RemoveAt(i);
                continue;
            }

            embers[i] = ember;
            drawList.AddCircleFilled(new Vector2(ember.X, ember.Y), ember.Size * ember.Life,
                ImGui.GetColorU32(new Vector4(ember.Color.X, ember.Color.Y, ember.Color.Z, ember.Life)));
        }
    }

    /// Amplitude drawn as color instead of size - every cell is the same fixed height, only its hue and
    /// brightness move, cool blue when quiet through to hot white when loud.
    private static void DrawHeatStrip(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        var bands = smoothed.Length;
        var cellWidth = size.X / bands;

        for (var i = 0; i < bands; i++)
        {
            var x = origin.X + (i * cellWidth);
            var hue = MathF.Max(0f, 0.62f - (smoothed[i] * 0.62f));            var value = 0.35f + (smoothed[i] * 0.65f);
            var rgb = HsvToRgb(hue, 0.85f, value);
            drawList.AddRectFilled(new Vector2(x, origin.Y), new Vector2(x + cellWidth - 1f, origin.Y + size.Y),
                ImGui.GetColorU32(new Vector4(rgb.X, rgb.Y, rgb.Z, 1f)), 2f);
        }
    }

    /// Every band is a dot orbiting the center at its own fixed radius, the whole ring slowly rotating -
    /// amplitude drives a dot's own size instead of its position, so the motion keeps going even through a
    /// quiet passage instead of the display going still.
    private static void DrawOrbitDots(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        var center = origin + (size / 2f) + new Vector2(0f, mirrorCenterOffset);
        var maxRadius = RadiusBudget(size, mirrorCenterOffset) / 2f;
        var rotation = (float)ImGui.GetTime() * 0.3f;

        for (var i = 0; i < bands; i++)
        {
            var radius = ((i + 1) / (float)bands) * maxRadius;
            var angle = rotation + ((i / (float)bands) * MathF.PI * 2f);
            var pos = center + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
            var dotRadius = 2f + (smoothed[i] * 8f);
            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);
            drawList.AddCircleFilled(pos, dotRadius * 1.6f, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.25f)));
            drawList.AddCircleFilled(pos, dotRadius, ImGui.GetColorU32(color));
        }
    }

    /// A jagged triangular mountain-range silhouette - one filled triangle per band meeting its neighbors at
    /// the baseline, instead of rectangles (Bars) or a smoothed curve (FilledArea).
    private static void DrawSkyline(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        var bands = smoothed.Length;
        var baseline = origin.Y + size.Y;
        var fillColor = ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.4f));
        var lineColor = ImGui.GetColorU32(accentColor);
        var bandWidth = size.X / bands;

        for (var i = 0; i < bands; i++)
        {
            var left = new Vector2(origin.X + (i * bandWidth), baseline);
            var right = new Vector2(origin.X + ((i + 1) * bandWidth), baseline);
            var peak = new Vector2(origin.X + ((i + 0.5f) * bandWidth), baseline - Math.Max(2f, smoothed[i] * size.Y));

            drawList.AddTriangleFilled(left, peak, right, fillColor);
            drawList.AddLine(left, peak, lineColor, 2f);
            drawList.AddLine(peak, right, lineColor, 2f);
        }
    }

    /// A flat line whose stroke thickness carries the amplitude instead of its height - every other style
    /// here maps loudness to a position or a size; this is the only one where a band's own vertical position
    /// never moves at all.
    private static void DrawPulseLine(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        var centerY = origin.Y + (size.Y / 2f) + mirrorCenterOffset;
        var maxThickness = MathF.Max(2f, RadiusBudget(size, mirrorCenterOffset) * 0.5f);

        for (var i = 0; i < bands - 1; i++)
        {
            var x0 = origin.X + (i / (float)bands * size.X);
            var x1 = origin.X + ((i + 1) / (float)bands * size.X);
            var thickness = 1.5f + (smoothed[i] * maxThickness);
            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);
            drawList.AddLine(new Vector2(x0, centerY), new Vector2(x1, centerY), ImGui.GetColorU32(color), thickness);
        }
    }

    /// A fixed field of stars scattered across the whole box (seeded per band index, so positions stay put
    /// frame to frame) that twinkle brighter with their own band's loudness - every other style lays bands
    /// out in a row or a circle; this scatters them freely.
    private static void DrawStarfield(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        var topMargin = mirrorCenterOffset * 2f;
        var usableHeight = MathF.Max(0f, size.Y - topMargin);
        for (var i = 0; i < bands; i++)
        {
            var seed = new Random(i * 7349);
            var x = origin.X + ((float)seed.NextDouble() * size.X);
            var y = origin.Y + topMargin + ((float)seed.NextDouble() * usableHeight);
            var twinkle = 0.15f + (smoothed[i] * 0.85f);
            var radius = 1.5f + (smoothed[i] * 2.5f);
            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);
            drawList.AddCircleFilled(new Vector2(x, y), radius, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, twinkle)));
        }
    }

    /// Each band is a column with its own falling, glowing trail that loops top to bottom continuously -
    /// loudness sets how fast that column falls, not its height or color.
    private static void DrawMatrixRain(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, string id, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        if (!RainByKey.TryGetValue(id, out var positions) || positions.Length != bands)
        {
            positions = new float[bands];
            var seed = new Random(12345);
            for (var i = 0; i < bands; i++)
                positions[i] = (float)seed.NextDouble();            RainByKey[id] = positions;
        }

        var topMargin = mirrorCenterOffset * 2f;
        var usableHeight = MathF.Max(0f, size.Y - topMargin);
        var dt = ImGui.GetIO().DeltaTime;
        var colWidth = size.X / bands;
        var trailLength = usableHeight * 0.35f;

        for (var i = 0; i < bands; i++)
        {
            var speed = 0.15f + (smoothed[i] * 0.6f);            positions[i] = (positions[i] + (speed * dt)) % 1f;

            var headY = origin.Y + topMargin + (positions[i] * usableHeight);
            var tailY = MathF.Max(origin.Y + topMargin, headY - trailLength);
            var x = origin.X + ((i + 0.5f) * colWidth);
            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);

            drawList.AddLine(new Vector2(x, tailY), new Vector2(x, headY),
                ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.25f + (smoothed[i] * 0.4f))), 2f);
            drawList.AddCircleFilled(new Vector2(x, headY), 2f + (smoothed[i] * 2f), ImGui.GetColorU32(color));
        }
    }

    /// Radial's spokes, packed into one wedge and repeated with rotational symmetry - a mandala instead of a
    /// single sweep, so the same spectrum shape reads three times over (SymmetryCount) around the circle
    /// instead of once.
    private static void DrawKaleidoscope(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        const int symmetryCount = 6;

        var bands = smoothed.Length;
        var center = origin + (size / 2f) + new Vector2(0f, mirrorCenterOffset);
        var budget = RadiusBudget(size, mirrorCenterOffset);
        var baseRadius = budget * 0.08f;
        var maxExtra = budget * 0.42f;
        var wedge = MathF.PI * 2f / symmetryCount;

        for (var i = 0; i < bands; i++)
        {
            var baseAngle = (i / (float)bands) * wedge;
            var length = baseRadius + (smoothed[i] * maxExtra);
            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);

            for (var s = 0; s < symmetryCount; s++)
            {
                var angle = baseAngle + (s * wedge);
                var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                drawList.AddLine(center + (dir * baseRadius), center + (dir * length), ImGui.GetColorU32(color), 2f);
            }
        }
    }

    /// The same amplitude curve DrawSmoothLine draws, dimmed, with a bright comet and fading trail riding
    /// along it over time - the curve itself is a pure function of this frame's spectrum like SmoothLine, but
    /// what's animated is a highlight traveling across an otherwise-static shape rather than the shape
    /// changing.
    private static void DrawCometRide(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        var bands = smoothed.Length;
        if (bands < 2)
            return;

        var points = new Vector2[bands];
        for (var i = 0; i < bands; i++)
        {
            var x = origin.X + ((i + 0.5f) / bands * size.X);
            var y = origin.Y + size.Y - (smoothed[i] * size.Y);
            points[i] = new Vector2(x, y);
        }

        var dimColor = ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.35f));
        for (var i = 0; i < bands - 1; i++)
            drawList.AddLine(points[i], points[i + 1], dimColor, 2f);

        Vector2 PointAt(float travelT)
        {
            var exact = Math.Clamp(travelT, 0f, 1f) * (bands - 1);
            var index = Math.Clamp((int)exact, 0, bands - 2);
            return Vector2.Lerp(points[index], points[index + 1], exact - index);
        }

        var travel = (float)(ImGui.GetTime() * 0.35 % 1.0);
        const int trailDots = 6;
        for (var t = trailDots - 1; t >= 0; t--)
        {
            var trailPos = PointAt(travel - (t * 0.015f));
            var alpha = (1f - (t / (float)trailDots)) * 0.8f;
            drawList.AddCircleFilled(trailPos, MathF.Max(1.5f, 5f - (t * 0.6f)), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)));
        }

        drawList.AddCircleFilled(PointAt(travel), 5f, ImGui.GetColorU32(accentColor));
    }

    /// Mirrored top and bottom curves like DrawMirroredFilledArea, but cross-braced with diagonal struts
    /// between them instead of a filled quad - a truss instead of a solid shape.
    private static void DrawMesh(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, float mirrorCenterOffset)
    {
        var bands = smoothed.Length;
        var centerY = origin.Y + (size.Y / 2f) + mirrorCenterOffset;
        var top = new Vector2[bands];
        var bottom = new Vector2[bands];
        for (var i = 0; i < bands; i++)
        {
            var x = origin.X + ((i + 0.5f) / bands * size.X);
            var halfHeight = smoothed[i] * size.Y / 2f;
            top[i] = new Vector2(x, centerY - halfHeight);
            bottom[i] = new Vector2(x, centerY + halfHeight);
        }

        var lineColor = ImGui.GetColorU32(new Vector4(accentColor.X, accentColor.Y, accentColor.Z, 0.55f));
        for (var i = 0; i < bands - 1; i++)
        {
            drawList.AddLine(top[i], top[i + 1], lineColor, 1.5f);
            drawList.AddLine(bottom[i], bottom[i + 1], lineColor, 1.5f);
            drawList.AddLine(top[i], bottom[i + 1], lineColor, 1f);
            drawList.AddLine(bottom[i], top[i + 1], lineColor, 1f);
        }

        var dotColor = ImGui.GetColorU32(accentColor);
        for (var i = 0; i < bands; i++)
        {
            drawList.AddCircleFilled(top[i], 2f, dotColor);
            drawList.AddCircleFilled(bottom[i], 2f, dotColor);
        }
    }

    /// Bars' rectangles tapered to a point at the top instead - discrete gapped shapes like Bars, unlike
    /// Skyline's continuous connected silhouette.
    private static void DrawPyramidBars(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        var bands = smoothed.Length;
        const float gap = 4f;
        var barWidth = (size.X - (gap * (bands - 1))) / bands;
        var baseline = origin.Y + size.Y;

        for (var i = 0; i < bands; i++)
        {
            var x = origin.X + (i * (barWidth + gap));
            var height = Math.Max(2f, smoothed[i] * size.Y);
            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);

            var bottomLeft = new Vector2(x, baseline);
            var bottomRight = new Vector2(x + barWidth, baseline);
            var apex = new Vector2(x + (barWidth / 2f), baseline - height);
            drawList.AddTriangleFilled(bottomLeft, apex, bottomRight, ImGui.GetColorU32(color));
        }
    }

    /// One independent ripple ring per band, each centered on its own fixed point along the bottom edge -
    /// unlike Rings, which shares a single center for every band, every band here gets its own origin.
    private static void DrawRippleField(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        var bands = smoothed.Length;
        var baseline = origin.Y + size.Y;
        var maxRadius = size.Y * 0.9f;

        for (var i = 0; i < bands; i++)
        {
            if (smoothed[i] <= 0.05f)
                continue;

            var x = origin.X + ((i + 0.5f) / bands * size.X);
            var radius = smoothed[i] * maxRadius;
            var alpha = ((1f - smoothed[i]) * 0.6f) + 0.1f;
            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);
            drawList.AddCircle(new Vector2(x, baseline), radius, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha)), 0, 1.5f);
            drawList.AddCircleFilled(new Vector2(x, baseline), 2.5f, ImGui.GetColorU32(color));
        }
    }

    /// A ball per band actually falls and bounces under gravity, kicked upward again each time it lands if
    /// that band's still loud - real physics state across frames rather than amplitude driving a position
    /// directly the way every non-stateful style here does.
    private static void DrawBounceBalls(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor, string id)
    {
        var bands = smoothed.Length;
        if (!BallsByKey.TryGetValue(id, out var state) || state.Y.Length != bands)
        {
            state = (new float[bands], new float[bands]);
            for (var i = 0; i < bands; i++)
                state.Y[i] = size.Y;
            BallsByKey[id] = state;
        }

        var dt = ImGui.GetIO().DeltaTime;
        const float gravity = 900f;

        for (var i = 0; i < bands; i++)
        {
            state.Velocity[i] += gravity * dt;
            state.Y[i] += state.Velocity[i] * dt;

            if (state.Y[i] >= size.Y)
            {
                state.Y[i] = size.Y;
                state.Velocity[i] = smoothed[i] > 0.08f ? -(300f + (smoothed[i] * 500f)) : 0f;
            }

            var x = origin.X + ((i + 0.5f) / bands * size.X);
            var y = origin.Y + Math.Clamp(state.Y[i], 0f, size.Y);
            var color = Vector4.Lerp(accentColor, Theme.Accent, i / (float)bands);
            drawList.AddCircleFilled(new Vector2(x, y), 4f, ImGui.GetColorU32(color));
        }
    }

    /// Three translucent curtains of light, each reading its own rotated slice of the spectrum and drifting
    /// sideways at its own speed - the first version of this sampled the same curve for every layer, just
    /// scaled down, so all three sat almost exactly on top of each other and read as one smudged, muddy fill
    /// instead of distinct curtains.
    private static void DrawAurora(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float[] smoothed, Vector4 accentColor)
    {
        const int layers = 3;

        var bands = smoothed.Length;
        var baseline = origin.Y + size.Y;
        var time = (float)ImGui.GetTime();

        for (var layer = 0; layer < layers; layer++)
        {
            var points = new Vector2[bands];
            var sampleShift = layer * bands / layers;
            var driftSpeed = 0.15f + (layer * 0.08f);
            var layerScale = 1f - (layer * 0.2f);
            for (var i = 0; i < bands; i++)
            {
                var x = origin.X + ((i + 0.5f) / bands * size.X);
                var sampled = smoothed[(i + sampleShift) % bands];
                var sway = MathF.Sin((time * driftSpeed) + (layer * 2.1f) + (i * 0.5f)) * (10f + (layer * 4f));
                var y = baseline - (sampled * size.Y * layerScale) + sway;
                points[i] = new Vector2(x, y);
            }

            var layerColor = Vector4.Lerp(accentColor, Theme.Accent, MathF.Min(1f, (layer / (float)layers) + 0.12f));
            var fillColor = ImGui.GetColorU32(new Vector4(layerColor.X, layerColor.Y, layerColor.Z, 0.28f - (layer * 0.06f)));
            for (var i = 0; i < bands - 1; i++)
            {
                drawList.AddQuadFilled(points[i], points[i + 1],
                    new Vector2(points[i + 1].X, baseline), new Vector2(points[i].X, baseline), fillColor);
            }

            if (layer == 0)
            {
                var edgeColor = ImGui.GetColorU32(new Vector4(layerColor.X, layerColor.Y, layerColor.Z, 0.7f));
                for (var i = 0; i < bands - 1; i++)
                    drawList.AddLine(points[i], points[i + 1], edgeColor, 1.5f);
            }
        }
    }

    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        var i = (int)(h * 6f);
        var f = (h * 6f) - i;
        var p = v * (1f - s);
        var q = v * (1f - (f * s));
        var t = v * (1f - ((1f - f) * s));
        return (((i % 6) + 6) % 6) switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }
}
