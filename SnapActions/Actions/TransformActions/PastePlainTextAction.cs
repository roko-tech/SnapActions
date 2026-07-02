using System.Windows;
using System.Windows.Threading;
using SnapActions.Core;
using SnapActions.Detection;

namespace SnapActions.Actions.TransformActions;

public class PastePlainTextAction : IAction
{
    public string Id => "paste_plain";
    public string Name => "Paste Plain Text";
    public string IconKey => "IconWhitespace";
    public ActionCategory Category => ActionCategory.Transform;

    public bool CanExecute(string text, TextAnalysis analysis)
    {
        try { return Clipboard.ContainsText(); } catch { return false; }
    }

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        // Check BEFORE touching the clipboard: if focus moved since the toolbar appeared, the
        // paste would land in the wrong app — and aborting after the clipboard swap would have
        // replaced the user's rich clipboard for nothing.
        if (!ForegroundGuard.StillValid())
            return new ActionResult(false, Message: "Focus moved — paste cancelled");

        try
        {
            // Snapshot the original IDataObject (which may include RTF/HTML in addition to plain
            // text) so the user's rich clipboard isn't lost just because they pasted as plain.
            var original = Clipboard.GetDataObject();
            var plain = Clipboard.GetText();
            if (string.IsNullOrEmpty(plain)) return new ActionResult(false, Message: "Clipboard empty");

            // Set plain text only, paste, then restore the original IDataObject after the paste
            // settles so subsequent paste-into-Word operations still see the rich formatting.
            Clipboard.SetText(plain, System.Windows.TextDataFormat.UnicodeText);
            var pasteTask = TextCapture.SimulatePasteAsync();

            if (original != null)
            {
                // Give the target app a moment to process the paste before we swap the clipboard
                // back. 200 ms is comfortably longer than typical paste handlers — but the paste
                // itself can be briefly deferred while a physically-held Ctrl/Alt is released, so
                // wait on it first or the restore could beat the paste and Shift+Insert would
                // deliver the restored rich clipboard instead of the plain text.
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try { await pasteTask; } catch { }
                    await Task.Delay(200);
                    try
                    {
                        // Only restore if the clipboard still holds the plain text we put there.
                        // If it changed in the interim (the user copied something else, or pasted
                        // into an app that re-set the clipboard), restoring would clobber their data.
                        if (Clipboard.ContainsText() && Clipboard.GetText() == plain)
                            Clipboard.SetDataObject(original, copy: true);
                    }
                    catch { }
                }, DispatcherPriority.Background);
            }

            return new ActionResult(true, Message: "Pasted as plain text");
        }
        catch
        {
            return new ActionResult(false, Message: "Failed to paste");
        }
    }
}
