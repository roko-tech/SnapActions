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
    /// Permissive check for selection toolbar (show transform buttons).
    /// Allows false positives - transforms just copy to clipboard harmlessly.
    /// </summary>
    public static bool IsEditableFieldFocused()
    {
        if (HasWin32Caret()) return true;
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;

            if (focused.Current.ControlType == ControlType.Edit) return true;
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                if (!((ValuePattern)vp).Current.IsReadOnly) return true;
            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out _)) return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Strict check for paste mode. Only returns true when we're confident
    /// the user is in a real text input (Win32 caret or ControlType.Edit).
    /// </summary>
    public static bool IsTextInputFocused()
    {
        if (HasWin32Caret()) return true;
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;

            // Only trust ControlType.Edit (browser <input>, <textarea>, Win32 edit boxes)
            if (focused.Current.ControlType == ControlType.Edit) return true;
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
