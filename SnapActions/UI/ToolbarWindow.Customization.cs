using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapActions.Actions;
using SnapActions.Config;
using SnapActions.Helpers;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Panel = System.Windows.Controls.Panel;

namespace SnapActions.UI;

public partial class ToolbarWindow
{
    private const string ActionDragFormat = "SnapActions.ToolbarAction";
    private IAction? _draggingAction;
    private ContextMenu? _activeActionMenu;
    private bool _refreshAfterInteraction;

    private void InitializeCustomization()
    {
        // Listen on the containers so even a disabled, read-only pin can be rearranged.
        foreach (var surface in new[] { MainToolbar, (Panel)SubMenuPanel })
        {
            Point pressPoint = default;
            Button? pressedButton = null;
            surface.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (_isPasteMode) return;
                pressPoint = e.GetPosition(surface);
                pressedButton = ActionButtonAt(surface, pressPoint);
            };
            surface.PreviewMouseLeftButtonUp += (_, _) => pressedButton = null;
            surface.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) { pressedButton = null; return; }
                if (pressedButton?.Tag is not IAction action || _draggingAction != null) return;
                var point = e.GetPosition(surface);
                if (Math.Abs(point.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(point.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

                // Release Button's click capture before entering OLE's nested drag loop.
                pressedButton.ReleaseMouseCapture();
                pressedButton = null;
                e.Handled = true;
                _draggingAction = action;
                _dismissTimer.Stop();
                try { DragDrop.DoDragDrop(surface, new DataObject(ActionDragFormat, action.Id), DragDropEffects.Move); }
                finally
                {
                    _draggingAction = null;
                    PinDropIndicator.Visibility = Visibility.Collapsed;
                    FinishCustomizationInteraction();
                }
            };
        }

        MainBorder.AllowDrop = true;
        MainBorder.PreviewDragOver += (_, e) =>
        {
            e.Handled = true;
            e.Effects = IsOwnActionDrag(e.Data) ? DragDropEffects.Move : DragDropEffects.None;
            if (e.Effects == DragDropEffects.None) return;
            var target = PinDropTarget(e.GetPosition(PinnedActionsPanel).X);
            var point = PinnedActionsPanel.TranslatePoint(new Point(target.X, 0), MainBorder);
            PinDropIndicator.Margin = new Thickness(point.X, point.Y, 0, 0);
            PinDropIndicator.Height = Math.Max(28, MainToolbar.ActualHeight);
            PinDropIndicator.Visibility = Visibility.Visible;
        };
        MainBorder.DragLeave += (_, _) => PinDropIndicator.Visibility = Visibility.Collapsed;
        MainBorder.PreviewDrop += (_, e) =>
        {
            e.Handled = true;
            e.Effects = DragDropEffects.None;
            PinDropIndicator.Visibility = Visibility.Collapsed;
            if (!IsOwnActionDrag(e.Data)) return;
            var target = PinDropTarget(e.GetPosition(PinnedActionsPanel).X);
            ToolbarPreferences.Pin(SettingsManager.Current, _draggingAction!, target.Id, target.After);
            SettingsManager.Save();
            e.Effects = DragDropEffects.Move;
        };
    }

    private bool IsOwnActionDrag(IDataObject data) => !_isPasteMode && _draggingAction != null
        && data.GetDataPresent(ActionDragFormat) && data.GetData(ActionDragFormat) as string == _draggingAction.Id;

