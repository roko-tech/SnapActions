using SnapActions.Detection;

namespace SnapActions.Actions.TransformActions;

public class WhitespaceAction(string id, string name, Func<string, string> transform) : IAction
{
    public string Id => $"ws_{id}";
    public string Name => name;
    public string IconKey => "IconWhitespace";
    public ActionCategory Category => ActionCategory.Transform;

    public bool CanExecute(string text, TextAnalysis analysis) => !string.IsNullOrEmpty(text);

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var result = transform(text);
        return new ActionResult(true, result, name);
    }
}
