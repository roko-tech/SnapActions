using System.Windows;
using SnapActions.Core;
using SnapActions.Helpers;

namespace SnapActions.Actions;

internal enum ResultDestination { Copy, Replace }

/// <summary>Owns action execution and the clipboard/input transaction. Views only choose and present outcomes.</summary>
internal static class ActionRunner
{
    internal static async Task<ActionResult> ExecuteAsync(IAction action, SelectionSnapshot selection)
    {
        var operation = selection.Operation;
        try
        {
            if (!await operation.CanUseSelectionAsync()) return Cancelled();
            if (action is IOperationAction targeted)
            {
                if (!selection.CanReplace || !operation.TryClaim()) return Cancelled();
                return await targeted.ExecuteAsync(selection.Text, selection.Analysis, operation);
            }
            ActionResult? result = null;
            return operation.TryCommit(() => { result = action.Execute(selection.Text, selection.Analysis); return true; })
                ? result! : Cancelled();
        }
        catch (Exception ex)
        {
            Log.Warn($"Action failed ({ex.GetType().Name})");
            return new(false, Message: "The action could not be completed.");
        }
    }

    internal static async Task<ActionResult> ApplyTextAsync(string text, SelectionSnapshot selection, ResultDestination destination)
    {
        var operation = selection.Operation;
        bool paste = destination == ResultDestination.Replace;
        if (!await operation.CanUseSelectionAsync()) return Cancelled();
        if (paste && (!selection.CanReplace || !await InputExecutor.PreparePasteAsync(operation))) return Cancelled();
        bool restoreAfterCopy = !paste && Config.SettingsManager.Current.RestoreClipboardAfterAction;
        ClipboardTransaction.ClipboardSnapshot? previous = null;
        ClipboardTransaction.ClipboardObservation? written = null;
        bool inputAttempted = false;
        try
        {
            if (paste || restoreAfterCopy)
            {
                previous = ClipboardTransaction.SnapshotClipboard();
                if (previous == null) return new(false, Message: "Clipboard formats couldn't be preserved safely");
                if (!ClipboardTransaction.CanStartClipboardWrite(previous, ClipboardTransaction.ObserveClipboard())) return Cancelled();
                written = await ClipboardTransaction.TrySetClipboardTextForOperationAsync(operation, previous, text, paste);
                if (written == null) return new(false, Message: "Clipboard changed or couldn't be written — action cancelled");
            }
            else if (!ClipboardTransaction.TryCommitClipboardMutation(operation, () => TryCopy(text)))
                return new(false, Message: "Couldn't write to the clipboard — try again");

            if (paste)
            {
                if (!await operation.CanUseSelectionAsync())
                {
                    ClipboardTransaction.RestoreClipboardIfUnchanged(previous!, written!.Value);
                    return Cancelled();
                }
                inputAttempted = true;
                var outcome = await InputExecutor.TrySimulatePasteAsync(operation, written);
                if (outcome.Status != InputExecutor.InputInjectionStatus.Succeeded)
                {
                    if (outcome.Status != InputExecutor.InputInjectionStatus.Partial || InputExecutor.CanRollbackAfterPartialPaste(outcome))
                        ClipboardTransaction.RestoreClipboardIfUnchanged(previous!, written!.Value);
                    return new(false, Message: outcome.Status == InputExecutor.InputInjectionStatus.Partial
                        ? "Windows accepted only part of the paste shortcut. Check the target before trying again."
                        : "Focus moved — paste cancelled");
                }
                return new(true, Message: "Replaced selection");
            }
            if (restoreAfterCopy && previous != null && written is { } accepted)
            {
                _ = RestoreLaterAsync(previous, accepted);
                previous = null; // delayed restore owns and disposes the snapshot
            }
            return new(true, Message: "Copied result");
        }
        catch (Exception ex)
        {
            // Once input may have reached the target, do not restore a payload it may still be reading.
            if (!inputAttempted && previous != null && written is { } accepted)
                ClipboardTransaction.RestoreClipboardIfUnchanged(previous, accepted);
            Log.Warn($"Applying result failed ({ex.GetType().Name})");
            return new(false, Message: inputAttempted ? "The paste could not be confirmed. Check the target before retrying." : "The result could not be copied. Try again.");
        }
        finally { previous?.Dispose(); }
    }

    private static async Task RestoreLaterAsync(ClipboardTransaction.ClipboardSnapshot snapshot,
        ClipboardTransaction.ClipboardObservation accepted)
    {
        try
        {
            await Task.Delay(3000);
            ClipboardTransaction.RestoreClipboardIfUnchanged(snapshot, accepted);
        }
        catch (Exception ex) { Log.Warn($"Clipboard restore failed ({ex.GetType().Name})"); }
        finally { snapshot.Dispose(); }
    }

    internal static bool TryCopy(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch { return false; }
    }

    private static ActionResult Cancelled() => new(false, Message: "Selection or focus changed — action cancelled");
}
