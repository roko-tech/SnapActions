using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace SnapActions.Core;

public static class ForegroundApp
{
    public static string? GetActiveProcessName()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch { return null; }
    }

    public static bool IsExcluded(IReadOnlyList<string> exclusionList)
    {
        var name = GetActiveProcessName();
        if (name == null) return false;
        if (name.Equals("SnapActions", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var ex in exclusionList)
            if (name.Equals(ex, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Detects if the user is in an editable text field.
    /// Uses Win32 caret check (fast, native controls) + UI Automation (browsers, WPF).
    /// </summary>
    public static bool IsEditableFieldFocused()
    {
        // Fast path: Win32 caret (Notepad, native Edit controls)
        if (HasWin32Caret()) return true;

        // UI Automation (browser inputs, Electron, ProseMirror, CodeMirror, etc.)
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;

            // Edit controls are always editable
            if (focused.Current.ControlType == ControlType.Edit)
                return true;

            // ValuePattern with IsReadOnly=false → editable
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                if (!((ValuePattern)vp).Current.IsReadOnly) return true;

            // TextPattern support → editable text area
            // Regular webpage text does NOT support TextPattern.
            // Only actual editors do (ProseMirror, CodeMirror, contenteditable, etc.)
            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out _))
                return true;
        }
        catch { }

        return false;
    }

    private static bool HasWin32Caret()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        uint threadId = GetWindowThreadProcessId(hwnd, out _);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(threadId, ref info)) return false;

        // Caret is blinking (standard Win32 text controls)
        if ((info.flags & 0x01) != 0) return true;

        // Caret window exists (some apps set this without the blinking flag)
        if (info.hwndCaret != IntPtr.Zero) return true;

        // Caret rect has dimensions (another signal of an active text cursor)
        if (info.rcCaret.right > info.rcCaret.left && info.rcCaret.bottom > info.rcCaret.top)
            return true;

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
}
