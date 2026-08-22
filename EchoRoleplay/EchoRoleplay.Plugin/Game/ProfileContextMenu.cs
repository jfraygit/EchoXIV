using System;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;

namespace EchoRoleplay.Game;

/// "Roleplay Profile" on the game's own right-click menu.
public sealed class ProfileContextMenu : IDisposable
{
    private readonly IContextMenu contextMenu;
    private readonly ProfileDirectory directory;
    private readonly Func<string> localCharacterKey;
    private readonly Action<string> open;

    public ProfileContextMenu(
        IContextMenu contextMenu, ProfileDirectory directory, Func<string> localCharacterKey,
        Action<string> open)
    {
        this.contextMenu = contextMenu;
        this.directory = directory;
        this.localCharacterKey = localCharacterKey;
        this.open = open;

        this.contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetDefault target)
            return;

        var key = KeyFor(target);
        if (key.Length == 0)
            return;

        var profile = directory.Lookup(key, localCharacterKey());
        if (profile is null || !profile.HasAnything)
            return;

        args.AddMenuItem(new MenuItem
        {
            Name = "Roleplay Profile",
            PrefixChar = 'R',

            PrefixColor = 541,

            Priority = 100,

            OnClicked = _ => open(key),
        });
    }

    /// "Name@HomeWorld" for whoever the menu is about.
    private static string KeyFor(MenuTargetDefault target)
    {
        var name = target.TargetName;
        var world = target.TargetHomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;

        return ProfileStore.CharacterKey(name, world);
    }
}
