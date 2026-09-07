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
        Services.LookupService.CanTranslate(text)
        && analysis.Type == TextType.PlainText;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        ResultPopup.ShowTranslation(text.Trim());
        return new ActionResult(true);
    }
}
