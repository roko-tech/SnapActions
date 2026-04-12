using SnapActions.Detection;
using SnapActions.UI;

namespace SnapActions.Actions.ContextActions;

public class DictionaryAction : IAction
{
    public string Id => "dictionary";
    public string Name => "Dictionary";
    public string IconKey => "IconInfo";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis)
    {
        var t = text.Trim();
        return t.Length > 0 && t.Length <= 60 && !t.Contains('\n')
            && t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3
            && analysis.Type == TextType.PlainText;
    }

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var popup = new ResultPopup();
        var word = text.Trim();
        Helpers.NativeMethods.GetCursorPos(out var pt);
        popup.ShowAt(pt.X, pt.Y, $"Define: {word}",
            async http => await ResultPopup.FetchDefinition(http, word));
        return new ActionResult(true, Message: "Looking up...");
    }
}
