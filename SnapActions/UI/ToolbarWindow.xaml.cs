using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SnapActions.Actions;
using SnapActions.Core;
using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.UI;

// This file is the toolbar window's shell — lifecycle, positioning, dismiss, type badge,
// and the simple top-level button handlers. Heavier concerns are split into:
//   - ToolbarWindow.Buttons.cs  : dynamic button creation (context, pinned, sub-menu, drag-drop)
//   - ToolbarWindow.Preview.cs  : hover-preview band, "Copied!" toast, failure UI
//   - ToolbarWindow.Actions.cs  : action execution, edit-mode toggles, sub-menu navigation
public partial class ToolbarWindow : Window
{
    private string _selectedText = "";
    private TextAnalysis _analysis = TextAnalysis.PlainText;
    private List<ActionGroup> _actionGroups = [];
    private readonly DispatcherTimer _dismissTimer;
    private double _dpiX = 1.0, _dpiY = 1.0;
    private bool _isEditable;
    private bool _isPasteMode;

    // Edit mode for action toggles
    private bool _editMode;
    private string? _currentSubMenuGroup;
    private ActionCategory? _currentSubMenuCategory;
    // True when the sub-menu popup is open just to host the hover-preview band (no submenu items
    // populated, no title). Reset whenever the popup is closed or repurposed for a real submenu.
    private bool _hoverPreviewMode;

    // Bumped on every (re)show. Async post-delay continuations (the "Copied!"/failure toasts and
    // the delayed clipboard-restore) capture this before awaiting and bail if it changed — without
    // it, a delay that fires AFTER a new selection reshowed the singleton toolbar would tear down
    // the freshly-shown bar (or restore a now-stale clipboard).
    private int _generation;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public ActionRegistry? Registry { get; set; }

    public ToolbarWindow()
    {
        InitializeComponent();
        _dismissTimer = new DispatcherTimer();
        _dismissTimer.Tick += OnDismissTimerTick;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Use the IntPtr variants so 64-bit ex-styles (e.g. anything past bit 31) survive.
            // SetWindowLong silently truncates to 32 bits on x64, which would corrupt high-bit flags.
            var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE,
                new IntPtr(style.ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        };

        // Esc dismisses the toolbar even though it never has keyboard focus (WS_EX_NOACTIVATE).
        // Subscribed for the life of the window — the SelectionTracker keeps a single toolbar
        // around for the whole process, so there's no leak; we still detach in Closed for safety.
        KeyboardHook.EscPressed += OnGlobalEsc;
        Closed += (_, _) => KeyboardHook.EscPressed -= OnGlobalEsc;
    }

