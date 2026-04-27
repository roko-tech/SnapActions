using System.Windows;
using System.Windows.Threading;
using SnapActions.Core;
using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

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
            TextCapture.SimulatePaste();

            if (original != null)
            {
                // Give the target app a moment to process the paste before we swap the clipboard
                // back. 200 ms is comfortably longer than typical paste handlers.
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(200);
                    try { Clipboard.SetDataObject(original, copy: true); } catch { }
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
