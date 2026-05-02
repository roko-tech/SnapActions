using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SnapActions.Actions;
using SnapActions.Core;
using SnapActions.Detection;
using SnapActions.Helpers;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace SnapActions.UI;

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
            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };

        // Esc dismisses the toolbar even when it doesn't have keyboard focus
        _escListener = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(120) };
        _escListener.Tick += (_, _) =>
        {
            if (!IsVisible) return;
            // GetAsyncKeyState returns high bit set when key is currently down
            if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) HideToolbar();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _escListener.Start(); else _escListener.Stop();
        };
    }

    private readonly System.Windows.Threading.DispatcherTimer _escListener;

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
        var monitorDpi = Helpers.ScreenHelper.GetDpiForPoint(new Point(cursorX, cursorY));
        _dpiX = monitorDpi.X > 0 ? monitorDpi.X : 1.0;
        _dpiY = monitorDpi.Y > 0 ? monitorDpi.Y : 1.0;

        Show();
        UpdateLayout();

        double tw = ActualWidth > 10 ? ActualWidth : 100;
        double th = ActualHeight > 10 ? ActualHeight : 44;

        var sb = Helpers.ScreenHelper.GetScreenBounds(new Point(cursorX, cursorY));
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
                var popupDpi = Helpers.ScreenHelper.GetDpiForPoint(pt);
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
                TextType.DateTime => "DATE/TIME", TextType.CodeSnippet => "CODE",
                TextType.Unit => $"UNIT {_analysis.Metadata?.GetValueOrDefault("symbol", "")}".TrimEnd(),
                _ => ""
            };
        }
        else TypeBadge.Visibility = Visibility.Collapsed;
    }

    // ── Context actions (direct buttons) ─────────────────────────

    private void BuildContextActions()
    {
        ContextActionsPanel.Children.Clear();
        var cg = _actionGroups.FirstOrDefault(g => g.Name == "Context");
        if (cg is { Actions.Count: > 0 })
        {
            ContextSeparator.Visibility = Visibility.Visible;
            int max = Math.Max(1, Config.SettingsManager.Current.MaxInlineContextActions);
            foreach (var a in cg.Actions.Take(max))
                ContextActionsPanel.Children.Add(CreateActionButton(a));
            // If the user has more applicable context actions than the inline cap, surface the
            // remainder via an overflow button instead of silently dropping them. Previously
            // selecting a URL with translate/dictionary/QR/etc. could produce 5+ actions and
            // anything past the cap was just gone from the UI.
            if (cg.Actions.Count > max)
                ContextActionsPanel.Children.Add(CreateContextOverflowButton(cg.Actions.Skip(max).ToList()));
        }
        else ContextSeparator.Visibility = Visibility.Collapsed;
    }

    private Button CreateContextOverflowButton(List<IAction> overflow)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("ActionButtonStyle"),
            ToolTip = $"{overflow.Count} more action{(overflow.Count == 1 ? "" : "s")}",
            Tag = overflow,
            Content = new TextBlock
            {
                Text = "...", FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        btn.Click += (_, _) => ShowContextOverflowSubMenu(overflow);
        return btn;
    }

    private void ShowContextOverflowSubMenu(List<IAction> actions)
    {
        // Reuse the existing sub-menu plumbing but skip _actionGroups (these are the *overflow*,
        // not a registered category). Edit-mode arrows / pin toggles aren't meaningful here.
        _currentSubMenuGroup = "More actions";
        _currentSubMenuCategory = null;
        _editMode = false;

        SubMenuPanel.Children.Clear();
        ResetPreview();
        SubMenuTitle.Text = "More actions";
        foreach (var a in actions)
            SubMenuPanel.Children.Add(CreateSubMenuButton(a, false));
        SubMenuPopup.IsOpen = true;
        StartDismissTimer();
    }

    private void BuildPinnedActions()
    {
        PinnedActionsPanel.Children.Clear();
        var pinned = Config.SettingsManager.Current.PinnedActionIds;
        if (pinned.Count == 0) { PinnedSeparator.Visibility = Visibility.Collapsed; return; }

        var allActions = new List<IAction>();
        foreach (var g in _actionGroups)
            allActions.AddRange(g.Actions);

        bool any = false;
        foreach (var id in pinned)
        {
            var action = allActions.FirstOrDefault(a => a.Id == id);
            if (action != null)
            {
                PinnedActionsPanel.Children.Add(CreatePinnedButton(action));
                any = true;
            }
        }
        PinnedSeparator.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private Button CreateActionButton(IAction action)
    {
        var geo = TryFindResource(action.IconKey) as Geometry;
        var btn = new Button
        {
            Style = (Style)FindResource("ActionButtonStyle"), ToolTip = action.Name, Tag = action,
            Content = geo != null
                ? new Path { Data = geo, Fill = (Brush)FindResource("TextBrush"), Width = 16, Height = 16, Stretch = Stretch.Uniform }
                : new TextBlock { Text = action.Name.Length > 3 ? action.Name[..3] : action.Name,
                    FontSize = 10, Foreground = (Brush)FindResource("TextBrush"),
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } as object
        };
        btn.Click += ActionButton_Click;
        return btn;
    }

    private const string PinnedDragFormat = "SnapActions.PinnedActionId";

    private Button CreatePinnedButton(IAction action)
    {
        var geo = TryFindResource(action.IconKey) as Geometry;
        var btn = new Button
        {
            Style = (Style)FindResource("ActionButtonStyle"),
            ToolTip = action.Name + "  (drag to reorder)",
            Tag = action,
            Width = double.NaN, Padding = new Thickness(6, 4, 6, 4),
            AllowDrop = true,
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (geo != null)
            sp.Children.Add(new Path { Data = geo, Fill = (Brush)FindResource("TextBrush"),
                Width = 12, Height = 12, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 4, 0) });
        sp.Children.Add(new TextBlock
        {
            Text = action.Name, FontSize = 10,
            Foreground = (Brush)FindResource("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        btn.Content = sp;
        btn.Click += ActionButton_Click;

        // Drag-to-reorder. We track the press point so a small click doesn't initiate drag.
        Point pressPoint = default;
        bool pressed = false;
        btn.PreviewMouseLeftButtonDown += (_, args) =>
        {
            pressPoint = args.GetPosition(btn);
            pressed = true;
        };
        btn.PreviewMouseMove += (_, args) =>
        {
            if (!pressed || args.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var pt = args.GetPosition(btn);
            if (Math.Abs(pt.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pt.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            pressed = false;
            // Suppress the dismiss timer while dragging — popping the toolbar mid-drag is jarring.
            _dismissTimer.Stop();
            DragDrop.DoDragDrop(btn, new DataObject(PinnedDragFormat, action.Id), DragDropEffects.Move);
            // Restart dismiss timer once drag completes (DoDragDrop is synchronous).
            StartDismissTimer();
        };
        btn.PreviewMouseLeftButtonUp += (_, _) => pressed = false;

        btn.DragOver += (_, args) =>
        {
            args.Effects = args.Data.GetDataPresent(PinnedDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            args.Handled = true;
        };
        btn.Drop += (_, args) =>
        {
            args.Handled = true;
            if (!args.Data.GetDataPresent(PinnedDragFormat)) return;
            var draggedId = args.Data.GetData(PinnedDragFormat) as string;
            if (string.IsNullOrEmpty(draggedId) || draggedId == action.Id) return;

            var pinned = Config.SettingsManager.Current.PinnedActionIds;
            int from = pinned.IndexOf(draggedId);
            int to = pinned.IndexOf(action.Id);
            if (from < 0 || to < 0) return;
            pinned.RemoveAt(from);
            // Adjust target index if removal shifted positions.
            if (from < to) to--;
            pinned.Insert(to, draggedId);
            Config.SettingsManager.Save();
            BuildPinnedActions();
        };

        // Right-click context menu remains for reorder by 1 + unpin (keyboardless users).
        var menu = new ContextMenu();
        var moveLeft = new MenuItem { Header = "Move Left", Tag = action };
        moveLeft.Click += (s, _) => MovePinned(((MenuItem)s!).Tag as IAction, -1);
        var moveRight = new MenuItem { Header = "Move Right", Tag = action };
        moveRight.Click += (s, _) => MovePinned(((MenuItem)s!).Tag as IAction, 1);
        var unpin = new MenuItem { Header = "Unpin", Tag = action };
        unpin.Click += (s, _) =>
        {
            if (((MenuItem)s!).Tag is IAction a)
            {
                Config.SettingsManager.Current.PinnedActionIds.Remove(a.Id);
                Config.SettingsManager.Save();
                BuildPinnedActions();
            }
        };
        menu.Items.Add(moveLeft);
        menu.Items.Add(moveRight);
        menu.Items.Add(new Separator());
        menu.Items.Add(unpin);
        btn.ContextMenu = menu;

        return btn;
    }

    private void MovePinned(IAction? action, int direction)
    {
        if (action == null) return;
        var pinned = Config.SettingsManager.Current.PinnedActionIds;
        int idx = pinned.IndexOf(action.Id);
        int newIdx = idx + direction;
        if (idx < 0 || newIdx < 0 || newIdx >= pinned.Count) return;
        (pinned[idx], pinned[newIdx]) = (pinned[newIdx], pinned[idx]);
        Config.SettingsManager.Save();
        BuildPinnedActions();
    }

    // ── Sub-menu buttons ─────────────────────────────────────────

    private Button CreateSubMenuButton(IAction action, bool isEditMode)
    {
        var pinned = Config.SettingsManager.Current.PinnedActionIds;
        bool isPinned = pinned.Contains(action.Id);

        // Search engines use SearchEngine.Enabled, other actions use DisabledActionIds
        bool isOff;
        if (action.Category == ActionCategory.Search)
        {
            var engineId = action.Id.Replace("search_", "");
            var engine = Config.SettingsManager.Current.SearchEngines.FirstOrDefault(e => e.Id == engineId);
            isOff = engine != null && !engine.Enabled;
        }
        else
        {
            isOff = Config.SettingsManager.Current.DisabledActionIds.Contains(action.Id);
        }

        var btn = new Button
        {
            Style = (Style)FindResource("ActionButtonStyle"), Tag = action,
            Width = double.NaN, MinWidth = 60,
            Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2),
            Opacity = isEditMode && isOff ? 0.4 : 1.0
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };

        if (isEditMode)
        {
            // Eye toggle (enable/disable)
            sp.Children.Add(new Path
            {
                Data = (Geometry)FindResource(isOff ? "IconEyeOff" : "IconEyeOn"),
                Fill = (Brush)FindResource(isOff ? "TextSecondaryBrush" : "AccentBrush"),
                Width = 12, Height = 12, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 4, 0)
            });
            // Pin toggle
            sp.Children.Add(new Path
            {
                Data = (Geometry)FindResource(isPinned ? "IconPin" : "IconPinOff"),
                Fill = (Brush)FindResource(isPinned ? "WarningBrush" : "TextSecondaryBrush"),
                Width = 12, Height = 12, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0)
            });
        }
        else
        {
            var geo = TryFindResource(action.IconKey) as Geometry;
            if (geo != null)
                sp.Children.Add(new Path { Data = geo, Fill = (Brush)FindResource("TextBrush"),
                    Width = 14, Height = 14, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0) });
        }

        sp.Children.Add(new TextBlock
        {
            Text = action.Name, FontSize = 12,
            Foreground = (Brush)FindResource(isEditMode && isOff ? "TextSecondaryBrush" : "TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextDecorations = isEditMode && isOff ? TextDecorations.Strikethrough : null
        });

        // Arrows only make sense for actions in an ordered list — search engines (ordered in
        // SearchEngines) and pinned actions (ordered in PinnedActionIds). For an unpinned non-search
        // action, MoveAction would silently no-op, leaving the user staring at buttons that do
        // nothing.
        bool canReorder = isEditMode && (action.Category == ActionCategory.Search || isPinned);
        if (canReorder)
        {
            // Move up/down arrows for reordering
            var moveUp = new Button
            {
                Content = new TextBlock { Text = "\u25B2", FontSize = 8, Foreground = (Brush)FindResource("TextSecondaryBrush") },
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0),
                Tag = action, Cursor = System.Windows.Input.Cursors.Hand
            };
            moveUp.Click += MoveActionUp_Click;
            sp.Children.Add(moveUp);

            var moveDown = new Button
            {
                Content = new TextBlock { Text = "\u25BC", FontSize = 8, Foreground = (Brush)FindResource("TextSecondaryBrush") },
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 0),
                Tag = action, Cursor = System.Windows.Input.Cursors.Hand
            };
            moveDown.Click += MoveActionDown_Click;
            sp.Children.Add(moveDown);
        }

        btn.Content = sp;
        if (isEditMode)
        {
            btn.Click += ToggleActionButton_Click;
            btn.MouseRightButtonUp += PinActionButton_Click;
            btn.ToolTip = "Click: show/hide  |  Right-click: pin  |  Arrows: reorder";
        }
        else { btn.Click += ActionButton_Click; btn.MouseEnter += SubMenuButton_MouseEnter; btn.MouseLeave += SubMenuButton_MouseLeave; }
        return btn;
    }

    // ── Preview on hover ─────────────────────────────────────────

    // Hover-preview executes synchronously on the UI thread. Don't run heavyweight parsers
    // (XDocument/JsonDocument) on huge selections — full Execute on click is fine.
    private const int MaxPreviewExecuteChars = 64 * 1024;

    private void SubMenuButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Button { Tag: IAction action }) return;
        string preview;
        string? swatchHex = null;

        // Preview is opt-in via IAction.IsPreviewSafe — only pure transforms run on hover.
        if (action.IsPreviewSafe
            && !string.IsNullOrEmpty(_selectedText)
            && _selectedText.Length <= MaxPreviewExecuteChars)
        {
            try
            {
                var r = action.Execute(_selectedText, _analysis);
                preview = r.ResultText != null ? Truncate(r.ResultText, 120) : action.Name;
                // For ConvertColorAction the result is a color string; show a swatch alongside.
                if (_analysis.Type == Detection.TextType.ColorCode)
                    swatchHex = r.ResultText ?? _selectedText;
            }
            catch { preview = action.Name; }
        }
        else if (action.Category == ActionCategory.Search)
            preview = $"Search {action.Name} for: \"{Truncate(_selectedText, 50)}\"";
        else
            preview = action.Name;

        // For color selections, also show a swatch for *non*-pure actions like Preview Color.
        if (_analysis.Type == Detection.TextType.ColorCode && swatchHex == null)
            swatchHex = _selectedText;

        PreviewText.Text = preview;
        PreviewText.Opacity = 1;
        SetSwatch(swatchHex);
    }

    private void SubMenuButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        ResetPreview();

    private void ResetPreview()
    {
        PreviewText.Opacity = 0;
        SetSwatch(null);
    }

    private void SetSwatch(string? colorText)
    {
        if (string.IsNullOrEmpty(colorText))
        {
            ColorSwatch.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var converter = new System.Windows.Media.BrushConverter();
            var brush = converter.ConvertFromString(colorText.Trim()) as Brush;
            if (brush == null) { ColorSwatch.Visibility = Visibility.Collapsed; return; }
            ColorSwatch.Background = brush;
            ColorSwatch.Visibility = Visibility.Visible;
        }
        catch
        {
            ColorSwatch.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Briefly flash a "Copied!" indicator in the preview band before the toolbar fades out.
    /// Uses the existing PreviewText/PreviewBorder so we don't need another widget.
    /// </summary>
    private async Task ShowCopiedToast()
    {
        if (!SubMenuPopup.IsOpen)
        {
            // The user clicked an inline button (no submenu open). Open the submenu briefly so
            // the preview band — which lives inside it — is visible.
            SubMenuPanel.Children.Clear();
            SubMenuTitle.Text = "";
            SubMenuPopup.IsOpen = true;
        }
        SetSwatch(null);
        PreviewText.Text = "Copied to clipboard";
        PreviewText.Opacity = 1;
        await Task.Delay(450);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    // ── Reorder ──────────────────────────────────────────────────

    private void MoveActionUp_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: IAction action }) return;
        MoveAction(action, -1);
    }

    private void MoveActionDown_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: IAction action }) return;
        MoveAction(action, 1);
    }

    private void MoveAction(IAction action, int direction)
    {
        if (action.Category == ActionCategory.Search)
        {
            var engines = Config.SettingsManager.Current.SearchEngines;
            var engineId = action.Id.Replace("search_", "");
            int idx = engines.FindIndex(e => e.Id == engineId);
            int newIdx = idx + direction;
            if (idx < 0 || newIdx < 0 || newIdx >= engines.Count) return;
            (engines[idx], engines[newIdx]) = (engines[newIdx], engines[idx]);
        }
        else
        {
            // For non-search actions, reorder in PinnedActionIds if pinned
            var pinned = Config.SettingsManager.Current.PinnedActionIds;
            int idx = pinned.IndexOf(action.Id);
            int newIdx = idx + direction;
            if (idx < 0 || newIdx < 0 || newIdx >= pinned.Count) return;
            (pinned[idx], pinned[newIdx]) = (pinned[newIdx], pinned[idx]);
        }
        Config.SettingsManager.Save();
        RebuildCurrentSubMenu();
    }

    // ── Action execution ─────────────────────────────────────────

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IAction action }) return;
        ActionResult result;
        try { result = action.Execute(_selectedText, _analysis); }
        catch (Exception ex) { result = new ActionResult(false, Message: $"Error: {ex.Message}"); }

        if (!result.Success && !string.IsNullOrEmpty(result.Message))
        {
            // Surface the failure in the same band that hover preview uses, then dismiss.
            // Without this, click-on-failed-action just made the toolbar disappear — silent failure.
            await ShowFailureAndHide(result.Message);
            return;
        }

        if (result.ResultText != null)
        {
            Clipboard.SetText(result.ResultText);
            // In paste mode or editable+transform: paste the result
            if (_isPasteMode || (_isEditable && Config.SettingsManager.Current.ReplaceSelectionOnTransform
                                && action.Category == ActionCategory.Transform))
            {
                // Snapshot the foreground window before we tear down our toolbar — if focus has
                // moved (e.g. the user Alt-Tabbed in the few hundred ms since the click), abort
                // rather than paste into the wrong app.
                IntPtr expected = NativeMethods.GetForegroundWindow();
                HideToolbar();
                IntPtr current = NativeMethods.GetForegroundWindow();
                if (current == expected || current == IntPtr.Zero)
                    TextCapture.SimulatePaste();
                return;
            }
            // Plain copy-to-clipboard path: flash a confirmation so the user knows it happened.
            await ShowCopiedToast();
        }
        HideToolbar();
    }

    private async Task ShowFailureAndHide(string message)
    {
        // Make sure the popup is open so PreviewText is visible.
        if (!SubMenuPopup.IsOpen)
        {
            SubMenuPanel.Children.Clear();
            SubMenuTitle.Text = "Error";
            SubMenuPopup.IsOpen = true;
        }
        PreviewText.Text = message;
        PreviewText.Opacity = 1;
        // Short visible window — long enough to read, short enough not to feel sticky.
        await Task.Delay(1500);
        HideToolbar();
    }

    // ── Edit mode (gear toggle) ──────────────────────────────────

    private void GearButton_Click(object sender, RoutedEventArgs e)
    {
        _editMode = !_editMode;
        RebuildCurrentSubMenu();
    }

    private void ToggleActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IAction action }) return;

        if (action.Category == ActionCategory.Search)
        {
            // Toggle SearchEngine.Enabled
            var engineId = action.Id.Replace("search_", "");
            var engine = Config.SettingsManager.Current.SearchEngines.FirstOrDefault(en => en.Id == engineId);
            if (engine != null) engine.Enabled = !engine.Enabled;
        }
        else
        {
            // Toggle DisabledActionIds
            var disabled = Config.SettingsManager.Current.DisabledActionIds;
            if (disabled.Contains(action.Id)) disabled.Remove(action.Id); else disabled.Add(action.Id);
        }
        Config.SettingsManager.Save();
        RebuildCurrentSubMenu();
    }

    private void PinActionButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: IAction action }) return;
        var pinned = Config.SettingsManager.Current.PinnedActionIds;
        if (pinned.Contains(action.Id)) pinned.Remove(action.Id); else pinned.Add(action.Id);
        Config.SettingsManager.Save();
        RebuildCurrentSubMenu();
    }

    // ── Sub-menu show/toggle ─────────────────────────────────────

    private void ShowSubMenu(string groupName, ActionCategory category)
    {
        if (SubMenuPopup.IsOpen && _currentSubMenuGroup == groupName)
        { SubMenuPopup.IsOpen = false; _editMode = false; PreviewBorder.Visibility = Visibility.Collapsed; return; }

        _currentSubMenuGroup = groupName;
        _currentSubMenuCategory = category;
        _editMode = false;
        RebuildCurrentSubMenu();
    }

    private void RebuildCurrentSubMenu()
    {
        SubMenuPanel.Children.Clear();
        ResetPreview();

        if (_editMode && Registry != null && _currentSubMenuCategory != null)
        {
            SubMenuTitle.Text = $"{_currentSubMenuGroup} (editing)";
            foreach (var a in Registry.GetAllActionsForCategory(_currentSubMenuCategory.Value))
                SubMenuPanel.Children.Add(CreateSubMenuButton(a, true));
        }
        else
        {
            SubMenuTitle.Text = _currentSubMenuGroup ?? "";
            var g = _actionGroups.FirstOrDefault(g => g.Name == _currentSubMenuGroup);
            if (g == null) return;
            foreach (var a in g.Actions)
                SubMenuPanel.Children.Add(CreateSubMenuButton(a, false));
        }

        // Position popup just below the toolbar, aligned left
        SubMenuPopup.IsOpen = true;
        StartDismissTimer();
    }

    // ── Paste button hover: show transform options as sub-menu ───

    private void PasteButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPasteMode || string.IsNullOrEmpty(_selectedText)) return;

        // Build a submenu with: Plain paste + all transform actions on clipboard text
        _currentSubMenuGroup = "Paste As";
        _currentSubMenuCategory = ActionCategory.Transform;

        SubMenuPanel.Children.Clear();
        ResetPreview();
        SubMenuTitle.Text = "Paste As";

        if (Registry != null)
        {
            var disabled = Config.SettingsManager.Current.DisabledActionIds;
            var transforms = Registry.GetAllActionsForCategory(ActionCategory.Transform)
                .Where(a => !disabled.Contains(a.Id) && a.CanExecute(_selectedText, _analysis))
                .ToList();
            var encodes = Registry.GetAllActionsForCategory(ActionCategory.Encode)
                .Where(a => !disabled.Contains(a.Id) && a.CanExecute(_selectedText, _analysis))
                .ToList();

            foreach (var a in transforms) SubMenuPanel.Children.Add(CreateSubMenuButton(a, false));
            if (encodes.Count > 0)
            {
                SubMenuPanel.Children.Add(new TextBlock
                {
                    Text = "Encode", FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    Margin = new Thickness(8, 6, 8, 2), Width = 380
                });
                foreach (var a in encodes) SubMenuPanel.Children.Add(CreateSubMenuButton(a, false));
            }
        }

        SubMenuPopup.IsOpen = true;
        StartDismissTimer();
    }

    // ── Button handlers ──────────────────────────────────────────

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_selectedText);
        await ShowCopiedToast();
        HideToolbar();
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

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
