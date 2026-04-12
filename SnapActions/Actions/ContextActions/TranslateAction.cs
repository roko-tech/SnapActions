using SnapActions.Detection;
using SnapActions.UI;

namespace SnapActions.Actions.ContextActions;

public class TranslateAction : IAction
{
    public string Id => "translate";
    public string Name => "Translate";
    public string IconKey => "IconTransform";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= 500;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var lang = Config.SettingsManager.Current.SearchLanguage;
        var popup = new ResultPopup();
        var trimmed = text.Trim();

        // Get cursor position for popup placement
        Helpers.NativeMethods.GetCursorPos(out var pt);
        popup.ShowAt(pt.X, pt.Y, $"Translate to {(string.IsNullOrEmpty(lang) ? "English" : lang.ToUpper())}",
            async http => await ResultPopup.FetchTranslation(http, trimmed, lang));

        return new ActionResult(true, Message: "Translating...");
    }
}
