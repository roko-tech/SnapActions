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
    // Most recent mouse-down position, packed atomically: high 32 bits = X, low 32 bits = Y as
    // uint bit pattern (preserves negative coords from monitors left/above the primary). Single
    // long via Interlocked so the UI-dispatcher reader can never see a torn (Xₙ, Yₙ₊₁) pair from
    // a concurrent hook-thread write.
    private long _lastMouseDownPacked;
    private const long DebounceMs = 250;
    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    public SelectionTracker()
    {
        _mouseHook = new MouseHook();
        _classifier = new TextClassifier();
        _actionRegistry = new ActionRegistry();
        _mouseHook.SelectionLikely += OnSelectionLikely;
        _mouseHook.LongPress += OnLongPress;
        _mouseHook.MouseDown += OnMouseDown;
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
        _mouseHook.Uninstall();
        _mouseHook.Dispose();
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

        // Capture for the OnSelectionLikely mouse-down UIA gate. Pack into one long via
        // Interlocked.Exchange so the UI-thread reader can never observe a torn pair.
        System.Threading.Interlocked.Exchange(
            ref _lastMouseDownPacked, ((long)pt.X << 32) | (uint)pt.Y);

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_toolbar is { IsVisible: true } && !_toolbar.IsPointInside(pt.X, pt.Y))
                _toolbar.HideToolbar();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Selection-likely pipeline (gates, in order):
    /// <list type="number">
    ///   <item>MouseHook.NCHITTEST gate (mouse-down) — rejects clicks on title bars / borders /
    ///         native scrollbars before tracking even starts.</item>
    ///   <item>MouseHook.LooksLikeScrollbarDrag (mouse-up) — rejects perpendicular drags along
    ///         the right/left/bottom edges (custom scrollbars in Chrome/Electron/etc.).</item>
    ///   <item>This method's pre-checks: self-PID, debounce, Enabled, IsPointInside (toolbar
    ///         self-click), ExcludedApps.</item>
    ///   <item>TextCapture.WM_COPY then unconditional Ctrl+Insert fallback if WM_COPY returned
    ///         empty. v1.6.7–1.6.9 had a focused-element gate before the synthetic key send;
    ///         removed in v1.6.10 because browsers/Electron focus parent panes that don't
    ///         themselves expose TextPattern, and the gate blocked the common case.</item>
    ///   <item>IsTextInputAtPoint(mouse-down) — rejects when the drag *started* on a non-text
    ///         element (catches drag-and-drop and object drags). The mouse-UP equivalent that
    ///         existed in v1.6.5–1.6.9 was removed in v1.6.10 for over-suppression.</item>
    /// </list>
    /// LongPress has a parallel pipeline: NCHITTEST + LooksLikeScrollbarPosition in MouseHook,
    /// then IsTextInputAtPoint at the cursor in OnLongPress.
    /// </summary>
    private void OnSelectionLikely(MouseHook.POINT cursorPos)
    {
        if (IsSelfFocused()) return;

        long now = Environment.TickCount64;
        if (now - _lastShowTicks < DebounceMs) return;
        _lastShowTicks = now;

        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (!SettingsManager.Current.Enabled) return;
                if (_toolbar is { IsVisible: true } && _toolbar.IsPointInside(cursorPos.X, cursorPos.Y)) return;
                if (ForegroundApp.IsExcluded(SettingsManager.Current.ExcludedApps)) return;
                if (_toolbar?.IsVisible == true) _toolbar.HideToolbar();

                var editableTask = Task.Run(() => ForegroundApp.IsEditableFieldFocused());
                // Mouse-down position gate. Text selections always start on a text element by
                // definition; file/icon/object drags start on non-text elements (file icons,
                // Trello cards, panel handles, etc.). The mouse-UP equivalent (added in v1.6.5)
                // was removed in v1.6.10 — it caused false positives whenever a real text
                // selection ended on whitespace/padding/non-text element under the cursor, AND
                // its main intended target (custom-chrome title-bar drags) is already covered by
                // this mouse-DOWN check (the title-bar drag has to start on the title bar too).
                long packed = System.Threading.Interlocked.Read(ref _lastMouseDownPacked);
                int downX = (int)(packed >> 32);
                int downY = (int)packed;
                var atDownTask = Task.Run(() => ForegroundApp.IsTextInputAtPoint(downX, downY));

                var text = await TextCapture.CaptureSelectedTextAsync();
                if (string.IsNullOrWhiteSpace(text)) return;

                if (!await atDownTask)
                {
                    SnapActions.Helpers.Log.Info($"Suppressed: mouse-down position ({downX},{downY}) wasn't a text element (likely drag-and-drop or object drag)");
                    return;
                }

                int showDelay = SettingsManager.Current.ToolbarShowDelay;
                if (showDelay > 0) await Task.Delay(showDelay);

                bool isEditable = await editableTask;

                var analysis = _classifier.Classify(text);
                var groups = _actionRegistry.GetActions(text, analysis);
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
