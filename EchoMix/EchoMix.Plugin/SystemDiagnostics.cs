using System;
using System.Collections.Generic;

namespace EchoMix.Plugin;

/// Captures a small amount of Dalamud/plugin state for a bug report - just enough to tell apart a
/// compatibility issue from a real bug, without collecting hardware details that aren't tied to a
/// demonstrated need.
public static class SystemDiagnostics
{
    public static string Capture(float uiScale)
    {
        var lines = new List<string>();

        lines.Add($"EchoMix UI Scale: {uiScale:0.00}x");

        try
        {
            var pi = Plugin.PluginInterface;
            lines.Add($"Dalamud version: {typeof(Dalamud.Plugin.IDalamudPluginInterface).Assembly.GetName().Version}");
            lines.Add($"Dalamud default font size: {pi.UiBuilder.FontDefaultSizePx:0.#}px");
            lines.Add($"Loaded as dev plugin: {pi.IsDev}");
            lines.Add($"Dalamud UI language: {pi.UiLanguage}");
        }
        catch (Exception ex)
        {
            lines.Add($"(couldn't read Dalamud info: {ex.Message})");
        }

        return string.Join("\n", lines);
    }
}