    private void OnGlobalEsc()
    {
        // Hook fires on the hook thread; marshal to UI before touching window state.
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (IsVisible) HideToolbar();
        });
    }

    // ── Show ─────────────────────────────────────────────────────

    public void Show(string text, TextAnalysis analysis, List<ActionGroup> groups,
                     double x, double y, bool isEditable = false)
    {
        _selectedText = text;
        _analysis = analysis;
        _actionGroups = groups;
        _isEditable = isEditable;
        _isPasteMode = false;
        _editMode = false;

        CopyButton.Visibility = Visibility.Visible;
        PasteButton.Visibility = Visibility.Collapsed;
        BuildToolbarButtons();
        BuildContextActions();
        BuildPinnedActions();
        UpdateTypeBadge();
        PositionAndShow(x, y);
    }

    public void ShowPasteMode(double x, double y)
    {
        try { _selectedText = Clipboard.ContainsText() ? Clipboard.GetText() ?? "" : ""; } catch { _selectedText = ""; }
        _analysis = TextAnalysis.PlainText;
        _actionGroups = [];
        _isPasteMode = true;
        _editMode = false;

        CopyButton.Visibility = Visibility.Collapsed;
        PasteButton.Visibility = Visibility.Visible;

        // Hide all category buttons - transforms are accessed via Paste hover
        ContextActionsPanel.Children.Clear();
        ContextSeparator.Visibility = Visibility.Collapsed;
        PinnedActionsPanel.Children.Clear();
        PinnedSeparator.Visibility = Visibility.Collapsed;
        TransformSeparator.Visibility = Visibility.Collapsed;
        TransformButton.Visibility = Visibility.Collapsed;
        EncodeButton.Visibility = Visibility.Collapsed;
        SearchSeparator.Visibility = Visibility.Collapsed;
        SearchButton.Visibility = Visibility.Collapsed;
        TypeBadge.Visibility = Visibility.Collapsed;

        PositionAndShow(x, y);
    }

    /// <summary>Only show transform/encode buttons when text is in an editable field.</summary>
    private void BuildToolbarButtons()
    {
        var s = Config.SettingsManager.Current;

        // Single pass to avoid repeated O(n) scans.
        var groupNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in _actionGroups) groupNames.Add(g.Name);

        bool hasTransform = _isEditable && s.ShowTransformActions && groupNames.Contains("Transform");
        bool hasEncode = _isEditable && s.ShowEncodeActions && groupNames.Contains("Encode");
        bool hasSearch = s.ShowSearchActions && groupNames.Contains("Search");
        // The separator before Transform also serves the encode-only case.
        TransformSeparator.Visibility = (hasTransform || hasEncode) ? Visibility.Visible : Visibility.Collapsed;
        TransformButton.Visibility = hasTransform ? Visibility.Visible : Visibility.Collapsed;
        EncodeButton.Visibility = hasEncode ? Visibility.Visible : Visibility.Collapsed;
        SearchSeparator.Visibility = hasSearch ? Visibility.Visible : Visibility.Collapsed;
        SearchButton.Visibility = hasSearch ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Positioning ──────────────────────────────────────────────

    private void PositionAndShow(double cursorX, double cursorY)
    {
        // New show — invalidate any in-flight post-delay continuations from the previous selection.
        _generation++;

        // Cancel any pending hide from a previous fade-out
        var fadeOut = (Storyboard)FindResource("FadeOut");
        fadeOut.Stop(this);

        SubMenuPopup.IsOpen = false;
        ResetPreview();

        Width = double.NaN;
        Height = double.NaN;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0;

        // Use the DPI of the monitor *under the cursor* — not the window's current monitor.
        // Without this, mixed-DPI setups place the toolbar at the wrong physical position.
        var monitorDpi = ScreenHelper.GetDpiForPoint(new Point(cursorX, cursorY));
        _dpiX = monitorDpi.X > 0 ? monitorDpi.X : 1.0;
        _dpiY = monitorDpi.Y > 0 ? monitorDpi.Y : 1.0;

        Show();
        UpdateLayout();

        double tw = ActualWidth > 10 ? ActualWidth : 100;
        double th = ActualHeight > 10 ? ActualHeight : 44;

        var sb = ScreenHelper.GetScreenBounds(new Point(cursorX, cursorY));
        double wpfX = cursorX / _dpiX, wpfY = cursorY / _dpiY;
        double sL = sb.Left / _dpiX, sT = sb.Top / _dpiY;
        double sR = sb.Right / _dpiX, sB = sb.Bottom / _dpiY;

        // Position above cursor
        double left = wpfX - tw / 2;
        double top = wpfY - th - 15;

        // Clamp to screen
        if (left < sL + 8) left = sL + 8;
        if (left + tw > sR - 8) left = sR - 8 - tw;
        if (top < sT + 8) top = wpfY + 20;
        if (top + th > sB - 8) top = sB - 8 - th;
        left = Math.Max(left, sL);
        top = Math.Max(top, sT);

        Left = left;
        Top = top;

        ((Storyboard)FindResource("FadeIn")).Begin(this);
        StartDismissTimer();
    }

    // ── Dismiss ──────────────────────────────────────────────────

    private void OnDismissTimerTick(object? sender, EventArgs e)
    {
        NativeMethods.GetCursorPos(out var pt);
        if (IsPointInside(pt.X, pt.Y)) { StartDismissTimer(); return; }
        HideToolbar();
    }

    private void StartDismissTimer()
    {
        _dismissTimer.Stop();
        int timeout = Config.SettingsManager.Current.ToolbarDismissTimeout;
        if (timeout > 0) { _dismissTimer.Interval = TimeSpan.FromMilliseconds(timeout); _dismissTimer.Start(); }
    }

    public void HideToolbar()
    {
        if (!IsVisible) return;
        _dismissTimer.Stop();
        _editMode = false;
        _hoverPreviewMode = false;
        SubMenuPopup.IsOpen = false;
        var fadeOut = (Storyboard)FindResource("FadeOut");
        fadeOut.Stop(this);
        fadeOut.Completed -= FadeOut_Completed;
        fadeOut.Completed += FadeOut_Completed;
        fadeOut.Begin(this);
    }

    private void FadeOut_Completed(object? sender, EventArgs e)
    {
        var fadeOut = (Storyboard)FindResource("FadeOut");
        fadeOut.Completed -= FadeOut_Completed;
        Hide();
    }

    public bool IsPointInside(int screenX, int screenY)
    {
        if (!IsVisible) return false;

        // Compare in physical pixels everywhere. The toolbar's Left/Top/ActualWidth/Height are
        // in DIPs of the monitor where it was placed (stored as _dpiX/_dpiY at show time). The
        // sub-menu popup may render on a *different* monitor (WPF auto-positions to keep it on
        // screen) and so needs its own DPI lookup — sharing _dpiX/_dpiY with the toolbar is
        // wrong when the two are on monitors of different scale.
        double mainDpiX = _dpiX > 0 ? _dpiX : 1.0;
        double mainDpiY = _dpiY > 0 ? _dpiY : 1.0;
        double mainL = Left * mainDpiX;
        double mainT = Top * mainDpiY;
        double mainR = mainL + ActualWidth * mainDpiX;
        double mainB = mainT + ActualHeight * mainDpiY;
        if (screenX >= mainL && screenX <= mainR && screenY >= mainT && screenY <= mainB)
            return true;

        if (SubMenuPopup.IsOpen && SubMenuPopup.Child is FrameworkElement child)
        {
            try
            {
                var pt = child.PointToScreen(new Point(0, 0));
                var popupDpi = ScreenHelper.GetDpiForPoint(pt);
                double pdx = popupDpi.X > 0 ? popupDpi.X : 1.0;
                double pdy = popupDpi.Y > 0 ? popupDpi.Y : 1.0;
                double popR = pt.X + child.ActualWidth * pdx;
                double popB = pt.Y + child.ActualHeight * pdy;
                if (screenX >= pt.X && screenX <= popR && screenY >= pt.Y && screenY <= popB)
                    return true;
            }
            catch { }
        }
        return false;
    }

    // ── Type badge ───────────────────────────────────────────────

    private void UpdateTypeBadge()
    {
        if (_analysis.Type != TextType.PlainText)
        {
            TypeBadge.Visibility = Visibility.Visible;
            TypeLabel.Text = _analysis.Type switch
            {
                TextType.Url => "URL", TextType.Email => "EMAIL",
                TextType.FilePath => "FILE PATH", TextType.Json => "JSON",
                TextType.ColorCode => $"COLOR {_analysis.Metadata?.GetValueOrDefault("format", "")?.ToUpper()}",
                TextType.XmlHtml => _analysis.Metadata?.GetValueOrDefault("subtype", "xml")?.ToUpper() ?? "XML",
                TextType.MathExpression => "MATH",
                TextType.IpAddress => _analysis.Metadata?.GetValueOrDefault("version", "IP") ?? "IP",
                TextType.Uuid => "UUID", TextType.Base64 => "BASE64", TextType.Jwt => "JWT",
                TextType.DateTime => "DATE/TIME",
                TextType.Unit => $"UNIT {_analysis.Metadata?.GetValueOrDefault("symbol", "")}".TrimEnd(),
                _ => ""
            };
        }
        else TypeBadge.Visibility = Visibility.Collapsed;
    }

    // ── Top-level button handlers ────────────────────────────────

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySetClipboardText(_selectedText))
        {
            await ShowFailureAndHide("Couldn't write to clipboard — try again");
            return;
        }
        int gen = _generation;
        await ShowCopiedToast();
        // Don't hide if a new selection reshowed the toolbar during the toast.
        if (_generation == gen) HideToolbar();
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        // Match the action-click paste flow: snapshot the foreground HWND first, run synchronously,
        // and abort if focus shifted between click and paste (rare, but Alt-Tab during the click
        // window would otherwise paste into the wrong app).
        IntPtr expected = NativeMethods.GetForegroundWindow();
        HideToolbar();
        IntPtr current = NativeMethods.GetForegroundWindow();
        if (current == expected || current == IntPtr.Zero)
            TextCapture.SimulatePaste();
    }

    private void TransformButton_Click(object sender, RoutedEventArgs e) =>
        ShowSubMenu("Transform", ActionCategory.Transform);
    private void EncodeButton_Click(object sender, RoutedEventArgs e) =>
        ShowSubMenu("Encode", ActionCategory.Encode);
    private void SearchButton_Click(object sender, RoutedEventArgs e) =>
        ShowSubMenu("Search", ActionCategory.Search);

    // Use *Ptr variants — 32-bit truncation in the legacy GetWindowLong/SetWindowLong corrupts
    // high-bit ex-style flags on 64-bit Windows. The current style mask (NOACTIVATE | TOOLWINDOW
    // = 0x08000080) fits in 32 bits so the legacy path worked, but new flags in future edits
    // could silently drop.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
