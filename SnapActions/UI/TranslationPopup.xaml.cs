using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SnapActions.Config;
using SnapActions.Core;
using SnapActions.Helpers;
using SnapActions.Services;

namespace SnapActions.UI;

/// <summary>A visible translation website hosted in a native popup, with no external browser handoff.</summary>
public partial class TranslationPopup : Window
{
    private static TranslationPopup? _current;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _pageDeadline = new() { Interval = TimeSpan.FromSeconds(10) };
    private WebView2? _browser;
    private CancellationTokenSource? _attemptCancellation;
    private Uri _retryUri = null!;
    private int _attempt;
    private ulong _navigation;
    private bool _closed, _pageFailed;

    public TranslationPopup()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            // The borderless WPF surface can disappear beside WebView2 on some display drivers.
            // Render only this small native frame in software; WebView2 keeps its own renderer.
            if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source)
                source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
        };
        KeyboardHook.EscPressed += OnGlobalEsc;
        MouseHook.GlobalMouseDown += OnGlobalMouseDown;
        Closed += (_, _) => DisposeState();
        _pageDeadline.Tick += (_, _) =>
        {
            _pageDeadline.Stop();
            if (_closed) return;
            _pageFailed = true;
            _browser?.CoreWebView2?.Stop();
            ShowFailure("Google Translate took too long to open. Try again.");
        };
    }

    public static void ShowNearCursor(string text)
    {
        if (!TranslationPage.CanTranslate(text) || !ResultPopup.EnsureOnlineLookupConsent()) return;
        CloseCurrent();
        ResultPopup.CloseCurrent();
        var popup = new TranslationPopup
        {
            _retryUri = TranslationPage.BuildUri(text, SettingsManager.Current.TranslationSourceLanguage,
                SettingsManager.Current.TranslationTargetLanguage)
        };
        _current = popup;
        NativeMethods.GetCursorPos(out var point);
        var position = new Point(point.X, point.Y);
        var bounds = ScreenHelper.GetScreenBounds(position);
        var dpi = ScreenHelper.GetDpiForPoint(position);
        double scale = dpi.X > 0 ? dpi.X : 1;
        popup.MaxWidth = Math.Max(100, bounds.Width / scale - 16);
        popup.MaxHeight = Math.Max(100, bounds.Height / scale - 16);
        popup.MinWidth = Math.Min(popup.MinWidth, popup.MaxWidth);
        popup.MinHeight = Math.Min(popup.MinHeight, popup.MaxHeight);
        popup.Width = Math.Min(popup.Width, popup.MaxWidth);
        popup.Height = Math.Min(popup.Height, popup.MaxHeight);
        popup.Left = Math.Clamp(point.X / scale - 100, bounds.Left / scale + 8, bounds.Right / scale - popup.Width - 8);
        popup.Top = Math.Clamp(point.Y / scale - 80, bounds.Top / scale + 8, bounds.Bottom / scale - popup.Height - 8);
        popup.Show();
        _ = popup.LoadPageAsync();
    }

    internal static void CloseCurrent() => _current?.Close();

    private async Task LoadPageAsync()
    {
        if (_closed) return;
        var attempt = ++_attempt;
        _pageDeadline.Stop();
        _attemptCancellation?.Cancel();
        _attemptCancellation?.Dispose();
        _attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var cancellation = _attemptCancellation.Token;
        DisposeBrowser();
        var browser = new WebView2();
        _browser = browser;
        BrowserHost.Children.Add(browser);
        BrowserHost.Visibility = Visibility.Visible;
        StatusText.Text = "Opening Google Translate…";
        StatusPanel.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(RuntimePaths.DataDirectory, "TranslationBrowser"))
                .WaitAsync(TimeSpan.FromSeconds(10), cancellation);
            if (!IsCurrent(attempt, browser)) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = "Translation";
            options.IsInPrivateModeEnabled = true;
            await browser.EnsureCoreWebView2Async(environment, options).WaitAsync(TimeSpan.FromSeconds(10), cancellation);
            if (!IsCurrent(attempt, browser)) return;
            var core = browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.ProcessFailed += (_, _) =>
            {
                if (IsCurrent(attempt, browser)) ShowFailure("The translation window stopped responding. Try again.");
            };
            core.NavigationStarting += (_, e) =>
            {
                if (!IsCurrent(attempt, browser) || !TranslationPage.IsAllowedNavigation(e.Uri))
                {
                    e.Cancel = true;
                    return;
                }
                if (_navigation != e.NavigationId)
                {
                    _navigation = e.NavigationId;
                    _pageFailed = false;
                    _pageDeadline.Stop();
                    _pageDeadline.Start();
                }
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (!IsCurrent(attempt, browser) || e.NavigationId != _navigation || _pageFailed) return;
                _pageDeadline.Stop();
                if (!e.IsSuccess || e.HttpStatusCode >= 400)
                    ShowFailure("Google Translate couldn't open. Check your connection and try again.");
                else
                {
                    StatusPanel.Visibility = Visibility.Collapsed;
                    BrowserHost.Visibility = Visibility.Visible;
                }
            };
            core.SourceChanged += (_, _) =>
            {
                if (!IsCurrent(attempt, browser) || !TranslationPage.IsAllowedNavigation(core.Source)) return;
                if (new Uri(core.Source).Host != "translate.google.com") return;
                _retryUri = new Uri(core.Source);
                if (!TranslationPage.TryReadLanguages(core.Source, out var source, out var target)) return;
                var settings = SettingsManager.Current;
                if (settings.TranslationSourceLanguage == source && settings.TranslationTargetLanguage == target) return;
                settings.TranslationSourceLanguage = source;
                settings.TranslationTargetLanguage = target;
                SettingsManager.Save();
            };
            _navigation = 0;
            core.Navigate(_retryUri.AbsoluteUri);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsCurrent(attempt, browser)) return;
            // Exception messages can include navigation URLs. Log only the type, never selected text.
            Log.Warn($"Translation browser failed ({ex.GetType().Name})");
            ShowFailure(ex is WebView2RuntimeNotFoundException
                ? "Microsoft Edge WebView2 Runtime is needed for translation. Install or repair it, then select Retry."
                : ex is TimeoutException ? "The translation window took too long to start. Try again."
                : "The translation window couldn't start. Try again.");
            DisposeBrowser();
        }
    }

    private bool IsCurrent(int attempt, WebView2 browser) => !_closed && attempt == _attempt && ReferenceEquals(_browser, browser);

    private void ShowFailure(string message)
    {
        _pageFailed = true;
        _pageDeadline.Stop();
        StatusText.Text = message;
        StatusPanel.Visibility = RetryButton.Visibility = Visibility.Visible;
        BrowserHost.Visibility = Visibility.Collapsed;
    }

    private void DisposeBrowser()
    {
        var browser = _browser;
        _browser = null;
        BrowserHost.Children.Clear();
        browser?.Dispose();
    }

    private void DisposeState()
    {
        if (_closed) return;
        _closed = true;
        _pageDeadline.Stop();
        KeyboardHook.EscPressed -= OnGlobalEsc;
        MouseHook.GlobalMouseDown -= OnGlobalMouseDown;
        _lifetime.Cancel();
        _attemptCancellation?.Dispose();
        _lifetime.Dispose();
        DisposeBrowser();
        if (ReferenceEquals(_current, this)) _current = null;
    }

    private void OnGlobalEsc() => Dispatcher.InvokeAsync(() => { if (!_closed) Close(); });

    private void OnGlobalMouseDown(MouseHook.POINT point) => Dispatcher.InvokeAsync(() =>
    {
        if (_closed) return;
        var handle = new WindowInteropHelper(this).Handle;
        // WebView child HWNDs can belong to another process. Physical window bounds still include them.
        if (GetWindowRect(handle, out var bounds) && point.X >= bounds.Left && point.X < bounds.Right
            && point.Y >= bounds.Top && point.Y < bounds.Bottom) return;
        if (IsChild(handle, WindowFromPoint(point))) return;
        Close();
    });

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void Retry_Click(object sender, RoutedEventArgs e) => await LoadPageAsync();

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowBounds bounds);
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(MouseHook.POINT point);
    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr child);
}
