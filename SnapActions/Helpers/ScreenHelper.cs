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
}
