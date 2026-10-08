using Dalamud.Bindings.ImGui;

namespace EchoMix.Plugin.UI;

/// Hands out non-overlapping slots to the toasts that are open this frame.
internal static class ToastStack
{
    private static int lastFrame = -1;
    private static float cursor;

    /// Returns this toast's offset from the anchor, in scaled pixels along the stack direction, then reserves
    /// `height` plus `gap` for it.
    public static float Claim(float height, float gap)
    {
        var frame = ImGui.GetFrameCount();
        if (frame != lastFrame)
        {
            lastFrame = frame;
            cursor = 0f;
        }

        var offset = cursor;
        cursor += height + gap;
        return offset;
    }
}
