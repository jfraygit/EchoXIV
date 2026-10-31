using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EchoGlam.UI.Foundation;

/// How much of the window a dialog should take.
public enum ModalSize
{
    /// A confirmation, a short form.
    Small,

    /// A form with several fields, or a block of prose to read.
    Medium,

    /// Something with a picture in it.
    Large,
}

/// A dialog sized against the window rather than against a number somebody typed.
public static class Modal
{
    /// Fractions of the window's width, clamped so a dialog is neither a slit on a narrow window nor a
    /// billboard on a wide one.
    private static (float Fraction, float Min, float Max) Bounds(ModalSize size) => size switch
    {
        ModalSize.Small => (0.30f, 260f, 420f),
        ModalSize.Medium => (0.42f, 360f, 560f),
        _ => (0.58f, 440f, 760f),
    };

    /// The content width a dialog of this size gets, in real pixels.
    public static float Width(ModalSize size)
    {
        var (fraction, min, max) = Bounds(size);
        var scale = UiHelpers.Scale;

        return Math.Clamp(MainWindow.DesignSize.X * fraction, min, max) * scale;
    }

    /// The width text inside the dialog should wrap to, with the frame padding and a scrollbar's worth of
    /// room taken off.
    public static float WrapWidth(ModalSize size) =>
        Width(size) - (ImGui.GetStyle().FramePadding.X * 2f) - (18f * UiHelpers.Scale);


    /// The plugin window's rectangle this frame, recorded by MainWindow.
    private static Vector2 windowMin;

    private static Vector2 windowSize;

    /// Called once a frame from MainWindow.Draw, before anything that can open a dialog.
    internal static void Frame(Vector2 min, Vector2 size)
    {
        windowMin = min;
        windowSize = size;
    }

    /// Centres the next popup in the plugin window.
    public static void Centre()
    {
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
            return;

        ImGui.SetNextWindowPos(
            windowMin + (windowSize / 2f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
    }

    /// Opens the popup body and establishes its width.
    public static bool Begin(string id, ModalSize size, bool modal = false)
    {
        var open = modal
            ? ImGui.BeginPopupModal(id, ImGuiWindowFlags.AlwaysAutoResize)
            : ImGui.BeginPopup(id);

        if (!open)
            return false;

        ImGui.Dummy(new Vector2(Width(size), 0f));
        return true;
    }

    public static void End() => ImGui.EndPopup();
}