    private static Button? ActionButtonAt(Panel surface, Point point)
    {
        var hit = VisualTreeHelper.HitTest(surface, point)?.VisualHit;
        while (hit != null && hit != surface)
        {
            if (hit is Button { Tag: IAction } button) return button;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    private (string? Id, bool After, double X) PinDropTarget(double x)
    {
        var buttons = PinnedActionsPanel.Children.OfType<Button>().ToList();
        foreach (var button in buttons)
        {
            double left = button.TranslatePoint(new Point(), PinnedActionsPanel).X;
            if (x < left + button.ActualWidth / 2)
                return (((IAction)button.Tag).Id, false, left);
        }
        var last = buttons.LastOrDefault();
        return last == null ? (null, false, 0)
            : (((IAction)last.Tag).Id, true, last.TranslatePoint(new Point(last.ActualWidth, 0), PinnedActionsPanel).X);
    }

    private void ConfigureActionButton(Button button, IAction action, bool editing = false)
    {
        if (!editing)
        {
            bool readOnly = action is IOperationAction && !_isEditable && !_isPasteMode;
            button.IsEnabled = !readOnly && action.CanExecute(_selectedText, _analysis);
            button.Opacity = button.IsEnabled ? 1 : 0.45;
            string hint = readOnly ? "Select text in an editable input to use this action."
                : !button.IsEnabled ? "This action does not apply to the current selection." : "Drag to pin or reorder. Right-click for options.";
            button.ToolTip = action.Name + " — " + hint;
            System.Windows.Automation.AutomationProperties.SetHelpText(button, hint);
            ToolTipService.SetShowOnDisabled(button, true);
        }
        if (_isPasteMode) return;

        var settings = SettingsManager.Current;
        bool pinned = settings.PinnedActionIds.Contains(action.Id);
        bool hidden = ToolbarPreferences.IsHidden(settings, action);
        var menu = new ContextMenu();
        var pin = new MenuItem { Header = pinned ? "Unpin from toolbar" : "Pin to toolbar" };
        pin.Click += (_, _) =>
        {
            if (pinned) settings.PinnedActionIds.RemoveAll(id => id == action.Id);
            else ToolbarPreferences.Pin(settings, action);
            SettingsManager.Save();
        };
        menu.Items.Add(pin);
        var hide = new MenuItem { Header = hidden ? "Show action" : "Hide action" };
        hide.Click += (_, _) => { ToolbarPreferences.SetHidden(settings, action, !hidden); SettingsManager.Save(); };
        menu.Items.Add(hide);
        if (pinned)
        {
            int index = settings.PinnedActionIds.IndexOf(action.Id);
            var left = new MenuItem { Header = "Move left", IsEnabled = index > 0 };
            left.Click += (_, _) => MovePinned(action, -1);
            var right = new MenuItem { Header = "Move right", IsEnabled = index < settings.PinnedActionIds.Count - 1 };
            right.Click += (_, _) => MovePinned(action, 1);
            menu.Items.Add(left); menu.Items.Add(right);
        }
        menu.Opened += (_, _) => { _activeActionMenu = menu; _dismissTimer.Stop(); };
        menu.Closed += (_, _) => { _activeActionMenu = null; FinishCustomizationInteraction(); };
        button.ContextMenu = menu;
        ContextMenuService.SetShowOnDisabled(button, true);
    }

    private void FinishCustomizationInteraction()
    {
        if (_refreshAfterInteraction)
        {
            _refreshAfterInteraction = false;
            RefreshActions();
        }
        StartDismissTimer();
    }

    private void OnSettingsChanged()
    {
        if (_draggingAction != null || _activeActionMenu != null) { _refreshAfterInteraction = true; return; }
        RefreshActions();
    }

    internal void RefreshActions()
    {
        if (Registry == null || _isPasteMode || string.IsNullOrEmpty(_selectedText)) return;
        _actionGroups = Registry.GetActions(_selectedText, _analysis, _appName)
            .Select(g => g with { Actions = g.Actions.Where(a => _isEditable || a is not IOperationAction).ToList() })
            .Where(g => g.Actions.Count > 0).ToList();
        BuildToolbarButtons();
        RebuildInlineActions();
        if (SubMenuPopup.IsOpen)
        {
            if (_hoverPreviewMode) { SubMenuPopup.IsOpen = false; ResetPreview(); }
            else RebuildCurrentSubMenu();
        }
        CustomizationHint.Text = SettingsManager.LastSaveError
            ?? "Drag onto the toolbar to pin. Right-click to hide or unpin.";
        if (!IsVisible) return;
        UpdateLayout();
        var bounds = ScreenHelper.GetScreenBounds(_anchorPoint);
        Left = Math.Clamp(Left, bounds.Left / _dpiX + 8,
            Math.Max(bounds.Left / _dpiX + 8, bounds.Right / _dpiX - ActualWidth - 8));
    }

    private void CustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (SubMenuPopup.IsOpen && _currentSubMenuGroup == "All actions" && !_hoverPreviewMode)
        { SubMenuPopup.IsOpen = false; return; }
        _currentSubMenuGroup = "All actions";
        _currentSubMenuCategory = null;
        _editMode = true;
        _hoverPreviewMode = false;
        RebuildCurrentSubMenu();
    }
}
