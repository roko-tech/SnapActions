using SnapActions.Detection;
using SnapActions.UI;

namespace SnapActions.Actions.ContextActions;

public class TranslateAction : IAction, ISelectionPresenter
{
    public string Id => "translate";
    public string Name => "Translate";
    public string IconKey => "IconTransform";
    public ActionCategory Category => ActionCategory.Context;

    // PlainText only — URLs, JSON, UUIDs, JWTs etc. aren't translatable prose, and offering
    // Translate for every short selection just crowded the toolbar for typed selections.
    // (Dictionary applies the same gate.)
    public bool CanExecute(string text, TextAnalysis analysis) =>
        Services.TranslationPage.CanTranslate(text)
        && analysis.Type == TextType.PlainText;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        TranslationPopup.ShowNearCursor(text);
        return new ActionResult(true);
    }

    // The card keeps the selection so its Replace button can paste the translation over it.
    bool ISelectionPresenter.Present(Core.SelectionSnapshot selection) =>
        TranslationPopup.ShowNearCursor(selection.Text, selection);
}
