using SnapActions.Services;
using SnapActions.Config;
using System.Windows.Controls;
using SnapActions.Actions;
using SnapActions.Core;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using SnapActions.Helpers;

namespace SnapActions.UI;

public partial class ResultPopup : Window
{
    private string _resultText = "";

    private bool _closed;
    private SelectionSnapshot? _selection;
    private readonly OperationActionGate _applyGate = new();
    private double _dpi = 1.0;
    private double _screenX, _screenY;
    private string _title = "";
    private Func<System.Threading.CancellationToken, Task<LookupResult>>? _fetch;
    private System.Threading.CancellationTokenSource _cts = new();

    // Track the currently-open popup so a new translation/dictionary lookup replaces the prior
    // one instead of letting them stack on screen. Previously we relied on cursor-leave to
    // dismiss the prior popup, but that closed it before the user finished reading.
    private static ResultPopup? _current;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public ResultPopup()
    {
        InitializeComponent();

        // Don't steal focus from the user's app — match ToolbarWindow's no-activate behavior.
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // *Ptr variants — see ToolbarWindow.xaml.cs for the rationale.
            var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE,
                new IntPtr(style.ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        };

        // Esc dismissal goes through the global keyboard hook, click-outside through the global
        // mouse hook — both event-driven. The previous 120 ms polling timer could miss a fast
        // click entirely (typical click ≈ 85 ms) and added dismiss latency. WS_EX_NOACTIVATE
        // means we never get focus/capture events of our own, hence the global hooks.
        SnapActions.Core.KeyboardHook.EscPressed += OnGlobalEsc;
        SnapActions.Core.MouseHook.GlobalMouseDown += OnGlobalMouseDown;
        Closed += (_, _) => SafeClose();
    }

