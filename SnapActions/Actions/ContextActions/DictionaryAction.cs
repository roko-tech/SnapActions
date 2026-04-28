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
        if (t.Length == 0 || t.Length > 60 || t.Contains('\n')) return false;
        if (analysis.Type != TextType.PlainText) return false;
        // Only show for selections that look like real words. Without this every 1–3 word plain
        // selection (names, code identifiers, slugs) offered Dictionary, cluttering the toolbar.
        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words.Length > 3) return false;
        foreach (var w in words)
            if (!IsDictionaryWord(w)) return false;
        return true;
    }

    private static bool IsDictionaryWord(string s)
    {
        if (s.Length < 2) return false;
        foreach (var c in s)
            if (!char.IsLetter(c) && c != '\'' && c != '-') return false;
        return true;
    }

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var word = text.Trim();
        var lang = Config.SettingsManager.Current.SearchLanguage;
        if (string.IsNullOrEmpty(lang)) lang = "en";
        ResultPopup.ShowNearCursor($"Define: {word}",
            (http, ct) => ResultPopup.FetchDefinition(http, word, lang, ct));
        return new ActionResult(true);
    }
}
