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
    ///         empty. Empty captured text aborts here.</item>
    /// </list>
    /// Three UIA-based gates have been added and removed across v1.6.5–1.6.12:
    ///   • atPointTask (mouse-up UIA) — removed v1.6.10, false-positive on whitespace endings
    ///   • IsForegroundTextCapable (focused-element UIA) — removed v1.6.10, browsers focus parent panes
    ///   • atDownTask (mouse-down UIA) — removed v1.6.12, blocks selections in apps with shallow UIA trees
    /// The lesson: UIA's TextPattern coverage is too inconsistent across apps to be a reliable gate.
    /// Drag-and-drop / object-drag false-positives now fall back to the user's ExcludedApps list.
    /// LongPress still uses IsTextInputAtPoint at the cursor — paste mode showing on a button or
    /// scrollbar is worse than the same false-positive cost there.
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

                var text = await TextCapture.CaptureSelectedTextAsync();
                if (string.IsNullOrWhiteSpace(text)) return;

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
