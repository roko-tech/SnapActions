using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SnapActions.Actions;
using SnapActions.Config;
using SnapActions.Core;
using SnapActions.Helpers;
using SnapActions.Services;

namespace SnapActions.UI;

/// <summary>
/// A translation card near the selection. Google Translate loads in an embedded page the user does
/// not see and its result is read into the card; on request, or when the result can't be read, the
/// popup shows that page instead. No external browser handoff.
/// </summary>
public partial class TranslationPopup : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly TimeSpan ResultDeadline = TimeSpan.FromSeconds(6);

    private static TranslationPopup? _current;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _pageDeadline = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _resultPoll = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly OperationActionGate _applyGate = new();
    private SelectionSnapshot? _selection;
    private WebView2? _browser;
    private CancellationTokenSource? _attemptCancellation;
    private Uri _retryUri = null!;
    private Rect _workArea = Rect.Empty;
    private DateTime _resultDue;
    private string _text = "", _source = "", _target = "en", _result = "";
    private string? _lastRead;
    private int _attempt;
    private ulong _navigation;
    private bool _closed, _pageFailed, _pageMode, _reading;

    public TranslationPopup()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            // The borderless WPF surface can disappear beside WebView2 on some display drivers.
            // Render only this small native frame in software; WebView2 keeps its own renderer.
            if (HwndSource.FromHwnd(handle) is { } source)
                source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            // Like the other result popups, the card never takes focus from the user's app, so
            // Replace can paste back into it.
            SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64()
                | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
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
        _resultPoll.Tick += async (_, _) => await ReadResultAsync();
        ParkBrowser();
    }

    internal static bool ShowNearCursor(string text, SelectionSnapshot? selection = null)
    {
        if (!TranslationPage.CanTranslate(text) || !ResultPopup.EnsureOnlineLookupConsent()) return false;
        CloseCurrent();
        ResultPopup.CloseCurrent();
        var settings = SettingsManager.Current;
        var popup = new TranslationPopup
        {
            _text = text, _selection = selection,
            _source = settings.TranslationSourceLanguage, _target = settings.TranslationTargetLanguage
        };
        popup._retryUri = TranslationPage.BuildUri(text.Trim(), popup._source, popup._target);
        _current = popup;
        NativeMethods.GetCursorPos(out var point);
        var position = new Point(point.X, point.Y);
        var bounds = ScreenHelper.GetScreenBounds(position);
        var dpi = ScreenHelper.GetDpiForPoint(position);
        double scale = dpi.X > 0 ? dpi.X : 1;
        popup._workArea = new Rect(bounds.Left / scale + 8, bounds.Top / scale + 8,
            Math.Max(100, bounds.Width / scale - 16), Math.Max(100, bounds.Height / scale - 16));
        popup.MaxWidth = popup._workArea.Width;
        popup.MaxHeight = popup._workArea.Height;
        popup.Width = Math.Min(popup.Width, popup.MaxWidth);
        popup.Left = point.X / scale - 100;
        popup.Top = point.Y / scale - 80;
        popup.UpdateTitle();
        popup.Show();
        popup.KeepOnScreen();
        _ = popup.LoadPageAsync();
        return true;
    }

    internal static void CloseCurrent() => _current?.Close();

    private async Task LoadPageAsync()
    {
        if (_closed) return;
        var attempt = ++_attempt;
        _pageDeadline.Stop();
        _resultPoll.Stop();
        _attemptCancellation?.Cancel();
        _attemptCancellation?.Dispose();
        _attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var cancellation = _attemptCancellation.Token;
        DisposeBrowser();
        var browser = new WebView2 { ZoomFactor = 0.8 };
        _browser = browser;
        ParkBrowser();
        BrowserHost.Children.Add(browser);
        BrowserHost.Visibility = Visibility.Visible;
        ShowStatus(_pageMode ? "Opening Google Translate…" : "Translating…");
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
                else if (_pageMode || new Uri(core.Source).Host != "translate.google.com")
                    ShowPage(); // e.g. a consent page, which needs the user
                else
                {
                    _lastRead = null;
                    _resultDue = DateTime.UtcNow + ResultDeadline;
                    _resultPoll.Start();
                }
            };
            core.SourceChanged += (_, _) =>
            {
                if (!IsCurrent(attempt, browser) || !TranslationPage.IsAllowedNavigation(core.Source)) return;
                if (new Uri(core.Source).Host != "translate.google.com") return;
                _retryUri = new Uri(core.Source);
                if (!TranslationPage.TryReadLanguages(core.Source, out var source, out var target)) return;
                (_source, _target) = (source, target);
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

    /// <summary>Takes Google's translation once two reads agree; shows the page if none arrives in time.</summary>
    private async Task ReadResultAsync()
    {
        if (_reading || _closed || _pageMode || _pageFailed || _browser?.CoreWebView2 is not { } core) return;
        var browser = _browser;
        var navigation = _navigation;
        _reading = true;
        try
        {
            var text = TranslationPage.ReadResult(await core.ExecuteScriptAsync(TranslationPage.ReadResultScript));
            if (_closed || _pageMode || _pageFailed || !ReferenceEquals(browser, _browser) || navigation != _navigation
                || !_resultPoll.IsEnabled) return;
            if (text != null && text == _lastRead)
            {
                _resultPoll.Stop();
                ShowResult(text);
            }
            else if (DateTime.UtcNow >= _resultDue)
            {
                // Google changed its page or is asking the user something; let them see it.
                Log.Info("Translation result was not readable in time; showing the Google Translate page");
                ShowPage();
            }
            else _lastRead = text;
        }
        catch (Exception ex)
        {
            if (_closed || !ReferenceEquals(browser, _browser) || navigation != _navigation) return;
            Log.Warn($"Reading the translation failed ({ex.GetType().Name}); showing the Google Translate page");
            ShowPage();
        }
        finally { _reading = false; }
    }

    private void UpdateTitle()
    {
        TitleText.Text = $"{TranslationPage.LanguageName(_source)} → {TranslationPage.LanguageName(_target)}";
        SwapButton.Visibility = _source == "" ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowStatus(string message)
    {
        _result = "";
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        ResultScroller.Visibility = CopyButton.Visibility = ReplaceButton.Visibility = RetryButton.Visibility = Visibility.Collapsed;
        OpenPageButton.Visibility = _pageMode ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowResult(string text)
    {
        _result = text;
        StatusText.Visibility = Visibility.Collapsed;
        ResultText.Text = text;
        ResultText.FlowDirection = ToolbarWindow.GetPreviewFlowDirection(text);
        ResultScroller.Visibility = CopyButton.Visibility = Visibility.Visible;
        ReplaceButton.Visibility = _selection?.CanReplace == true ? Visibility.Visible : Visibility.Collapsed;
        KeepOnScreen();
    }

    private void ShowFailure(string message)
    {
        _pageFailed = true;
        _pageDeadline.Stop();
        _resultPoll.Stop();
        ShowStatus(message);
        RetryButton.Visibility = Visibility.Visible;
        OpenPageButton.Visibility = PageArea.Visibility = BrowserHost.Visibility = Visibility.Collapsed;
        CardPanel.Visibility = Visibility.Visible;
        KeepOnScreen();
    }

    private void ShowPage()
    {
        _resultPoll.Stop();
        if (!_pageMode)
        {
            _pageMode = true;
            // Google's page needs keyboard focus for editing, so this view may activate.
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64() & ~WS_EX_NOACTIVATE));
            SizeToContent = SizeToContent.Manual;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = Math.Min(360, MaxWidth);
            MinHeight = Math.Min(320, MaxHeight);
            Width = Math.Min(440, MaxWidth);
            Height = Math.Min(440, MaxHeight);
            TitleText.Text = "Google Translate";
            SwapButton.Visibility = Visibility.Collapsed;
        }
        CardPanel.Visibility = Visibility.Collapsed;
        PageArea.Visibility = BrowserHost.Visibility = Visibility.Visible;
        KeepOnScreen();
        PlaceBrowser();
    }

    // Card mode keeps the page out of view; see the note in the XAML.
    private void ParkBrowser()
    {
        Canvas.SetLeft(BrowserHost, 0);
        Canvas.SetTop(BrowserHost, SystemParameters.VirtualScreenHeight + 100);
        BrowserHost.Width = 520;
        BrowserHost.Height = 440;
    }

    private void PlaceBrowser()
    {
        if (!_pageMode || PageArea.Visibility != Visibility.Visible || PageArea.ActualWidth <= 0) return;
        var origin = PageArea.TranslatePoint(new Point(), BrowserLayer);
        Canvas.SetLeft(BrowserHost, origin.X);
        Canvas.SetTop(BrowserHost, origin.Y);
        BrowserHost.Width = PageArea.ActualWidth;
        BrowserHost.Height = PageArea.ActualHeight;
    }

    private void PageArea_SizeChanged(object sender, SizeChangedEventArgs e) => PlaceBrowser();

    private void KeepOnScreen()
    {
        if (_workArea.IsEmpty || !IsVisible) return;
        UpdateLayout();
        Left = Math.Clamp(Left, _workArea.Left, Math.Max(_workArea.Left, _workArea.Right - ActualWidth));
        Top = Math.Clamp(Top, _workArea.Top, Math.Max(_workArea.Top, _workArea.Bottom - ActualHeight));
    }

    private async void Swap_Click(object sender, RoutedEventArgs e)
    {
        if (_pageMode || _source == "") return;
        (_source, _target) = (_target, _source);
        var settings = SettingsManager.Current;
        settings.TranslationSourceLanguage = _source;
        settings.TranslationTargetLanguage = _target;
        SettingsManager.Save();
        UpdateTitle();
        _retryUri = TranslationPage.BuildUri(_text.Trim(), _source, _target);
        if (_browser?.CoreWebView2 is { } core && !_pageFailed)
        {
            _resultPoll.Stop();
            ShowStatus("Translating…");
            KeepOnScreen();
            core.Navigate(_retryUri.AbsoluteUri);
        }
        else await LoadPageAsync();
    }

    private void OpenPage_Click(object sender, RoutedEventArgs e) => ShowPage();

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_selection != null) { await ApplyResultAsync(ResultDestination.Copy); return; }
        if (!ActionRunner.TryCopy(_result)) { ShowNotice("The clipboard is busy. Try Copy again."); return; }
        Close();
    }

    private async void Replace_Click(object sender, RoutedEventArgs e) => await ApplyResultAsync(ResultDestination.Replace);

    private async Task ApplyResultAsync(ResultDestination destination)
    {
        if (_selection == null || _result == "" || !_applyGate.TryStart()) return;
        CopyButton.IsEnabled = ReplaceButton.IsEnabled = false;
        var text = destination == ResultDestination.Replace ? TranslationPage.WithOuterWhitespace(_text, _result) : _result;
        var outcome = await ActionRunner.ApplyTextAsync(text, _selection, destination);
        if (_closed) return;
        if (outcome.Success) { Close(); return; }
        if (outcome.CanRetry && await _selection.Operation.CanUseSelectionAsync())
        {
            if (_closed) return;
            _applyGate.AllowRetry();
            CopyButton.IsEnabled = true;
            ReplaceButton.IsEnabled = _selection.CanReplace;
        }
        ShowNotice(outcome.Message ?? "The action could not be completed.");
    }

    // Shown above a translation without hiding it.
    private void ShowNotice(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        KeepOnScreen();
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
        _resultPoll.Stop();
        KeyboardHook.EscPressed -= OnGlobalEsc;
        MouseHook.GlobalMouseDown -= OnGlobalMouseDown;
        _selection?.Operation.InvalidateIfCurrent();
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
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
