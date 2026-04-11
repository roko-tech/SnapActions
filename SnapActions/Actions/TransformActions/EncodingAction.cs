using SnapActions.Detection;

namespace SnapActions.Actions.TransformActions;

public class EncodingAction(string id, string name, string iconKey, Func<string, string> transform) : IAction
{
    public string Id => $"enc_{id}";
    public string Name => name;
    public string IconKey => iconKey;
    public ActionCategory Category => ActionCategory.Encode;

    public bool CanExecute(string text, TextAnalysis analysis) => !string.IsNullOrEmpty(text);

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var result = transform(text);
        return new ActionResult(true, result, name);
    }
}
