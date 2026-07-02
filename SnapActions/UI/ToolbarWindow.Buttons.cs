using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SnapActions.Actions;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace SnapActions.UI;

// Dynamic-button construction lives here: inline context buttons, pinned buttons (with
// drag-to-reorder), the overflow button, and the sub-menu buttons that the toolbar pops out.
// The split is geometric rather than logical — these methods are what makes the file long,
// not what makes the toolbar conceptually distinct.
public partial class ToolbarWindow
{
    private const string PinnedDragFormat = "SnapActions.PinnedActionId";

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
        _hoverPreviewMode = false;

        SubMenuPanel.Children.Clear();
        ResetPreview();
        SubMenuTitle.Text = "More actions";
        GearButton.Visibility = Visibility.Collapsed; // no edit mode for the ad-hoc overflow list
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
        // Hover preview — same MouseEnter/Leave handlers as submenu buttons but routed through
        // InlineButton_* so the popup opens in preview-only mode if it isn't already open.
        btn.MouseEnter += InlineButton_MouseEnter;
        btn.MouseLeave += InlineButton_MouseLeave;
        return btn;
    }

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
        // Hover preview for pinned actions too — same routing as inline context buttons.
        btn.MouseEnter += InlineButton_MouseEnter;
        btn.MouseLeave += InlineButton_MouseLeave;

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
        if (isEditMode)
        {
            btn.Click += ToggleActionButton_Click;
            btn.MouseRightButtonUp += PinActionButton_Click;
            btn.ToolTip = "Click: show/hide  |  Right-click: pin  |  Arrows: reorder";
        }
        else { btn.Click += ActionButton_Click; btn.MouseEnter += SubMenuButton_MouseEnter; btn.MouseLeave += SubMenuButton_MouseLeave; }
        return btn;
    }
}
