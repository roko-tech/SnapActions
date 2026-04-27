using System.Runtime.InteropServices;
using System.Windows;

namespace SnapActions.Helpers;

public static class ScreenHelper
{
    public static Rect GetScreenBounds(Point cursorPos)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)cursorPos.X, (int)cursorPos.Y));
        var wa = screen.WorkingArea;
        return new Rect(wa.X, wa.Y, wa.Width, wa.Height);
    }

    /// <summary>
    /// Returns the DPI scale factor for the monitor under the given physical-pixel point.
    /// Returns (1.0, 1.0) if the OS can't tell — caller should treat that as system DPI.
    /// </summary>
    public static (double X, double Y) GetDpiForPoint(Point cursorPos)
    {
        try
        {
            var pt = new POINT { X = (int)cursorPos.X, Y = (int)cursorPos.Y };
            var hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (hMon != IntPtr.Zero &&
                GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0)
                return (dpiX / 96.0, dpiY / 96.0);
        }
        catch { }
        return (1.0, 1.0);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
