using System;
using System.Collections.Generic;
using System.Numerics;

namespace EchoGlam.UI.Design;

/// Ready-made accents for Settings, carrying the suite's theme names.
public static class ColourThemes
{
    public readonly record struct Theme(string Name, string Group, Vector4 Accent);

    private static Vector4 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    /// EchoGlam's own, first so it reads as the baseline rather than as one option among many.
    public static readonly Theme Default =
        new("EchoGlam Orchid", "Signature", UI.Theme.DefaultAccent);

    public static readonly IReadOnlyList<Theme> All = new[]
    {
        Default,

        new Theme("EchoMix Default", "Signature", Rgb(64, 217, 242)),

        new Theme("Jack-o'-Lantern", "Halloween", Rgb(255, 138, 24)),
        new Theme("Witching Hour", "Halloween", Rgb(176, 108, 255)),
        new Theme("Candy Corn", "Halloween", Rgb(255, 214, 92)),
        new Theme("Haunted Fog", "Halloween", Rgb(140, 255, 210)),

        new Theme("Blood Moon", "Halloween", Rgb(228, 42, 78)),

        new Theme("Black Cat", "Halloween", Rgb(150, 162, 196)),
        new Theme("Poison Apple", "Halloween", Rgb(122, 230, 110)),

        new Theme("Ice & Ember", "Cool", Rgb(130, 210, 255)),
        new Theme("Deep Sea", "Cool", Rgb(80, 220, 220)),
        new Theme("Aurora", "Cool", Rgb(150, 130, 255)),
        new Theme("Frostbite", "Cool", Rgb(170, 230, 255)),
        new Theme("Moonlight", "Cool", Rgb(200, 215, 255)),

        new Theme("Sunset Strip", "Warm", Rgb(255, 90, 140)),
        new Theme("Ember & Ash", "Warm", Rgb(255, 140, 70)),
        new Theme("Desert Dusk", "Warm", Rgb(210, 110, 160)),

        new Theme("Molten", "Warm", Rgb(255, 100, 50)),

        new Theme("Neon Nightclub", "Neon", Rgb(255, 80, 200)),
        new Theme("Synthwave", "Neon", Rgb(255, 95, 162)),
        new Theme("Laser Show", "Neon", Rgb(120, 255, 120)),

        new Theme("Arcade", "Neon", Rgb(90, 200, 255)),
        new Theme("Vaporwave", "Neon", Rgb(255, 150, 220)),

        new Theme("Forest Floor", "Natural", Rgb(150, 220, 120)),
        new Theme("Coral Reef", "Natural", Rgb(255, 140, 120)),
        new Theme("Wildflower", "Natural", Rgb(230, 160, 255)),
        new Theme("Storm Front", "Natural", Rgb(150, 180, 220)),

        new Theme("Monochrome", "Restrained", Rgb(225, 230, 240)),
        new Theme("Slate", "Restrained", Rgb(150, 190, 215)),
        new Theme("Sepia", "Restrained", Rgb(230, 200, 150)),
    };

    /// The groups, in list order, with the themes in each.
    public static readonly IReadOnlyList<(string Group, IReadOnlyList<Theme> Themes)> Grouped = BuildGroups();

    private static (string, IReadOnlyList<Theme>)[] BuildGroups()
    {
        var groups = new List<(string Group, List<Theme> Themes)>();

        foreach (var theme in All)
        {
            var index = groups.FindIndex(g => g.Group == theme.Group);

            if (index < 0)
            {
                groups.Add((theme.Group, [theme]));
                continue;
            }

            groups[index].Themes.Add(theme);
        }

        var built = new (string, IReadOnlyList<Theme>)[groups.Count];

        for (var i = 0; i < groups.Count; i++)
            built[i] = (groups[i].Group, groups[i].Themes);

        return built;
    }

    /// The theme matching this accent, or null for one picked off the colour wheel.
    public static Theme? Match(Vector4 accent)
    {
        foreach (var theme in All)
        {
            if (Near(theme.Accent, accent))
                return theme;
        }

        return null;
    }

    /// Tolerant to half a step of 0-255 rounding, since these round-trip through a colour picker and a JSON
    /// float on the way back.
    private static bool Near(Vector4 a, Vector4 b) =>
        MathF.Abs(a.X - b.X) < 0.004f
        && MathF.Abs(a.Y - b.Y) < 0.004f
        && MathF.Abs(a.Z - b.Z) < 0.004f;
}
