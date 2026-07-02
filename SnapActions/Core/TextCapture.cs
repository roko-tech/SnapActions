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

    // Clipboard formats we can faithfully snapshot and put back. Everything else is deliberately
    // skipped: arbitrary OLE / app-specific formats serialize through the (removed-on-.NET-9)
    // BinaryFormatter path when RestoreClipboard flushes with copy:true, which throws and can drop
    // the WHOLE clipboard. Text/HTML/RTF/CSV are strings, FileDrop is string[], Bitmap is OLE-
    // rendered — all round-trip without BinaryFormatter. (Trade-off: an exotic-format-only
    // clipboard isn't preserved across a capture. The fuller fix is to stop clearing the clipboard
    // entirely (an event-driven, no-clear capture), which is a larger change.)
    private static readonly HashSet<string> RoundTrippableFormats = new(StringComparer.Ordinal)
    {
        System.Windows.DataFormats.UnicodeText, System.Windows.DataFormats.Text,
        System.Windows.DataFormats.Rtf, System.Windows.DataFormats.Html,
        System.Windows.DataFormats.CommaSeparatedValue, System.Windows.DataFormats.FileDrop,
        System.Windows.DataFormats.Bitmap,
    };

    /// <summary>
    /// Captures the current selection. <paramref name="isDrag"/> distinguishes a drag gesture
    /// (strongest selection intent — both the I-beam and drag-distance gates agreed) from a
    /// multi-click; <paramref name="allowSyntheticKeys"/> is false for quiet-only captures
    /// (<see cref="CaptureAggressiveness.Quiet"/>) where a Ctrl+Insert must never be injected.
    /// </summary>
    public static async Task<string?> CaptureSelectedTextAsync(bool isDrag, bool allowSyntheticKeys)
    {
        // Skip if a capture is already running — the caller will simply not show a toolbar this round.
        if (!await _captureLock.WaitAsync(0))
        {
            SnapActions.Helpers.Log.Warn("Capture skipped — another capture is already in progress");
            return null;
        }
        try
        {
            // UIA pre-gate. Outcomes:
            //   HasText            — a real text selection; use it, skip the whole clipboard dance.
            //   SuppressItemElement — focus is a non-text item (Explorer file, desktop icon, list
            //                         row). Bail out: WM_COPY against those "succeeds" by copying
            //                         the filename/item text even though no text is selected.
            //   EmptyTextPattern   — TextPattern present but reports no selection. NOT definitive:
            //                         some providers return empty selections even when the user
            //                         clearly has text selected (the same app class Ctrl+Insert
            //                         exists for), so DecidePlan continues with a restricted
            //                         cascade instead of bailing out — see that method.
            //   Unknown            — UIA can't tell (no TextPattern, exception, shallow tree).
            // Skip UIA entirely for apps whose accessibility provider is known to hang/misbehave in
            // TextPattern — go straight to the clipboard path for them.
            bool skipUia = UiaSkipApps.Contains(ForegroundApp.GetActiveProcessName() ?? "");
            var outcome = SelectionProbeOutcome.Unknown;
            if (!skipUia)
            {
                // Bounded (RunBoundedUiaAsync) so a wedged provider can't hang here and permanently
                // hold the capture lock.
                var probe = await RunBoundedUiaAsync(
                    ProbeSelectionViaUIA(),
                    new SelectionProbe(SelectionProbeOutcome.Unknown, null, "UIA pre-gate timed out"));
                if (probe.Outcome == SelectionProbeOutcome.HasText)
                {
                    SnapActions.Helpers.Log.Info($"UIA pre-gate returned text ({probe.Text!.Length} chars) — skipping clipboard pipeline");
                    return probe.Text;
                }
                outcome = probe.Outcome;
                if (outcome == SelectionProbeOutcome.SuppressItemElement)
                {
                    SnapActions.Helpers.Log.Info($"UIA pre-gate suppressed capture: {probe.Reason}");
                    return null;
                }
                if (outcome == SelectionProbeOutcome.EmptyTextPattern)
                    SnapActions.Helpers.Log.Info($"UIA pre-gate saw an empty TextPattern ({probe.Reason}) — continuing with restricted cascade");
            }

            var plan = DecidePlan(outcome, isDrag, allowSyntheticKeys);
            if (skipUia) plan = plan with { RunUia = false };
            if (!plan.RunWmCopy && !plan.RunUia && !plan.RunKeystroke) return null;

            // No-clear capture. We do NOT wipe the
            // clipboard first. Instead we snapshot it and detect whether each copy step actually
            // wrote to the clipboard via its sequence number, restoring ONLY if something did. A
            // gesture that copies nothing leaves the clipboard completely untouched — no Clear, no
            // restore — so it's invisible to other apps / clipboard managers, and the user's data is
            // never momentarily absent (the old Clear()-first window could wipe it on a fault).
            uint seqBefore = SnapActions.Helpers.NativeMethods.GetClipboardSequenceNumber();
            var saved = await Application.Current.Dispatcher.InvokeAsync(SnapshotClipboard);
            bool changed = false;
            uint seqAfterCopy = 0; // sequence number right after OUR copy landed — for a safe restore
            try
            {
                // Try WM_COPY first (no keyboard events). A sequence-number change means it landed.
                string? text = null;
                if (plan.RunWmCopy)
                {
                    CopyViaWindowMessage();
                    await Task.Delay(20);
                    uint seqNow = SnapActions.Helpers.NativeMethods.GetClipboardSequenceNumber();
                    if (seqNow != seqBefore)
                    {
                        changed = true;
                        seqAfterCopy = seqNow;
                        text = await ReadClipboard();
                    }
                }

                // Then UI Automation — reads the selection from the accessibility tree without firing
                // any keystrokes or touching the clipboard; walks up parents for browser/Electron
                // panes. Bounded so a wedged provider can't hang and hold the capture lock.
                if (string.IsNullOrEmpty(text) && plan.RunUia)
                    text = await RunBoundedUiaAsync(CopyViaUIA(), null);

                // Last resort: Ctrl+Insert. Some apps respond to neither WM_COPY nor UIA (VS Code's
                // editor, older Edge tabs, Java Swing). Wait for the user to release Shift/Alt first
                // (a held modifier would turn our chord into Ctrl+Shift+Insert etc. and copy nothing),
                // then detect the result by the clipboard sequence number.
                if (string.IsNullOrEmpty(text) && plan.RunKeystroke)
                {
                    await WaitForModifierKeysReleasedAsync();
                    uint seqBeforeKbd = SnapActions.Helpers.NativeMethods.GetClipboardSequenceNumber();
                    CopyViaKeyboard();
                    for (int i = 0; i < 25; i++)
                    {
                        await Task.Delay(10);
                        uint s = SnapActions.Helpers.NativeMethods.GetClipboardSequenceNumber();
                        if (s != seqBeforeKbd)
                        {
                            changed = true;
                            seqAfterCopy = s;
                            text = await ReadClipboard();
                            if (!string.IsNullOrEmpty(text)) break;
                        }
                    }
                }

                return text;
            }
            finally
            {
                // Restore only if a copy step changed the clipboard. The seq re-check lives INSIDE the
                // dispatcher callback so it's atomic with the restore — no other UI-thread work can run
                // between checking and writing. If the sequence number no longer matches the one right
                // after our copy, a third party owns the clipboard now and we must not clobber them.
                // If nothing copied at all, the clipboard was never touched (zero mutations).
                if (changed)
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (SnapActions.Helpers.NativeMethods.GetClipboardSequenceNumber() == seqAfterCopy)
                            RestoreClipboard(saved);
                    });
            }
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

    // Apps whose UIA TextPattern provider is known to hang or misbehave — skip UIA for them and use
    // the clipboard path directly. Process name, no .exe.
    private static readonly HashSet<string> UiaSkipApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "thunderbird",
    };

    private const int UiaCallTimeoutMs = 500;

    /// <summary>
    /// Runs a UIA worker task with a hard timeout. ProbeSelectionViaUIA / CopyViaUIA run on Task.Run
    /// worker threads; if a broken accessibility provider blocks inside GetSelection/GetText the
    /// worker never completes. Without this bound the await would hang forever, <see cref="_captureLock"/>
    /// would never be released, and EVERY later capture would be silently dropped at WaitAsync(0) for
    /// the rest of the session. On timeout we abandon the worker and return the fallback so the
    /// clipboard cascade still runs and the lock is released.
    /// </summary>
    private static async Task<T> RunBoundedUiaAsync<T>(Task<T> uiaTask, T onTimeout)
    {
        var done = await Task.WhenAny(uiaTask, Task.Delay(UiaCallTimeoutMs));
        return done == uiaTask ? await uiaTask : onTimeout;
    }

    /// <summary>
    /// Waits up to ~300 ms for the user to release Shift/Alt before we inject the synthetic Ctrl+Insert.
    /// A held modifier at gesture end (e.g. Shift+drag to
    /// extend a selection) would otherwise turn our chord into Ctrl+Shift+Insert etc., which copies
    /// nothing in many apps. Ctrl is fine — we press it ourselves.
    /// </summary>
    private static async Task WaitForModifierKeysReleasedAsync()
    {
        const int VK_SHIFT = 0x10, VK_MENU = 0x12; // Alt
        for (int i = 0; i < 15; i++)
        {
            bool held = (SnapActions.Helpers.NativeMethods.GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0
                     || (SnapActions.Helpers.NativeMethods.GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            if (!held) return;
            await Task.Delay(20);
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
                if (!RoundTrippableFormats.Contains(fmt)) continue;
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

    /// <summary>
    /// Reads whatever text is already on the clipboard, with no clear / synthetic-copy dance. Used
    /// by the opt-in "capture on real Ctrl+C" trigger, where the user has already copied the text —
    /// so there is zero clipboard mutation and nothing for other apps to observe.
    /// </summary>
    public static Task<string?> ReadCurrentClipboardTextAsync() => ReadClipboard();

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
        /// <summary>The focused element is a non-text item (Explorer file, desktop icon, list row).
        /// Definitive — capture must not run (WM_COPY would copy the item's name).</summary>
        SuppressItemElement,
        /// <summary>A TextPattern was found but reported an empty selection. Usually means "no
        /// selection", but some providers lie (report empty despite a real selection), so this is
        /// a restriction signal, not a hard stop — see <see cref="DecidePlan"/>.</summary>
        EmptyTextPattern,
        /// <summary>UIA couldn't determine. Fall through to WM_COPY / Ctrl+Insert.</summary>
        Unknown,
    }

    internal readonly record struct SelectionProbe(SelectionProbeOutcome Outcome, string? Text, string? Reason);

    /// <summary>Which capture layers a given gesture may run. Produced by <see cref="DecidePlan"/>.</summary>
    internal readonly record struct CapturePlan(bool RunWmCopy, bool RunUia, bool RunKeystroke);

    /// <summary>
    /// Pure policy: probe outcome × gesture → which layers run. The balance being struck:
    /// <list type="bullet">
    ///   <item><b>EmptyTextPattern + drag</b> — full cascade (keystroke allowed). A drag that
    ///     passed the I-beam and distance gates is the strongest possible selection signal; a
    ///     provider reporting "empty" against it is exactly the lying-provider class the
    ///     Ctrl+Insert fallback exists for. If there genuinely is no selection, Ctrl+Insert
    ///     copies nothing and the sequence number stays put — self-gating.</item>
    ///   <item><b>EmptyTextPattern + multi-click</b> — WM_COPY only. Double-click is the gesture
    ///     most prone to non-text false positives, so no synthetic keystroke; but WM_COPY is
    ///     silent and a no-op when nothing is selected, so lying providers that answer it still
    ///     get their toolbar. UIA is skipped — the probe just walked the same tree and came back
    ///     empty.</item>
    ///   <item><b>Unknown</b> — normal cascade, capped by the caller's aggressiveness (a quiet
    ///     capture never injects keys regardless of outcome).</item>
    /// </list>
    /// (HasText / SuppressItemElement are resolved before planning.)
    /// </summary>
    internal static CapturePlan DecidePlan(SelectionProbeOutcome outcome, bool isDrag, bool allowSyntheticKeys) =>
        outcome switch
        {
            SelectionProbeOutcome.SuppressItemElement => new(false, false, false),
            SelectionProbeOutcome.EmptyTextPattern => new(true, false, isDrag && allowSyntheticKeys),
            _ => new(true, true, allowSyntheticKeys),
        };

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
    /// suppressed legitimate selections. This one is more conservative: only a clearly non-text
    /// item element (SuppressItemElement) is a hard stop. An explicitly-empty TextPattern is a
    /// *restriction* signal (EmptyTextPattern), not a stop — some providers report an empty
    /// selection even when the user clearly has text selected, so <see cref="DecidePlan"/> keeps
    /// a reduced cascade alive for it. Anything ambiguous (no TextPattern, exception, shallow
    /// tree) returns Unknown, which leaves the full WM_COPY → Ctrl+Insert fallback intact.
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
                    return new SelectionProbe(SelectionProbeOutcome.EmptyTextPattern,
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
                        return new SelectionProbe(SelectionProbeOutcome.SuppressItemElement,
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