    private void OnGlobalMouseDown(SnapActions.Core.MouseHook.POINT pt)
    {
        // Hook fires on its own thread; marshal to UI before reading window bounds.
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_closed) return;
            if (MouseHook.IsProcessWindowAtPoint(pt, (uint)Environment.ProcessId)) return;
            double l = Left * _dpi, t = Top * _dpi;
            double r = l + ActualWidth * _dpi, b = t + ActualHeight * _dpi;
            if (pt.X < l || pt.X > r || pt.Y < t || pt.Y > b)
                SafeClose();
        });
    }

    private void OnGlobalEsc()
    {
        // Hook fires on its own thread; marshal to UI.
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (!_closed) SafeClose();
        });
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private void SafeClose()
    {
        if (_closed) return;
        _closed = true;
        _selection?.Operation.InvalidateIfCurrent();
        try { SnapActions.Core.KeyboardHook.EscPressed -= OnGlobalEsc; } catch { }
        try { SnapActions.Core.MouseHook.GlobalMouseDown -= OnGlobalMouseDown; } catch { }
        try { _cts.Cancel(); } catch { }
        try { _cts.Dispose(); } catch { }
        if (ReferenceEquals(_current, this)) _current = null;
        try { Close(); } catch { }
    }

    /// <summary>Static helper: creates popup, positions near cursor, fetches result.</summary>
    public static void ShowNearCursor(string title, Func<System.Threading.CancellationToken, Task<LookupResult>> fetchResult)
    {
        // _current is read/written from this method and SafeClose; both must run on the UI
        // dispatcher or the static-instance handoff is racy. Cheap to assert in DEBUG builds.
        System.Diagnostics.Debug.Assert(
            System.Windows.Application.Current?.Dispatcher.CheckAccess() ?? true,
            "ResultPopup.ShowNearCursor must run on the UI dispatcher");

        // Privacy gate: these popups send the user's selected text to a third-party web service.
        // Ask once (or whenever the user has turned online lookups off in Settings) before anything
        // leaves the machine.
        if (!EnsureOnlineLookupConsent()) return;

        // Replace any existing popup so two back-to-back lookups don't stack on screen.
        _current?.SafeClose();
        var popup = new ResultPopup();
        _current = popup;
        NativeMethods.GetCursorPos(out var pt);
        popup.ShowAt(pt.X, pt.Y, title, fetchResult);
    }

    private string _translationText = "";

    public static void ShowTranslation(string text)
    {
        if (!EnsureOnlineLookupConsent()) return;
        _current?.SafeClose();
        var popup = new ResultPopup();
        _current = popup;
        popup._translationText = text;
        popup.LanguagePanel.Visibility = Visibility.Visible;
        popup.SourceLanguage.ItemsSource = new[] { new LanguageOption("", "Choose source language") }.Concat(LanguageOptions.All);
        popup.TargetLanguage.ItemsSource = LanguageOptions.All;
        popup.SourceLanguage.SelectedValue = SettingsManager.Current.TranslationSourceLanguage;
        popup.TargetLanguage.SelectedValue = SettingsManager.Current.TranslationTargetLanguage;
        NativeMethods.GetCursorPos(out var pt);
        popup.ShowAt(pt.X, pt.Y, "Translate", popup.FetchTranslation);
    }

    private Task<LookupResult> FetchTranslation(System.Threading.CancellationToken ct) =>
        LookupService.Shared.Translate(_translationText, SourceLanguage.SelectedValue as string ?? "",
            TargetLanguage.SelectedValue as string ?? "en", ct);

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.TranslationSourceLanguage = SourceLanguage.SelectedValue as string ?? "";
        SettingsManager.Current.TranslationTargetLanguage = TargetLanguage.SelectedValue as string ?? "en";
        SettingsManager.Save();
        _cts.Cancel();
        _cts.Dispose();
        _cts = new();
        await RunFetchAsync();
    }

    private void SwapLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (SourceLanguage.SelectedValue is not string { Length: > 0 }) return;
        (SourceLanguage.SelectedValue, TargetLanguage.SelectedValue) = (TargetLanguage.SelectedValue, SourceLanguage.SelectedValue);
    }

    public async void ShowAt(double screenX, double screenY, string title,
        Func<System.Threading.CancellationToken, Task<LookupResult>> fetchResult)
    {
        // Async void — the caller (ShowNearCursor) treats this as fire-and-forget. Wrap the
        // whole body so a synchronous WPF exception during setup (rare but possible during
        // teardown) reaches the logger instead of escaping into the dispatcher's unhandled path.
        try
        {
            _screenX = screenX;
            _screenY = screenY;
            _title = title;
            _fetch = fetchResult;
            TitleText.Text = title;

            // Use the DPI of the monitor under the cursor, not whatever monitor the window starts on.
            var monitorDpi = ScreenHelper.GetDpiForPoint(new System.Windows.Point(screenX, screenY));
            _dpi = monitorDpi.X > 0 ? monitorDpi.X : 1.0;
            Show();
            ClampToScreen();
            // Topmost is already declared in XAML; no Activate() call so the user's app keeps focus.

            await RunFetchAsync();
        }
        catch (Exception ex)
        {
            // Setup-time exception (e.g. a stale window handle from a racing teardown). Don't
            // let it escape into the dispatcher unhandled handler.
            Log.Error("ResultPopup.ShowAt failed during setup", ex);
            try { SafeClose(); } catch { }
        }
    }

    /// <summary>
    /// (Re)compute Left/Top from the current window size, anchored above-left of the cursor and
    /// clamped to the working area. Called at show time and again after the result text grows the
    /// SizeToContent window, so a long definition/translation near a screen edge doesn't overflow
    /// off-screen.
    /// </summary>
    private void ClampToScreen()
    {
        UpdateLayout();
        var sb = ScreenHelper.GetScreenBounds(new System.Windows.Point(_screenX, _screenY));
        double sL = sb.Left / _dpi, sT = sb.Top / _dpi;
        double sR = sb.Right / _dpi, sB = sb.Bottom / _dpi;

        double w = ActualWidth > 10 ? ActualWidth : 220;
        double h = ActualHeight > 10 ? ActualHeight : 120;

        double left = (_screenX / _dpi) - 100;
        double top = (_screenY / _dpi) - 80;
        if (left < sL + 8) left = sL + 8;
        if (left + w > sR - 8) left = sR - 8 - w;
        if (top < sT + 8) top = sT + 8;
        if (top + h > sB - 8) top = sB - 8 - h;

        Left = left;
        Top = top;
    }

    /// <summary>Runs the stored fetch delegate and renders loading / result / problem states.</summary>
    internal async Task RunFetchAsync()
    {
        if (_fetch == null) return;
        var request = _cts;
        _resultText = "";
        LoadingText.Text = "Loading...";
        LoadingText.Visibility = Visibility.Visible;
        ResultText.Visibility = CopyButton.Visibility = RetryButton.Visibility = Visibility.Collapsed;
        var outcome = await LookupExecution.RunAsync(_fetch, request.Token);
        if (_closed || !ReferenceEquals(request, _cts) || outcome.Status == LookupStatus.Cancelled) return;
        LoadingText.Visibility = Visibility.Collapsed;
        ResultText.Text = outcome.Text;
        ResultText.FlowDirection = ToolbarWindow.GetPreviewFlowDirection(outcome.Text);
        ResultText.Visibility = Visibility.Visible;
        bool success = outcome.Status == LookupStatus.Success;
        _resultText = success ? outcome.Text : "";
        CopyButton.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = outcome.Status == LookupStatus.Error ? Visibility.Visible : Visibility.Collapsed;
        ClampToScreen();
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        // Fresh token for the new attempt — the previous one may have been canceled.
        try { _cts.Cancel(); _cts.Dispose(); } catch { }
        _cts = new System.Threading.CancellationTokenSource();
        await RunFetchAsync();
    }

    /// <summary>
    /// First-use (and whenever the user has turned it off) consent for sending the selected text to
    /// the third-party lookup services. Returns false if the user declines, in which case the popup
    /// is not shown and nothing is sent.
    /// </summary>
    private static bool EnsureOnlineLookupConsent()
    {
        if (Config.SettingsManager.Current.AllowOnlineLookups) return true;
        var msg = "Some actions send your selected text to a third-party online service over HTTPS " +
                  "to fetch a result: the built-in Translate, Dictionary, and Currency lookups " +
                  "(MyMemory, dictionaryapi.dev, open.er-api.com), and any custom \"fetch\" actions " +
                  "you add (which send to the host in their own URL).\n\nAllow these online lookups? " +
                  "You can turn this back off in Settings.";
        var answer = System.Windows.MessageBox.Show(msg, "Allow online lookups?",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No, System.Windows.MessageBoxOptions.DefaultDesktopOnly);
        if (answer != System.Windows.MessageBoxResult.Yes) return false;
        Config.SettingsManager.Current.AllowOnlineLookups = true;
        Config.SettingsManager.Save();
        return true;
    }

    internal static void ShowActionResult(string title, string result, SelectionSnapshot selection)
    {
        _current?.SafeClose();
        var popup = new ResultPopup { _selection = selection };
        _current = popup;
        popup.SourceText.Text = selection.Text.Length > 240 ? selection.Text[..240] + "…" : selection.Text;
        popup.SourceText.FlowDirection = ToolbarWindow.GetPreviewFlowDirection(selection.Text);
        popup.SourceText.Visibility = Visibility.Visible;
        popup.ReplaceButton.Visibility = selection.CanReplace ? Visibility.Visible : Visibility.Collapsed;
        NativeMethods.GetCursorPos(out var pt);
        popup.ShowAt(pt.X, pt.Y, title, _ => Task.FromResult(new LookupResult(LookupStatus.Success, result)));
    }

    internal static void ShowLocalResult(string title, string text)
    {
        _current?.SafeClose();
        var popup = new ResultPopup();
        _current = popup;
        NativeMethods.GetCursorPos(out var pt);
        popup.ShowAt(pt.X, pt.Y, title, _ => Task.FromResult(LookupResult.Success(text)));
    }

    private async void Replace_Click(object sender, RoutedEventArgs e) => await ApplyResultAsync(ResultDestination.Replace);

    private async Task ApplyResultAsync(ResultDestination destination)
    {
        if (_selection == null || !_applyGate.TryStart()) return;
        CopyButton.IsEnabled = ReplaceButton.IsEnabled = false;
        var result = await ActionRunner.ApplyTextAsync(_resultText, _selection, destination);
        if (_closed) return;
        if (result.Success) { SafeClose(); return; }
        LoadingText.Text = result.Message;
        LoadingText.Visibility = Visibility.Visible;
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_selection != null) { await ApplyResultAsync(ResultDestination.Copy); return; }
        if (!string.IsNullOrEmpty(_resultText))
        {
            try { Clipboard.SetText(_resultText); }
            catch
            {
                LoadingText.Text = "The clipboard is busy. Try Copy again.";
                LoadingText.Visibility = Visibility.Visible;
                return;
            }
        }
        SafeClose();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SafeClose();

}
