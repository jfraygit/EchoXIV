using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;

namespace EchoGlam.UI.Foundation;

/// One screen, drawn three ways, so the look can be chosen by looking at it.
internal static class Styleframes
{
    internal enum Look
    {
        /// Type, rules and whitespace.
        Editorial,

        /// Translucent panes over a gradient ground.
        Glass,

        /// Black, with the accent as emitted light.
        Neon,
    }

    private static Look current = Look.Glass;


    private static IDalamudTextureWrap?[] photos = [];
    private static bool photosRequested;
    private static string photoSource = "looking...";

    private static void EnsurePhotos()
    {
        if (photosRequested)
            return;

        photosRequested = true;

        _ = Task.Run(async () =>
        {
            try
            {
                var files = Game.Screenshots.Recent(Cards.Length);
                var source = $"your {files.Count} newest FFXIV screenshots";

                if (files.Count == 0)
                {
                    files = Placeholders();
                    source = files.Count > 0
                        ? "placeholders from the plugin's config folder"
                        : "none found - drawing gradients";
                }

                var loaded = new IDalamudTextureWrap?[Cards.Length];
                var ok = 0;

                for (var i = 0; i < files.Count && i < loaded.Length; i++)
                {
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(files[i]).ConfigureAwait(false);
                        loaded[i] = await Plugin.TextureProvider
                            .CreateFromImageAsync(bytes).ConfigureAwait(false);

                        ok++;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Debug(ex, $"[EchoGlam] Styleframe could not decode {files[i]}");
                    }
                }

                photos = loaded;
                photoSource = ok > 0 ? source : $"{files.Count} file(s) found, none decoded";
            }
            catch (Exception ex)
            {
                photoSource = $"failed: {ex.Message}";
                Plugin.Log.Debug(ex, "[EchoGlam] Styleframe photos unavailable");
            }
        });
    }

    /// Images dropped into {pluginConfig}/styleframe.
    private static List<string> Placeholders()
    {
        try
        {
            var dir = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "styleframe");

            if (!Directory.Exists(dir))
                return [];

            return
            [
                .. Directory.EnumerateFiles(dir)
                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase),
            ];
        }
        catch
        {
            return [];
        }
    }

    private static IDalamudTextureWrap? PhotoFor(int index) =>
        index < photos.Length ? photos[index] : null;

    /// Releases the decoded screenshots.
    public static void Unload()
    {
        foreach (var photo in photos)
            photo?.Dispose();

        photos = [];
        photosRequested = false;
    }

    /// Stand-ins at the REAL LIMITS, not comfortable samples.
    private static readonly (string Title, string Author, string World, bool New)[] Cards =
    [
        ("Midnight Archivist", "Echo Mix", "Sargatanas", true),

        ("Sunbleached Linen for the Long Walk Home at Midsummer's End",
            "Alexandrina Featherwhistle", "Sargatanas", false),

        ("Ser", "Kai", "Zodiark", false),
    ];

    public static void Draw()
    {
        EnsurePhotos();

        ImGui.TextColored(Col.TextDim, "The same screen in three visual languages. Pick one, or name a mix.");

        using (Text.PushCaption())
            ImGui.TextColored(Col.TextMuted, $"Pictures: {photoSource}  -  cover-cropped to 3:4 as the real grid does.");

        ImGui.Dummy(new Vector2(0f, Sp.Md));

        var looks = new[] { Look.Editorial, Look.Glass, Look.Neon };

        foreach (var look in looks)
        {
            var selected = current == look;

            if (EchoGlam.UI.Controls.EchoButton.Draw(
                    $"##look{look}", look.ToString(), new Vector2(0f, Metrics.Row), selected: selected))
            {
                current = look;
            }

            ImGui.SameLine(0f, Sp.Sm);
        }

        ImGui.NewLine();
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        var region = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();

        DrawGround(origin, origin + region, current);

        ImGui.BeginChild("##styleframe", region, false);

        var full = ImGui.GetContentRegionAvail();
        var top = ImGui.GetCursorPosY();
        var left = ImGui.GetCursorPosX();

        DrawMasthead(MastheadWidth, full.Y, current);

        ImGui.SetCursorPos(new Vector2(left + MastheadWidth + Sp.Xxl, top));

        ImGui.BeginGroup();

        var width = MathF.Max(0f, full.X - MastheadWidth - Sp.Xxl);
        var gutter = Sp.Xl;
        var listWidth = (width - gutter) * 0.66f;
        var sideWidth = width - gutter - listWidth;

        var contentTop = ImGui.GetCursorPosY();
        var contentLeft = ImGui.GetCursorPosX();

        ImGui.BeginGroup();
        DrawShowcase(listWidth, current);
        ImGui.EndGroup();

        ImGui.SetCursorPos(new Vector2(contentLeft + listWidth + gutter, contentTop));

        ImGui.BeginGroup();
        DrawToolSide(sideWidth, current);
        ImGui.EndGroup();

        ImGui.EndGroup();

        DrawWindowChrome(ImGui.GetWindowPos(), full.X + MastheadWidth + Sp.Xxl, current);

        ImGui.EndChild();
    }


    private static float MastheadWidth => 190f * UiHelpers.Scale;

    private static readonly string[] Destinations = ["Dressing Room", "Gallery", "Profile", "Friends"];

    private static int destination = 1;

    /// The wordmark, a numbered index of destinations, and the window buttons at the foot.
    private static void DrawMasthead(float width, float height, Look look)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var cursor = ImGui.GetCursorPos();

        if (look == Look.Glass)
        {
            drawList.AddRectFilled(
                origin, new Vector2(origin.X + width, origin.Y + height),
                Col.U32(new Vector4(1f, 1f, 1f, 0.022f)), Rad.Card);
        }

        var x = origin.X + Sp.Lg;
        var y = origin.Y + Sp.Lg;

        using (Text.PushDisplay())
        {
            Text.Tracked(drawList, new Vector2(x, y), "ECHOGLAM", Col.U32(Col.Text), 2.4f, width - Sp.Xxl);
            y += Text.DisplaySize * 1.25f;
        }

        var ruleWidth = width - (Sp.Lg * 2f);

        switch (look)
        {
            case Look.Neon:
                drawList.AddRectFilled(
                    new Vector2(x, y), new Vector2(x + (ruleWidth * 0.55f), y + Stroke.Mark),
                    Col.U32(Theme.Accent));

                drawList.AddRectFilled(
                    new Vector2(x, y - UiHelpers.Scale),
                    new Vector2(x + (ruleWidth * 0.55f), y + Stroke.Mark + UiHelpers.Scale),
                    Col.U32(Theme.Accent with { W = 0.18f }));
                break;

            case Look.Editorial:
                drawList.AddRectFilled(
                    new Vector2(x, y), new Vector2(x + ruleWidth, y + Stroke.Hair),
                    Col.U32(Col.Text with { W = 0.25f }));
                break;

            default:
                drawList.AddRectFilledMultiColor(
                    new Vector2(x, y), new Vector2(x + (ruleWidth * 0.6f), y + (Stroke.Mark * 0.8f)),
                    Col.U32(Theme.Accent with { W = 0.8f }), Col.U32(Theme.Accent with { W = 0f }),
                    Col.U32(Theme.Accent with { W = 0f }), Col.U32(Theme.Accent with { W = 0.8f }));
                break;
        }

        y += Sp.Xxl;

        var rowHeight = Metrics.RowTall * 1.15f;

        for (var i = 0; i < Destinations.Length; i++)
        {
            var min = new Vector2(origin.X, y);
            var max = new Vector2(origin.X + width, y + rowHeight);

            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton($"##dest{look}{i}", new Vector2(width, rowHeight), ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();
            var active = i == destination;

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                destination = i;

            var lit = Motion.Toward($"dest{look}{i}", active, 16f);
            var warm = Motion.Toward($"desth{look}{i}", hovered || active, 18f);

            if (lit > 0.01f)
            {
                var barTop = y + (rowHeight * 0.22f);
                var barBottom = y + (rowHeight * 0.78f);
                var bar = 2.5f * UiHelpers.Scale;

                if (look == Look.Neon)
                {
                    for (var ring = 3; ring >= 1; ring--)
                    {
                        var spread = ring * 2.4f * UiHelpers.Scale;
                        drawList.AddRectFilled(
                            new Vector2(origin.X - spread, barTop - spread),
                            new Vector2(origin.X + bar + spread, barBottom + spread),
                            Col.U32(Theme.Accent with { W = 0.05f * lit }),
                            spread);
                    }
                }

                drawList.AddRectFilled(
                    new Vector2(origin.X, barTop),
                    new Vector2(origin.X + bar, barTop + ((barBottom - barTop) * lit)),
                    Col.U32(Theme.Accent), Rad.Chip);
            }

            var numberColour = active ? Theme.Accent with { W = 0.9f } : Col.TextMuted;
            var nameColour = active
                ? Col.Text
                : Vector4.Lerp(Col.TextDim, Col.Text, warm);

            var textY = y + ((rowHeight - Text.CaptionSize) / 2f);

            using (Text.PushCaption())
            {
                var number = $"{i + 1:00}";
                Text.Tracked(drawList, new Vector2(x, textY), number, Col.U32(numberColour), 1.6f);

                var nameX = x + (Sp.Xxl * 1.15f);
                Text.Tracked(
                    drawList, new Vector2(nameX, textY),
                    Destinations[i].ToUpperInvariant(), Col.U32(nameColour), 1.8f,
                    (origin.X + width - Sp.Lg) - nameX);
            }

            y += rowHeight;
        }

        var lockSize = Metrics.TouchMin;
        var lockY = origin.Y + height - lockSize - Sp.Lg;
        var ruleY = lockY - Sp.Lg;

        drawList.AddRectFilled(
            new Vector2(x, ruleY), new Vector2(x + ruleWidth, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.45f }));

        var lockMin = new Vector2(x, lockY);

        ImGui.SetCursorScreenPos(lockMin);
        ImGui.InvisibleButton(
            $"##lock{look}", new Vector2(ruleWidth, lockSize), ImGuiButtonFlags.MouseButtonLeft);

        var lockOver = ImGui.IsItemHovered();

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            locked = !locked;

        using (Text.PushIcon())
        {
            UiHelpers.DrawScaledIcon(
                drawList, locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                lockMin + new Vector2(lockSize / 2f, lockSize / 2f),
                Col.U32(locked ? Theme.Accent : lockOver ? Col.Text : Col.TextMuted));
        }

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList,
                new Vector2(x + lockSize + Sp.Sm, lockY + ((lockSize - Text.CaptionSize) / 2f)),
                locked ? "LOCKED" : "UNLOCKED",
                Col.U32(locked ? Theme.Accent with { W = 0.85f } : Col.TextMuted),
                1.6f,
                ruleWidth - lockSize - Sp.Sm);
        }

        ImGui.SetCursorPos(cursor);
    }

    private static bool locked;

    /// Settings, minimise and close, at the window's top right.
    private static void DrawWindowChrome(Vector2 regionMin, float regionWidth, Look look)
    {
        var drawList = ImGui.GetWindowDrawList();

        var icons = new[] { FontAwesomeIcon.Cog, FontAwesomeIcon.WindowMinimize, FontAwesomeIcon.Times };
        var size = Metrics.TouchMin;
        var gap = Sp.Sm;

        var total = (size * icons.Length) + (gap * (icons.Length - 1));
        var x = regionMin.X + regionWidth - total;
        var y = regionMin.Y;

        for (var i = 0; i < icons.Length; i++)
        {
            var min = new Vector2(x, y);
            var over = ImGui.IsMouseHoveringRect(min, min + new Vector2(size, size));

            var tint = over
                ? i == icons.Length - 1 ? Col.Danger : Theme.Accent
                : Col.TextMuted;

            if (over && look == Look.Neon)
            {
                drawList.AddCircleFilled(
                    min + new Vector2(size / 2f, size / 2f), size * 0.62f,
                    Col.U32(tint with { W = 0.10f }), 24);
            }

            using (Text.PushIcon())
                UiHelpers.DrawScaledIcon(drawList, icons[i], min + new Vector2(size / 2f, size / 2f), Col.U32(tint));

            x += size + gap;
        }
    }


    private static void DrawGround(Vector2 min, Vector2 max, Look look)
    {
        var drawList = ImGui.GetWindowDrawList();

        switch (look)
        {
            case Look.Editorial:
                drawList.AddRectFilled(min, max, Col.U32(new Vector4(0.043f, 0.043f, 0.055f, 1f)));
                break;

            case Look.Glass:
            {
                var topColour = Col.U32(new Vector4(0.063f, 0.055f, 0.078f, 1f));
                var bottomColour = Col.U32(new Vector4(0.027f, 0.027f, 0.039f, 1f));
                drawList.AddRectFilledMultiColor(min, max, topColour, topColour, bottomColour, bottomColour);

                var centre = min + new Vector2((max.X - min.X) * 0.16f, 0f);
                var reach = (max.X - min.X) * 0.55f;

                for (var i = 7; i >= 1; i--)
                {
                    drawList.AddCircleFilled(
                        centre, reach * (i / 7f),
                        Col.U32(Theme.Accent with { W = 0.012f }), 48);
                }

                break;
            }

            default:
                drawList.AddRectFilled(min, max, Col.U32(new Vector4(0.016f, 0.012f, 0.02f, 1f)));
                break;
        }
    }


    private static void DrawShowcase(float width, Look look)
    {
        var subtitles = new[]
        {
            "Put any item in the game on your character",
            "Glamours other people have published",
            "Your published looks and the ones you saved",
            "Who can see your glamours, and who asked",
        };

        Heading(Destinations[destination], subtitles[destination], width, look);

        var gap = look == Look.Editorial ? Sp.Xl : Sp.Lg;

        var (columns, cardWidth) = Layout.Fit(width, Metrics.CardMin, gap);
        var imageHeight = cardWidth / 0.75f;

        for (var i = 0; i < Cards.Length; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine(0f, gap);

            DrawShowcaseCard(Cards[i], cardWidth, imageHeight, look, i);
        }

        ImGui.NewLine();
    }

    private static void DrawShowcaseCard(
        (string Title, string Author, string World, bool New) entry,
        float width, float imageHeight, Look look, int index)
    {
        var drawList = ImGui.GetWindowDrawList();

        var titleLine = ImGui.GetTextLineHeight() * 1.15f;
        var authorLine = ImGui.GetTextLineHeight() * 1.25f;
        var stripHeight = titleLine + authorLine + Sp.Md;

        var size = new Vector2(width, imageHeight + stripHeight);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;

        ImGui.InvisibleButton($"##frame{look}{index}", size, ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();
        var lift = Motion.Ease($"frame{look}{index}", hovered ? 1f : 0f, 14f);

        var imageMax = new Vector2(max.X, min.Y + imageHeight);

        switch (look)
        {
            case Look.Editorial:
                DrawFakePhoto(drawList, min, imageMax, 0f, index, lift);

                drawList.AddRectFilled(
                    new Vector2(min.X, imageMax.Y + Sp.Md),
                    new Vector2(max.X, imageMax.Y + Sp.Md + Stroke.Hair),
                    Col.U32(Col.EdgeRest with { W = 0.4f + (lift * 0.5f) }));
                break;

            case Look.Glass:
            {
                Elev.Shadow(drawList, min, max, Elev.Float, Rad.Card, 1f + (lift * 0.9f));

                if (lift > 0.01f)
                    Elev.Glow(drawList, min, max, Theme.Accent, 0.15f * lift, Rad.Card, 9.6f);

                drawList.AddRectFilled(
                    min, max, Col.U32(new Vector4(1f, 1f, 1f, 0.035f + (lift * 0.03f))), Rad.Card);

                DrawFakePhoto(drawList, min, imageMax, Rad.Card, index, lift);
                LitTopEdge(drawList, min, max, Rad.Card, 0.09f + (lift * 0.06f));
                break;
            }

            default:
            {
                drawList.AddRectFilled(min, max, Col.U32(new Vector4(0.035f, 0.027f, 0.047f, 1f)), Rad.Chip);
                DrawFakePhoto(drawList, min, imageMax, Rad.Chip, index, lift);

                var strength = 0.35f + (lift * 0.65f);

                for (var ring = 3; ring >= 0; ring--)
                {
                    var spread = ring * 1.6f * UiHelpers.Scale;
                    drawList.AddRect(
                        min - new Vector2(spread, spread), max + new Vector2(spread, spread),
                        Col.U32(Theme.Accent with { W = strength * (ring == 0 ? 0.9f : 0.07f) }),
                        Rad.Chip + spread, ImDrawFlags.None, ring == 0 ? Stroke.Edge : Stroke.Hair);
                }

                break;
            }
        }

        var textLeft = min.X + (look == Look.Editorial ? 0f : Sp.Lg);
        var textWidth = (max.X - textLeft) - (look == Look.Editorial ? 0f : Sp.Lg);
        var textTop = imageMax.Y + (look == Look.Editorial ? Sp.Lg : Sp.Sm);

        drawList.AddText(
            new Vector2(textLeft, textTop), Col.U32(Col.Text), Text.Truncate(entry.Title, textWidth));

        var by = $"{entry.Author} · {entry.World}";

        drawList.AddText(
            new Vector2(textLeft, textTop + titleLine),
            Col.U32(Col.TextDim),
            Text.Truncate(by, textWidth));

        if (!entry.New)
            return;

        var badge = min + new Vector2(Sp.Md, Sp.Md);

        switch (look)
        {
            case Look.Editorial:
                using (Text.PushCaption())
                {
                    Text.Tracked(drawList, badge, "NEW", Col.U32(new Vector4(1f, 1f, 1f, 0.92f)), 2.2f);
                }

                break;

            case Look.Glass:
                Pill.DrawAt(badge, "New", Pill.Tone.Accent);
                break;

            default:
                using (Text.PushCaption())
                {
                    var run = Text.TrackedWidth("NEW", 2.2f);
                    var pad = Sp.Sm;

                    drawList.AddRectFilled(
                        badge - new Vector2(pad, pad * 0.6f),
                        badge + new Vector2(run + pad, Text.CaptionSize + (pad * 0.6f)),
                        Col.U32(Theme.Accent), Rad.Chip);

                    Text.Tracked(drawList, badge, "NEW", Col.U32(Theme.Background), 2.2f);
                }

                break;
        }
    }

    /// A stand-in for a glamour screenshot: a vertical gradient with a figure-ish shape in it, so the frames
    /// can be judged with something picture-shaped in them rather than grey rectangles.
    private static void DrawFakePhoto(
        ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, int seed, float lift)
    {
        if (PhotoFor(seed) is { } photo)
        {
            CardGrid.CoverImage(drawList, photo, min, max, lift > 0.5f);
            return;
        }

        var hues = new[]
        {
            (new Vector4(0.24f, 0.18f, 0.32f, 1f), new Vector4(0.11f, 0.10f, 0.18f, 1f)),
            (new Vector4(0.32f, 0.22f, 0.20f, 1f), new Vector4(0.13f, 0.10f, 0.12f, 1f)),
            (new Vector4(0.16f, 0.24f, 0.30f, 1f), new Vector4(0.08f, 0.12f, 0.17f, 1f)),
        };

        var (top, bottom) = hues[seed % hues.Length];

        if (rounding > 0f)
        {
            drawList.AddRectFilled(
                min, new Vector2(max.X, min.Y + rounding), Col.U32(top), rounding, ImDrawFlags.RoundCornersTop);
        }

        drawList.AddRectFilledMultiColor(
            new Vector2(min.X, min.Y + rounding), max,
            Col.U32(top), Col.U32(top), Col.U32(bottom), Col.U32(bottom));

        var size = max - min;
        var inset = size * 0.015f * lift;
        var body = new Vector2(size.X * 0.22f, size.Y * 0.46f);
        var centre = new Vector2(min.X + (size.X / 2f), max.Y - (size.Y * 0.30f)) + inset;

        drawList.AddCircleFilled(
            centre - new Vector2(0f, body.Y * 0.95f), size.X * 0.095f,
            Col.U32(new Vector4(1f, 1f, 1f, 0.07f)), 28);

        drawList.AddRectFilled(
            centre - new Vector2(body.X / 2f, body.Y * 0.55f),
            centre + new Vector2(body.X / 2f, body.Y * 0.55f),
            Col.U32(new Vector4(1f, 1f, 1f, 0.06f)),
            body.X * 0.4f);
    }


    private static void DrawToolSide(float width, Look look)
    {
        Heading("Filters", "Where a look either holds up or falls over", width, look);

        var rows = new[] { "Head", "Body", "Hands", "Legs", "Feet" };

        switch (look)
        {
            case Look.Editorial:
                SectionLabel("Slots", look);

                for (var i = 0; i < rows.Length; i++)
                    DrawToolRow(rows[i], width, look, i, i == 1);

                ImGui.Dummy(new Vector2(0f, Sp.Xl));
                SectionLabel("Sources", look);
                DrawChips(width, look);
                break;

            case Look.Glass:
            {
                using (var pane = Surface.Begin(width, SurfaceKind.Card))
                {
                    SectionLabel("Slots", look);

                    for (var i = 0; i < rows.Length; i++)
                        DrawToolRow(rows[i], pane.Box.Inner, look, i, i == 1);

                    ImGui.Dummy(new Vector2(0f, Sp.Lg));
                    SectionLabel("Sources", look);
                    DrawChips(pane.Box.Inner, look);
                }

                break;
            }

            default:
            {
                var drawList = ImGui.GetWindowDrawList();
                var start = ImGui.GetCursorScreenPos();

                SectionLabel("Slots", look);

                for (var i = 0; i < rows.Length; i++)
                    DrawToolRow(rows[i], width, look, i, i == 1);

                ImGui.Dummy(new Vector2(0f, Sp.Lg));
                SectionLabel("Sources", look);
                DrawChips(width, look);

                var end = ImGui.GetCursorScreenPos();
                drawList.AddRectFilled(
                    new Vector2(start.X - Sp.Md, start.Y),
                    new Vector2(start.X - Sp.Md + (2f * UiHelpers.Scale), end.Y),
                    Col.U32(Theme.Accent with { W = 0.7f }));
                break;
            }
        }
    }

    private static void DrawToolRow(string label, float width, Look look, int index, bool selected)
    {
        var drawList = ImGui.GetWindowDrawList();
        var height = Metrics.RowTall;
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, height);
        var max = min + size;

        ImGui.InvisibleButton($"##toolrow{look}{index}", size, ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();
        var warmth = Motion.Ease($"toolrow{look}{index}", hovered || selected ? 1f : 0f, 16f);

        switch (look)
        {
            case Look.Editorial:
                drawList.AddRectFilled(
                    new Vector2(min.X, max.Y - Stroke.Hair), max,
                    Col.U32(Col.EdgeRest with { W = 0.3f }));

                if (selected || warmth > 0.01f)
                {
                    drawList.AddRectFilled(
                        new Vector2(min.X, min.Y + (height * 0.34f)),
                        new Vector2(min.X + (Sp.Lg * warmth), min.Y + (height * 0.34f) + Stroke.Mark),
                        Col.U32(Theme.Accent with { W = warmth }));
                }

                break;

            case Look.Glass:
                if (warmth > 0.01f)
                {
                    drawList.AddRectFilled(
                        min, max, Col.U32(new Vector4(1f, 1f, 1f, 0.04f * warmth)), Rad.Ctl);
                }

                if (selected)
                {
                    drawList.AddRectFilled(
                        min, new Vector2(min.X + (3f * UiHelpers.Scale), max.Y),
                        Col.U32(Theme.Accent), Rad.Chip, ImDrawFlags.RoundCornersLeft);
                }

                break;

            default:
                drawList.AddRectFilled(
                    min, max,
                    Col.U32(new Vector4(1f, 1f, 1f, 0.02f + (0.03f * warmth))), Rad.Chip);

                if (selected)
                {
                    drawList.AddRect(
                        min, max, Col.U32(Theme.Accent with { W = 0.8f }), Rad.Chip,
                        ImDrawFlags.None, Stroke.Edge);
                }

                break;
        }

        var textLeft = min.X + (look == Look.Editorial ? Sp.Xl : Sp.Lg);
        var tint = selected ? Col.Text : Col.TextDim;

        if (look == Look.Neon)
        {
            Text.Tracked(
                drawList, new Vector2(textLeft, min.Y + ((height - ImGui.GetTextLineHeight()) / 2f)),
                label.ToUpperInvariant(), Col.U32(selected ? Theme.Accent : tint), 1.4f,
                (max.X - textLeft) - Sp.Xxl);
        }
        else
        {
            drawList.AddText(
                new Vector2(textLeft, min.Y + ((height - ImGui.GetTextLineHeight()) / 2f)),
                Col.U32(tint), label);
        }

        using (Text.PushCaption())
        {
            const string worn = "Scion Adventurer's Jacket";
            var caption = Text.Truncate(worn, (max.X - textLeft) - Sp.Xxl);

            drawList.AddText(
                new Vector2(max.X - Sp.Lg - Text.Width(caption), min.Y + ((height - Text.CaptionSize) / 2f)),
                Col.U32(Col.TextMuted),
                caption);
        }
    }

    private static void DrawChips(float width, Look look)
    {
        var chips = new[] { "Marketable", "Store", "Seasonal", "Owned" };
        var x = 0f;

        for (var i = 0; i < chips.Length; i++)
        {
            var measured = Pill.Measure(chips[i]);

            if (x > 0f && x + measured > width)
            {
                ImGui.NewLine();
                x = 0f;
            }
            else if (x > 0f)
            {
                ImGui.SameLine(0f, Sp.Sm);
            }

            var tone = look switch
            {
                Look.Neon => i == 0 ? Pill.Tone.Accent : Pill.Tone.Neutral,
                _ => i == 0 ? Pill.Tone.Info : Pill.Tone.Neutral,
            };

            Pill.Draw(chips[i], tone);
            x += measured + Sp.Sm;
        }

        ImGui.NewLine();
    }


    private static void Heading(string title, string subtitle, float width, Look look)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        using (Text.PushDisplay())
        {
            if (look == Look.Neon)
                Text.Tracked(drawList, origin, title.ToUpperInvariant(), Col.U32(Col.Text), 3f, width);
            else
                drawList.AddText(origin, Col.U32(Col.Text), Text.Truncate(title, width));
        }

        var y = origin.Y + (Text.DisplaySize * 1.15f);

        using (Text.PushCaption())
        {
            drawList.AddText(new Vector2(origin.X, y), Col.U32(Col.TextMuted), subtitle);
        }

        var ruleY = y + (Text.CaptionSize * 1.9f);

        switch (look)
        {
            case Look.Editorial:
                drawList.AddRectFilled(
                    new Vector2(origin.X, ruleY), new Vector2(origin.X + width, ruleY + Stroke.Hair),
                    Col.U32(Col.Text with { W = 0.22f }));
                break;

            case Look.Glass:
                drawList.AddRectFilledMultiColor(
                    new Vector2(origin.X, ruleY),
                    new Vector2(origin.X + (width * 0.32f), ruleY + (Stroke.Mark * 0.8f)),
                    Col.U32(Theme.Accent with { W = 0.85f }), Col.U32(Theme.Accent with { W = 0f }),
                    Col.U32(Theme.Accent with { W = 0f }), Col.U32(Theme.Accent with { W = 0.85f }));
                break;

            default:
                drawList.AddRectFilled(
                    new Vector2(origin.X, ruleY), new Vector2(origin.X + width, ruleY + Stroke.Mark),
                    Col.U32(Theme.Accent with { W = 0.55f }));

                drawList.AddRectFilled(
                    new Vector2(origin.X, ruleY - UiHelpers.Scale),
                    new Vector2(origin.X + width, ruleY + Stroke.Mark + UiHelpers.Scale),
                    Col.U32(Theme.Accent with { W = 0.12f }));
                break;
        }

        ImGui.Dummy(new Vector2(width, (ruleY - origin.Y) + Sp.Xl));
    }

    private static void SectionLabel(string label, Look look)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        using (Text.PushCaption())
        {
            var colour = look == Look.Neon ? Theme.Accent with { W = 0.85f } : Col.TextMuted;
            Text.Tracked(drawList, origin, label.ToUpperInvariant(), Col.U32(colour), 2f);
        }

        ImGui.Dummy(new Vector2(0f, Text.CaptionSize * 1.9f));
    }

    /// The one-pixel highlight along a pane's top edge, which is what reads as glass catching light.
    private static void LitTopEdge(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float alpha)
    {
        drawList.AddLine(
            new Vector2(min.X + rounding, min.Y + (Stroke.Hair / 2f)),
            new Vector2(max.X - rounding, min.Y + (Stroke.Hair / 2f)),
            Col.U32(new Vector4(1f, 1f, 1f, alpha)),
            Stroke.Hair);
    }
}
