using SnapActions.Detection;
using SnapActions.Helpers;

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
        // Only for short text (1-3 words, no special chars)
        return t.Length > 0 && t.Length <= 60 && !t.Contains('\n')
            && t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3
            && analysis.Type == TextType.PlainText;
    }

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var encoded = Uri.EscapeDataString(text.Trim());
        return ProcessHelper.TryShellOpen(
            $"https://www.google.com/search?q=define+{encoded}",
            "Looking up definition...");
    }
}
