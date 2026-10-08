using System.Numerics;

namespace EchoMix.Plugin.UI.Design;

/// Named colour roles for the 2.0 look: text ranks, status colours and brand colours.
public static class Semantic
{
    public static Vector4 DeckA => Theme.CyanAccent;
    public static Vector4 DeckB => Theme.OrangeAccent;
    public static Vector4 Primary => Theme.NeutralAccent;
    public static Vector4 PrimaryHover => Theme.NeutralAccentHover;
    public static Vector4 PrimaryActive => Theme.NeutralAccentActive;

    /// Accents that must stay put regardless of what a given user picked for their own decks - another DJ's
    /// listing should look the same to everyone.
    public static Vector4 FixedA => Theme.FixedCyan;
    public static Vector4 FixedB => Theme.FixedOrange;

    public static readonly Vector4 TextPrimary = new(0.95f, 0.95f, 0.97f, 1f);
    public static readonly Vector4 TextSecondary = new(0.95f, 0.95f, 0.97f, 0.66f);
    public static readonly Vector4 TextTertiary = new(0.95f, 0.95f, 0.97f, 0.42f);
    public static readonly Vector4 TextDisabled = new(0.95f, 0.95f, 0.97f, 0.26f);

    /// Text sitting on top of a lit accent fill.
    public static readonly Vector4 TextOnAccent = new(0.04f, 0.04f, 0.05f, 1f);

    public static readonly Vector4 Live = new(0.35f, 0.85f, 0.4f, 1f);
    public static readonly Vector4 Success = new(0.35f, 0.85f, 0.4f, 1f);
    public static readonly Vector4 Warning = new(1f, 0.68f, 0.2f, 1f);
    public static readonly Vector4 Danger = new(0.95f, 0.32f, 0.32f, 1f);

    public static readonly Vector4 Discord = new(0.345f, 0.396f, 0.949f, 1f);
    public static readonly Vector4 Spotify = new(0.118f, 0.843f, 0.376f, 1f);

    public static readonly Vector4 MeterOk = new(0.2f, 0.85f, 0.3f, 1f);
    public static readonly Vector4 MeterWarn = new(0.95f, 0.85f, 0.15f, 1f);
    public static readonly Vector4 MeterClip = new(0.95f, 0.2f, 0.2f, 1f);

    /// Same colour at a different alpha.
    public static Vector4 Alpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

    /// Blends toward white, for hover lifts.
    public static Vector4 Lift(Vector4 color, float amount = 0.1f)
        => Vector4.Lerp(color, Vector4.One, amount) with { W = color.W };

    /// Blends toward black, for pressed states.
    public static Vector4 Sink(Vector4 color, float amount = 0.18f)
        => new(color.X * (1f - amount), color.Y * (1f - amount), color.Z * (1f - amount), color.W);
}
