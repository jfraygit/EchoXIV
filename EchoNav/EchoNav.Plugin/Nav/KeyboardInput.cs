using System;
using System.Runtime.InteropServices;

namespace EchoNav.Nav;

/// Synthesises the same keyboard input a player would produce, via Win32 SendInput.
public static class KeyboardInput
{
    /// The four movement keys, and deliberately nothing else.
    public const ushort ScanW = 0x11;
    public const ushort ScanA = 0x1E;
    public const ushort ScanS = 0x1F;
    public const ushort ScanD = 0x20;

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputUnion
    {
        public uint Type;
        public KeyboardInputData Keyboard;
        private readonly int padding1;
        private readonly int padding2;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] InputUnion[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// Whether synthesised input would actually reach the game.
    public static bool IsGameFocused
    {
        get
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(foreground, out var processId);
            return processId == (uint)Environment.ProcessId;
        }
    }

    public static void Press(ushort scanCode) => Send(scanCode, keyUp: false);

    public static void Release(ushort scanCode) => Send(scanCode, keyUp: true);

    private static void Send(ushort scanCode, bool keyUp)
    {
        var input = new InputUnion
        {
            Type = InputKeyboard,
            Keyboard = new KeyboardInputData
            {
                VirtualKey = 0,
                ScanCode = scanCode,
                Flags = KeyEventScanCode | (keyUp ? KeyEventKeyUp : 0),
                Time = 0,
                ExtraInfo = IntPtr.Zero,
            },
        };

        SendInput(1, [input], Marshal.SizeOf<InputUnion>());
    }
}
