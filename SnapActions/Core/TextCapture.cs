using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace SnapActions.Core;

public static class TextCapture
{
    private const int INPUT_KEYBOARD = 1;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_INSERT = 0x2D;  // Ctrl+Insert = Copy / Shift+Insert = Paste
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint WM_COPY = 0x0301;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint WM_COPY_TIMEOUT_MS = 100;

    private static readonly INPUT[] CtrlInsertInputs = BuildExtendedInsertCombo(VK_CONTROL);
    private static readonly INPUT[] ShiftInsertInputs = BuildExtendedInsertCombo(VK_SHIFT);
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // Serialize captures so two rapid selections can't interleave snapshot/restore and corrupt
    // the clipboard. Acquired non-blocking via WaitAsync(0): if another capture is in flight we
    // drop this round entirely rather than queueing — the SelectionTracker debounce already
    // gates us at 250 ms and a captured-but-deferred selection would be stale by the time it ran.
    private static readonly System.Threading.SemaphoreSlim _captureLock = new(1, 1);

    public static async Task<string?> CaptureSelectedTextAsync()
    {
        // Skip if a capture is already running — the caller will simply not show a toolbar this round.
        if (!await _captureLock.WaitAsync(0))
        {
            SnapActions.Helpers.Log.Warn("Capture skipped — another capture is already in progress");
            return null;
        }
        try
        {
            // UIA pre-gate. Three outcomes:
            //   HasText  — there is a real text selection; use it, skip the whole clipboard dance.
            //   Suppress — UIA *definitively* says no selection (TextPattern present but degenerate,
            //              or focus is on a non-text item like an Explorer file). Bail out.
            //   Unknown  — UIA can't tell (no TextPattern, exception, shallow tree). Fall through.
            // Why the pipeline used to fire SelectionLikely on a double-click in Explorer or a
            // double-click on a desktop icon: WM_COPY succeeds against those (copies the filename
            // or item text) even though no *text* is selected. The Suppress branch kills that path
            // for any app that exposes either TextPattern or item-selection patterns.
            var probe = await ProbeSelectionViaUIA();
            switch (probe.Outcome)
            {
                case SelectionProbeOutcome.HasText:
                    SnapActions.Helpers.Log.Info($"UIA pre-gate returned text ({probe.Text!.Length} chars) — skipping clipboard pipeline");
                    return probe.Text;
                case SelectionProbeOutcome.Suppress:
                    SnapActions.Helpers.Log.Info($"UIA pre-gate suppressed capture: {probe.Reason}");
                    return null;
                // case SelectionProbeOutcome.Unknown: fall through
            }

            // Snapshot ALL clipboard formats so images/files/RTF survive
            var saved = await Application.Current.Dispatcher.InvokeAsync(SnapshotClipboard);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try { Clipboard.Clear(); } catch { }
            });

            // Try WM_COPY first (no keyboard events)
            CopyViaWindowMessage();
            await Task.Delay(20);
            var text = await ReadClipboard();

            // Try UI Automation next — TextPattern.GetSelection reads the selected text from
            // the accessibility tree without firing any keystrokes or touching the clipboard.
            // Slower than WM_COPY (50–500 ms in apps where a11y isn't loaded), but quiet — apps
            // with global key hooks (h5player, AutoHotkey, IMEs, game overlays) don't see this
            // path at all. Walks up the focused element's parents because browsers / Electron
            // routinely focus a parent pane while the document with TextPattern is one or two
            // levels up.
            if (string.IsNullOrEmpty(text))
                text = await CopyViaUIA();

            // Last resort: Ctrl+Insert. Up to 250 ms total. Some apps respond to neither WM_COPY
            // nor UIA — VS Code's editor sometimes lands here, as do older Edge tabs and Java
            // Swing windows. This path is what other apps' global key hooks can intercept (the
            // user-reported interference), so we only fire it when both quieter mechanisms came
            // back empty. If a specific app still misbehaves on the Ctrl+Insert, add it to
            // Settings → Excluded apps to suppress capture there entirely.
            if (string.IsNullOrEmpty(text))
            {
                CopyViaKeyboard();
                for (int i = 0; i < 25; i++)
                {
                    await Task.Delay(10);
                    text = await ReadClipboard();
                    if (!string.IsNullOrEmpty(text)) break;
                }
            }

