namespace SnapActions.Core;

/// <summary>
/// Immutable identity for one selection/paste operation. A newer operation from the same source
/// makes every older token stale without mutating the token itself.
/// </summary>
internal readonly struct SelectionOperation
{
    private readonly SelectionOperationSource? _source;
    private readonly long _generation;
    private readonly Func<Task<bool>>? _validateSelection;
    private readonly Func<bool>? _validateInput;

    internal SelectionOperation(
        SelectionOperationSource source, long generation, ForegroundTarget target,
        Func<Task<bool>>? validateSelection = null, Func<bool>? validateInput = null)
    {
        _source = source;
        _generation = generation;
        Target = target;
        _validateSelection = validateSelection;
        _validateInput = validateInput;
    }

    internal ForegroundTarget Target { get; }
    internal bool HasInputValidation => _validateInput != null;

    internal bool IsCurrent =>
        _source != null && _source.IsCurrent(_generation);

    internal bool CanInjectInput =>
        IsCurrent && ForegroundGuard.StillValid(Target);

    internal async Task<bool> CanInjectInputAsync() =>
        IsCurrent
        && await ForegroundGuard.StillValidAsync(Target)
        && await CanUseSelectionAsync();

    internal async Task<bool> CanUseSelectionAsync() =>
        IsCurrent
        && (_validateSelection == null || await _validateSelection())
        && IsCurrent;

    internal async Task<bool> CanMutateTargetAsync() =>
        await CanInjectInputAsync()
        && await ForegroundGuard.RunBoundedAutomationAsync(ValidateInput, false, 500)
        && IsCurrent;

    // Called on the bounded UIA worker, including immediately before clipboard/input commits.
    // Missing range/capability evidence permits using the captured text, but never target edits.
    internal bool ValidateInput()
    {
        try { return IsCurrent && _validateInput != null && _validateInput() && IsCurrent; }
        catch { return false; }
    }

    internal bool TryCommit(Func<bool> action) =>
        _source != null && _source.TryCommit(_generation, action);

    internal bool TryClaim() =>
        _source != null && _source.TryClaim(_generation);

    internal void InvalidateIfCurrent() =>
        _source?.InvalidateIfCurrent(_generation);

    internal SelectionOperation WithTarget(ForegroundTarget target) =>
        _source == null
            ? default
            : new SelectionOperation(_source, _generation, target, _validateSelection, _validateInput);

    internal SelectionOperation WithSelectionValidation(Func<Task<bool>> validateSelection) =>
        _source == null ? default : new SelectionOperation(_source, _generation, Target, validateSelection, _validateInput);

    internal SelectionOperation WithInputValidation(Func<bool>? validateInput) =>
        _source == null ? default : new SelectionOperation(_source, _generation, Target, _validateSelection, validateInput);
}

internal sealed class SelectionOperationSource
{
    private long _generation;

    internal long CurrentGeneration => Volatile.Read(ref _generation);

    internal SelectionOperation Begin(ForegroundTarget target)
    {
        long generation = Interlocked.Increment(ref _generation);
        return new SelectionOperation(this, generation, target);
    }

    internal void Invalidate() =>
        Interlocked.Increment(ref _generation);

    internal void InvalidateIfCurrent(long expectedGeneration) =>
        Interlocked.CompareExchange(
            ref _generation,
            unchecked(expectedGeneration + 1),
            expectedGeneration);

    internal bool IsCurrent(long generation) =>
        Volatile.Read(ref _generation) == generation;

    // An interlocked read-modify-write gives the final action boundary a total order against
    // Begin/Invalidate without ever blocking a low-level hook callback behind clipboard, UIA, or
    // cross-process work. A successful final claim is the commit point; later input is ordered
    // after that mutation, while every continuation that has not claimed is made stale.
    internal bool TryClaim(long generation) =>
        Interlocked.CompareExchange(
            ref _generation, generation, generation) == generation;

    internal bool TryCommit(long generation, Func<bool> action)
        => TryClaim(generation) && action();
}

internal sealed class OperationActionGate
{
    private int _started;

    internal bool TryStart() =>
        Interlocked.CompareExchange(ref _started, 1, 0) == 0;

    internal void AllowRetry() => Interlocked.CompareExchange(ref _started, 0, 1);
}
