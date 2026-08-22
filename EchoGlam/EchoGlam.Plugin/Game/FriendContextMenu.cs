using System;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using EchoGlam.Shared;

namespace EchoGlam.Game;

/// "Add EchoGlam Friend" on the game's own right-click menu.
public sealed class FriendContextMenu : IDisposable
{
    private readonly IContextMenu contextMenu;
    private readonly Plugin plugin;

    public FriendContextMenu(IContextMenu contextMenu, Plugin plugin)
    {
        this.contextMenu = contextMenu;
        this.plugin = plugin;

        this.contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetDefault target)
            return;

        if (!plugin.Friendships.SharingEnabled)
            return;

        var key = KeyFor(target);

        if (key.Length == 0)
            return;

        var (name, world) = GalleryState.Character;

        if (string.Equals(key, CharacterHash.Key(name, world), StringComparison.OrdinalIgnoreCase))
            return;

        if (plugin.Friendships.KnownTo(key))
            return;

        args.AddMenuItem(new MenuItem
        {
            Name = "Add EchoGlam Friend",
            PrefixChar = 'G',

            PrefixColor = 541,

            Priority = 100,

            OnClicked = _ => plugin.Friendships.Ask(key),
        });
    }

    /// "Name@HomeWorld" for whoever the menu is about.
    private static string KeyFor(MenuTargetDefault target)
    {
        var name = target.TargetName;
        var world = target.TargetHomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;

        return CharacterHash.Key(name, world);
    }
}
