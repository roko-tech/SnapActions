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

    // Maximum UIA parent levels to walk when probing for text capability. Leaf nodes in a
    // browser DOM (`<span>`, `<a>`, `<i>`, `<svg>`) routinely don't expose TextPattern on
    // themselves even though their paragraph / article / document ancestor does. 4 levels is
    // enough to reach `<p>` from a nested inline element (`<a><span>text</span></a>` style).
    private const int TextPatternParentWalkDepth = 4;

    /// <summary>
    /// True when the UI Automation element under (<paramref name="x"/>, <paramref name="y"/>)
    /// — or any of its first <see cref="TextPatternParentWalkDepth"/> ancestors — is a
    /// text-bearing element (Edit / Document / TextPattern). False for title bars, scrollbars,
    /// tabs, panes, draggable file icons, etc.
    /// </summary>
    /// <remarks>
    /// The parent walk is the v1.6.11 fix for the v1.6.10 over-suppression bug: in browsers
    /// and Electron apps `FromPoint` returns the deepest element under the cursor, which is
    /// often a leaf inline element with no TextPattern of its own. Walking up to the paragraph
    /// or document recovers the real "is this text?" answer.
    /// Slow (50–500 ms on Electron with a11y not loaded); call from a worker thread, never the
    /// hook thread or the dispatcher synchronously.
    /// Uncertainty handling: a UIA *exception* returns true (transient quirk; don't suppress
    /// legitimate selections), but a definite null FromPoint result returns false (UIA gave us
    /// a clear "no element here" answer — suppress to avoid showing paste mode over voids).
    /// </remarks>
    public static bool IsTextInputAtPoint(int x, int y)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (element == null) return false;

            var walker = TreeWalker.RawViewWalker;
            for (int depth = 0; element != null && depth < TextPatternParentWalkDepth; depth++)
            {
                try
                {
                    var ct = element.Current.ControlType;
                    if (ct == ControlType.Edit) return true;
                    if (ct == ControlType.Document) return true;
                    if (element.TryGetCurrentPattern(TextPattern.Pattern, out _)) return true;
                }
                catch { /* per-level UIA failure — try the parent */ }

                try { element = walker.GetParent(element); }
                catch { break; }
            }
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
