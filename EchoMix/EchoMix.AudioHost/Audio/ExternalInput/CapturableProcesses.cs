using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace EchoMix.AudioHost.Audio.ExternalInput;

/// Lists running processes with a visible top-level window, for External Input Mode's "capture an
/// application" picker - the same "pick a window" workflow OBS's own Window Capture audio source uses (a DJ
/// report described routing VirtualDJ that exact way: VirtualDJ -> OBS Window Capture -> Twitch), so this
/// matches what a DJ switching to it already expects to see.
public static class CapturableProcesses
{
    public sealed record Entry(string ProcessName, string DisplayName);

    public static List<Entry> List()
    {
        var result = new List<Entry>();
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    var title = process.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    if (!seen.Add(process.ProcessName))
                        continue;

                    result.Add(new Entry(process.ProcessName, $"{title} ({process.ProcessName}.exe)"));
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
        }

        return result.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
