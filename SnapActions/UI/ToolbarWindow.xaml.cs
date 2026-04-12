using System.Diagnostics;
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
    private bool _dpiCached;
    private bool _isEditable;
    private bool _isPasteMode;

    // Edit mode for action toggles
    private bool _editMode;
    private string? _currentSubMenuGroup;
    private ActionCategory? _currentSubMenuCategory;
    private FrameworkElement? _currentSubMenuTarget;

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

        bool hasTransform = _isEditable && s.ShowTransformActions && _actionGroups.Any(g => g.Name == "Transform");
        bool hasEncode = _isEditable && s.ShowEncodeActions && _actionGroups.Any(g => g.Name == "Encode");
        bool hasSearch = s.ShowSearchActions && _actionGroups.Any(g => g.Name == "Search");
        TransformSeparator.Visibility = hasTransform ? Visibility.Visible : Visibility.Collapsed;
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

        Show();
        CacheDpi();
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
        GetCursorPos(out var pt);
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
        CacheDpi();
        double x = screenX / _dpiX, y = screenY / _dpiY;

        if (x >= Left && x <= Left + ActualWidth && y >= Top && y <= Top + ActualHeight)
            return true;

        if (SubMenuPopup.IsOpen && SubMenuPopup.Child is FrameworkElement child)
        {
            try
            {
                var pt = child.PointToScreen(new Point(0, 0));
                double px = pt.X / _dpiX, py = pt.Y / _dpiY;
                if (x >= px && x <= px + child.ActualWidth && y >= py && y <= py + child.ActualHeight)
                    return true;
            }
            catch { }
        }
        return false;
    }

    private void CacheDpi()
    {
        if (_dpiCached) return;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            _dpiX = source.CompositionTarget.TransformToDevice.M11;
            _dpiY = source.CompositionTarget.TransformToDevice.M22;
        }
        _dpiCached = true;
    }

    // ── Type badge ───────────────────────────────────────────────

    private void UpdateTypeBadge()
    {
        if (_analysis.Type != TextType.PlainText)
        {
            TypeBadge.Visibility = Visibility.Visible;
            TypeLabel.Text = _analysis.Type switch
            {
                TextType.Url => "URL", TextType.Email => "EMAIL", TextType.Phone => "PHONE",
                TextType.FilePath => "FILE PATH", TextType.Json => "JSON",
                TextType.ColorCode => $"COLOR {_analysis.Metadata?.GetValueOrDefault("format", "")?.ToUpper()}",
                TextType.XmlHtml => _analysis.Metadata?.GetValueOrDefault("subtype", "xml")?.ToUpper() ?? "XML",
                TextType.MathExpression => "MATH",
                TextType.IpAddress => _analysis.Metadata?.GetValueOrDefault("version", "IP") ?? "IP",
                TextType.Uuid => "UUID", TextType.Base64 => "BASE64",
                TextType.DateTime => "DATE/TIME", TextType.CodeSnippet => "CODE", _ => ""
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
            foreach (var a in cg.Actions.Take(4))
                ContextActionsPanel.Children.Add(CreateActionButton(a));
        }
        else ContextSeparator.Visibility = Visibility.Collapsed;
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

    private Button CreatePinnedButton(IAction action)
    {
        var geo = TryFindResource(action.IconKey) as Geometry;
        var btn = new Button
        {
            Style = (Style)FindResource("ActionButtonStyle"),
            ToolTip = action.Name, Tag = action,
            Width = double.NaN, Padding = new Thickness(6, 4, 6, 4)
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

        // Right-click context menu for reordering pinned actions
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

        if (isEditMode)
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

    // Actions that have side effects and must NOT be executed during preview
    private static readonly HashSet<string> _noPreviewIds =
        ["delete_text", "paste_plain", "translate", "dictionary", "currency_convert"];

    private void SubMenuButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Button { Tag: IAction action }) return;
        string preview;

        if (_noPreviewIds.Contains(action.Id))
        {
            preview = action.Name;
        }
        else if (action.Category == ActionCategory.Transform || action.Category == ActionCategory.Encode)
        {
            if (!string.IsNullOrEmpty(_selectedText))
            {
                try
                {
                    var r = action.Execute(_selectedText, _analysis);
                    preview = r.ResultText != null ? Truncate(r.ResultText, 120) : action.Name;
                }
                catch { preview = action.Name; }
            }
            else preview = action.Name;
        }
        else if (action.Category == ActionCategory.Search)
            preview = $"Search {action.Name} for: \"{Truncate(_selectedText, 50)}\"";
        else
            preview = action.Name;

        PreviewText.Text = preview;
        PreviewText.Opacity = 1;
    }

    private void SubMenuButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        ResetPreview();

    private void ResetPreview() => PreviewText.Opacity = 0;

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

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IAction action }) return;
        var result = action.Execute(_selectedText, _analysis);
        if (result.ResultText != null)
        {
            Clipboard.SetText(result.ResultText);
            // In paste mode or editable+transform: paste the result
            if (_isPasteMode || (_isEditable && Config.SettingsManager.Current.ReplaceSelectionOnTransform
                                && action.Category == ActionCategory.Transform))
            {
                HideToolbar();
                Dispatcher.InvokeAsync(() => TextCapture.SimulateCtrlV(), DispatcherPriority.Background);
                return;
            }
        }
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

    private void ShowSubMenu(string groupName, ActionCategory category, FrameworkElement target)
    {
        if (SubMenuPopup.IsOpen && _currentSubMenuGroup == groupName)
        { SubMenuPopup.IsOpen = false; _editMode = false; PreviewBorder.Visibility = Visibility.Collapsed; return; }

        _currentSubMenuGroup = groupName;
        _currentSubMenuCategory = category;
        _currentSubMenuTarget = target;
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

    private void ShowMultiGroupSubMenu(string[] names, ActionCategory[] categories, FrameworkElement target)
    {
        var key = string.Join("+", names);
        if (SubMenuPopup.IsOpen && _currentSubMenuGroup == key)
        { SubMenuPopup.IsOpen = false; _editMode = false; PreviewBorder.Visibility = Visibility.Collapsed; return; }

        _currentSubMenuGroup = key;
        _currentSubMenuCategory = null;
        _currentSubMenuTarget = target;
        _editMode = false;

        SubMenuPanel.Children.Clear();
        ResetPreview();
        SubMenuTitle.Text = string.Join(" / ", names);

        foreach (var name in names)
        {
            var g = _actionGroups.FirstOrDefault(g => g.Name == name);
            if (g == null) continue;
            SubMenuPanel.Children.Add(new TextBlock
            {
                Text = g.Name, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush"),
                Margin = new Thickness(8, 6, 8, 2), Width = 380
            });
            foreach (var a in g.Actions) SubMenuPanel.Children.Add(CreateSubMenuButton(a, false));
        }

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
        _currentSubMenuTarget = PasteButton;

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

    private void CopyButton_Click(object sender, RoutedEventArgs e) { Clipboard.SetText(_selectedText); HideToolbar(); }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        HideToolbar();
        Dispatcher.InvokeAsync(() => TextCapture.SimulateCtrlV(), DispatcherPriority.Background);
    }

    private void TransformButton_Click(object sender, RoutedEventArgs e) =>
        ShowSubMenu("Transform", ActionCategory.Transform, (FrameworkElement)sender);
    private void EncodeButton_Click(object sender, RoutedEventArgs e) =>
        ShowSubMenu("Encode", ActionCategory.Encode, (FrameworkElement)sender);
    private void SearchButton_Click(object sender, RoutedEventArgs e) =>
        ShowSubMenu("Search", ActionCategory.Search, (FrameworkElement)sender);

    // ── P/Invoke ─────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
