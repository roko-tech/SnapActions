using System.Windows.Controls;
using SnapActions.Detection;
using SnapActions.UI;
using TextBox = System.Windows.Controls.TextBox;

namespace SnapActions.Actions.ContextActions;

public class SpellCheckAction : IAction
{
    public string Id => "spell_check";
    public string Name => "Spelling";
    public string IconKey => "IconInfo";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis)
    {
        var t = text.Trim();
        // Only for short plain text (1-3 words, no special chars or newlines)
        return t.Length > 0 && t.Length <= 50 && !t.Contains('\n')
            && t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3
            && analysis.Type == TextType.PlainText;
    }

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var word = text.Trim();
        var suggestions = GetSuggestions(word);

        if (suggestions.Count == 0)
        {
            ResultPopup.ShowNearCursor("Spelling", _ => Task.FromResult($"\u2713 \"{word}\" is spelled correctly"));
        }
        else
        {
            var result = $"Suggestions for \"{word}\":\n\n" + string.Join("\n", suggestions.Select((s, i) => $"  {i + 1}. {s}"));
            ResultPopup.ShowNearCursor("Spelling", _ => Task.FromResult(result));
        }

        return new ActionResult(true);
    }

    private static List<string> GetSuggestions(string text)
    {
        var suggestions = new List<string>();

        // Use WPF's built-in spell checker via a hidden TextBox
        var tb = new TextBox
        {
            SpellCheck = { IsEnabled = true },
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(
                System.Globalization.CultureInfo.CurrentCulture.Name)
        };

        // Check each word
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            tb.Text = word;
            tb.UpdateLayout();

            var error = tb.GetSpellingError(0);
            if (error != null)
            {
                foreach (var s in error.Suggestions.Take(8))
                    suggestions.Add(s);
            }
        }

        return suggestions;
    }
}
