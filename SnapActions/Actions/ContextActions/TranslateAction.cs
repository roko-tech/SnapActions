using SnapActions.Detection;
using SnapActions.UI;

namespace SnapActions.Actions.ContextActions;

public class TranslateAction : IAction
{
    public string Id => "translate";
    public string Name => "Translate";
    public string IconKey => "IconTransform";
    public ActionCategory Category => ActionCategory.Context;

    // PlainText only — URLs, JSON, UUIDs, JWTs etc. aren't translatable prose, and offering
    // Translate for every short selection just crowded the toolbar for typed selections.
    // (Dictionary applies the same gate.)
    public bool CanExecute(string text, TextAnalysis analysis) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= 500
        && analysis.Type == TextType.PlainText;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var lang = Config.SettingsManager.Current.SearchLanguage;
        var trimmed = text.Trim();
        ResultPopup.ShowNearCursor(
            $"Translate to {(string.IsNullOrEmpty(lang) ? "English" : lang.ToUpper())}",
            (http, ct) => ResultPopup.FetchTranslation(http, trimmed, lang, ct));
        return new ActionResult(true);
    }
}
