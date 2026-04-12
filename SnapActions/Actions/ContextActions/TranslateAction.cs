using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.ContextActions;

public class TranslateAction : IAction
{
    public string Id => "translate";
    public string Name => "Translate";
    public string IconKey => "IconTransform";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= 5000;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var lang = Config.SettingsManager.Current.SearchLanguage;
        var tl = string.IsNullOrEmpty(lang) ? "en" : lang;
        var encoded = Uri.EscapeDataString(text.Trim());
        return ProcessHelper.TryShellOpen(
            $"https://translate.google.com/?sl=auto&tl={tl}&text={encoded}",
            "Opened Google Translate");
    }
}
