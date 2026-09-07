using SnapActions.Config;
using SnapActions.Detection;

namespace SnapActions.Actions.UserActions;

public sealed class TextRecipeAction(TextRecipeDefinition recipe, IReadOnlyDictionary<string, IAction> operations) : IAction
{
    public string Id => $"recipe_{recipe.Id}";
    public string Name => recipe.Name;
    public string IconKey => "IconTransform";
    public ActionCategory Category => ActionCategory.Transform;
    public bool IsPreviewSafe => true;
    public bool CanExecute(string text, TextAnalysis analysis) => text.Length > 0 && recipe.Steps.Count is > 0 and <= 12;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        if (!CanExecute(text, analysis)) return new(false, Message: "Recipes require 1–12 text operations.");
        foreach (var id in recipe.Steps)
        {
            if (!operations.TryGetValue(id, out var action) || !action.IsPreviewSafe || action is IOperationAction)
                return new(false, Message: "A recipe step is unavailable. Edit this recipe in Settings.");
            var result = action.Execute(text, TextAnalysis.PlainText);
            if (!result.Success || result.ResultText == null) return new(false, Message: result.Message ?? $"{action.Name} failed");
            text = result.ResultText;
            if (text.Length > 128 * 1024) return new(false, Message: "Recipe output exceeds 128K characters.");
        }
        return new(true, text, Name);
    }
}
