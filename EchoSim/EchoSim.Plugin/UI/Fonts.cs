using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;

namespace EchoSim.UI;

/// Header and icon font handles from Dalamud's managed font atlas.
public sealed class Fonts
{
    public IFontHandle Header { get; }
    public IFontHandle Numeric { get; }
    public IFontHandle Display { get; }
    public IFontHandle Icon { get; }

    /// The size Display is baked at.
    public const float DisplayBakedSize = 52f;

    public Fonts(IDalamudPluginInterface pluginInterface)
    {
        var atlas = pluginInterface.UiBuilder.FontAtlas;
        Header = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(21)));
        Numeric = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(30)));
        Display = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(DisplayBakedSize)));
        Icon = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new() { SizePt = 17f })));
    }
}

public static class FontHandleExtensions
{
    public static IDisposable? PushSafe(this IFontHandle handle) => handle.Available ? handle.Push() : null;
}
