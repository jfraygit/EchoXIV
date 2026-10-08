using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;

namespace EchoMix.Plugin.UI.Shell;

/// A note from the developer, shown once, the first time anybody opens the 2.0 decks.
public sealed class WelcomeToTwoScreen
{
    private const string Title = "Thank You for Using EchoMix";

    /// The letter.
    private static readonly string[] Body =
    {
        "EchoMix is a passion project. I build it because I love building it. I use it myself "
        + "during my own FF14 sessions, so anyone else who has found a place for it in theirs "
        + "genuinely means a lot to me. Thank you for being here.",

        "This is 2.0, and it is a full redesign rather than a new coat of paint. 1.0 grew a feature "
        + "at a time and it showed: nothing in it was placed knowing what would come later. This "
        + "one was built with the whole picture in mind. Every screen is laid out on purpose, and "
        + "everything you used before is still here, just somewhere that makes sense.",
    };

    /// The practical asides, kept out of the letter above - mixing an instruction into a personal note makes
    /// both read worse.
    private static readonly (string Title, string Body)[] Callouts =
    {
        ("Prefer the old look?",
            "Settings > Appearance > EchoMix 2.0 Design switches back, any time. Nothing is lost "
            + "either way."),

        ("Prefer the keyboard?",
            "Left and Right open and close the nav menu. Up and Down move through it, or through "
            + "the current screen's own list once it is collapsed. While the window is open these "
            + "keys belong to EchoMix rather than the game."),

        ("Found a bug, or got an idea?",
            "Report a Bug is in Settings > About, and it sends me the session log with it. For "
            + "feedback and suggestions, come and say so on the Discord. The invite is in that "
            + "same section."),
    };

    private readonly Plugin plugin;

    /// Eased in rather than appearing - this lands straight after the intro animation, and a hard cut between
    /// the two would undo what the intro just earned.
    private float appear;

    public WelcomeToTwoScreen(Plugin plugin) => this.plugin = plugin;

    /// Draws over the content area.
    public bool Draw(Vector2 contentOrigin, Vector2 contentSize)
    {
        appear = Motion.Approach(appear, 1f, Motion.SpeedFast);

        ImGui.SetCursorScreenPos(contentOrigin);
        ImGui.BeginChild("##v2TwoPointOhNote", contentSize, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var dismissed = DrawCard(contentOrigin, contentSize);

        ImGui.EndChild();
        return dismissed;
    }

    private bool DrawCard(Vector2 contentOrigin, Vector2 contentSize)
    {
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(contentOrigin, contentOrigin + contentSize,
            ImGui.GetColorU32(Semantic.Alpha(Vector4.Zero, 0.78f * appear)));

        var cardWidth = MathF.Min(MathF.Round(580f * Metrics.Scale), contentSize.X - (Metrics.Xxxl * 2f));
        var inner = cardWidth - (Metrics.Xxxl * 2f);
        var lineHeight = ImGui.GetTextLineHeight();

        float titleHeight;
        using (TypeScale.Heading())
            titleHeight = ImGui.CalcTextSize(Title).Y;

        var bodyHeight = 0f;
        foreach (var paragraph in Body)
        {
            using (TypeScale.Body())
                bodyHeight += UiHelpers.WrapToWidth(paragraph, inner, 12).Length * lineHeight;

            bodyHeight += Metrics.Lg;
        }

        var calloutHeights = new float[Callouts.Length];
        var calloutsTotal = 0f;

        for (var i = 0; i < Callouts.Length; i++)
        {
            float lines;
            using (TypeScale.Caption())
                lines = UiHelpers.WrapToWidth(Callouts[i].Body, inner - (Metrics.Lg * 2f), 4).Length;

            calloutHeights[i] = MathF.Round((Metrics.Md * 2f) + lineHeight + Metrics.Xs + (lines * lineHeight));
            calloutsTotal += calloutHeights[i] + Metrics.Md;
        }

        calloutsTotal = MathF.Max(0f, calloutsTotal - Metrics.Md);
        var buttonHeight = MathF.Round(Metrics.ControlLg);

        var cardHeight = MathF.Round(
            (Metrics.Xxxl * 2f)
            + titleHeight + Metrics.Sm + Metrics.Xl
            + bodyHeight
            + calloutsTotal + Metrics.Xxl
            + buttonHeight);

        var cardMin = Chrome.Snap(new Vector2(
            contentOrigin.X + ((contentSize.X - cardWidth) * 0.5f),
            contentOrigin.Y + MathF.Max(Metrics.Xxl, (contentSize.Y - cardHeight) * 0.5f)));

        cardMin.Y += (1f - appear) * 18f * Metrics.Scale;

        var cardMax = cardMin + new Vector2(cardWidth, cardHeight);

        Elevation.DrawSurface(drawList, cardMin, cardMax,
            Semantic.Alpha(Elevation.Overlay, appear), Metrics.RadiusPanel,
            Elevation.ShadowSpec.High, topEdge: true,
            Semantic.Alpha(Semantic.Primary, 0.3f * appear));

        var x = cardMin.X + Metrics.Xxxl;
        var y = cardMin.Y + Metrics.Xxxl;

        using (TypeScale.Heading())
            Chrome.Text(drawList, new Vector2(x, y),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, appear)), Title);

