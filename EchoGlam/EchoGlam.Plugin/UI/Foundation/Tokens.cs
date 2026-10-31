using System;
using System.Numerics;

namespace EchoGlam.UI.Foundation;

/// Spacing.
public static class Sp
{
    private static float K => UiHelpers.Scale;

    public static float Xxs => 2f * K;
    public static float Xs => 4f * K;
    public static float Sm => 6f * K;
    public static float Md => 8f * K;
    public static float Lg => 12f * K;
    public static float Xl => 16f * K;
    public static float Xxl => 24f * K;
    public static float Xxxl => 32f * K;

    /// A scaled pair, for padding and offsets.
    public static Vector2 XY(float x, float y) => new(x * K, y * K);

    /// A measured one-off that genuinely is not on the scale - a glyph's optical centre, a value read off a
    /// game sheet.
    public static float Px(float designPx) => designPx * K;
}

/// Corner radii.
public static class Rad
{
    private static float K => UiHelpers.Scale;

    /// Badges, pills, ticks, swatch interiors.
    public static float Chip => 3f * K;

    /// Buttons, inputs, frames.
    public static float Ctl => 6f * K;

    /// A surface nested inside another.
    public static float Panel => 9f * K;

    /// A top-level card.
    public static float Card => 12f * K;

    /// A capsule, from an ALREADY-SCALED height.
    public static float Pill(float scaledHeight) => scaledHeight / 2f;

    /// A dye chip, from an already-scaled size.
    public static float Swatch(float scaledSize) => scaledSize * 0.3f;
}

/// Line weights.
public static class Stroke
{
    private static float K => UiHelpers.Scale;

    /// Dividers and table rules.
    public static float Hair => 1f * K;

    /// A surface's border at rest.
    public static float Edge => 1.2f * K;

    /// Hover, selection and focus rings.
    public static float Focus => 1.8f * K;

    /// Emphasis - the wordmark's underline, a selection bar.
    public static float Mark => 2f * K;
}

/// Named lengths that are not spacing: the sizes of things.
public static class Metrics
{
    private static float K => UiHelpers.Scale;

    /// The navigation rail.
    public static float Rail => 64f * K;


    /// The strip carrying the wordmark and the window buttons.
    public static float Chrome => 34f * K;

    /// A control row.
    public static float Row => 28f * K;

    public static float RowTall => 34f * K;

    /// A text input.
    public static float Field => 30f * K;

    /// Narrowest a gear tile may be before the grid drops a column.
    public static float TileMin => 168f * K;

    /// Narrowest a gallery card may be.
    public static float CardMin => 224f * K;

    /// Narrowest a layout column may be before it is dropped or stacked.
    public static float ColumnMin => 220f * K;

    /// Smallest a thing you are expected to hit with a pointer may be.
    public static float TouchMin => 24f * K;
}

/// Animation constants.
public static class Anim
{
    /// The approach rate for a toggle knob or a segment fill.
    public const float SlideSpeed = 18f;

    /// Faster, for a selection underline that should feel attached to the click.
    public const float SelectSpeed = 22f;

    /// A crossfade.
    public const float FadeSeconds = 0.11f;

    /// How fast the window itself grows and shrinks.
    public const float ResizeSpeed = 16f;

    /// Clamps a frame delta before it drives anything.
    public static float Step(float deltaTime) => MathF.Min(deltaTime, 1f / 30f);
}
