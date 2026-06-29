using System.Runtime.InteropServices;

namespace SnapActions.Core;

/// <summary>
/// Detects whether the mouse cursor is currently the system text (I-beam) cursor — the OS's own
/// signal for "the pointer is over selectable text". Used to gate the selection toolbar so it
/// doesn't appear after non-text gestures (dragging a file / window, clicking a button, panning a
/// canvas). This is the gate that keeps the toolbar from appearing in the same false-positive
/// spots, and it's a more universal signal than UIA TextPattern (which has uneven app coverage).
/// </summary>
public static class CursorShape
{
    private const int IDC_IBEAM = 32513;
    private const int CURSOR_SHOWING = 0x0001;

    // The shared system I-beam handle. LoadCursor(NULL, IDC_IBEAM) returns the same shared handle
    // GetCursorInfo reports when any app sets the standard I-beam, so a plain handle comparison
    // covers the common case. Cached once — system cursor handles are stable for the session.
    private static readonly IntPtr SystemIBeam = LoadCursor(IntPtr.Zero, IDC_IBEAM);

    /// <summary>
    /// True unless we can positively confirm the cursor is a non-text shape. Deliberately permissive
    /// on uncertainty: returns true when the cursor can't be read (API failure) or isn't showing
    /// (touch input, full-screen video) so a real selection is never suppressed just because the
    /// cursor state is unavailable. Returns false only when the cursor is visibly some shape other
    /// than the system I-beam — which is exactly the drag-a-file / click-a-button / pan-a-canvas
    /// case the selection toolbar should ignore.
    /// </summary>
    public static bool IsTextCursor()
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci)) return true;            // API failure — don't suppress
        if ((ci.flags & CURSOR_SHOWING) == 0) return true;  // cursor hidden (touch / full-screen)
        return ci.hCursor == SystemIBeam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public int ptScreenPosX;
        public int ptScreenPosY;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
}
