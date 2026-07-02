using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SnapActions.Actions;
using SnapActions.Config;
using SnapActions.Detection;
using SnapActions.UI;

namespace SnapActions.Core;

public class SelectionTracker
{
    private readonly MouseHook _mouseHook;
    private readonly TextClassifier _classifier;
    private readonly ActionRegistry _actionRegistry;
    private ToolbarWindow? _toolbar;
    // TickCount64 is monotonic — wall-clock jumps (NTP sync, hibernation resume, manual time
    // change) used to spuriously suppress or re-fire the debounce when DateTime.UtcNow drifted.
    private long _lastShowTicks;
    private const long DebounceMs = 250;
    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    /// <summary>
    /// Foreground HWND snapshotted at every WM_LBUTTONDOWN, BEFORE the OS routes the click to
    /// the target window and any double-click action runs. Used by the double-click paste-mode
    /// fallthrough to detect when the click launched a new app / window — if the foreground
    /// HWND has changed by paste-mode-check time, the click opened something (shortcut, file,
    /// folder in new window) and we should suppress, regardless of what the new app focused.
    /// Written from the hook thread, read after marshalling to the UI dispatcher. IntPtr writes
    /// are atomic on Windows for both x86 and x64, so no lock needed.
    /// </summary>
    private IntPtr _foregroundAtMouseDown;

    /// <summary>
    /// The cursor shape at the most recent mouse-down. Sampled at press time because that's the
    /// reliable moment: a press that starts a selection lands directly on text. By the time a
    /// click cluster resolves (double/triple-click go through the multi-click delay timer) a live
    /// cursor read can miss the I-beam — the app may swap the cursor over the new selection, or
    /// the pointer drifts off the word. Written and read on the hook thread (OnMouseDown and the
    /// pre-dispatch part of OnSelectionLikely both run there).
    /// </summary>
    private CursorKind _mouseDownCursor = CursorKind.Unreadable;

    public SelectionTracker()
    {
        _mouseHook = new MouseHook();
        _classifier = new TextClassifier();
        _actionRegistry = new ActionRegistry();
        _mouseHook.SelectionLikely += OnSelectionLikely;
        _mouseHook.LongPress += OnLongPress;
        _mouseHook.MouseDown += OnMouseDown;
        KeyboardHook.CtrlCPressed += OnCtrlCPressed;
    }

