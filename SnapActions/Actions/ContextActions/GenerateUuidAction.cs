using SnapActions.Detection;

namespace SnapActions.Actions.ContextActions;

public class GenerateUuidAction : IAction
{
    public string Id => "generate_uuid";
    public string Name => "New UUID";
    public string IconKey => "IconUuid";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.Uuid;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var newUuid = Guid.NewGuid().ToString();
        return new ActionResult(true, newUuid, $"New UUID: {newUuid}");
    }
}
