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

    /// <summary>
    /// True if Execute() is pure (no I/O, no clipboard write, no key/mouse input, no process launch).
    /// Hover preview only runs Execute() for actions where this is true. Default: false.
    /// </summary>
    bool IsPreviewSafe => false;
}
