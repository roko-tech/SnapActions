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
            // Determine whether we'll be in the plain copy-to-clipboard path (not paste-mode,
            // not the editable+transform auto-paste path). The restore-after-copy setting only
            // applies there — for paste-mode the result IS the paste, no restore wanted.
            bool willPaste = _isPasteMode || (_isEditable && Config.SettingsManager.Current.ReplaceSelectionOnTransform
                                              && action.Category == ActionCategory.Transform);

            // Snapshot the previous clipboard BEFORE we overwrite it, so we can restore it
            // if the user has opted into the restore-after-copy setting.
            IDataObject? previous = null;
            if (!willPaste && Config.SettingsManager.Current.RestoreClipboardAfterAction)
            {
                try { previous = Clipboard.GetDataObject(); }
                catch { previous = null; }
            }

            // The Windows clipboard frequently throws transient ExternalException/COMException
            // when another process briefly holds it. Without this guard the throw would propagate
            // through async void to DispatcherUnhandledException, which logs noise but keeps the
            // app alive. We also must not paste in paste-mode if the write failed — that would
            // paste whatever stale content is currently on the clipboard.
            if (!TrySetClipboardText(result.ResultText))
            {
                await ShowFailureAndHide("Couldn't write to clipboard — try again");
                return;
            }
            if (willPaste)
            {
                // Abort if focus moved since the toolbar was shown (an Alt-Tab before or after
                // the click) rather than paste into the wrong app.
                HideToolbar();
                if (ForegroundGuard.StillValid())
                    TextCapture.SimulatePaste();
                return;
            }
            // Plain copy-to-clipboard path: flash a confirmation so the user knows it happened.
            int gen = _generation;
            await ShowCopiedToast();
            if (previous != null) ScheduleClipboardRestore(previous, result.ResultText);
            // A new selection during the toast reshowed the toolbar — leave it up.
            if (_generation != gen) return;
        }
        HideToolbar();
    }

    /// <summary>
    /// Restore the snapshot to the clipboard ~3 seconds after we wrote our action's result.
    /// Long enough that the user has had time to Alt-Tab + Ctrl+V somewhere; short enough that
    /// the restore isn't surprising. Best-effort: if Clipboard.SetDataObject throws because the
    /// snapshot wrapper's source COM data is gone, we just leave the action result in place.
    /// </summary>
    private static void ScheduleClipboardRestore(IDataObject snapshot, string? ourResult)
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(3000);
            try
            {
                // Only restore if OUR action result is still on the clipboard. If the user copied
                // something else in those 3s, restoring the old snapshot would clobber their data.
                if (Clipboard.ContainsText() && Clipboard.GetText() == ourResult)
                    Clipboard.SetDataObject(snapshot, copy: true);
            }
            catch (Exception ex) { Log.Warn($"Clipboard restore failed: {ex.Message}"); }
        }, DispatcherPriority.Background);
    }

    private static bool TrySetClipboardText(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch (Exception ex)
        {
            Log.Warn($"Clipboard.SetText failed: {ex.Message}");
            return false;
        }
    }

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
        RebuildCurrentSubMenu();
    }

    // ── Sub-menu show/toggle ─────────────────────────────────────

    private void ShowSubMenu(string groupName, ActionCategory category)
    {
        if (SubMenuPopup.IsOpen && _currentSubMenuGroup == groupName && !_hoverPreviewMode)
        { SubMenuPopup.IsOpen = false; _editMode = false; PreviewBorder.Visibility = Visibility.Collapsed; return; }

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
        // Only real category submenus support edit mode; overflow / hover-preview popups hide the gear.
        GearButton.Visibility = _currentSubMenuCategory != null ? Visibility.Visible : Visibility.Collapsed;

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
        _hoverPreviewMode = false;

        SubMenuPanel.Children.Clear();
        ResetPreview();
        SubMenuTitle.Text = "Paste As";
        GearButton.Visibility = Visibility.Visible; // real Transform category — edit mode works here

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
}
