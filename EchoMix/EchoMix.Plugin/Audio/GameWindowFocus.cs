using System;
using System.Runtime.InteropServices;

namespace EchoMix.Plugin.Audio;

/// Dalamud doesn't expose OS-level window focus directly, so this checks it the plain Win32 way: is the
/// foreground window owned by the current process.
public static class GameWindowFocus
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static bool IsGameFocused
    {
        get
        {
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId);
            return foregroundProcessId == (uint)Environment.ProcessId;
        }
    }
}
