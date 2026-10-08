using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI.Design;

/// Typographic roles for the 2.0 look, mapped onto the font handles in Fonts.cs.
public static class TypeScale
{
    /// Set once by the Plugin constructor, same lifetime as Fonts itself.
    private static Fonts? fonts;

    public static void Initialize(Fonts value) => fonts = value;


    /// Largest.
    public static IDisposable? Display() => fonts?.HeaderLarge.PushSafe();

    /// Screen and destination titles.
    public static IDisposable? Title() => fonts?.Header.PushSafe();

    /// Section headings inside a screen, and panel labels.
    public static IDisposable? Heading() => fonts?.Header.PushSafe();

    /// Default running text, control labels, list rows.
    public static IDisposable? Body() => null;

    /// Secondary metadata and helper text.
    public static IDisposable? Caption() => null;

    /// FontAwesome at body scale.
    public static IDisposable? Icon() => fonts?.Icon.PushSafe();

    /// FontAwesome at display scale, for empty-state and onboarding glyphs.
    public static IDisposable? IconLarge() => fonts?.IconLarge.PushSafe();

    /// Text with a role and a colour in one call, which is the overwhelmingly common case.
    public static void Text(Func<IDisposable?> role, Vector4 color, string text)
    {
        using (role())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, color);
            ImGui.TextUnformatted(text);
        }
    }

    /// Measures under a role without drawing - needed whenever layout has to be computed before the text is
    /// emitted, which in this codebase is most of the time.
    public static Vector2 Measure(Func<IDisposable?> role, string text)
    {
        using (role())
            return ImGui.CalcTextSize(text);
    }
}
