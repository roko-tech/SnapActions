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
    /// Requires affirmative editable capability. A caret, Edit control type, or TextPattern
    /// alone proves neither that selected text is writable nor that replacement is safe.
    /// </summary>
    public static bool IsEditableFieldFocused()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            return focused != null && ReadEditability(focused) == true;
        }
        catch { }
        return false;
    }

    internal static bool IsEditableEvidence(bool enabled, bool? valueReadOnly, bool? textReadOnly) =>
        enabled && !(valueReadOnly ?? textReadOnly ?? true);

    private static bool? ReadEditability(AutomationElement element)
    {
        if (!element.Current.IsEnabled || Array.IndexOf(NonTextFocusableTypes, element.Current.ControlType) >= 0)
            return false;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
            return IsEditableEvidence(true, ((ValuePattern)value).Current.IsReadOnly, null);
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var text))
        {
            var readOnly = ((TextPattern)text).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
            return IsEditableEvidence(true, null, readOnly is bool flag ? flag : null);
        }
        return null; // A leaf without text patterns may belong to an editable ancestor.
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
    /// True when the foreground app is Explorer / the desktop / a known file manager — a shell
    /// item container where a synthetic Ctrl+Insert would copy FILES (CF_HDROP), not text, and
    /// could silently downgrade a pending Ctrl+X cut to a copy on the clipboard restore. Used to
    /// withhold the ambiguous-cursor drag keystroke there; the browser-feed selection fix it exists
    /// for never targets these apps. (Same process set as the double-click paste-mode reject.)
    /// </summary>
    public static bool IsFileManagerFocused()
    {
        var name = GetActiveProcessName();
        return name != null && NoDoubleClickPasteModeProcesses.Contains(name);
    }

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
    /// Uses the shared read-only capability check and excludes file-manager double-clicks,
    /// which can move focus to an address bar as a side effect of opening an item.
    /// </summary>
    public static bool IsStrictlyEditableFocused()
    {
        // File-manager process reject — even if Explorer happens to focus its address bar after
        // navigation, we don't want paste mode there. Cheap check first.
        var process = GetActiveProcessName();
        if (process != null && NoDoubleClickPasteModeProcesses.Contains(process)) return false;

        return IsEditableFieldFocused();
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
    /// An explicit read-only/disabled verdict stops the walk. Only leaves without capability
    /// evidence may defer to an ancestor; control type and focusability never override read-only.
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
                    if (ReadEditability(element) is { } editable) return editable;
                }
                catch { return false; }

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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

}
