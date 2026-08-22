using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EchoGlam.Game;

/// Grabs the game window as a bitmap.
public static class WindowCapture
{
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// Render the window's full content, including layered and DirectX surfaces where the driver allows it.
    private const uint RenderFullContent = 0x00000002;

    /// The game's own window.
    public static nint GameWindow => Process.GetCurrentProcess().MainWindowHandle;

    /// Captures the game window to a PNG, or null with a sentence saying why.
    public static byte[]? Capture(out string? problem)
    {
        problem = null;

        var hwnd = GameWindow;

        if (hwnd == nint.Zero)
        {
            problem = "Couldn't find the game window.";
            return null;
        }

        if (IsIconic(hwnd))
        {
            problem = "The game window is minimised.";
            return null;
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            problem = "Couldn't measure the game window.";
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
        {
            problem = "The game window has no size.";
            return null;
        }

        try
        {
            using var bitmap = Render(hwnd, rect, width, height);
            using var buffer = new System.IO.MemoryStream();

            bitmap.Save(buffer, ImageFormat.Png);
            return buffer.ToArray();
        }
        catch (Exception ex)
        {
            problem = $"Couldn't capture the window: {ex.Message}";
            return null;
        }
    }

    private static Bitmap Render(nint hwnd, Rect rect, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();

            try
            {
                PrintWindow(hwnd, hdc, RenderFullContent);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        if (!IsBlank(bitmap))
            return bitmap;

        bitmap.Dispose();

        var fromScreen = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(fromScreen))
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

        return fromScreen;
    }

    /// Whether a capture came back essentially empty.
    private static bool IsBlank(Bitmap bitmap)
    {
        const int steps = 16;

        for (var x = 0; x < steps; x++)
        {
            for (var y = 0; y < steps; y++)
            {
                var pixel = bitmap.GetPixel(
                    Math.Min(bitmap.Width - 1, x * bitmap.Width / steps),
                    Math.Min(bitmap.Height - 1, y * bitmap.Height / steps));

                if (pixel.R > 8 || pixel.G > 8 || pixel.B > 8)
                    return false;
            }
        }

        return true;
    }
}
