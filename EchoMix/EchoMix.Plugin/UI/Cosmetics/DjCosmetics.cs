using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Cosmetics;

/// The avatar frames and name effects a DJ chooses for their listing.
public static class DjCosmetics
{
    /// Renders whichever frame style the profile's owner picked - see the individual case blocks below for
    /// each one's mechanics.
    public static void DrawAvatarFrame(
        ImDrawListPtr drawList,
        Vector2 origin,
        Vector2 size,
        float rounding,
        Vector4 color,
        string style,
        float scale)
    {
        var thickness = 3f * scale;
        var outset = thickness / 2f;
        var outerMin = origin - new Vector2(outset);
        var outerMax = origin + size + new Vector2(outset);
        var outerSize = outerMax - outerMin;
        var r = MathF.Min(rounding + outset, MathF.Min(outerSize.X, outerSize.Y) / 2f);
        var col = ImGui.GetColorU32(color);

        switch (style)
        {
            case "Dashed":
            {
                const int segments = 20;
                for (var i = 0; i < segments; i += 2)
                {
                    var p0 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, i / (float)segments);
                    var p1 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, (i + 0.6f) / segments);
                    drawList.AddLine(p0, p1, col, thickness);
                }
                break;
            }

            case "Dotted":
            {
                const int dots = 28;
                for (var i = 0; i < dots; i++)
                {
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, i / (float)dots);
                    drawList.AddCircleFilled(pos, thickness * 0.6f, col);
                }
                break;
            }

            case "Double":
            {
                var farGap = 4f * scale;
                drawList.AddRect(outerMin, outerMax, col, r, ImDrawFlags.None, 1.5f * scale);
                drawList.AddRect(outerMin - new Vector2(farGap), outerMax + new Vector2(farGap), col, r + farGap, ImDrawFlags.None, 1.5f * scale);
                break;
            }

            case "Corners":
            {
                var armLength = MathF.Min(outerSize.X, outerSize.Y) * 0.22f;
                void DrawCorner(Vector2 corner, Vector2 dirX, Vector2 dirY)
                {
                    drawList.AddLine(corner, corner + (dirX * armLength), col, thickness);
                    drawList.AddLine(corner, corner + (dirY * armLength), col, thickness);
                }

                DrawCorner(outerMin, new Vector2(1f, 0f), new Vector2(0f, 1f));
                DrawCorner(new Vector2(outerMax.X, outerMin.Y), new Vector2(-1f, 0f), new Vector2(0f, 1f));
                DrawCorner(outerMax, new Vector2(-1f, 0f), new Vector2(0f, -1f));
                DrawCorner(new Vector2(outerMin.X, outerMax.Y), new Vector2(1f, 0f), new Vector2(0f, -1f));
                break;
            }

            case "Gradient":
            {
                var tint = new Vector4(MathF.Min(1f, color.X + 0.35f), MathF.Min(1f, color.Y + 0.35f), MathF.Min(1f, color.Z + 0.35f), 1f);
                var c1 = ImGui.GetColorU32(tint);
                var topLeft = outerMin;
                var topRight = new Vector2(outerMax.X, outerMin.Y);
                var bottomRight = outerMax;
                var bottomLeft = new Vector2(outerMin.X, outerMax.Y);
                drawList.AddLine(topLeft, topRight, c1, thickness);
                drawList.AddLine(topRight, bottomRight, col, thickness);
                drawList.AddLine(bottomRight, bottomLeft, c1, thickness);
                drawList.AddLine(bottomLeft, topLeft, col, thickness);
                break;
            }

            case "Glow":
            {
                for (var i = 4; i >= 1; i--)
                {
                    var haloOutset = i * 2.5f * scale;
                    var alpha = 0.16f / i;
                    drawList.AddRect(outerMin - new Vector2(haloOutset), outerMax + new Vector2(haloOutset),
                        ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha)), r + haloOutset, ImDrawFlags.None, thickness);
                }
                drawList.AddRect(outerMin, outerMax, col, r, ImDrawFlags.None, thickness * 0.7f);
                break;
            }

            case "Pulse":
            {
                var pulse = 0.55f + (0.45f * MathF.Sin((float)ImGui.GetTime() * 2.2f));
                var pulseColor = new Vector4(color.X, color.Y, color.Z, MathF.Max(0.25f, pulse));
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(pulseColor), r, ImDrawFlags.None, (thickness * 0.7f) + (pulse * thickness * 0.6f));
                break;
            }

            case "Chase":
            {
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.3f)), r, ImDrawFlags.None, thickness * 0.6f);

                const int trailDots = 6;
                const float trailSpacing = 0.02f;
                const float loopsPerSecond = 0.3f;
                var headT = (float)(ImGui.GetTime() * loopsPerSecond % 1.0);
                for (var i = 0; i < trailDots; i++)
                {
                    var t = headT - (i * trailSpacing);
                    var alpha = MathF.Pow(1f - (i / (float)trailDots), 1.5f);
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var dotRadius = MathF.Max(1.5f, 4f - (i * 0.4f)) * scale;
                    drawList.AddCircleFilled(pos, dotRadius, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha)));
                }
                break;
            }

            case "Spin":
            {
                const int segments = 40;
                var timeOffset = (float)(ImGui.GetTime() * 0.25 % 1.0);
                var tint = new Vector4(MathF.Min(1f, color.X + 0.4f), MathF.Min(1f, color.Y + 0.4f), MathF.Min(1f, color.Z + 0.4f), 1f);
                for (var i = 0; i < segments; i++)
                {
                    var t0 = i / (float)segments;
                    var t1 = (i + 1f) / segments;
                    var blend = (MathF.Sin((t0 + timeOffset) * MathF.PI * 2f) + 1f) / 2f;
                    var segColor = Vector4.Lerp(color, tint, blend);
                    var p0 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t0);
                    var p1 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t1);
                    drawList.AddLine(p0, p1, ImGui.GetColorU32(segColor), thickness);
                }
                break;
            }

            case "Rainbow":
            {
                var hue = (float)(ImGui.GetTime() * 0.15 % 1.0);
                var rainbow = HsvToRgb(hue, 0.75f, 1f);
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(rainbow.X, rainbow.Y, rainbow.Z, 1f)), r, ImDrawFlags.None, thickness);
                break;
            }

            case "Sparkle":
            {
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.35f)), r, ImDrawFlags.None, thickness * 0.6f);

                const int sparkleCount = 8;
                var time = (float)ImGui.GetTime();
                for (var i = 0; i < sparkleCount; i++)
                {
                    var t = i / (float)sparkleCount;
                    var phase = (time * 1.3f) + (i * 1.7f);
                    var twinkle = MathF.Max(0f, MathF.Sin(phase));
                    if (twinkle <= 0.05f)
                        continue;

                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var radius = (1.5f + (twinkle * 2.5f)) * scale;
                    drawList.AddCircleFilled(pos, radius, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, twinkle * 0.9f)));
                    drawList.AddCircleFilled(pos, radius * 0.5f, col);
                }
                break;
            }

            case "Ticks":
            {
                var center = origin + (size / 2f);
                const int tickCount = 16;
                var tickLength = thickness * 1.8f;
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.35f)), r, ImDrawFlags.None, thickness * 0.5f);
                for (var i = 0; i < tickCount; i++)
                {
                    var t = i / (float)tickCount;
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var dir = pos - center;
                    if (dir.LengthSquared() > 0.0001f)
                        dir = Vector2.Normalize(dir);
                    drawList.AddLine(pos, pos + (dir * tickLength), col, thickness * 0.8f);
                }
                break;
            }

            case "Chain":
            {
                const int links = 18;
                for (var i = 0; i < links; i++)
                {
                    var t = i / (float)links;
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var radius = (i % 2 == 0 ? thickness * 0.85f : thickness * 0.45f);
                    drawList.AddCircleFilled(pos, radius, col);
                }
                break;
            }

            case "Brackets":
            {
                var insetFromCorner = outerSize.X * 0.16f;
                var tickDrop = thickness * 1.6f;
                void DrawBracket(float y, float dropDir)
                {
                    var lineStart = new Vector2(outerMin.X + insetFromCorner, y);
                    var lineEnd = new Vector2(outerMax.X - insetFromCorner, y);
                    drawList.AddLine(lineStart, lineEnd, col, thickness);
                    drawList.AddLine(lineStart, lineStart + new Vector2(0f, tickDrop * dropDir), col, thickness);
                    drawList.AddLine(lineEnd, lineEnd + new Vector2(0f, tickDrop * dropDir), col, thickness);
                }
                DrawBracket(outerMin.Y, 1f);
                DrawBracket(outerMax.Y, -1f);
                break;
            }

            case "Stitch":
            {
                const int stitches = 24;
                var stitchLength = thickness * 2.2f;
                for (var i = 0; i < stitches; i++)
                {
                    var t0 = i / (float)stitches;
                    var t1 = (i + 0.15f) / stitches;
                    var p0 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t0);
                    var p1 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t1);
                    var tangent = p1 - p0;
                    if (tangent.LengthSquared() < 0.0001f)
                        tangent = new Vector2(1f, 0f);
                    tangent = Vector2.Normalize(tangent);
                    var normal = new Vector2(-tangent.Y, tangent.X) * (i % 2 == 0 ? 1f : -1f);
                    var diagonal = Vector2.Normalize(tangent + normal) * stitchLength;
                    var mid = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, (t0 + t1) / 2f);
                    drawList.AddLine(mid - (diagonal / 2f), mid + (diagonal / 2f), col, thickness * 0.6f);
                }
                break;
            }

            case "Blocks":
            {
                const int blockCount = 20;
                var blockSize = thickness * 1.6f;
                for (var i = 0; i < blockCount; i++)
                {
                    var t = i / (float)blockCount;
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var half = new Vector2(blockSize / 2f);
                    var alpha = i % 2 == 0 ? 1f : 0.3f;
                    drawList.AddRectFilled(pos - half, pos + half, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, alpha)));
                }
                break;
            }

            case "Sentry":
            {
                Span<Vector2> corners = [outerMin, new Vector2(outerMax.X, outerMin.Y), outerMax, new Vector2(outerMin.X, outerMax.Y)];
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.3f)), r, ImDrawFlags.None, thickness * 0.6f);

                const float secondsPerCorner = 0.6f;
                var cycle = (float)ImGui.GetTime() / secondsPerCorner;
                var index = (int)cycle % corners.Length;
                var localT = cycle - MathF.Floor(cycle);
                var from = corners[index];
                var to = corners[(index + 1) % corners.Length];
                drawList.AddCircleFilled(Vector2.Lerp(from, to, localT), thickness, col);
                break;
            }

            case "Anchor":
            {
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.3f)), r, ImDrawFlags.None, thickness * 0.6f);
                Span<float> anchorT = [0f, 0.25f, 0.5f, 0.75f];
                var time = (float)ImGui.GetTime();
                for (var i = 0; i < anchorT.Length; i++)
                {
                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, anchorT[i]);
                    var breathe = (MathF.Sin((time * 1.8f) + (i * MathF.PI / 2f)) + 1f) / 2f;
                    drawList.AddCircleFilled(pos, (thickness * 0.6f) + (breathe * thickness * 0.9f), col);
                }
                break;
            }

            case "Pendulum":
            {
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.3f)), r, ImDrawFlags.None, thickness * 0.6f);
                var swing = (MathF.Sin((float)ImGui.GetTime() * 1.4f) + 1f) / 2f;
                var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, swing * 0.5f);
                drawList.AddCircleFilled(pos, thickness * 0.9f, col);
                break;
            }

            case "Glitch":
            {
                var bucket = (int)((float)ImGui.GetTime() * 6f);
                var rng = new Random(bucket);
                var jitter = new Vector2((rng.NextSingle() - 0.5f) * thickness, (rng.NextSingle() - 0.5f) * thickness);

                const int segments = 16;
                for (var i = 0; i < segments; i++)
                {
                    if (rng.NextDouble() < 0.25)
                        continue;

                    var t0 = i / (float)segments;
                    var t1 = (i + 0.8f) / segments;
                    var p0 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t0) + jitter;
                    var p1 = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t1) + jitter;
                    drawList.AddLine(p0, p1, col, thickness);
                }
                break;
            }

            case "Confetti":
            {
                drawList.AddRect(outerMin, outerMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.25f)), r, ImDrawFlags.None, thickness * 0.5f);

                const int particleCount = 10;
                const float cycleSeconds = 2.2f;
                var time = (float)ImGui.GetTime();
                for (var i = 0; i < particleCount; i++)
                {
                    var slotRng = new Random(i * 7919);
                    var t = (float)slotRng.NextDouble();
                    var hue = (float)slotRng.NextDouble();
                    var phase = ((time / cycleSeconds) + (i / (float)particleCount)) % 1f;
                    var life = MathF.Sin(phase * MathF.PI);
                    if (life <= 0.02f)
                        continue;

                    var pos = Theme.RoundedRectPerimeterPoint(outerMin, outerSize, r, t);
                    var rgb = HsvToRgb(hue, 0.7f, 1f);
                    drawList.AddCircleFilled(pos, (1.5f + (life * 2f)) * scale, ImGui.GetColorU32(new Vector4(rgb.X, rgb.Y, rgb.Z, life)));
                }
                break;
            }

            default:                drawList.AddRect(outerMin, outerMax, col, r, ImDrawFlags.None, thickness);
                break;
        }
    }

    public static Vector3 HsvToRgb(float h, float s, float v)
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

    /// Draws a DJ's name at an explicit size (fontScale relative to the Header font's own natural size, same
    /// "AddText(font, size, ...)" technique as UiHelpers.DrawScaledIcon - no separate big-name font asset
    /// needed) in whichever text effect they picked.
    public static void DrawDjName(
        Fonts fonts,
        string text,
        Vector4 color,
        string effect,
        float fontScale,
        float scale)
    {
        ImFontPtr font;
        using (fonts.HeaderLarge.PushSafe())
            font = ImGui.GetFont();

        using (fonts.Header.PushSafe())
        {
            var drawFontSize = ImGui.GetFontSize() * fontScale;
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();

            switch (effect)
            {
                case "Pulse":
                {
                    var pulse = 0.6f + (0.4f * MathF.Sin((float)ImGui.GetTime() * 2.2f));
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, pulse)), text);
                    break;
                }

                case "Rainbow":
                {
                    var hue = (float)(ImGui.GetTime() * 0.15 % 1.0);
                    var rainbow = HsvToRgb(hue, 0.7f, 1f);
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(new Vector4(rainbow.X, rainbow.Y, rainbow.Z, 1f)), text);
                    break;
                }

                case "Wave":
                {
                    var x = pos.X;
                    var time = (float)ImGui.GetTime();
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var yOffset = MathF.Sin((time * 4f) + (i * 0.6f)) * 3f * scale;
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y + yOffset), ImGui.GetColorU32(color), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Gradient":
                {
                    var x = pos.X;
                    var tint = new Vector4(MathF.Min(1f, color.X + 0.4f), MathF.Min(1f, color.Y + 0.4f), MathF.Min(1f, color.Z + 0.4f), 1f);
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var t = text.Length > 1 ? i / (float)(text.Length - 1) : 0f;
                        var segColor = Vector4.Lerp(color, tint, t);
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y), ImGui.GetColorU32(segColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Glow":
                {
                    var haloColor = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.22f));
                    var haloOffset = 2.2f * scale;
                    Span<Vector2> haloDirs = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];
                    foreach (var d in haloDirs)
                        drawList.AddText(font, drawFontSize, pos + (d * haloOffset), haloColor, text);
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    break;
                }

                case "Shimmer":
                {
                    var x = pos.X;
                    var totalWidth = ImGui.CalcTextSize(text).X * fontScale;
                    var sweep = ((float)(ImGui.GetTime() * 0.6 % 1.6)) - 0.3f;
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var charT = totalWidth > 0f ? (x - pos.X + (charWidth / 2f)) / totalWidth : 0f;
                        var dist = MathF.Abs(charT - sweep);
                        var highlight = MathF.Max(0f, 1f - (dist * 4f));
                        var segColor = Vector4.Lerp(color, new Vector4(1f, 1f, 1f, 1f), highlight);
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y), ImGui.GetColorU32(segColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Chase":
                {
                    var x = pos.X;
                    var timeOffset = (float)(ImGui.GetTime() * 0.4 % 1.0);
                    var tint = new Vector4(MathF.Min(1f, color.X + 0.4f), MathF.Min(1f, color.Y + 0.4f), MathF.Min(1f, color.Z + 0.4f), 1f);
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var t = text.Length > 1 ? i / (float)(text.Length - 1) : 0f;
                        var blend = (MathF.Sin((t + timeOffset) * MathF.PI * 2f) + 1f) / 2f;
                        var segColor = Vector4.Lerp(color, tint, blend);
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y), ImGui.GetColorU32(segColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Flicker":
                {
                    var t = (float)ImGui.GetTime();
                    var flicker = 0.65f + (0.35f * MathF.Sin(t * 13f) * MathF.Sin(t * 7f));
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, MathF.Max(0.3f, flicker))), text);
                    break;
                }

                case "Typewriter":
                {
                    const float cycleSeconds = 3.2f;
                    const float holdFraction = 0.25f;                    var cyclePos = (float)(ImGui.GetTime() % cycleSeconds) / cycleSeconds;
                    var revealPortion = MathF.Min(1f, cyclePos / (1f - holdFraction));
                    var revealCount = (int)MathF.Ceiling(revealPortion * text.Length);
                    var showCursor = ((int)(ImGui.GetTime() * 2f) % 2) == 0 && revealCount < text.Length;

                    var x = pos.X;
                    for (var i = 0; i < revealCount; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y), ImGui.GetColorU32(color), ch);
                        x += charWidth;
                    }
                    if (showCursor)
                        drawList.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + drawFontSize), ImGui.GetColorU32(color), 2f * scale);
                    break;
                }

                case "Marquee":
                {
                    var textWidth = ImGui.CalcTextSize(text).X * fontScale;
                    var gap = textWidth * 0.6f + (20f * scale);
                    var cycleWidth = textWidth + gap;
                    var scrollX = ((float)ImGui.GetTime() * 40f * scale) % cycleWidth;

                    drawList.PushClipRect(pos, pos + new Vector2(textWidth, drawFontSize), true);
                    drawList.AddText(font, drawFontSize, new Vector2(pos.X - scrollX, pos.Y), ImGui.GetColorU32(color), text);
                    drawList.AddText(font, drawFontSize, new Vector2(pos.X - scrollX + cycleWidth, pos.Y), ImGui.GetColorU32(color), text);
                    drawList.PopClipRect();
                    break;
                }

                case "Glitch":
                {
                    var bucket = (int)((float)ImGui.GetTime() * 8f);
                    var x = pos.X;
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var charRng = new Random((bucket * 131) + i);
                        var jitterX = ((float)charRng.NextDouble() - 0.5f) * 3f * scale;
                        var jitterY = ((float)charRng.NextDouble() - 0.5f) * 3f * scale;
                        var corrupted = charRng.NextDouble() < 0.12;
                        var charColor = corrupted ? new Vector4(1f, 1f, 1f, 0.9f) : color;
                        drawList.AddText(font, drawFontSize, new Vector2(x + jitterX, pos.Y + jitterY), ImGui.GetColorU32(charColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Outline":
                {
                    var outlineColor = ImGui.GetColorU32(new Vector4(color.X * 0.25f, color.Y * 0.25f, color.Z * 0.25f, 1f));
                    var outlineOffset = 1.4f * scale;
                    Span<Vector2> dirs = [new(-1, -1), new(1, -1), new(-1, 1), new(1, 1), new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];
                    foreach (var d in dirs)
                        drawList.AddText(font, drawFontSize, pos + (d * outlineOffset), outlineColor, text);
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    break;
                }

                case "Underline":
                {
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    var underlineWidth = ImGui.CalcTextSize(text).X * fontScale;
                    var lineY = pos.Y + drawFontSize + (2f * scale);
                    drawList.AddLine(new Vector2(pos.X, lineY), new Vector2(pos.X + underlineWidth, lineY), ImGui.GetColorU32(color), 2f * scale);
                    drawList.AddCircleFilled(new Vector2(pos.X, lineY), 2.2f * scale, ImGui.GetColorU32(color));
                    drawList.AddCircleFilled(new Vector2(pos.X + underlineWidth, lineY), 2.2f * scale, ImGui.GetColorU32(color));
                    break;
                }

                case "Embossed":
                {
                    var shadowOffset = 1.6f * scale;
                    var shadowColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f));
                    var highlightColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f));
                    drawList.AddText(font, drawFontSize, pos + new Vector2(shadowOffset, shadowOffset), shadowColor, text);
                    drawList.AddText(font, drawFontSize, pos - new Vector2(shadowOffset, shadowOffset), highlightColor, text);
                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    break;
                }

                case "Cascade":
                {
                    const float cycleSeconds = 2.2f;
                    const float dropDuration = 0.45f;
                    var time = (float)ImGui.GetTime();
                    var x = pos.X;
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var staggerStart = i * 0.05f;
                        var localT = (time + cycleSeconds - staggerStart) % cycleSeconds;
                        var dropProgress = Math.Clamp(localT / dropDuration, 0f, 1f);
                        var eased = 1f - MathF.Pow(1f - dropProgress, 3f);
                        var yOffset = (1f - eased) * -12f * scale;
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y + yOffset), ImGui.GetColorU32(color), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Heatwave":
                {
                    var x = pos.X;
                    var time = (float)ImGui.GetTime();
                    var warm = new Vector4(MathF.Min(1f, color.X + 0.3f), color.Y, MathF.Max(0f, color.Z - 0.2f), 1f);
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var wobble = MathF.Sin((time * 5f) + (i * 1.1f)) * 1.5f * scale;
                        var warmth = (MathF.Sin((time * 2f) + (i * 0.4f)) + 1f) / 2f;
                        var charColor = Vector4.Lerp(color, warm, warmth * 0.6f);
                        drawList.AddText(font, drawFontSize, new Vector2(x + wobble, pos.Y), ImGui.GetColorU32(charColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                case "Blink":
                {
                    const float onSeconds = 1.6f;
                    const float offSeconds = 0.35f;
                    var cyclePos = (float)ImGui.GetTime() % (onSeconds + offSeconds);
                    if (cyclePos < onSeconds)
                        drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    break;
                }

                case "Split":
                {
                    var x = pos.X;
                    var tint = new Vector4(MathF.Min(1f, color.X + 0.45f), MathF.Min(1f, color.Y + 0.45f), MathF.Min(1f, color.Z + 0.45f), 1f);
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i].ToString();
                        var charWidth = ImGui.CalcTextSize(ch).X * fontScale;
                        var charColor = i % 2 == 0 ? color : tint;
                        drawList.AddText(font, drawFontSize, new Vector2(x, pos.Y), ImGui.GetColorU32(charColor), ch);
                        x += charWidth;
                    }
                    break;
                }

                default:                    drawList.AddText(font, drawFontSize, pos, ImGui.GetColorU32(color), text);
                    break;
            }

            var textSize = ImGui.CalcTextSize(text) * fontScale;
            ImGui.Dummy(textSize);
        }
    }
}