            // Restore original clipboard contents
            await Application.Current.Dispatcher.InvokeAsync(() => RestoreClipboard(saved));

            return text;
        }
        catch (Exception ex)
        {
            SnapActions.Helpers.Log.Error("Capture error", ex);
            return null;
        }
        finally
        {
            _captureLock.Release();
        }
    }

    /// <summary>
    /// Snapshots the clipboard. Returns null on error (so the restore step can skip and avoid
    /// destroying user data); returns an empty dict when the clipboard was actually empty.
    /// </summary>
    private static Dictionary<string, object>? SnapshotClipboard()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null)
                return new Dictionary<string, object>(); // empty clipboard, not an error
            var snap = new Dictionary<string, object>();
            foreach (var fmt in data.GetFormats(autoConvert: false))
            {
                try
                {
                    var obj = data.GetData(fmt, autoConvert: false);
                    if (obj != null) snap[fmt] = obj;
                }
                catch { /* delay-rendered formats may throw — skip */ }
            }
            return snap;
        }
        catch
        {
            // Distinguish failure from empty: returning null tells RestoreClipboard to leave the
            // clipboard alone, which preserves whatever's there now (the WM_COPY/Ctrl+Insert
            // result if it succeeded, or unmodified state otherwise).
            return null;
        }
    }

    private static void RestoreClipboard(Dictionary<string, object>? snapshot)
    {
        try
        {
            // Snapshot failed — best we can do is leave the clipboard as-is. Clearing it would
            // destroy the WM_COPY result we just put there (which the caller is about to use)
            // AND lose whatever the user had before us.
            if (snapshot == null) return;

            if (snapshot.Count == 0)
            {
                // Clipboard was empty before us — restore that state.
                Clipboard.Clear();
                return;
            }

            var data = new System.Windows.DataObject();
            foreach (var (fmt, obj) in snapshot)
            {
                try { data.SetData(fmt, obj); } catch { }
            }
            Clipboard.SetDataObject(data, copy: true);
        }
        catch { }
    }

    private static async Task<string?> ReadClipboard()
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch { return null; }
        });
    }

    /// <summary>
    /// Maximum UIA parent levels to walk when probing for a TextPattern. Same rationale as
    /// <see cref="ForegroundApp.IsTextInputAtPoint"/>: leaf elements (a span / anchor / svg)
    /// usually don't expose TextPattern themselves even though the paragraph / document
    /// ancestor does.
    /// </summary>
    private const int TextPatternParentWalkDepth = 6;

    internal enum SelectionProbeOutcome
    {
        /// <summary>UIA gave us the selected text directly — use it and skip the clipboard pipeline.</summary>
        HasText,
        /// <summary>UIA definitively said no selection (empty TextPattern, or a non-text item). Suppress.</summary>
        Suppress,
        /// <summary>UIA couldn't determine. Fall through to WM_COPY / Ctrl+Insert.</summary>
        Unknown,
    }

    internal readonly record struct SelectionProbe(SelectionProbeOutcome Outcome, string? Text, string? Reason);

    /// <summary>
    /// Item-style control types that are NOT text. When the focused element is one of these
    /// AND exposes SelectionItemPattern AND we found no TextPattern up the tree, we treat the
    /// "selection" as an item selection (file in Explorer, desktop icon, list-box row, tree
    /// node) and suppress. Deliberately narrow — Pane / Custom / Document stay out because
    /// browsers and Electron focus those for real text contexts.
    /// </summary>
    private static readonly System.Windows.Automation.ControlType[] NonTextItemTypes =
    [
        System.Windows.Automation.ControlType.DataItem,
        System.Windows.Automation.ControlType.ListItem,
        System.Windows.Automation.ControlType.TreeItem,
    ];

    /// <summary>
    /// Probes UI Automation to decide whether a real text selection exists right now. Layered
    /// gate to prevent the WM_COPY pipeline from misreading non-text contexts (Explorer file
    /// double-click, desktop icon, list row) as text selections. Runs on a worker thread —
    /// UIA calls can take 50–500 ms cold.
    /// </summary>
    /// <remarks>
    /// Three UIA-based gates were tried and removed across v1.6.5–1.6.12 because they over-
    /// suppressed legitimate selections. This one is more conservative: it only suppresses
    /// when UIA gives a *definitive* answer — TextPattern explicitly empty, or a clearly non-
    /// text item element. Anything ambiguous (no TextPattern, exception, shallow tree)
    /// returns Unknown, which leaves the existing WM_COPY → Ctrl+Insert fallback intact.
    /// </remarks>
    internal static async Task<SelectionProbe> ProbeSelectionViaUIA()
    {
        return await Task.Run(() =>
        {
            AutomationElement? originalFocused = null;
            try
            {
                originalFocused = AutomationElement.FocusedElement;
                if (originalFocused == null)
                    return new SelectionProbe(SelectionProbeOutcome.Unknown, null, "no focused element");

                // Walk up looking for TextPattern. If ANY ancestor has TextPattern with non-empty
                // selection → HasText (return immediately). If we exhaust the walk and saw at least
                // one TextPattern but all were empty → Suppress. If we never saw TextPattern → fall
                // through to the item-element check below.
                var walker = TreeWalker.RawViewWalker;
                var element = originalFocused;
                bool sawAnyTextPattern = false;
                for (int depth = 0; element != null && depth < TextPatternParentWalkDepth; depth++)
                {
                    try
                    {
                        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pat))
                        {
                            sawAnyTextPattern = true;
                            var tp = (TextPattern)pat;
                            var ranges = tp.GetSelection();
                            if (ranges != null && ranges.Length > 0)
                            {
                                var combined = ranges.Length == 1
                                    ? ranges[0].GetText(-1)
                                    : string.Join("\n",
                                        ranges.Select(r => r.GetText(-1)).Where(s => !string.IsNullOrEmpty(s)));
                                if (!string.IsNullOrEmpty(combined))
                                    return new SelectionProbe(SelectionProbeOutcome.HasText, combined, null);
                            }
                            // TextPattern at this level returned no selection text. Keep walking up
                            // — an ancestor pane / document may have the real selection (browsers
                            // often expose TextPattern at multiple levels with the leaf empty).
                        }
                    }
                    catch { /* per-level UIA failure — try the parent */ }

                    try { element = walker.GetParent(element); }
                    catch { break; }
                }

                if (sawAnyTextPattern)
                    return new SelectionProbe(SelectionProbeOutcome.Suppress,
                        null, "TextPattern present but selection is empty");

                // Layer C: no TextPattern anywhere up the walk. Check the originally-focused
                // element for non-text item patterns — Explorer file rows, desktop icons,
                // list-box rows. SelectionItemPattern means "I am a selectable item" (vs.
                // text); ControlType keeps us off Pane / Custom / Document which browsers
                // and Electron focus for real text contexts.
                try
                {
                    var ct = originalFocused.Current.ControlType;
                    bool isItemType = NonTextItemTypes.Contains(ct);
                    bool hasItemPattern = originalFocused.TryGetCurrentPattern(
                        SelectionItemPattern.Pattern, out _);
                    if (isItemType && hasItemPattern)
                        return new SelectionProbe(SelectionProbeOutcome.Suppress,
                            null, $"focused element is {ct.ProgrammaticName} with SelectionItemPattern");
                }
                catch { /* couldn't read ControlType — fall through to Unknown */ }

                return new SelectionProbe(SelectionProbeOutcome.Unknown, null, "no TextPattern, not a known non-text item");
            }
            catch (Exception ex)
            {
                // Total UIA failure — be permissive (fall through to clipboard pipeline) so we
                // don't silently break selections in apps where UIA misbehaves.
                return new SelectionProbe(SelectionProbeOutcome.Unknown, null, $"UIA exception: {ex.GetType().Name}");
            }
        });
    }

    /// <summary>
    /// Reads the current selection via UI Automation. Returns null when no focused element,
    /// no TextPattern within the walk depth, no selection ranges, or any UIA failure. Runs on
    /// a worker thread because UIA calls can take hundreds of ms in apps where a11y is cold.
    /// </summary>
    /// <remarks>
    /// Why not the only capture mechanism: TextPattern coverage is uneven — Java Swing, some
    /// Edge contexts, and certain custom Electron renderers either don't expose it or expose
    /// a pattern that returns empty selections even when the user clearly has text selected.
    /// Keeping Ctrl+Insert as a last-resort fallback covers those.
    /// </remarks>
    private static async Task<string?> CopyViaUIA()
    {
        return await Task.Run(() =>
        {
            try
            {
                var element = AutomationElement.FocusedElement;
                if (element == null) return null;

                var walker = TreeWalker.RawViewWalker;
                for (int depth = 0; element != null && depth < TextPatternParentWalkDepth; depth++)
                {
                    try
                    {
                        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pat))
                        {
                            var tp = (TextPattern)pat;
                            var ranges = tp.GetSelection();
                            if (ranges != null && ranges.Length > 0)
                            {
                                // GetText(-1) returns the entire range with no length cap. For
                                // discontiguous selections (rare — Ctrl-click in Excel-style
                                // grids) join with \n so the caller sees all of it.
                                var combined = ranges.Length == 1
                                    ? ranges[0].GetText(-1)
                                    : string.Join("\n",
                                        ranges.Select(r => r.GetText(-1)).Where(s => !string.IsNullOrEmpty(s)));
                                if (!string.IsNullOrEmpty(combined)) return combined;
                            }
                        }
                    }
                    catch { /* per-level UIA failure — try the parent */ }

                    try { element = walker.GetParent(element); }
                    catch { break; }
                }
            }
            catch { /* UIA failure */ }
            return null;
        });
    }

    private static void CopyViaWindowMessage()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        uint threadId = GetWindowThreadProcessId(hwnd, out _);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };

        IntPtr target = hwnd;
        if (GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
            target = info.hwndFocus;

        // Timeout-bounded so a hung target window can't block the dispatcher
        SendMessageTimeout(target, WM_COPY, IntPtr.Zero, IntPtr.Zero,
            SMTO_ABORTIFHUNG, WM_COPY_TIMEOUT_MS, out _);
    }

    private static void CopyViaKeyboard()
    {
        // Use Ctrl+Insert instead of Ctrl+C.
        // Browser extensions (like h5player) hook letter keys but not Insert.
        SendInput((uint)CtrlInsertInputs.Length, CtrlInsertInputs, InputSize);
    }

    /// <summary>
    /// Send Shift+Insert (canonical paste). We deliberately don't use Ctrl+V — browser extensions
    /// like h5player hook letter keys, which is the same reason capture uses Ctrl+Insert.
    /// </summary>
    public static void SimulatePaste()
    {
        SendInput((uint)ShiftInsertInputs.Length, ShiftInsertInputs, InputSize);
    }

    // Insert is an extended key — without the flag some apps see numpad-0 instead.
    private static INPUT[] BuildExtendedInsertCombo(ushort modifier) =>
    [
        MakeKeyInput(modifier, false, extended: false),
        MakeKeyInput(VK_INSERT, false, extended: true),
        MakeKeyInput(VK_INSERT, true, extended: true),
        MakeKeyInput(modifier, true, extended: false),
    ];

    private static INPUT MakeKeyInput(ushort vk, bool keyUp, bool extended)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki.wVk = vk;
        uint flags = 0;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (keyUp) flags |= KEYEVENTF_KEYUP;
        input.u.ki.dwFlags = flags;
        return input;
    }

    // P/Invoke structs
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
