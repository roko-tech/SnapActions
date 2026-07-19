namespace SnapActions.Core;

/// <summary>
/// Immutable identity for one selection/paste operation. A newer operation from the same source
/// makes every older token stale without mutating the token itself.
/// </summary>
internal readonly struct SelectionOperation
{
    private readonly SelectionOperationSource? _source;
    private readonly long _generation;

    internal SelectionOperation(
        SelectionOperationSource source, long generation, ForegroundTarget target)
    {
        _source = source;
        _generation = generation;
        Target = target;
    }

    internal ForegroundTarget Target { get; }

    internal bool IsCurrent =>
        _source != null && _source.IsCurrent(_generation);

    internal bool CanInjectInput =>
        IsCurrent && ForegroundGuard.StillValid(Target);

    internal async Task<bool> CanInjectInputAsync() =>
        IsCurrent
        && await ForegroundGuard.StillValidAsync(Target)
        && IsCurrent;

    internal bool TryCommit(Func<bool> action) =>
        _source != null && _source.TryCommit(_generation, action);

    internal bool TryClaim() =>
        _source != null && _source.TryClaim(_generation);

    internal void InvalidateIfCurrent() =>
        _source?.InvalidateIfCurrent(_generation);

    internal SelectionOperation WithTarget(ForegroundTarget target) =>
        _source == null
            ? default
            : new SelectionOperation(_source, _generation, target);
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
}
