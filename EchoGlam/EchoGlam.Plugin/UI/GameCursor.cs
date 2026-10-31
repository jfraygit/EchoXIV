using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Events;
using Dalamud.Plugin.Services;

namespace EchoGlam.UI;

/// Draws the GAME's pointer over EchoGlam rather than the operating system's.
internal sealed class GameCursor
{
    private readonly IAddonEventManager events;

    /// Whether the override is currently handed over, so the restore happens once rather than every frame the
    /// window is shut.
    private bool holding;

    public GameCursor(IAddonEventManager events) => this.events = events;

    /// Called at the very end of the draw, after every window has had its say.
    public void Update(bool active)
    {
        if (!active)
        {
            Release();
            return;
        }

        if (!holding)
        {
            holding = true;
            Plugin.PluginInterface.UiBuilder.OverrideGameCursor = false;
        }

        if (!ImGui.GetIO().WantCaptureMouse)
        {
            events.ResetCursor();
            return;
        }

        events.SetCursor(Translate(ImGui.GetMouseCursor()));
    }

    /// Hands the pointer back.
    public void Release()
    {
        if (!holding)
            return;

        holding = false;
        events.ResetCursor();
        Plugin.PluginInterface.UiBuilder.OverrideGameCursor = true;
    }

    /// The nearest thing the game has to the shape ImGui asked for.
    private static AddonCursorType Translate(ImGuiMouseCursor cursor) => cursor switch
    {
        ImGuiMouseCursor.Hand => AddonCursorType.Clickable,
        ImGuiMouseCursor.TextInput => AddonCursorType.TextInput,
        ImGuiMouseCursor.ResizeEw => AddonCursorType.ResizeWE,
        ImGuiMouseCursor.ResizeNs => AddonCursorType.ResizeNS,
        ImGuiMouseCursor.ResizeNesw => AddonCursorType.ResizeNESW,

        ImGuiMouseCursor.ResizeNwse => AddonCursorType.ResizeNWSR,

        _ => AddonCursorType.Arrow,
    };
}
