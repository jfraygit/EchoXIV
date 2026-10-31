using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// Every token, drawn at its real size, so the scale can be looked at rather than reasoned about.
internal static class TokenSpecimen
{
    public static void Draw()
    {
        var scale = UiHelpers.Scale;

        ImGui.TextColored(
            Col.TextDim,
            $"scale {scale:0.00}   body {Text.BodySize:0.0}px   " +
            $"title {Text.TitleSize:0.0}px   caption {Text.CaptionSize:0.0}px   " +
            $"window {MainWindow.DesignSize.X:0}x{MainWindow.DesignSize.Y:0}");

        var atlas = ImGui.GetIO().Fonts;
        var bytes = (long)atlas.TexWidth * atlas.TexHeight * 4L;
        ImGui.TextColored(
            Col.TextDim,
            $"shared font atlas {atlas.TexWidth}x{atlas.TexHeight} ~{bytes / (1024f * 1024f):0.0} MB");

        ImGui.Separator();

        DrawType();
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        DrawSpacing();
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        DrawRadius();
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        DrawStroke();
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        DrawElevation();
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        DrawColours();
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        DrawPills();
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        DrawRows();
    }

    private static void DrawPills()
    {
        Heading("Pills");

        (string Label, Pill.Tone Tone)[] pills =
        [
            ("Neutral", Pill.Tone.Neutral),
            ("New", Pill.Tone.Accent),
            ("Marketable", Pill.Tone.Info),
            ("Owned", Pill.Tone.Success),
            ("Hidden", Pill.Tone.Caution),
            ("Can't wear", Pill.Tone.Danger),
        ];

        foreach (var (label, tone) in pills)
        {
            Pill.Draw(label, tone, $"Tone: {tone}");
            ImGui.SameLine(0f, Sp.Sm);
        }

        ImGui.NewLine();
    }

    /// Every control state on one row type, which is the only way to see that disabled is actually
    /// distinguishable from rest - three controls had no disabled state at all before ControlState existed,
    /// so nobody had ever had to look at one.
    private static void DrawRows()
    {
        Heading("Rows");

        var width = MathF.Min(ImGui.GetContentRegionAvail().X, 420f * UiHelpers.Scale);

        (string Label, ControlState State, Vector4? Accent, Vector4? Tone)[] rows =
        [
            ("Rest", ControlState.Rest, null, null),
            ("Selected", ControlState.Selected, null, null),
            ("Disabled", ControlState.Disabled, null, null),
            ("With an accent rail", ControlState.Rest, Theme.Accent, null),
            ("Confirming something destructive", ControlState.Rest, null, Col.Danger),
        ];

        for (var i = 0; i < rows.Length; i++)
        {
            var (label, state, accent, tone) = rows[i];

            var row = Row.Draw(
                $"##specimenrow{i}", width, forced: state, accent: accent, tone: tone,
                enabled: state != ControlState.Disabled);

            Row.TwoLines(row, row.ContentRight - row.TextLeft, label, $"state: {row.State}");

            ImGui.Dummy(new Vector2(0f, Sp.Xs));
        }
    }

    /// THE SCENE THAT PROVES THE FONT FIX.
    private static void DrawType()
    {
        Heading("Type");

        var display = $"Display  {Text.DisplaySize:0.0}px";
        var title = $"Title  {Text.TitleSize:0.0}px";
        var body = $"Body  {Text.BodySize:0.0}px";
        var caption = $"Caption  {Text.CaptionSize:0.0}px";

        using (Text.PushDisplay())
            ImGui.TextUnformatted(display);

        using (Text.PushTitle())
            ImGui.TextUnformatted(title);

        ImGui.TextUnformatted(body);

        using (Text.PushCaption())
            ImGui.TextUnformatted(caption);

        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        var tracked = ImGui.GetCursorScreenPos();
        var run = Text.Tracked(ImGui.GetWindowDrawList(), tracked, "TRACKED CAPITALS", Col.U32(Col.TextDim), 1.6f);
        ImGui.Dummy(new Vector2(run, ImGui.GetTextLineHeight()));
    }

    private static void DrawSpacing()
    {
        Heading("Spacing");

        (string Name, float Value)[] steps =
        [
            ("Xxs", Sp.Xxs), ("Xs", Sp.Xs), ("Sm", Sp.Sm), ("Md", Sp.Md),
            ("Lg", Sp.Lg), ("Xl", Sp.Xl), ("Xxl", Sp.Xxl), ("Xxxl", Sp.Xxxl),
        ];

        var drawList = ImGui.GetWindowDrawList();
        var bar = ImGui.GetTextLineHeight();

        foreach (var (name, value) in steps)
        {
            var origin = ImGui.GetCursorScreenPos();
            drawList.AddRectFilled(
                origin, origin + new Vector2(MathF.Max(value, 1f), bar), Col.U32(Theme.Accent), Rad.Chip);

            ImGui.Dummy(new Vector2(Sp.Xxxl + Sp.Md, bar));
            ImGui.SameLine(0f, Sp.Md);
            ImGui.TextColored(Col.TextDim, $"{name}  {value:0.0}px");
        }
    }

