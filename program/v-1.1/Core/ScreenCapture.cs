using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AutoTyper.Core;

public static class ScreenCapture
{
    public static Bitmap CaptureScreen()
    {
        var screen = Screen.PrimaryScreen;
        var bitmap = new Bitmap(screen!.Bounds.Width, screen.Bounds.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(screen.Bounds.Location, Point.Empty, screen.Bounds.Size);
        }
        return bitmap;
    }

    public static Bitmap? CaptureWindow(IntPtr hwnd)
    {
        try
        {
            Rect rect;
            GetWindowRect(hwnd, out rect);
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return null;
            var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
            }
            return bitmap;
        }
        catch { return null; }
    }

    public static Bitmap CaptureRectangle(Rectangle rect)
    {
        var bitmap = new Bitmap(rect.Width, rect.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
        }
        return bitmap;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
