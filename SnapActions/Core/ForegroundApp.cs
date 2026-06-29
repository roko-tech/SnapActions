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
    /// Process names where the double-click paste-mode trigger should be suppressed regardless
    /// of focused element. Explorer's native double-click action is "open the folder under the
    /// cursor" — and after that action, focus can briefly land on the address bar
    /// (ControlType.Edit) even though the user clearly meant to navigate, not type. Long-press
    /// paste mode still works in these apps (it uses the cursor-at-point check, which correctly
    /// rejects folder icons / file rows). Match is by process name (no .exe suffix).
    /// </summary>
    private static readonly HashSet<string> NoDoubleClickPasteModeProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "TOTALCMD", "TOTALCMD64", "doublecmd", "dopus", "Files",
    };

    /// <summary>
    /// Item-like control types that definitively are NOT text inputs. Used as an early-reject in
    /// the strict editable check so a folder-row / list-row focus after a double-click action
    /// can't pass via some side pattern.
    /// </summary>
    private static readonly System.Windows.Automation.ControlType[] NonTextFocusableTypes =
    [
        ControlType.ListItem, ControlType.DataItem, ControlType.TreeItem,
        ControlType.Button, ControlType.MenuItem, ControlType.TabItem,
        ControlType.Image, ControlType.Hyperlink, ControlType.ScrollBar,
        ControlType.CheckBox, ControlType.RadioButton,
    ];

    /// <summary>
    /// Strict editable-focus check: ControlType.Edit or non-read-only ValuePattern, gated by an
    /// item-type early-reject AND a file-manager process-name early-reject. Deliberately omits
    /// the bare TextPattern branch that <see cref="IsEditableFieldFocused"/> allows (TextPattern
    /// is present on read-only documents like Twitter articles) AND the Win32 caret branch (a
    /// registered-but-invisible caret on Explorer's address bar would falsely trigger).
    /// </summary>
    /// <remarks>
    /// Used by the double-click paste-mode trigger where false positives matter much more than
    /// for the selection-toolbar's transform-button visibility. Layered against three known
    /// failure modes:
    ///   1. Cursor-at-point check (<see cref="IsTextInputAtPoint"/>) is unreliable for clicks
    ///      because the UI under the cursor shifts during the click action (Explorer folder
    ///      opens, content view re-renders). Focused-element check is stable.
    ///   2. Post-navigation focus in browsers / Outlook / Explorer can transiently land on an
    ///      Edit element even though the click meant "open this". The process-name reject and
    ///      item-type reject collectively rule out the common cases.
    ///   3. The Win32 caret signal includes "caret is registered" (not just "currently
    ///      blinking"), which Explorer leaves set for the address bar. Dropping it here means
    ///      paste mode in Notepad-like apps relies on UIA exposing the text area as Edit — which
    ///      every real Win32 edit does.
    /// </remarks>
    public static bool IsStrictlyEditableFocused()
    {
        // File-manager process reject — even if Explorer happens to focus its address bar after
        // navigation, we don't want paste mode there. Cheap check first.
        var process = GetActiveProcessName();
        if (process != null && NoDoubleClickPasteModeProcesses.Contains(process)) return false;

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;

            var ct = focused.Current.ControlType;
            // Explicit non-text items take priority over any pattern check. A focused ListItem
            // / Button / Image is the user's interaction target; never paste mode.
            if (System.Array.IndexOf(NonTextFocusableTypes, ct) >= 0) return false;

            if (ct == ControlType.Edit) return true;
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp)
                && !((ValuePattern)vp).Current.IsReadOnly)
                return true;
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
    /// — or any of its first <see cref="TextPatternParentWalkDepth"/> ancestors — is an
    /// *editable* text input. False for title bars, scrollbars, tabs, panes, draggable file
    /// icons, AND for read-only text content like Twitter feed articles or Wikipedia paragraphs.
    /// </summary>
    /// <remarks>
    /// The parent walk handles the case where `FromPoint` returns a leaf inline element (a
    /// `&lt;span&gt;` inside a contenteditable, etc.) and we need to climb up to the actual editor.
    /// Slow (50–500 ms on Electron with a11y not loaded); call from a worker thread, never the
    /// hook thread or the dispatcher synchronously.
    /// What counts as "editable":
    ///   • ControlType.Edit — standard &lt;input&gt;/&lt;textarea&gt;/&lt;div role="textbox"&gt;, native
    ///     Win32 edits, and most contenteditable elements that Chrome maps to Edit.
    ///   • ControlType.Group with TextPattern AND IsKeyboardFocusable — covers ProseMirror /
    ///     CodeMirror / contenteditable rich-text editors in Electron apps (Claude Desktop,
    ///     Slack, VS Code) without also matching read-only Group+TextPattern content like
    ///     Twitter &lt;article&gt; feeds (focusable=false because the article itself isn't
    ///     keyboard-navigable; only its interactive descendants are).
    /// Pre-v1.6.17 also matched ControlType.Document and bare TextPattern on any control type.
    /// Both were too loose: Twitter feed articles are Document/Group with TextPattern, so a
    /// long-press in feed padding walked up to the article and summoned paste mode falsely.
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
                    // Group+TextPattern matches both rich-text editors (focusable) and read-only
                    // article-like containers (not focusable). The IsKeyboardFocusable check
                    // separates them — editors accept focus, feed articles don't.
                    if (ct == ControlType.Group
                        && element.Current.IsKeyboardFocusable
                        && element.TryGetCurrentPattern(TextPattern.Pattern, out _))
                        return true;
                }
                catch { /* per-level UIA failure — try the parent */ }

                try { element = walker.GetParent(element); }
                catch { break; }
            }
            return false;
        }
        catch
        {
            // Previously returned true on any FromPoint failure (with the comment "transient quirk;
            // don't suppress legitimate selections"). Returning true here is what produced false
            // positives when UIA stuttered over browser content — paste mode over a Twitter feed
            // is worse than missing one legitimate trigger (the user can retry). Default to false.
            return false;
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
