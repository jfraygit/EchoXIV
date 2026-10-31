using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EchoGlam.UI.Foundation;

/// What a message means.
public enum Tone
{
    Neutral,
    Info,
    Success,
    Caution,
    Danger,
}

/// A transient message and what it means.
public readonly record struct Message(string Text, Tone Tone)
{
    public static readonly Message None = new(string.Empty, Tone.Neutral);

    public bool Any => Text.Length > 0;

    public static Message Ok(string text) => new(text, Tone.Success);

    public static Message Failed(string text) => new(text, Tone.Danger);

    public static Message Warn(string text) => new(text, Tone.Caution);

    public Vector4 Colour => Tone switch
    {
        Tone.Success => Col.Success,
        Tone.Danger => Col.Danger,
        Tone.Caution => Col.Caution,
        Tone.Info => Col.Info,
        _ => Col.TextDim,
    };

    /// Draws the message wrapped to a width, or nothing if there is none.
    public void Draw(float wrapWidth)
    {
        if (!Any)
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, Colour);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapWidth);
        ImGui.TextUnformatted(Text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
    }
}

/// The three things a pane says when it has no content to show: nothing here, working on it, or it went
/// wrong.
public static class Notice
{
    /// Nothing here, and what to do about it.
    public static bool Empty(
        float width, FontAwesomeIcon icon, string heading, string body, string? action = null)
    {
        return Draw(width, icon, heading, body, action, Theme.AccentSoft);
    }

    /// Working on it.
    public static void Busy(float width, string heading, string body)
    {
        Draw(width, FontAwesomeIcon.Spinner, heading, body, null, Theme.AccentSoft);
    }

    /// It went wrong, and whether it is worth trying again.
    public static bool Fail(float width, string heading, string body, string? retry = null)
    {
        return Draw(width, FontAwesomeIcon.PlugCircleExclamation, heading, body, retry, Col.Danger);
    }

    /// Returns true on the frame the action button was pressed.
    private static bool Draw(
        float width, FontAwesomeIcon icon, string heading, string body, string? action, Vector4 iconColour)
    {
        var scale = UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();

        var available = ImGui.GetContentRegionAvail().Y;
        ImGui.Dummy(new Vector2(0f, MathF.Max(Sp.Xl, (available * 0.26f) - (40f * scale))));

        var glyphSize = Text.BodySize * 2.1f;
        var centreX = ImGui.GetCursorPosX() + (width / 2f);
        var glyphCentre = ImGui.GetCursorScreenPos() + new Vector2(width / 2f, glyphSize / 2f);

        UiHelpers.DrawScaledIcon(drawList, icon, glyphCentre, Col.U32(iconColour));
        ImGui.Dummy(new Vector2(0f, glyphSize + Sp.Lg));

        Centred(heading, Col.Text, width, centreX);
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        var natural = MathF.Min(Text.Width(body), 420f * scale);
        var bodyWidth = MathF.Min(MathF.Max(natural, 1f), MathF.Max(width - (40f * scale), 1f));

        ImGui.SetCursorPosX(centreX - (bodyWidth / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, Col.TextDim);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + bodyWidth);
        ImGui.TextUnformatted(body);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        if (action is null)
            return false;

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        var size = EchoGlam.UI.Controls.EchoButton.ContentSize(action);
        var buttonWidth = size.X + Sp.Xl;

        ImGui.SetCursorPosX(centreX - (buttonWidth / 2f));

        return EchoGlam.UI.Controls.EchoButton.Draw(
            $"##notice{action}", action, new Vector2(buttonWidth, Metrics.Row), primary: true);
    }

    private static void Centred(string text, Vector4 colour, float width, float centreX)
    {
        ImGui.SetCursorPosX(centreX - (Text.Width(text) / 2f));
        ImGui.TextColored(colour, text);
    }
}
