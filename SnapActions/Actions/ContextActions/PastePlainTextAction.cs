using System.Windows;
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
            var plain = Clipboard.GetText();
            if (string.IsNullOrEmpty(plain)) return new ActionResult(false, Message: "Clipboard empty");
            Clipboard.SetText(plain, System.Windows.TextDataFormat.UnicodeText);
            TextCapture.SimulateCtrlV();
            return new ActionResult(true, Message: "Pasted as plain text");
        }
        catch
        {
            return new ActionResult(false, Message: "Failed to paste");
        }
    }
}
