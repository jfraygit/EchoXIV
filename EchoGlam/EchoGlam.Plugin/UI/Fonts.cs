using System;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI;

/// The plugin's font handles.
public sealed class Fonts : IDisposable
{
    /// The one big thing on a screen - a gallery detail's title, the intro wordmark.
    public IFontHandle Display { get; }

    /// Pane titles, card headings, the wordmark.
    public IFontHandle Header { get; }

    /// Metadata, stat labels, pill text, tracked small capitals.
    public IFontHandle Caption { get; }

    /// FontAwesome.
    public IFontHandle Icon { get; }

    public Fonts(IDalamudPluginInterface pluginInterface)
    {
        var atlas = pluginInterface.UiBuilder.FontAtlas;

        Display = atlas.NewDelegateFontHandle(
            e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(-Text.DisplayRatio)));

        Header = atlas.NewDelegateFontHandle(
            e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(-Text.TitleRatio)));

        Caption = atlas.NewDelegateFontHandle(
            e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(-Text.CaptionRatio)));

        Icon = atlas.NewDelegateFontHandle(
            e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new() { SizePt = 17f })));
    }

    /// IFontHandle is disposable and this class was not, so both handles leaked on every plugin disable - and
    /// a dev build is disabled and re-enabled dozens of times an evening.
    public void Dispose()
    {
        Display.Dispose();
        Header.Dispose();
        Caption.Dispose();
        Icon.Dispose();
    }
}

public static class FontHandleExtensions
{
    public static IDisposable? PushSafe(this IFontHandle handle) => handle.Available ? handle.Push() : null;
}
