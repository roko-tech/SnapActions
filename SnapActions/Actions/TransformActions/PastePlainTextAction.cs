using System.Windows;
using System.Windows.Threading;
using SnapActions.Core;
using SnapActions.Detection;

namespace SnapActions.Actions.TransformActions;

public class PastePlainTextAction : IAction, IOperationAction
{
    public string Id => "paste_plain";
    public string Name => "Paste Plain Text";
    public string IconKey => "IconPaste";
    public ActionCategory Category => ActionCategory.Transform;

    public bool CanExecute(string text, TextAnalysis analysis)
    {
        try { return Clipboard.ContainsText(); } catch { return false; }
    }

    public ActionResult Execute(string text, TextAnalysis analysis)
        => new(false, Message: "Paste requires a current selection target");

    async Task<ActionResult> IOperationAction.ExecuteAsync(
        string text, TextAnalysis analysis, SelectionOperation operation)
    {
        // Wait out physical modifiers and validate the immutable target before touching clipboard
        // data. If focus changes during the wait, the user's rich clipboard remains untouched.
        if (!await InputExecutor.PreparePasteAsync(operation))
            return new ActionResult(false, Message: "Focus moved — paste cancelled");

        ClipboardTransaction.ClipboardSnapshot? original = null;
        bool deferredRestoreOwnsSnapshot = false;
        try
        {
            original = ClipboardTransaction.SnapshotClipboard();
            if (original == null)
                return new ActionResult(
                    false, Message: "Clipboard formats couldn't be preserved safely");

            string? plain = original.Data.TryGetValue(
                System.Windows.DataFormats.UnicodeText, out var unicode)
                ? unicode as string
                : original.Data.TryGetValue(System.Windows.DataFormats.Text, out var ansi)
                    ? ansi as string
                    : null;
            if (string.IsNullOrEmpty(plain)) return new ActionResult(false, Message: "Clipboard empty");

            if (!await operation.CanInjectInputAsync())
                return new ActionResult(false, Message: "Focus moved — paste cancelled");
            if (!ClipboardTransaction.CanStartClipboardWrite(
                    original, ClipboardTransaction.ObserveClipboard()))
                return new ActionResult(false, Message: "Clipboard changed — paste cancelled");

            var written = await ClipboardTransaction.TrySetClipboardTextForOperationAsync(
                operation,
                original,
                plain,
                requireExactTarget: true);
            if (written == null)
                return new ActionResult(false, Message: "Clipboard changed — paste cancelled");

            var pasteOutcome = await InputExecutor.TrySimulatePasteAsync(
                operation, written.Value);
            if (pasteOutcome.Status == InputExecutor.InputInjectionStatus.Partial)
            {
                if (InputExecutor.CanRollbackAfterPartialPaste(pasteOutcome))
                    ClipboardTransaction.RestoreClipboardIfUnchanged(
                        original, written.Value);
                return new ActionResult(
                    false,
                    Message: InputExecutor.CanRollbackAfterPartialPaste(pasteOutcome)
                        ? "Windows rejected the paste shortcut after the held key was safely released"
                        : pasteOutcome.CleanupSucceeded
                            ? "Windows accepted only part of the paste shortcut; clipboard restoration was skipped for safety"
                        : "Windows accepted part of the paste shortcut and key release was incomplete");
            }
            if (pasteOutcome.Status != InputExecutor.InputInjectionStatus.Succeeded)
            {
                ClipboardTransaction.RestoreClipboardIfUnchanged(original, written.Value);
                return new ActionResult(false, Message: "Focus moved — paste cancelled");
            }

            // Give the target time to consume the plain text, then restore only while the exact
            // sequence and clipboard owner from our write are unchanged.
            var restoreSnapshot = original;
            _ = Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await Task.Delay(200);
                    ClipboardTransaction.RestoreClipboardIfUnchanged(
                        restoreSnapshot, written.Value);
                }
                finally
                {
                    restoreSnapshot.Dispose();
                }
            }, DispatcherPriority.Background);
            deferredRestoreOwnsSnapshot = true;

            return new ActionResult(true, Message: "Pasted as plain text");
        }
        catch
        {
            return new ActionResult(false, Message: "Failed to paste");
        }
        finally
        {
            if (!deferredRestoreOwnsSnapshot)
                original?.Dispose();
        }
    }
}
