using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SnapActions.Actions;

namespace SnapActions.UI;

// Dynamic-button construction lives here: inline context buttons, pinned buttons (with
// drag-to-reorder), the overflow button, and the sub-menu buttons that the toolbar pops out.
// The split is geometric rather than logical — these methods are what makes the file long,
// not what makes the toolbar conceptually distinct.
public partial class ToolbarWindow
{
    internal void RebuildInlineActions()
    {
        ContextActionsPanel.Children.Clear();
        PinnedActionsPanel.Children.Clear();
        ContextSeparator.Visibility = PinnedSeparator.Visibility = MoreButton.Visibility = Visibility.Collapsed;
        var all = _actionGroups.SelectMany(g => g.Actions).ToList();
        var pinned = Registry?.GetPinnedActions(_appName) ?? [];
        var pinnedIds = pinned.Select(a => a.Id).ToHashSet();
        var context = all.Where(a => a.Category == ActionCategory.Context && !pinnedIds.Contains(a.Id)).ToList();
        var overflow = new List<IAction>();
        double reserved = 10 + 44 + 16; // border/padding, More, and the two inline separators
        foreach (UIElement child in MainToolbar.Children)
        {
            if (child == ContextActionsPanel || child == PinnedActionsPanel || child == MoreButton || child.Visibility != Visibility.Visible) continue;
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            reserved += child.DesiredSize.Width;
        }
        double remaining = Math.Max(0, MainBorder.MaxWidth - reserved);
        bool pinsOverflowed = false;
        foreach (var action in pinned)
        {
            var button = CreatePinnedButton(action);
            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (pinsOverflowed || button.DesiredSize.Width > remaining)
            { overflow.Add(action); pinsOverflowed = true; continue; }
            PinnedActionsPanel.Children.Add(button);
            remaining -= button.DesiredSize.Width;
        }
        int maxContext = Config.SettingsManager.Current.MaxInlineContextActions;
        foreach (var action in context)
        {
            var button = CreateActionButton(action);
            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (ContextActionsPanel.Children.Count >= maxContext || button.DesiredSize.Width > remaining)
            { overflow.Add(action); continue; }
            ContextActionsPanel.Children.Add(button);
            remaining -= button.DesiredSize.Width;
        }
        ContextSeparator.Visibility = ContextActionsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PinnedSeparator.Visibility = PinnedActionsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.Tag = overflow;
        MoreButton.Visibility = overflow.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.ToolTip = $"More actions ({overflow.Count})";
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.Tag is List<IAction> actions) ShowContextOverflowSubMenu(actions);
    }

    private void ShowContextOverflowSubMenu(List<IAction> actions)
    {
        // Reuse the existing sub-menu plumbing but skip _actionGroups (these are the *overflow*,
        // not a registered category). Edit-mode arrows / pin toggles aren't meaningful here.
        _currentSubMenuGroup = "More actions";
        _currentSubMenuCategory = null;
        _editMode = false;
        _hoverPreviewMode = false;

        SubMenuPanel.Children.Clear();
        ResetPreview();
        SubMenuTitle.Text = "More actions";
        SubMenuHeader.Visibility = Visibility.Visible;
        CustomizationHint.Visibility = Visibility.Visible;
        GearButton.Visibility = Visibility.Collapsed; // no edit mode for the ad-hoc overflow list
        foreach (var a in actions)
            SubMenuPanel.Children.Add(CreateSubMenuButton(a, false));
        SubMenuPopup.IsOpen = true;
        StartDismissTimer();
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
        System.Windows.Automation.AutomationProperties.SetName(btn, action.Name);
        btn.Click += ActionButton_Click;
        // Hover preview — same MouseEnter/Leave handlers as submenu buttons but routed through
        // InlineButton_* so the popup opens in preview-only mode if it isn't already open.
        btn.MouseEnter += InlineButton_MouseEnter;
        btn.MouseLeave += InlineButton_MouseLeave;
        ConfigureActionButton(btn, action);
        return btn;
    }

    private Button CreatePinnedButton(IAction action)
    {
        if (action.Id == "paste_plain") return CreateActionButton(action);
        var geo = TryFindResource(action.IconKey) as Geometry;
        var btn = new Button
        {
            Style = (Style)FindResource("ActionButtonStyle"),
            ToolTip = action.Name + "  (drag to reorder)",
            Tag = action,
            Width = double.NaN, MaxWidth = 160, Padding = new Thickness(6, 4, 6, 4),
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (geo != null)
            sp.Children.Add(new Path { Data = geo, Fill = (Brush)FindResource("TextBrush"),
                Width = 12, Height = 12, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 4, 0) });
        sp.Children.Add(new TextBlock
        {
            Text = action.Name, FontSize = 10, MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        btn.Content = sp;
        System.Windows.Automation.AutomationProperties.SetName(btn, action.Name);
        btn.Click += ActionButton_Click;
        // Hover preview for pinned actions too — same routing as inline context buttons.
        btn.MouseEnter += InlineButton_MouseEnter;
        btn.MouseLeave += InlineButton_MouseLeave;

        ConfigureActionButton(btn, action);

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
    }

    private Button CreateSubMenuButton(IAction action, bool isEditMode)
    {
        var pinned = Config.SettingsManager.Current.PinnedActionIds;
        bool isPinned = pinned.Contains(action.Id);

        bool isOff = Config.ToolbarPreferences.IsHidden(Config.SettingsManager.Current, action);

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
                Content = new TextBlock { Text = "▲", FontSize = 8, Foreground = (Brush)FindResource("TextSecondaryBrush") },
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0),
                Tag = action, Cursor = System.Windows.Input.Cursors.Hand
            };
            moveUp.Click += MoveActionUp_Click;
            sp.Children.Add(moveUp);

            var moveDown = new Button
            {
                Content = new TextBlock { Text = "▼", FontSize = 8, Foreground = (Brush)FindResource("TextSecondaryBrush") },
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 0),
                Tag = action, Cursor = System.Windows.Input.Cursors.Hand
            };
            moveDown.Click += MoveActionDown_Click;
            sp.Children.Add(moveDown);
        }

        btn.Content = sp;
        System.Windows.Automation.AutomationProperties.SetName(btn, action.Name);
        if (isEditMode)
        {
            btn.Click += ToggleActionButton_Click;
            btn.ToolTip = "Drag to pin  |  Click to show/hide  |  Right-click for options";
        }
        else { btn.Click += ActionButton_Click; btn.MouseEnter += SubMenuButton_MouseEnter; btn.MouseLeave += SubMenuButton_MouseLeave; }
        ConfigureActionButton(btn, action, isEditMode);
        return btn;
    }
}
