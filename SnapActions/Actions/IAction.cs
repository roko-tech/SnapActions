using SnapActions.Detection;

namespace SnapActions.Actions;

public record ActionResult(bool Success, string? ResultText = null, string? Message = null);

public interface IAction
{
    string Id { get; }
    string Name { get; }
    string IconKey { get; }
    ActionCategory Category { get; }
    bool CanExecute(string text, TextAnalysis analysis);
    ActionResult Execute(string text, TextAnalysis analysis);
}
