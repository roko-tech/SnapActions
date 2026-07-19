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
        int actionGeneration = _generation;
        if (!TryStartToolbarAction(out var operation)) return;
        ActionResult result;
        try
        {
            if (action is IOperationAction operationAction)
            {
                result = operation.TryClaim()
                    ? await operationAction.ExecuteAsync(
                        _selectedText, _analysis, operation)
                    : new ActionResult(
                        false, Message: "Selection changed — action cancelled");
            }
            else
            {
                ActionResult? claimedResult = null;
                bool started = operation.TryCommit(() =>
                {
                    claimedResult = action.Execute(_selectedText, _analysis);
                    return true;
                });
                result = started
                    ? claimedResult!
                    : new ActionResult(
                        false, Message: "Selection changed — action cancelled");
            }
        }
        catch (Exception ex) { result = new ActionResult(false, Message: $"Error: {ex.Message}"); }

        // A newer selection reshowed the singleton toolbar while a targeted action was awaiting.
        if (_generation != actionGeneration) return;

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

            // Modifier waits and both target checks happen before the clipboard write. This closes
            // the old window where an Alt-Tab could cancel paste only after the result had already
            // replaced the user's clipboard.
            if (willPaste)
            {
                bool prepared = await TextCapture.PreparePasteAsync(operation);
                if (_generation != actionGeneration) return;
                if (!prepared)
                {
                    await ShowFailureAndHide("Focus moved — paste cancelled");
                    return;
                }
            }

            // Snapshot before any paste (for rollback on a final target/input failure), and before
            // ordinary copy when the user explicitly enabled restore-after-copy.
            bool restoreAfterCopy = !willPaste
                                    && Config.SettingsManager.Current.RestoreClipboardAfterAction;
            TextCapture.ClipboardSnapshot? previous = null;
            if (willPaste || restoreAfterCopy)
            {
                previous = TextCapture.SnapshotClipboard();
                if (previous == null)
                {
                    await ShowFailureAndHide("Clipboard formats couldn't be preserved safely");
                    return;
                }
                if (!TextCapture.CanStartClipboardWrite(
                        previous, TextCapture.ObserveClipboard()))
                {
                    await ShowFailureAndHide("Clipboard changed — action cancelled");
                    return;
                }
            }

            TextCapture.ClipboardObservation? written = null;
            bool writeSucceeded;
            if (previous != null)
            {
                written = await TextCapture.TrySetClipboardTextForOperationAsync(
                    operation,
                    previous,
                    result.ResultText,
                    requireExactTarget: willPaste);
                writeSucceeded = written != null;
            }
            else
            {
                // An ordinary explicit copy does not target the foreground app, but it still
                // belongs to this operation: a newer selection must suppress the stale write.
                writeSucceeded = TextCapture.TryCommitClipboardMutation(
                    operation,
                    () => TrySetClipboardText(result.ResultText));
            }

            if (_generation != actionGeneration)
            {
                if (previous != null && written is { } supersededWrite)
                {
                    if (willPaste)
                        TextCapture.RestoreClipboardIfUnchanged(
                            previous, supersededWrite);
                    else
                        ScheduleClipboardRestore(previous, supersededWrite);
                }
                return;
            }
            if (!writeSucceeded)
            {
                await ShowFailureAndHide(
                    "Clipboard changed or couldn't be written — action cancelled");
                return;
            }

            if (willPaste)
            {
                var expectedClipboard = written!.Value;
                var pasteOutcome = await TextCapture.TrySimulatePasteAsync(
                    operation, expectedClipboard);
                if (pasteOutcome.Status
                    == TextCapture.InputInjectionStatus.Partial)
                {
                    bool restored =
                        TextCapture.CanRollbackAfterPartialPaste(pasteOutcome)
                        && previous != null
                        && written is { } partialWrite
                        && TextCapture.RestoreClipboardIfUnchanged(
                            previous, partialWrite);
                    if (_generation != actionGeneration) return;
                    await ShowFailureAndHide(
                        restored
                            ? "Windows rejected the paste shortcut after the held key was safely released"
                            : pasteOutcome.CleanupSucceeded
                                ? "Windows accepted only part of the paste shortcut; clipboard restoration was skipped for safety"
                            : "Windows accepted part of the paste shortcut and key release was incomplete");
                    return;
                }
                if (pasteOutcome.Status
                    != TextCapture.InputInjectionStatus.Succeeded)
                {
                    if (previous != null && written is { } failedWrite)
                        TextCapture.RestoreClipboardIfUnchanged(previous, failedWrite);
                    if (_generation != actionGeneration) return;
                    await ShowFailureAndHide("Focus moved — paste cancelled");
                    return;
                }
                if (_generation != actionGeneration) return;
                HideToolbar();
                return;
            }
            // Plain copy-to-clipboard path: flash a confirmation so the user knows it happened.
            int gen = _generation;
            await ShowCopiedToast();
            if (previous != null && written is { } acceptedWrite)
                ScheduleClipboardRestore(previous, acceptedWrite);
            // A new selection during the toast reshowed the toolbar — leave it up.
            if (_generation != gen) return;
        }
        HideToolbar();
    }

    /// <summary>
    /// Restore the snapshot to the clipboard ~3 seconds after we wrote our action's result.
    /// Long enough that the user has had time to Alt-Tab + Ctrl+V somewhere; short enough that
    /// the restore isn't surprising. The exact accepted sequence and owner must still match, so
    /// another copy of even identical text is never overwritten.
    /// </summary>
    private static void ScheduleClipboardRestore(
        TextCapture.ClipboardSnapshot snapshot,
        TextCapture.ClipboardObservation acceptedWrite)
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(3000);
            if (!TextCapture.RestoreClipboardIfUnchanged(snapshot, acceptedWrite))
                Log.Info("Clipboard restore skipped because ownership changed");
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
