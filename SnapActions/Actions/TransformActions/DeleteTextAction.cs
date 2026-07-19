using SnapActions.Detection;

namespace SnapActions.Actions.TransformActions;

public class DeleteTextAction : IAction, IOperationAction
{
    public string Id => "delete_text";
    public string Name => "Delete";
    public string IconKey => "IconContext";
    public ActionCategory Category => ActionCategory.Transform;

    public bool CanExecute(string text, TextAnalysis analysis) => !string.IsNullOrEmpty(text);

    public ActionResult Execute(string text, TextAnalysis analysis)
        => new(false, Message: "Delete requires a current selection target");

    async Task<ActionResult> IOperationAction.ExecuteAsync(
        string text, TextAnalysis analysis, Core.SelectionOperation operation)
    {
        var outcome = await Core.TextCapture.SimulateDeleteAsync(operation);
        return outcome.Status switch
        {
            Core.TextCapture.InputInjectionStatus.Succeeded =>
                new ActionResult(true),
            Core.TextCapture.InputInjectionStatus.Partial =>
                new ActionResult(
                    false,
                    Message: outcome.CleanupSucceeded
                        ? "Windows accepted only part of the delete input; the selection may have changed"
                        : "Windows accepted part of the delete input and key release was incomplete"),
            _ => new ActionResult(
                false,
                Message: "Focus moved or Windows rejected the delete input"),
        };
    }
}
