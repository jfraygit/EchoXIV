using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Design;
using EchoMix.Plugin.UI.State;

namespace EchoMix.Plugin.UI.Screens;

/// The first screen anyone sees: DJ or Listener.
public sealed class WelcomeScreen
{
    private const string Greeting = "Welcome to";

    private readonly Plugin plugin;

    /// Eased in rather than appearing, because this lands straight out of the intro's dissolve and a hard cut
    /// between the two would undo what the intro just earned.
    private float appear;
    private float djHover;
    private float listenerHover;

    /// The last frame Draw ran on.
    private int lastDrawnFrame = -2;

    public WelcomeScreen(Plugin plugin) => this.plugin = plugin;

    public void Draw(Vector2 origin, Vector2 size)
    {
        var frame = ImGui.GetFrameCount();
        if (frame - lastDrawnFrame > 1)
        {
            appear = 0f;
            djHover = 0f;
            listenerHover = 0f;
        }

        lastDrawnFrame = frame;

        appear = Motion.Approach(appear, 1f, Motion.SpeedNormal);

        var drawList = ImGui.GetWindowDrawList();

        var cardSize = new Vector2(MathF.Round(176f * Metrics.Scale), MathF.Round(178f * Metrics.Scale));
        var gap = MathF.Round(Metrics.Xxl);

        var greetingHeight = TypeScale.Measure(TypeScale.Body, Greeting).Y;

        float markHeight;
        using (TypeScale.Display())
            markHeight = ImGui.GetTextLineHeight();

        var blockHeight = greetingHeight + Metrics.Xs + markHeight + Metrics.Xxl + cardSize.Y;
        var top = origin.Y + MathF.Max(Metrics.Xxl, (size.Y - blockHeight) * 0.5f);

        top += (1f - Motion.EaseOutCubic(appear)) * 16f * Metrics.Scale;

        using (TypeScale.Body())
        {
            var greetingWidth = ImGui.CalcTextSize(Greeting).X;
            Chrome.Text(drawList, new Vector2(origin.X + ((size.X - greetingWidth) * 0.5f), top),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextSecondary, appear)), Greeting);
        }

        top += greetingHeight + Metrics.Xs;

        using (TypeScale.Display())
        {
            var markWidth = Wordmark.Measure();
            Wordmark.Draw(drawList,
                new Vector2(origin.X + ((size.X - markWidth) * 0.5f), top), appear);
        }

        top += markHeight + Metrics.Xxl;

        var groupWidth = (cardSize.X * 2f) + gap;
        var left = origin.X + ((size.X - groupWidth) * 0.5f);

        if (DrawChoice("##v2welcomeDj", new Vector2(left, top), cardSize, FontAwesomeIcon.RecordVinyl,
                "DJ", "Mix tracks and broadcast your own show.", Semantic.DeckA, ref djHover))
        {
            Choose(UserRole.Dj, EchoMixView.Deck);
        }

        if (DrawChoice("##v2welcomeListener", new Vector2(left + cardSize.X + gap, top), cardSize,
                FontAwesomeIcon.Headphones, "Listener", "Join a show someone else is hosting.",
                Semantic.DeckB, ref listenerHover))
        {
            Choose(UserRole.Listener, EchoMixView.JoinShow);
        }
    }

    private void Choose(UserRole role, EchoMixView view)
    {
        plugin.Configuration.LastChosenRole = role;
        plugin.Configuration.Save();

        plugin.Router.HasClearedWelcomeThisSession = true;

        plugin.Router.RequestView(view);
    }

    /// One card.
    private bool DrawChoice(
        string id, Vector2 pos, Vector2 size, FontAwesomeIcon icon,
        string label, string detail, Vector4 accent, ref float hover)
    {
        pos = Chrome.Snap(pos);

        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        hover = Motion.Approach(hover, held ? 1f : hovered ? 0.6f : 0f, Motion.SpeedFast);

        var drawList = ImGui.GetWindowDrawList();
        var sink = MathF.Round(held ? 2f * Metrics.Scale : 0f);
        var min = pos + new Vector2(0f, sink);
        var max = min + size;

        if (hover > 0.01f)
        {
            for (var i = 3; i >= 1; i--)
            {
                var grow = i * 3f * Metrics.Scale * hover;
                drawList.AddRectFilled(min - new Vector2(grow, grow), max + new Vector2(grow, grow),
                    ImGui.GetColorU32(Semantic.Alpha(accent, 0.05f * hover)),
                    Metrics.RadiusCard + grow);
            }
        }

        Elevation.DrawSurface(drawList, min, max,
            Semantic.Alpha(Vector4.Lerp(Elevation.Raised, Elevation.Overlay, hover), appear),
            Metrics.RadiusCard,
            held ? Elevation.ShadowSpec.Low : Elevation.ShadowSpec.Medium,
            topEdge: true,
            Semantic.Alpha(accent, (0.35f + (0.55f * hover)) * appear));

        var glyphColor = ImGui.GetColorU32(
            Semantic.Alpha(Semantic.Lift(accent, 0.25f * hover), appear));

        using (TypeScale.IconLarge())
        {
            UiHelpers.DrawScaledIcon(drawList, icon,
                Chrome.Snap(new Vector2(min.X + (size.X * 0.5f), min.Y + (size.Y * 0.34f))),
                glyphColor);
        }

        var textTop = min.Y + (size.Y * 0.56f);

        using (TypeScale.Heading())
        {
            var labelSize = ImGui.CalcTextSize(label);
            Chrome.Text(drawList, new Vector2(min.X + ((size.X - labelSize.X) * 0.5f), textTop),
                ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, appear)), label);
            textTop += labelSize.Y + Metrics.Sm;
        }

        using (TypeScale.Caption())
        {
            var lineHeight = ImGui.GetTextLineHeight();
            foreach (var line in UiHelpers.WrapToWidth(detail, size.X - (Metrics.Lg * 2f), 3))
            {
                var lineWidth = ImGui.CalcTextSize(line).X;
                Chrome.Text(drawList, new Vector2(min.X + ((size.X - lineWidth) * 0.5f), textTop),
                    ImGui.GetColorU32(Semantic.Alpha(Semantic.TextTertiary, appear)), line);
                textTop += lineHeight;
            }
        }

        return clicked;
    }
}
