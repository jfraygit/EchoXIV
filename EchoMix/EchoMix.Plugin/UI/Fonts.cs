using System;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;

namespace EchoMix.Plugin.UI;

/// Header and icon font handles built via Dalamud's managed font atlas.
public sealed class Fonts
{
    public IFontHandle Header { get; }
    public IFontHandle HeaderLarge { get; }
    public IFontHandle Icon { get; }
    public IFontHandle IconLarge { get; }

    public Fonts(IDalamudPluginInterface pluginInterface)
    {
        var atlas = pluginInterface.UiBuilder.FontAtlas;
        Header = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(21)));
        HeaderLarge = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(34)));
        Icon = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new() { SizePt = 17f })));
        IconLarge = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new() { SizePt = 56f })));
    }
}

public static class FontHandleExtensions
{
    public static IDisposable? PushSafe(this IFontHandle handle) => handle.Available ? handle.Push() : null;
}
