using SnapActions.Detection;

namespace SnapActions.Actions.TransformActions;

public class WrapAction(string id, string name, string prefix, string suffix) : IAction
{
    public string Id => id;
    public string Name => name;
    public string IconKey => "IconWrap";
    public ActionCategory Category => ActionCategory.Transform;
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => !string.IsNullOrEmpty(text);

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var result = $"{prefix}{text}{suffix}";
        return new ActionResult(true, result, $"Wrapped: {name}");
    }
}
