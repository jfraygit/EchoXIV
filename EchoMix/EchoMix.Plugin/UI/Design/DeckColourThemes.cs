using System;
using System.Collections.Generic;
using System.Numerics;

namespace EchoMix.Plugin.UI.Design;

/// Ready-made Deck A / Deck B / Blend combinations for Settings > Appearance.
public static class DeckColourThemes
{
    public readonly record struct Theme(string Name, Vector3 DeckA, Vector3 DeckB, Vector3 Blend);

    private static Vector3 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    /// The shipped default, first so it reads as the baseline rather than as one option among many.
    public static readonly Theme Default = new("EchoMix Default",
        new Vector3(0.25f, 0.85f, 0.95f),
        new Vector3(1f, 0.6f, 0.15f),
        new Vector3(0.625f, 0.725f, 0.55f));

    /// Seasonal entries carry their occasion in the name.
    public static readonly IReadOnlyList<Theme> All = new[]
    {
        Default,

        new("Jack-o'-Lantern (Halloween)", Rgb(255, 138, 24), Rgb(150, 86, 220), Rgb(214, 108, 96)),
        new("Witching Hour (Halloween)", Rgb(176, 108, 255), Rgb(120, 230, 120), Rgb(150, 170, 200)),
        new("Candy Corn (Halloween)", Rgb(255, 214, 92), Rgb(255, 122, 36), Rgb(255, 168, 64)),
        new("Haunted Fog (Halloween)", Rgb(140, 255, 210), Rgb(170, 150, 220), Rgb(155, 200, 215)),
        new("Blood Moon (Halloween)", Rgb(255, 86, 86), Rgb(255, 170, 60), Rgb(255, 128, 72)),
        new("Black Cat (Halloween)", Rgb(255, 176, 48), Rgb(120, 130, 160), Rgb(190, 152, 104)),
        new("Poison Apple (Halloween)", Rgb(122, 230, 110), Rgb(220, 70, 130), Rgb(172, 150, 120)),

        new("Ice & Ember", Rgb(130, 210, 255), Rgb(255, 130, 90), Rgb(192, 170, 172)),
        new("Deep Sea", Rgb(80, 220, 220), Rgb(90, 140, 255), Rgb(85, 180, 238)),
        new("Aurora", Rgb(110, 240, 190), Rgb(150, 130, 255), Rgb(130, 185, 222)),
        new("Frostbite", Rgb(170, 230, 255), Rgb(90, 160, 240), Rgb(130, 195, 248)),
        new("Moonlight", Rgb(200, 215, 255), Rgb(140, 160, 210), Rgb(170, 188, 232)),

        new("Sunset Strip", Rgb(255, 170, 60), Rgb(255, 90, 140), Rgb(255, 130, 100)),
        new("Ember & Ash", Rgb(255, 140, 70), Rgb(190, 190, 200), Rgb(222, 165, 135)),
        new("Desert Dusk", Rgb(255, 196, 110), Rgb(210, 110, 160), Rgb(232, 153, 135)),
        new("Molten", Rgb(255, 210, 80), Rgb(255, 100, 50), Rgb(255, 155, 65)),

        new("Neon Nightclub", Rgb(255, 80, 200), Rgb(80, 240, 255), Rgb(168, 160, 228)),
        new("Synthwave", Rgb(255, 95, 162), Rgb(125, 120, 255), Rgb(190, 108, 208)),
        new("Laser Show", Rgb(120, 255, 120), Rgb(255, 90, 230), Rgb(188, 172, 175)),
        new("Arcade", Rgb(255, 230, 70), Rgb(90, 200, 255), Rgb(172, 215, 162)),
        new("Vaporwave", Rgb(255, 150, 220), Rgb(130, 230, 240), Rgb(192, 190, 230)),

        new("Forest Floor", Rgb(150, 220, 120), Rgb(215, 170, 90), Rgb(182, 195, 105)),
        new("Coral Reef", Rgb(255, 140, 120), Rgb(90, 220, 200), Rgb(172, 180, 160)),
        new("Wildflower", Rgb(230, 160, 255), Rgb(255, 210, 110), Rgb(242, 185, 182)),
        new("Storm Front", Rgb(150, 180, 220), Rgb(230, 220, 130), Rgb(190, 200, 175)),

        new("Monochrome", Rgb(225, 230, 240), Rgb(140, 150, 170), Rgb(182, 190, 205)),
        new("Slate", Rgb(150, 190, 215), Rgb(195, 160, 140), Rgb(172, 175, 178)),
        new("Sepia", Rgb(230, 200, 150), Rgb(180, 150, 115), Rgb(205, 175, 132)),
    };

    /// Display names, in list order.
    public static readonly IReadOnlyList<string> Names = BuildNames();

    private static string[] BuildNames()
    {
        var names = new string[All.Count];
        for (var i = 0; i < All.Count; i++)
            names[i] = All[i].Name;

        return names;
    }

    /// Index of the theme matching these three colours, or -1 for a hand-picked combination that isn't a
    /// preset.
    public static int IndexOf(Vector3 deckA, Vector3 deckB, Vector3 blend)
    {
        for (var i = 0; i < All.Count; i++)
        {
            var theme = All[i];
            if (Near(theme.DeckA, deckA) && Near(theme.DeckB, deckB) && Near(theme.Blend, blend))
                return i;
        }

        return -1;
    }

    /// Tolerant to half a step of 0-255 rounding, since these round-trip through a colour picker and a JSON
    /// float on the way back.
    private static bool Near(Vector3 a, Vector3 b)
        => MathF.Abs(a.X - b.X) < 0.004f
            && MathF.Abs(a.Y - b.Y) < 0.004f
            && MathF.Abs(a.Z - b.Z) < 0.004f;
}
