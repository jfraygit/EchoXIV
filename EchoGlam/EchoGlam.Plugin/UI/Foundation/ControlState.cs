using System.Numerics;

namespace EchoGlam.UI.Foundation;

/// What a control is currently doing, as one vocabulary instead of five.
public enum ControlState
{
    Rest,
    Hover,
    Active,
    Selected,

    /// Present, visible, and not usable.
    Disabled,

    /// Reserved.
    Focus,
}

public static class ControlStates
{
    /// Reads a state from the flags a hand-drawn control already has to hand.
    public static ControlState Of(bool hovered, bool held = false, bool selected = false, bool enabled = true)
    {
        if (!enabled)
            return ControlState.Disabled;

        if (held)
            return ControlState.Active;

        if (selected)
            return ControlState.Selected;

        return hovered ? ControlState.Hover : ControlState.Rest;
    }

    /// The body colour for a control in this state.
    public static Vector4 Fill(this ControlState state) => state switch
    {
        ControlState.Hover => Col.CtlHover,
        ControlState.Active => Col.CtlActive,
        ControlState.Selected => Col.SelRest,
        ControlState.Disabled => Col.Disabled(Col.CtlRest),
        _ => Col.CtlRest,
    };

    /// The border colour, or null for no border at all.
    public static Vector4? Edge(this ControlState state) => state switch
    {
        ControlState.Hover => Col.EdgeHover,
        ControlState.Active => Col.EdgeFocus,
        ControlState.Selected => Col.EdgeHover,
        ControlState.Focus => Col.EdgeFocus,
        ControlState.Disabled => null,
        _ => null,
    };

    public static float Stroke(this ControlState state) => state switch
    {
        ControlState.Active or ControlState.Focus => Foundation.Stroke.Focus,
        _ => Foundation.Stroke.Edge,
    };

    /// The text colour on a control in this state.
    public static Vector4 Label(this ControlState state) => state switch
    {
        ControlState.Disabled => Col.TextMuted,
        ControlState.Rest => Col.TextDim,
        _ => Col.Text,
    };

    public static bool Interactive(this ControlState state) => state != ControlState.Disabled;
}
