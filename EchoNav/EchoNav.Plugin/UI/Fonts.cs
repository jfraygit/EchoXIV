using System;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;

namespace EchoNav.UI;

/// Header and icon font handles from Dalamud's managed font atlas.
public sealed class Fonts
{
    public IFontHandle Header { get; }
    public IFontHandle Icon { get; }

    public Fonts(IDalamudPluginInterface pluginInterface)
    {
        var atlas = pluginInterface.UiBuilder.FontAtlas;
        Header = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(21)));
        Icon = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new() { SizePt = 17f })));
    }
}

public static class FontHandleExtensions
{
    public static IDisposable? PushSafe(this IFontHandle handle) => handle.Available ? handle.Push() : null;
}
