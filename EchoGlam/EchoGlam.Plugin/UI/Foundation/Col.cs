using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// Colour by role rather than by recipe.
public static class Col
{

    /// The window behind everything.
    public static Vector4 Window => Theme.Background;

    /// A surface at the base level.
    public static Vector4 Surface => Theme.Panel;

    /// A surface sitting on another one, a step closer to the light.
    public static Vector4 Raised => Lift(Theme.Panel, 0.035f);

    /// A well: an image placeholder, an icon recess, a slider track's bed.
    public static Vector4 Sunken => new(0.014f, 0.012f, 0.020f, 1f);

    /// A surface one step further up again.
    public static Vector4 RaisedHigh => Lift(Theme.Panel, 0.07f);

    /// Mixes toward white by a fraction, keeping the alpha.
    private static Vector4 Lift(Vector4 colour, float amount) =>
        Vector4.Lerp(colour, Vector4.One, amount) with { W = colour.W };

    /// The neutral grey behind a toggle or a segmented control.
    public static Vector4 Track => new(0.24f, 0.24f, 0.29f, 1f);

    /// The moving part of a control: a toggle's knob.
    public static Vector4 Knob => new(0.96f, 0.96f, 0.98f, 1f);


    public static Vector4 CtlRest => Theme.Tinted(0.10f);
    public static Vector4 CtlHover => Theme.Tinted(0.20f);
    public static Vector4 CtlActive => Theme.Tinted(0.32f);

    /// A selected row, tab or header.
    public static Vector4 SelRest => Theme.Tinted(0.18f);
    public static Vector4 SelHover => Theme.Tinted(0.26f);
    public static Vector4 SelActive => Theme.Tinted(0.34f);

    /// An input's interior.
    public static Vector4 FieldRest => Theme.Tinted(0.05f);
    public static Vector4 FieldHover => Theme.Tinted(0.14f);
    public static Vector4 FieldActive => Theme.Tinted(0.22f);


    public static Vector4 EdgeRest => Theme.Border;
    public static Vector4 EdgeHover => Theme.Accent with { W = 0.55f };
    public static Vector4 EdgeFocus => Theme.Accent;


    public static Vector4 Text => Theme.Text;

    /// Labels, units, secondary detail.
    public static Vector4 TextDim => Theme.TextDim;

    /// Present but not currently meaningful.
    public static Vector4 TextMuted => Theme.TextDisabled;

    /// On a surface that has been filled with the accent.
    public static Vector4 TextOnAccent => Theme.Background;


    public static Vector4 Info => Theme.Accent;
    public static Vector4 Success => Theme.Good;
    public static Vector4 Caution => Theme.Warning;
    public static Vector4 Danger => Theme.Bad;

    /// The vote mark.
    public static Vector4 Vote => Theme.Vote;

    /// The favourite mark.
    public static Vector4 Favourite => Theme.Favourite;


    /// Anything that is present but cannot be used.
    public static Vector4 Disabled(Vector4 colour) => colour with { W = colour.W * 0.38f };

    public static Vector4 Alpha(Vector4 colour, float alpha) => colour with { W = alpha };

    /// THE ONLY WAY A COLOUR REACHES A DRAW LIST, and it must stay that way.
    public static uint U32(Vector4 colour) => ImGui.GetColorU32(colour);
}
