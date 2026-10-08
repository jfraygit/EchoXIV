namespace EchoMix.Plugin.UI.Design;

/// Spacing, radius and control-size scales for the 2.0 look.
public static class Metrics
{
    /// Set per frame by whichever window is drawing.
    public static float Scale { get; set; } = 1f;

    public static float Xs => 2f * Scale;
    public static float Sm => 4f * Scale;
    public static float Md => 8f * Scale;
    public static float Lg => 12f * Scale;
    public static float Xl => 16f * Scale;
    public static float Xxl => 24f * Scale;
    public static float Xxxl => 32f * Scale;

    /// Radius scale.
    public static float RadiusSharp => 3f * Scale;
    public static float RadiusSoft => 7f * Scale;
    public static float RadiusCard => 11f * Scale;
    public static float RadiusPanel => 15f * Scale;

    /// Half the shorter side, so a rect becomes a capsule regardless of its dimensions.
    public static float Pill(float shorterSide) => shorterSide * 0.5f;

    public static float ControlXs => 20f * Scale;
    public static float ControlSm => 26f * Scale;
    public static float ControlMd => 32f * Scale;
    public static float ControlLg => 40f * Scale;
    public static float ControlXl => 48f * Scale;

    public static float RailWidth => 190f * Scale;
    public static float RailCollapsedWidth => 12f * Scale;
    public static float RailItemHeight => 38f * Scale;
    public static float TopBarHeight => 44f * Scale;

    /// Standard inner padding for a panel or card.
    public static System.Numerics.Vector2 PanelPadding => new(14f * Scale, 12f * Scale);

    /// Hairline stroke width.
    public static float Hairline => Scale < 1.25f ? 1f : 1.5f;
}
