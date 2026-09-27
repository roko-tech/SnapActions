using SnapActions.Detection;

namespace SnapActions.Actions;

public record ActionResult(bool Success, string? ResultText = null, string? Message = null)
{
    // Only failures known not to have attempted target input may offer an in-place retry.
    internal bool CanRetry { get; init; }

    // A result view now owns the selection operation; the toolbar or palette must not revoke it.
    internal bool SelectionTransferred { get; init; }
}

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

/// <summary>
/// Implemented only by actions that can mutate the focused application. They require the
/// immutable selection operation; the ordinary IAction entry point must fail closed.
/// </summary>
internal interface IOperationAction
{
    Task<ActionResult> ExecuteAsync(
        string text, TextAnalysis analysis, Core.SelectionOperation operation);
}

/// <summary>
/// Implemented by actions that show their own result view, which takes over the selection for a
/// later Copy or Replace. Returns false when nothing was shown, so the caller keeps ownership.
/// </summary>
internal interface ISelectionPresenter
{
    bool Present(Core.SelectionSnapshot selection);
}
