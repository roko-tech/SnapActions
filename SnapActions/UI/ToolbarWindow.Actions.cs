using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SnapActions.Actions;
using SnapActions.Core;
using SnapActions.Helpers;

namespace SnapActions.UI;

// Action execution + edit-mode plumbing + sub-menu navigation. All the user-interaction
// handlers that fire when a button in the toolbar or its sub-menu is clicked end up here.
public partial class ToolbarWindow
{
    // ── Action execution ─────────────────────────────────────────

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IAction action }) return;
        int generation = _generation;
        if (!TryStartToolbarAction(out var operation)) return;
        var selection = new SelectionSnapshot(_selectedText, _analysis, operation,
            _isEditable || _isPasteMode, _isPasteMode ? SelectionProviderKind.Clipboard : _selectionProvider,
            _selectionFlowDirection == FlowDirection.RightToLeft);
        var result = await ActionRunner.ExecuteAsync(action, selection);
        if (_generation != generation) return;
        if (!result.Success)
        {
            await ShowFailureAndHide(result.Message ?? "The action could not be completed");
            return;
        }
        if (result.ResultText != null)
        {
            // Transfer this operation to the result preview; hiding its old view must not invalidate it.
            Volatile.Write(ref _operationContext, null);
            _dismissTimer.Stop();
            SubMenuPopup.IsOpen = false;
            Hide();
            ResultPopup.ShowActionResult(action.Name, result.ResultText, selection);
            return;
        }
        HideToolbar();
    }

    private static bool TrySetClipboardText(string text) => ActionRunner.TryCopy(text);

    // ── Edit mode (gear toggle) ──────────────────────────────────

    private void GearButton_Click(object sender, RoutedEventArgs e)
    {
        // No edit mode without a real category (overflow / hover-preview popups) — toggling it
        // there used to blank the popup because RebuildCurrentSubMenu can't rebuild those lists.
        if (_currentSubMenuCategory == null) return;
        _editMode = !_editMode;
        RebuildCurrentSubMenu();
    }

    private void ToggleActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IAction action }) return;

        var settings = Config.SettingsManager.Current;
        Config.ToolbarPreferences.SetHidden(settings, action, !Config.ToolbarPreferences.IsHidden(settings, action));
        Config.SettingsManager.Save();
    }

    // ── Reorder (search engines / pinned actions) ────────────────

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
    }

    // ── Sub-menu show/toggle ─────────────────────────────────────

    private void ShowSubMenu(string groupName, ActionCategory category)
    {
        if (SubMenuPopup.IsOpen && _currentSubMenuGroup == groupName && !_hoverPreviewMode)
        { SubMenuPopup.IsOpen = false; _editMode = false; ResetPreview(); return; }

        _currentSubMenuGroup = groupName;
        _currentSubMenuCategory = category;
        _editMode = false;
        _hoverPreviewMode = false;
        RebuildCurrentSubMenu();
    }

    private void RebuildCurrentSubMenu()
    {
        SubMenuPanel.Children.Clear();
        ResetPreview();
        SubMenuHeader.Visibility = Visibility.Visible;
        CustomizationHint.Visibility = Visibility.Visible;
        GearButton.Visibility = _currentSubMenuCategory != null ? Visibility.Visible : Visibility.Collapsed;

        if (_currentSubMenuGroup == "All actions" && Registry != null)
        {
            SubMenuTitle.Text = "All actions — drag to pin, click to show/hide";
            foreach (var category in Enum.GetValues<ActionCategory>())
            {
                SubMenuPanel.Children.Add(new TextBlock
                {
                    Text = category.ToString(), FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("AccentBrush"), Width = 370, Margin = new Thickness(8, 6, 8, 2)
                });
                foreach (var action in Registry.GetAllActionsForCategory(category))
                    SubMenuPanel.Children.Add(CreateSubMenuButton(action, true));
            }
        }
        else if (_currentSubMenuGroup == "More actions" && MoreButton.Tag is List<IAction> overflow)
        {
            SubMenuTitle.Text = "More actions";
            foreach (var action in overflow) SubMenuPanel.Children.Add(CreateSubMenuButton(action, false));
        }
        else if (_editMode && Registry != null && _currentSubMenuCategory != null)
        {
            SubMenuTitle.Text = $"{_currentSubMenuGroup} (editing)";
            foreach (var a in Registry.GetAllActionsForCategory(_currentSubMenuCategory.Value))
                SubMenuPanel.Children.Add(CreateSubMenuButton(a, true));
        }
        else
        {
            SubMenuTitle.Text = _currentSubMenuGroup ?? "";
            var g = _actionGroups.FirstOrDefault(g => g.Name == _currentSubMenuGroup);
            if (g == null) { SubMenuPopup.IsOpen = false; return; }
            foreach (var a in g.Actions)
                SubMenuPanel.Children.Add(CreateSubMenuButton(a, false));
        }

        // Position popup just below the toolbar, aligned left
        SubMenuPopup.IsOpen = true;
        StartDismissTimer();
    }

    // ── Paste As sub-menu (paste mode) ───────────────────────────

    // Re-opens the menu if the user closed it and hovers the paste button again.
    private void PasteButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPasteMode || string.IsNullOrEmpty(_selectedText)) return;
        ShowPasteAsMenu();
    }

    /// <summary>
    /// Builds and opens the "Paste As" submenu (transforms + encodes applied to the clipboard
    /// text). Opened immediately when paste mode shows — it's paste mode's only content, and
    /// when hovering the bare V button was the sole way in, nothing hinted the options existed.
    /// </summary>
    private void ShowPasteAsMenu()
    {
        // Build a submenu with: Plain paste + all transform actions on clipboard text
        _currentSubMenuGroup = "Paste As";
        _currentSubMenuCategory = null;
        _editMode = false;
        _hoverPreviewMode = false;

        SubMenuPanel.Children.Clear();
        ResetPreview();
        SubMenuTitle.Text = "Paste As";
        SubMenuHeader.Visibility = Visibility.Visible;
        CustomizationHint.Visibility = Visibility.Collapsed;
        GearButton.Visibility = Visibility.Collapsed;

        if (Registry != null)
        {
            var applicable = Registry.GetActions(_selectedText, _analysis, ForegroundApp.GetActiveProcessName())
                .SelectMany(g => g.Actions).Where(a => a.IsPreviewSafe).ToList();
            var transforms = applicable.Where(a => a.Category == ActionCategory.Transform).ToList();
            var encodes = applicable.Where(a => a.Category == ActionCategory.Encode).ToList();

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
}
