using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace SnapActions.Core;

public static class ForegroundApp
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static string? GetActiveProcessName()
    {
        // Avoid Process.GetProcessById here — it allocates a Process object and reads the full
        // module path through a slower path. We do this on every selection; faster matters.
        IntPtr handle = IntPtr.Zero;
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;

            handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) return null;

            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
                return null;

            return Path.GetFileNameWithoutExtension(buffer.ToString(0, size));
        }
        catch { return null; }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

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
    /// Strict gate for the Ctrl+Insert capture-fallback: only true when we're confident the
    /// foreground has *selectable text*. Caret check first (cheap, covers native Win32 edits);
    /// then the focused element's ControlType.Edit or TextPattern. Deliberately does NOT accept
    /// non-readonly ValuePattern (sliders, spinners, ComboBoxes) — those aren't text and
    /// sending Ctrl+Insert into them is at best a no-op and at worst conflicts with app hotkeys.
    /// </summary>
    public static bool IsTextSelectionCapable()
    {
        if (HasWin32Caret()) return true;
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;

            if (focused.Current.ControlType == ControlType.Edit) return true;
            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out _)) return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// True when the UI Automation element directly under (<paramref name="x"/>, <paramref name="y"/>)
    /// is a text-bearing element — Edit, Document, Group+TextPattern, or anything else exposing
    /// TextPattern. False for buttons, title bars, scrollbars, tabs, panes, etc.
    /// </summary>
    /// <remarks>
    /// Why we need this on top of NCHITTEST + the focused-element checks: Chrome and other
    /// Electron apps draw their own title bars in the same Win32 client area, so NCHITTEST
    /// returns HTCLIENT for a title-bar drag and the focused-element check returns true if any
    /// address bar / search box elsewhere in the window happens to still be focused. Asking
    /// "what's under the cursor" via UI Automation cuts through that — the title-bar element
    /// doesn't expose TextPattern.
    /// Slow (50–300 ms on Electron); call from a worker thread, never the hook thread or the
    /// dispatcher synchronously. Permissive on uncertainty (null element OR exception): better
    /// to occasionally show the toolbar than to suppress legitimate selections in apps with
    /// quirky a11y trees.
    /// </remarks>
    public static bool IsTextInputAtPoint(int x, int y)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            // Permissive on null too — same rationale as the catch below. The previous strict
            // null branch made the gate inconsistent: errors permissive, missing element strict.
            if (element == null) return true;

            var ct = element.Current.ControlType;

            // Native edit controls + browser <input>/<textarea>.
            if (ct == ControlType.Edit) return true;

            // Browser document body — selectable text lives here.
            if (ct == ControlType.Document) return true;

            // Rich-text editors in Electron apps (ProseMirror/CodeMirror/Slate) — Group + TextPattern.
            if (ct == ControlType.Group && element.TryGetCurrentPattern(TextPattern.Pattern, out _))
                return true;

            // Catch-all for anything else exposing user-selectable text — labels, paragraphs in
            // accessible apps, etc. Chrome/Electron title bars and tab strips do NOT expose
            // TextPattern, so window-drag cases fall through to false here.
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out _)) return true;

            return false;
        }
        catch
        {
            return true;
        }
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
