using SnapActions.Detection;

namespace SnapActions.Actions.TransformActions;

public class CaseTransformAction(string id, string name, string iconKey, Func<string, string> transform) : IAction
{
    public string Id => $"case_{id}";
    public string Name => name;
    public string IconKey => iconKey;
    public ActionCategory Category => ActionCategory.Transform;
    public bool IsPreviewSafe => true; // Pure string transformation, safe to run on hover.

    public bool CanExecute(string text, TextAnalysis analysis) => !string.IsNullOrEmpty(text);

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var result = transform(text);
        return new ActionResult(true, result, $"Transformed to {name}");
    }
}