    private static void DrawRadius()
    {
        Heading("Radius");

        (string Name, float Value)[] radii =
            [("Chip", Rad.Chip), ("Ctl", Rad.Ctl), ("Panel", Rad.Panel), ("Card", Rad.Card)];

        var drawList = ImGui.GetWindowDrawList();
        var box = 44f * UiHelpers.Scale;

        foreach (var (name, value) in radii)
        {
            var origin = ImGui.GetCursorScreenPos();
            drawList.AddRectFilled(origin, origin + new Vector2(box, box), Col.U32(Col.Raised), value);
            drawList.AddRect(
                origin, origin + new Vector2(box, box), Col.U32(Col.EdgeRest), value,
                ImDrawFlags.None, Stroke.Edge);

            ImGui.Dummy(new Vector2(box, box));
            ImGui.SameLine(0f, Sp.Md);
            ImGui.TextColored(Col.TextDim, $"{name} {value:0.0}");
            ImGui.SameLine(0f, Sp.Lg);
        }

        ImGui.NewLine();
    }

    private static void DrawStroke()
    {
        Heading("Stroke");

        (string Name, float Value)[] strokes =
            [("Hair", Stroke.Hair), ("Edge", Stroke.Edge), ("Focus", Stroke.Focus), ("Mark", Stroke.Mark)];

        var drawList = ImGui.GetWindowDrawList();
        var run = 70f * UiHelpers.Scale;
        var bar = ImGui.GetTextLineHeight();

        foreach (var (name, value) in strokes)
        {
            var origin = ImGui.GetCursorScreenPos();
            var mid = origin.Y + (bar / 2f);
            drawList.AddRectFilled(
                new Vector2(origin.X, mid - (value / 2f)),
                new Vector2(origin.X + run, mid + (value / 2f)),
                Col.U32(Col.Text));

            ImGui.Dummy(new Vector2(run, bar));
            ImGui.SameLine(0f, Sp.Md);
            ImGui.TextColored(Col.TextDim, $"{name} {value:0.0}");
            ImGui.SameLine(0f, Sp.Lg);
        }

        ImGui.NewLine();
    }

    /// Four levels side by side.
    private static void DrawElevation()
    {
        Heading("Elevation");

        var drawList = ImGui.GetWindowDrawList();
        var box = new Vector2(96f, 52f) * UiHelpers.Scale;

        for (var level = 0; level <= Elev.Float; level++)
        {
            var origin = ImGui.GetCursorScreenPos();

            Elev.Shadow(drawList, origin, origin + box, level, Rad.Card);
            drawList.AddRectFilled(origin, origin + box, Col.U32(Col.Surface), Rad.Card);
            drawList.AddRect(
                origin, origin + box, Col.U32(Col.EdgeRest), Rad.Card, ImDrawFlags.None, Stroke.Edge);

            using (Text.PushCaption())
            {
                var label = level switch
                {
                    Elev.Block => "Block",
                    Elev.Card => "Card",
                    Elev.Float => "Float",
                    _ => "None",
                };

                drawList.AddText(origin + new Vector2(Sp.Md, Sp.Sm), Col.U32(Col.TextDim), label);
            }

            ImGui.Dummy(box);
            ImGui.SameLine(0f, Sp.Xl);
        }

        ImGui.NewLine();
        ImGui.Dummy(new Vector2(0f, Sp.Md));
    }

    private static void DrawColours()
    {
        Heading("Colour roles");

        (string Name, Vector4 Value)[][] rows =
        [
            [("Window", Col.Window), ("Surface", Col.Surface), ("Raised", Col.Raised), ("Sunken", Col.Sunken), ("Track", Col.Track)],
            [("CtlRest", Col.CtlRest), ("CtlHover", Col.CtlHover), ("CtlActive", Col.CtlActive), ("SelRest", Col.SelRest), ("SelActive", Col.SelActive)],
            [("Text", Col.Text), ("TextDim", Col.TextDim), ("TextMuted", Col.TextMuted), ("Knob", Col.Knob), ("EdgeRest", Col.EdgeRest)],
            [("Info", Col.Info), ("Success", Col.Success), ("Caution", Col.Caution), ("Danger", Col.Danger), ("Vote", Col.Vote)],
        ];

        var drawList = ImGui.GetWindowDrawList();
        var chip = 34f * UiHelpers.Scale;

        foreach (var row in rows)
        {
            foreach (var (name, value) in row)
            {
                var origin = ImGui.GetCursorScreenPos();

                drawList.AddRectFilled(
                    origin, origin + new Vector2(chip, chip),
                    Col.U32(new Vector4(0f, 0f, 0f, 0.55f)), Rad.Chip);

                drawList.AddRectFilled(
                    origin + new Vector2(Sp.Xxs, Sp.Xxs),
                    origin + new Vector2(chip - Sp.Xxs, chip - Sp.Xxs),
                    Col.U32(value), Rad.Chip);

                ImGui.Dummy(new Vector2(chip, chip));

                if (ImGui.IsItemHovered())
                    UiHelpers.WrappedTooltip($"{name}\n{value.X:0.00}, {value.Y:0.00}, {value.Z:0.00}, {value.W:0.00}");

                ImGui.SameLine(0f, Sp.Xs);

                using (Text.PushCaption())
                    ImGui.TextColored(Col.TextDim, name);

                ImGui.SameLine(0f, Sp.Lg);
            }

            ImGui.NewLine();
        }
    }

    private static void Heading(string label)
    {
        using (Text.PushTitle())
            ImGui.TextColored(Theme.Accent, label);

        ImGui.Dummy(new Vector2(0f, Sp.Xs));
    }
}
