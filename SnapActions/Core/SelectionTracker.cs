using System.Diagnostics;
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

    private bool IsClickOnToolbar(MouseHook.POINT pt) =>
        _toolbar is { IsVisible: true } && _toolbar.IsPointInside(pt.X, pt.Y);

    private void OnMouseDown(MouseHook.POINT pt)
    {
        if (IsClickOnToolbar(pt))
        {
            _mouseHook.CancelTracking();
            return;
        }

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_toolbar is { IsVisible: true } && !_toolbar.IsPointInside(pt.X, pt.Y))
                _toolbar.HideToolbar();
        }, DispatcherPriority.Background);
    }

    private void OnSelectionLikely(MouseHook.POINT cursorPos)
    {
        if (IsClickOnToolbar(cursorPos)) return;

        var now = DateTime.UtcNow;
        if ((now - _lastShowTime).TotalMilliseconds < DebounceMs) return;
        _lastShowTime = now;

        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (!SettingsManager.Current.Enabled) return;
                if (ForegroundApp.IsExcluded(SettingsManager.Current.ExcludedApps)) return;
                if (_toolbar?.IsVisible == true) _toolbar.HideToolbar();

                // Run editable check in parallel with text capture (background thread)
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
                Trace.WriteLine($"[SnapActions] ERROR: {ex.Message}");
            }
        });
    }

    private void OnLongPress(MouseHook.POINT cursorPos)
    {
        if (IsClickOnToolbar(cursorPos))
        {
            Trace.WriteLine("[SnapActions] LongPress: on toolbar, skipped");
            return;
        }

        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (!SettingsManager.Current.Enabled) return;
                if (ForegroundApp.IsExcluded(SettingsManager.Current.ExcludedApps)) return;
                if (_toolbar?.IsVisible == true) _toolbar.HideToolbar();

                _toolbar ??= new ToolbarWindow();
                _toolbar.Registry = _actionRegistry;
                Trace.WriteLine($"[SnapActions] LongPress: showing at ({cursorPos.X},{cursorPos.Y})");
                _toolbar.ShowPasteMode(cursorPos.X, cursorPos.Y);
                _lastShowTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[SnapActions] Paste error: {ex.Message}");
            }
        });
    }
}
