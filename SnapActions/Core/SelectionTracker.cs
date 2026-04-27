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
    private DateTime _lastShowTime = DateTime.MinValue;
    private const int DebounceMs = 250;
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

    private void OnSelectionLikely(MouseHook.POINT cursorPos)
    {
        if (IsSelfFocused()) return;

        var now = DateTime.UtcNow;
        if ((now - _lastShowTime).TotalMilliseconds < DebounceMs) return;
        _lastShowTime = now;

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

                if (!await Task.Run(() => ForegroundApp.IsTextInputFocused())) return;

                if (_toolbar?.IsVisible == true) _toolbar.HideToolbar();

                _toolbar ??= new ToolbarWindow();
                _toolbar.Registry = _actionRegistry;
                _toolbar.ShowPasteMode(cursorPos.X, cursorPos.Y);
                _lastShowTime = DateTime.UtcNow;
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