    public void Start()
    {
        _mouseHook.Install();
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _toolbar = new ToolbarWindow { Registry = _actionRegistry };
            _toolbar.Left = -9999; _toolbar.Top = -9999; _toolbar.Opacity = 0;
            _toolbar.Show(); _toolbar.Hide();
        });
    }

    public void Stop()
    {
        KeyboardHook.CtrlCPressed -= OnCtrlCPressed;
        _mouseHook.Uninstall();
        _mouseHook.Dispose();
    }

    /// <summary>
    /// Opt-in trigger: the user pressed Ctrl+C, so they explicitly copied text — show the toolbar
    /// for it with NO synthetic keystroke and NO clipboard clear (zero interference, intent is
    /// unambiguous, so the I-beam/selection gates are skipped).
    /// </summary>
    private void OnCtrlCPressed()
    {
        if (!SettingsManager.Current.CaptureOnCtrlC) return;
        if (IsSelfFocused()) return;

        // Captured on the hook thread BEFORE CallNextHookEx delivers Ctrl+C to the app, so it's the
        // pre-copy clipboard sequence number. If it doesn't change, the Ctrl+C copied nothing (e.g.
        // pressed with no selection) and we must not pop the toolbar on stale clipboard text.
        uint seqBefore = SnapActions.Helpers.NativeMethods.GetClipboardSequenceNumber();

        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (!SettingsManager.Current.Enabled) return;
                if (ForegroundApp.IsExcluded(SettingsManager.Current.ExcludedApps)) return;

                long now = Environment.TickCount64;
                if (now - _lastShowTicks < DebounceMs) return;
                _lastShowTicks = now; // claim the debounce slot before the await so a rapid second Ctrl+C is dropped

                await Task.Delay(100); // let the OS finish placing the copied text on the clipboard
                if (SnapActions.Helpers.NativeMethods.GetClipboardSequenceNumber() == seqBefore) return;

                var text = await TextCapture.ReadCurrentClipboardTextAsync();
                if (string.IsNullOrWhiteSpace(text)) return;

                SnapActions.Helpers.NativeMethods.GetCursorPos(out var pt);
                if (_toolbar?.IsVisible == true) _toolbar.HideToolbar();

                bool isEditable = await Task.Run(ForegroundApp.IsEditableFieldFocused);
                var analysis = _classifier.Classify(text);
                var groups = _actionRegistry.GetActions(text, analysis, ForegroundApp.GetActiveProcessName());
                if (groups.Count == 0) return;

                _toolbar ??= new ToolbarWindow();
                _toolbar.Registry = _actionRegistry;
                _toolbar.Show(text, analysis, groups, pt.X, pt.Y, isEditable);
            }
            catch (Exception ex)
            {
                SnapActions.Helpers.Log.Error("Ctrl+C capture handler", ex);
            }
        });
    }

    // Cheap PID check — no Process allocation, no string comparison
    private static bool IsSelfFocused()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == OwnPid;
    }

    // These handlers fire on the HOOK THREAD, not the UI thread.
    // Keep them minimal — just check and dispatch.

    private void OnMouseDown(MouseHook.POINT pt)
    {
        // Quick checks only — no WPF access from hook thread
        if (IsSelfFocused()) { _mouseHook.CancelTracking(); return; }

        // Sample the cursor shape now, while the press is on its target — see _mouseDownCursor.
        _mouseDownCursor = CursorShape.Classify();

        // Snapshot foreground HWND before the click-triggered action runs. The low-level mouse
        // hook fires before the OS dispatches WM_LBUTTONDOWN to the target, so any double-click
        // action (folder open, shortcut launch, file open) has NOT happened yet at this point.
        // See _foregroundAtMouseDown for why this matters for the paste-mode fallthrough.
        _foregroundAtMouseDown = GetForegroundWindow();

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_toolbar is { IsVisible: true } && !_toolbar.IsPointInside(pt.X, pt.Y))
                _toolbar.HideToolbar();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Selection-likely pipeline (gates, in order):
    /// <list type="number">
    ///   <item>MouseHook.LooksLikeScrollbarDrag + NCHITTEST gate (gesture-fire time) — rejects
    ///         perpendicular edge drags (custom scrollbars in Chrome/Electron/etc.) and gestures
    ///         that started on a title bar / border / native scrollbar.</item>
    ///   <item>This method's cursor gate — I-beam at press or release → full capture; a known
    ///         non-text system cursor at both → suppress; a custom (unclassifiable) cursor →
    ///         quiet capture, no synthetic keystrokes (see CursorShape.DecideCaptureAggressiveness).</item>
    ///   <item>This method's pre-checks: self-PID, debounce, Enabled, IsPointInside (toolbar
    ///         self-click), ExcludedApps.</item>
    ///   <item>TextCapture's probe-planned cascade (WM_COPY → UIA → Ctrl+Insert; see
    ///         TextCapture.DecidePlan for how the probe outcome, trigger, and aggressiveness
    ///         restrict it). Empty captured text aborts here — except when the trigger was a
    ///         multi-click AND <see cref="PasteModeTrigger.DoubleClick"/> is configured, in
    ///         which case empty text falls through to paste mode if the cursor is over an
    ///         editable input.</item>
    /// </list>
    /// Three UIA-based gates were added and removed across v1.6.5–1.6.12:
    ///   • atPointTask (mouse-up UIA) — removed v1.6.10, false-positive on whitespace endings
    ///   • IsForegroundTextCapable (focused-element UIA) — removed v1.6.10, browsers focus parent panes
    ///   • atDownTask (mouse-down UIA) — removed v1.6.12, blocks selections in apps with shallow UIA trees
    /// The lesson from those: UIA's TextPattern coverage is too inconsistent across apps to be
    /// a *required* gate (false negatives broke legitimate selections).
    /// TextCapture.ProbeSelectionViaUIA (the gate inside the pipeline below) avoids that trap:
    /// only a clearly non-text item element is a hard stop; an empty TextPattern merely
    /// restricts the cascade (some providers report empty despite a real selection — the
    /// lying-provider class), and everything ambiguous falls through to the clipboard path —
    /// so the historical false-negative apps (Java Swing, some Edge, custom Electron) work.
    /// Paste-mode triggers (long-press or double-click) still use IsTextInputAtPoint at the cursor —
    /// paste mode showing on a button or scrollbar is worse than the same false-positive cost there.
    /// </summary>
    private void OnSelectionLikely(MouseHook.POINT cursorPos, MouseHook.SelectionTrigger trigger)
    {
        if (IsSelfFocused()) return;

        // Cursor gate. The OS shows the text (I-beam) cursor only when the pointer is over
        // selectable text, so it's the most universal "was this gesture on text?" signal — more
        // reliable across apps than UIA TextPattern. I-beam at mouse-down (the press landed on
        // text) or right now (the gesture ended on text) → full capture; a positively-identified
        // non-text system cursor (arrow, hand, resize, …) at BOTH points → suppressed before any
        // clipboard or keystroke work; a custom cursor we can't classify → quiet capture only
        // (WM_COPY + UIA, never a synthetic keystroke), because some apps draw their own I-beam
        // and used to lose the toolbar entirely here. Unreadable cursors (touch, full-screen)
        // stay fully permissive. See CursorShape.DecideCaptureAggressiveness.
        // Checked before the debounce so a suppressed gesture doesn't burn it.
        var aggressiveness = CursorShape.DecideCaptureAggressiveness(_mouseDownCursor, CursorShape.Classify());
        if (aggressiveness == null)
        {
            SnapActions.Helpers.Log.Info($"Suppressed selection at ({cursorPos.X},{cursorPos.Y}): cursor was a known non-text shape at press and release");
            return;
        }

        long now = Environment.TickCount64;
        if (now - _lastShowTicks < DebounceMs) return;
        _lastShowTicks = now;

        // Capture into a local so the closure sees the value at *this* SelectionLikely fire,
        // not whatever a later click might overwrite it with while we're awaiting UIA.
        IntPtr foregroundAtClick = _foregroundAtMouseDown;

        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (!SettingsManager.Current.Enabled) return;
                if (_toolbar is { IsVisible: true } && _toolbar.IsPointInside(cursorPos.X, cursorPos.Y)) return;
                if (ForegroundApp.IsExcluded(SettingsManager.Current.ExcludedApps)) return;
                if (_toolbar?.IsVisible == true) _toolbar.HideToolbar();

                var editableTask = Task.Run(() => ForegroundApp.IsEditableFieldFocused());

                var text = await TextCapture.CaptureSelectedTextAsync(
                    isDrag: trigger == MouseHook.SelectionTrigger.Drag,
                    allowSyntheticKeys: aggressiveness == CaptureAggressiveness.Full);
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Double-click on an empty editable input is the configured paste-mode
                    // trigger — fall through to ShowPasteMode here so the user gets the menu
                    // without holding the button. Drag with no selection (also empty here) does
                    // NOT trigger paste mode: dragging on a non-text target shouldn't summon a
                    // paste menu over what the user was actually trying to drag.
                    //
                    // Two-layer suppression for the false-positive scenarios users have reported:
                    //   1. Foreground HWND unchanged. Double-clicking a shortcut / file launches
                    //      a new app whose initial focus is often an editable element (browser
                    //      search box, Outlook search, app login field). Without this check the
                    //      newly-launched app's editable focus would trigger paste mode after the
                    //      user clearly meant "open this thing".
                    //   2. Strictly editable focused. After in-window navigation (Explorer folder
                    //      open in same window), the HWND is unchanged but focus shifts to the
                    //      new content view (Pane/Custom — not Edit). IsStrictlyEditableFocused
                    //      excludes those AND the bare-TextPattern read-only documents that
                    //      caught us on Twitter (those expose TextPattern for selection but no
                    //      Edit / non-readonly ValuePattern, so they're rejected here).
                    if (trigger == MouseHook.SelectionTrigger.MultiClick
                        && SettingsManager.Current.PasteModeTrigger == Config.PasteModeTrigger.DoubleClick
                        && GetForegroundWindow() == foregroundAtClick
                        && await Task.Run(ForegroundApp.IsStrictlyEditableFocused))
                    {
                        _toolbar ??= new ToolbarWindow();
                        _toolbar.Registry = _actionRegistry;
                        _toolbar.ShowPasteMode(cursorPos.X, cursorPos.Y);
                    }
                    return;
                }

                int showDelay = SettingsManager.Current.ToolbarShowDelay;
                if (showDelay > 0) await Task.Delay(showDelay);

                bool isEditable = await editableTask;

                var analysis = _classifier.Classify(text);
                var groups = _actionRegistry.GetActions(text, analysis, ForegroundApp.GetActiveProcessName());
                if (groups.Count == 0) return;

                _toolbar ??= new ToolbarWindow();
                _toolbar.Registry = _actionRegistry;
                _toolbar.Show(text, analysis, groups, cursorPos.X, cursorPos.Y, isEditable);
            }
            catch (Exception ex)
            {
                SnapActions.Helpers.Log.Error("Selection-likely handler", ex);
            }
        });
    }

    private void OnLongPress(MouseHook.POINT cursorPos)
    {
        if (IsSelfFocused()) return;

        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (!SettingsManager.Current.Enabled) return;
                // Defense-in-depth: MouseHook also gates the long-press timer start on this
                // setting, but a setting change between mouse-down and timer-fire would slip
                // through. Re-check here so the in-flight press doesn't summon a menu the user
                // has since disabled.
                if (SettingsManager.Current.PasteModeTrigger != Config.PasteModeTrigger.LongPress) return;
                if (ForegroundApp.IsExcluded(SettingsManager.Current.ExcludedApps)) return;

                // Use the point-based check, not the focused-element check. Holding the mouse on
                // a Chrome/Electron title bar leaves whatever was previously focused (search box,
                // address bar) "focused" — IsTextInputFocused would return true and the paste
                // menu would pop up over the title bar. IsTextInputAtPoint asks UI Automation
                // what's literally under the cursor instead.
                if (!await Task.Run(() => ForegroundApp.IsTextInputAtPoint(cursorPos.X, cursorPos.Y)))
                {
                    SnapActions.Helpers.Log.Info($"Suppressed long-press: hold position ({cursorPos.X},{cursorPos.Y}) isn't a text element");
                    return;
                }

                if (_toolbar?.IsVisible == true) _toolbar.HideToolbar();

                _toolbar ??= new ToolbarWindow();
                _toolbar.Registry = _actionRegistry;
                _toolbar.ShowPasteMode(cursorPos.X, cursorPos.Y);
                _lastShowTicks = Environment.TickCount64;
            }
            catch (Exception ex)
            {
                SnapActions.Helpers.Log.Error("Paste-mode handler", ex);
            }
        });
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