        y += titleHeight + Metrics.Sm;

        drawList.AddLine(
            Chrome.Snap(new Vector2(x, y)),
            Chrome.Snap(new Vector2(x + (inner * 0.18f), y)),
            ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.8f * appear)),
            MathF.Max(2f, 2f * Metrics.Scale));

        y += Metrics.Xl;

        foreach (var paragraph in Body)
        {
            using (TypeScale.Body())
            {
                foreach (var line in UiHelpers.WrapToWidth(paragraph, inner, 12))
                {
                    Chrome.Text(drawList, new Vector2(x, y),
                        ImGui.GetColorU32(Semantic.Alpha(Semantic.TextSecondary, appear)), line);

                    y += lineHeight;
                }
            }

            y += Metrics.Lg;
        }

        for (var i = 0; i < Callouts.Length; i++)
        {
            DrawCallout(drawList, new Vector2(x, y), inner, calloutHeights[i], lineHeight, Callouts[i]);
            y += calloutHeights[i] + Metrics.Md;
        }

        y += Metrics.Xxl - Metrics.Md;

        var buttonWidth = MathF.Round(180f * Metrics.Scale);
        ImGui.SetCursorScreenPos(new Vector2(cardMax.X - Metrics.Xxxl - buttonWidth, y));

        if (!Fields.Button("Let's Mix", Fields.ButtonStyle.Primary, buttonWidth,
                height: buttonHeight, idSuffix: "welcomeTwo"))
        {
            return false;
        }

        plugin.Configuration.HasSeenTwoPointOhNote = true;
        plugin.Configuration.Save();
        return true;
    }

    /// The one practical instruction, in a tinted inset so it reads as a tip rather than as the last line of
    /// the letter.
    private void DrawCallout(
        ImDrawListPtr drawList, Vector2 origin, float width, float height, float lineHeight,
        (string Title, string Body) callout)
    {
        var max = origin + new Vector2(width, height);

        drawList.AddRectFilled(Chrome.Snap(origin), Chrome.Snap(max),
            ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.08f * appear)), Metrics.RadiusSoft);

        drawList.AddRectFilled(
            Chrome.Snap(origin),
            Chrome.Snap(new Vector2(origin.X + MathF.Max(2f, 3f * Metrics.Scale), max.Y)),
            ImGui.GetColorU32(Semantic.Alpha(Semantic.Primary, 0.7f * appear)), Metrics.RadiusSharp);

        var textX = origin.X + Metrics.Lg;
        var y = origin.Y + Metrics.Md;

        using (TypeScale.Body())
            Chrome.Text(drawList, new Vector2(textX, y),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, appear)), callout.Title);

        y += lineHeight + Metrics.Xs;

        using (TypeScale.Caption())
        {
            foreach (var line in UiHelpers.WrapToWidth(callout.Body, width - (Metrics.Lg * 2f), 4))
            {
                Chrome.Text(drawList, new Vector2(textX, y),
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.TextTertiary, appear)), line);

                y += lineHeight;
            }
        }
    }
}
