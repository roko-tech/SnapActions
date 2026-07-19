using SnapActions.Core;
using SnapActions.Actions.TransformActions;
using SnapActions.Detection;
using Xunit;

namespace SnapActions.Tests;

public class OperationSafetyTests
{
    private static readonly ForegroundTarget Target = new(
        ForegroundWindow: new IntPtr(10),
        FocusedWindow: new IntPtr(11),
        ProcessId: 12,
        ThreadId: 13);

    [Fact]
    public async Task NewOperation_InvalidatesContinuationWaitingOnOlderOperation()
    {
        var source = new SelectionOperationSource();
        var first = source.Begin(Target);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var continuation = Task.Run(async () =>
        {
            await resume.Task;
            return first.IsCurrent;
        });

        var second = source.Begin(Target);
        resume.SetResult();

        Assert.False(await continuation);
        Assert.True(second.IsCurrent);
    }

    [Fact]
    public void DelayedMouseDownInvalidation_DoesNotInvalidateNewerOperation()
    {
        var source = new SelectionOperationSource();
        long observedAtMouseDown = source.CurrentGeneration;
        var newer = source.Begin(Target);

        source.InvalidateIfCurrent(observedAtMouseDown);

        Assert.True(newer.IsCurrent);
    }

    [Fact]
    public void DelayedTargetCapture_CannotOvertakeNewerReservedOperation()
    {
        var source = new SelectionOperationSource();
        var older = source.Begin(default);
        var newer = source.Begin(Target);

        older = older.WithTarget(Target);

        Assert.False(older.IsCurrent);
        Assert.True(newer.IsCurrent);
    }

    [Fact]
    public async Task DismissedOperation_CannotResumeMutation()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);
        var resume = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool invoked = false;

        var continuation = Task.Run(async () =>
        {
            await resume.Task;
            return operation.TryCommit(() =>
            {
                invoked = true;
                return true;
            });
        });

        operation.InvalidateIfCurrent();
        resume.SetResult();

        Assert.False(await continuation);
        Assert.False(invoked);
    }

    [Fact]
    public void ForegroundTarget_ExactIdentityMatches()
    {
        Assert.True(ForegroundGuard.Matches(Target, Target));
    }

    [Fact]
    public void ForegroundTarget_ZeroIdentityFailsClosed()
    {
        Assert.False(ForegroundGuard.Matches(default, default));
        Assert.False(ForegroundGuard.Matches(Target, default));
    }

    [Fact]
    public void ForegroundTarget_SameTopWindowButDifferentFocusedChildDoesNotMatch()
    {
        var current = Target with { FocusedWindow = new IntPtr(99) };
        Assert.False(ForegroundGuard.Matches(Target, current));
    }

    [Fact]
    public void ForegroundTarget_SameHandlesButDifferentAutomationFocusDoesNotMatch()
    {
        var expected = Target with { AutomationRuntimeId = "42,1" };
        var current = Target with { AutomationRuntimeId = "42,2" };

        Assert.False(ForegroundGuard.Matches(expected, current));
        Assert.False(ForegroundGuard.Matches(
            expected, current with { AutomationRuntimeId = null }));
        Assert.True(ForegroundGuard.Matches(
            expected, current with { AutomationRuntimeId = "42,1" }));
    }

    [Theory]
    [InlineData(99u, 13u)]
    [InlineData(12u, 99u)]
    public void ForegroundTarget_ReusedWindowWithDifferentProcessOrThreadDoesNotMatch(
        uint processId, uint threadId)
    {
        var current = Target with { ProcessId = processId, ThreadId = threadId };
        Assert.False(ForegroundGuard.Matches(Target, current));
    }

    [Fact]
    public void DestructiveActions_FailClosedWithoutOperationToken()
    {
        Assert.False(new DeleteTextAction().Execute("selected", TextAnalysis.PlainText).Success);
        Assert.False(new PastePlainTextAction().Execute("selected", TextAnalysis.PlainText).Success);
    }

    [Fact]
    public void ClipboardSnapshot_CustomFormatIsIncomplete()
    {
        var observation = new TextCapture.ClipboardObservation(
            Sequence: 10, OwnerWindow: new IntPtr(20), OwnerProcessId: 30);
        var reads = new[]
        {
            new TextCapture.ClipboardFormatRead("application/x-custom", ReadSucceeded: true, HasValue: true),
        };

        Assert.False(TextCapture.IsCompleteSnapshot(observation, observation, reads));
    }

    [Fact]
    public void ClipboardSnapshot_SupportedFormatReadFailureIsIncomplete()
    {
        var observation = new TextCapture.ClipboardObservation(
            Sequence: 10, OwnerWindow: new IntPtr(20), OwnerProcessId: 30);
        var reads = new[]
        {
            new TextCapture.ClipboardFormatRead(
                System.Windows.DataFormats.UnicodeText, ReadSucceeded: false, HasValue: false),
        };

        Assert.False(TextCapture.IsCompleteSnapshot(observation, observation, reads));
    }

    [Fact]
    public void ClipboardSnapshot_SequenceChangeDuringReadIsIncomplete()
    {
        var before = new TextCapture.ClipboardObservation(
            Sequence: 10, OwnerWindow: new IntPtr(20), OwnerProcessId: 30);
        var reads = new[]
        {
            new TextCapture.ClipboardFormatRead(
                System.Windows.DataFormats.UnicodeText, ReadSucceeded: true, HasValue: true),
        };

        Assert.False(TextCapture.IsCompleteSnapshot(
            before, before with { Sequence = 11 }, reads));
    }

    [Fact]
    public void ClipboardSnapshot_OwnerChangeBeforeSequenceMovesIsIncomplete()
    {
        var before = new TextCapture.ClipboardObservation(
            Sequence: 10, OwnerWindow: new IntPtr(20), OwnerProcessId: 30);
        var reads = new[]
        {
            new TextCapture.ClipboardFormatRead(
                System.Windows.DataFormats.UnicodeText, ReadSucceeded: true, HasValue: true),
        };

        Assert.False(TextCapture.IsCompleteSnapshot(
            before,
            before with { OwnerWindow = new IntPtr(21), OwnerProcessId = 31 },
            reads));
    }

    [Fact]
    public void ClipboardSnapshot_EmptyClipboardIsComplete()
    {
        var observation = new TextCapture.ClipboardObservation(
            Sequence: 10, OwnerWindow: IntPtr.Zero, OwnerProcessId: 0);
        Assert.True(TextCapture.IsCompleteSnapshot(
            observation, observation, Array.Empty<TextCapture.ClipboardFormatRead>()));
    }

    [Fact]
    public void ClipboardMutation_SequencePlusOneFromForeignOwnerIsAmbiguous()
    {
        var before = new TextCapture.ClipboardObservation(20, new IntPtr(30), OwnerProcessId: 40);
        var after = new TextCapture.ClipboardObservation(21, new IntPtr(31), OwnerProcessId: 41);

        Assert.Equal(TextCapture.ClipboardMutationOwnership.Ambiguous,
            TextCapture.ClassifyClipboardMutation(
                before, after, requestDelivered: true, expectedOwnerProcessId: 40,
                targetStillValid: true));
    }

    [Fact]
    public void ClipboardMutation_ExpectedOwnerAndValidTargetIsOwned()
    {
        var before = new TextCapture.ClipboardObservation(20, new IntPtr(30), OwnerProcessId: 40);
        var after = new TextCapture.ClipboardObservation(21, new IntPtr(31), OwnerProcessId: 40);

        Assert.Equal(TextCapture.ClipboardMutationOwnership.Owned,
            TextCapture.ClassifyClipboardMutation(
                before, after, requestDelivered: true, expectedOwnerProcessId: 40,
                targetStillValid: true));
    }

    [Fact]
    public void ClipboardMutation_DelayedRenderOwnerTransferIsOwnedBeforeSequenceMoves()
    {
        var before = new TextCapture.ClipboardObservation(20, new IntPtr(30), OwnerProcessId: 40);
        var after = new TextCapture.ClipboardObservation(20, new IntPtr(31), OwnerProcessId: 99);

        Assert.Equal(TextCapture.ClipboardMutationOwnership.Owned,
            TextCapture.ClassifyClipboardMutation(
                before, after, requestDelivered: true, expectedOwnerProcessId: 99,
                targetStillValid: true));
    }

    [Fact]
    public void ClipboardMutation_InvalidTargetIsAmbiguousEvenWithExpectedOwner()
    {
        var before = new TextCapture.ClipboardObservation(20, new IntPtr(30), OwnerProcessId: 40);
        var after = new TextCapture.ClipboardObservation(21, new IntPtr(31), OwnerProcessId: 40);

        Assert.Equal(TextCapture.ClipboardMutationOwnership.Ambiguous,
            TextCapture.ClassifyClipboardMutation(
                before, after, requestDelivered: true, expectedOwnerProcessId: 40,
                targetStillValid: false));
    }

    [Fact]
    public void ClipboardMutation_MultipleWritesRemainAmbiguous()
    {
        var before = new TextCapture.ClipboardObservation(20, new IntPtr(30), OwnerProcessId: 40);
        var after = new TextCapture.ClipboardObservation(22, new IntPtr(31), OwnerProcessId: 40);

        Assert.Equal(TextCapture.ClipboardMutationOwnership.Ambiguous,
            TextCapture.ClassifyClipboardMutation(
                before, after, requestDelivered: true, expectedOwnerProcessId: 40,
                targetStillValid: true));
    }

    [Fact]
    public void LockedClipboardWrite_UsesWriterOwnerRatherThanSequenceArithmetic()
    {
        var before = new TextCapture.ClipboardObservation(20, new IntPtr(30), OwnerProcessId: 40);
        var after = new TextCapture.ClipboardObservation(23, new IntPtr(31), OwnerProcessId: 99);

        Assert.Equal(TextCapture.ClipboardMutationOwnership.Owned,
            TextCapture.ClassifyLockedClipboardWrite(
                before, after, writerProcessId: 99));
        Assert.Equal(TextCapture.ClipboardMutationOwnership.Ambiguous,
            TextCapture.ClassifyLockedClipboardWrite(
                before, after, writerProcessId: 40));
    }

    [Fact]
    public void LockedClipboardWrite_DelayedOwnerTransferIsOwnedBeforeSequenceMoves()
    {
        var before = new TextCapture.ClipboardObservation(20, new IntPtr(30), OwnerProcessId: 40);
        var after = new TextCapture.ClipboardObservation(20, new IntPtr(31), OwnerProcessId: 99);

        Assert.Equal(TextCapture.ClipboardMutationOwnership.Owned,
            TextCapture.ClassifyLockedClipboardWrite(
                before, after, writerProcessId: 99));
    }

    [Fact]
    public void ClosedClipboardWrite_RequiresSuccessfulCloseAndExactOwnerWindow()
    {
        var before = new TextCapture.ClipboardObservation(20, new IntPtr(30), OwnerProcessId: 40);
        var ours = new TextCapture.ClipboardObservation(21, new IntPtr(31), OwnerProcessId: 99);

        Assert.True(TextCapture.CanAcceptClosedClipboardWrite(
            before, ours, writerWindow: new IntPtr(31), writerProcessId: 99,
            clipboardClosed: true));
        Assert.False(TextCapture.CanAcceptClosedClipboardWrite(
            before, ours, writerWindow: new IntPtr(31), writerProcessId: 99,
            clipboardClosed: false));
        Assert.False(TextCapture.CanAcceptClosedClipboardWrite(
            before, ours with { OwnerWindow = new IntPtr(32) },
            writerWindow: new IntPtr(31), writerProcessId: 99,
            clipboardClosed: true));
    }

    [Fact]
    public void ClipboardRestore_RequiresExactAcceptedObservation()
    {
        var accepted = new TextCapture.ClipboardObservation(21, new IntPtr(31), OwnerProcessId: 40);

        Assert.True(TextCapture.CanRestoreClipboard(accepted, accepted));
        Assert.False(TextCapture.CanRestoreClipboard(
            accepted, accepted with { Sequence = accepted.Sequence + 1 }));
        Assert.False(TextCapture.CanRestoreClipboard(
            accepted, accepted with { OwnerProcessId = 99 }));
    }

    [Fact]
    public void ClipboardOwnership_ContinuesAcrossDelayedRenderingSequenceAdvance()
    {
        var accepted = new TextCapture.ClipboardObservation(
            Sequence: 20, OwnerWindow: new IntPtr(31), OwnerProcessId: 99);
        var rendered = accepted with { Sequence = 23 };

        Assert.True(TextCapture.ContinuesOwnedClipboard(
            accepted, rendered, expectedOwnerProcessId: 99));
        Assert.False(TextCapture.ContinuesOwnedClipboard(
            accepted, rendered with { OwnerWindow = new IntPtr(32) },
            expectedOwnerProcessId: 99));
    }

    [Fact]
    public void ClipboardWrite_DoesNotStartAfterSnapshotObservationChanges()
    {
        var observed = new TextCapture.ClipboardObservation(
            Sequence: 20, OwnerWindow: new IntPtr(30), OwnerProcessId: 40);
        var snapshot = new TextCapture.ClipboardSnapshot(new Dictionary<string, object>(), observed);

        Assert.True(TextCapture.CanStartClipboardWrite(snapshot, observed));
        Assert.False(TextCapture.CanStartClipboardWrite(
            snapshot, observed with { Sequence = 21 }));
    }

    [Fact]
    public void PasteBoundary_RejectsTargetChangeAfterClipboardWrite()
    {
        var expectedTarget = Target with { AutomationRuntimeId = "42,1" };
        var written = new TextCapture.ClipboardObservation(
            Sequence: 21, OwnerWindow: new IntPtr(31), OwnerProcessId: 99);

        Assert.False(TextCapture.CanInjectAtBoundary(
            operationCurrent: true,
            expectedTarget,
            expectedTarget with { AutomationRuntimeId = "42,2" },
            written,
            written));
    }

    [Fact]
    public void PasteBoundary_RejectsClipboardChangeAfterWrite()
    {
        var expectedTarget = Target with { AutomationRuntimeId = "42,1" };
        var written = new TextCapture.ClipboardObservation(
            Sequence: 21, OwnerWindow: new IntPtr(31), OwnerProcessId: 99);

        Assert.False(TextCapture.CanInjectAtBoundary(
            operationCurrent: true,
            expectedTarget,
            expectedTarget,
            written,
            written with { Sequence = 22 }));
    }

    [Fact]
    public void PasteBoundary_RejectsSharedWindowWithoutLogicalFocusIdentity()
    {
        var sharedWindowTarget = Target with
        {
            FocusedWindow = Target.ForegroundWindow,
            AutomationRuntimeId = null,
        };

        Assert.False(TextCapture.CanInjectAtBoundary(
            operationCurrent: true,
            sharedWindowTarget,
            sharedWindowTarget,
            expectedClipboard: null,
            currentClipboard: default));
    }

    [Fact]
    public void PasteBoundary_RejectsNativeChildWithoutLogicalFocusIdentity()
    {
        Assert.False(TextCapture.CanInjectAtBoundary(
            operationCurrent: true,
            Target,
            Target,
            expectedClipboard: null,
            currentClipboard: default));
    }

    [Fact]
    public void PasteBoundary_AcceptsExactLogicalTargetAndClipboardObservation()
    {
        var exactTarget = Target with { AutomationRuntimeId = "42,1" };
        var written = new TextCapture.ClipboardObservation(
            Sequence: 21, OwnerWindow: new IntPtr(31), OwnerProcessId: 99);

        Assert.True(TextCapture.CanInjectAtBoundary(
            operationCurrent: true,
            exactTarget,
            exactTarget,
            written,
            written));
    }

    [Fact]
    public void KeySequence_ZeroAcceptedIsRejectedWithoutCleanup()
    {
        var strokes = BuildPasteStrokes();
        int calls = 0;

        var outcome = TextCapture.SendKeySequence(strokes, _ =>
        {
            calls++;
            return 0;
        });

        Assert.Equal(TextCapture.InputInjectionStatus.Rejected, outcome.Status);
        Assert.True(outcome.CleanupSucceeded);
        Assert.Equal(0u, outcome.AcceptedCount);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void KeySequence_FullBatchIsSucceededWithoutCleanup()
    {
        var strokes = BuildPasteStrokes();
        int calls = 0;

        var outcome = TextCapture.SendKeySequence(strokes, batch =>
        {
            calls++;
            return (uint)batch.Count;
        });

        Assert.Equal(TextCapture.InputInjectionStatus.Succeeded, outcome.Status);
        Assert.True(outcome.CleanupSucceeded);
        Assert.Equal((uint)strokes.Length, outcome.AcceptedCount);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(1, new ushort[] { 0x10 })]
    [InlineData(2, new ushort[] { 0x2D, 0x10 })]
    [InlineData(3, new ushort[] { 0x10 })]
    public void KeySequence_EveryPartialPastePrefixReleasesPressedKeys(
        int accepted, ushort[] expectedReleases)
    {
        var calls = new List<TextCapture.KeyStroke[]>();
        var outcome = TextCapture.SendKeySequence(
            BuildPasteStrokes(),
            batch =>
            {
                calls.Add(batch.ToArray());
                return calls.Count == 1
                    ? (uint)accepted
                    : (uint)batch.Count;
            });

        Assert.Equal(TextCapture.InputInjectionStatus.Partial, outcome.Status);
        Assert.True(outcome.CleanupSucceeded);
        Assert.Equal((uint)accepted, outcome.AcceptedCount);
        Assert.Equal(
            expectedReleases,
            calls.Skip(1).Select(call => Assert.Single(call).VirtualKey));
        Assert.All(
            calls.Skip(1),
            call => Assert.True(Assert.Single(call).KeyUp));
    }

    [Fact]
    public void KeySequence_PartialDeleteReleasesDelete()
    {
        TextCapture.KeyStroke[] strokes =
        [
            new(0x2E, KeyUp: false, Extended: true),
            new(0x2E, KeyUp: true, Extended: true),
        ];
        var calls = new List<TextCapture.KeyStroke[]>();

        var outcome = TextCapture.SendKeySequence(strokes, batch =>
        {
            calls.Add(batch.ToArray());
            return calls.Count == 1 ? 1u : (uint)batch.Count;
        });

        Assert.Equal(TextCapture.InputInjectionStatus.Partial, outcome.Status);
        Assert.True(outcome.CleanupSucceeded);
        Assert.Equal(1u, outcome.AcceptedCount);
        var release = Assert.Single(Assert.Single(calls.Skip(1)));
        Assert.Equal((ushort)0x2E, release.VirtualKey);
        Assert.True(release.KeyUp);
        Assert.True(release.Extended);
    }

    [Fact]
    public void KeySequence_ReportsIncompleteKeyUpCleanup()
    {
        int calls = 0;
        var outcome = TextCapture.SendKeySequence(
            BuildPasteStrokes(),
            batch =>
            {
                calls++;
                return calls == 1 ? 2u : 0u;
            });

        Assert.Equal(TextCapture.InputInjectionStatus.Partial, outcome.Status);
        Assert.False(outcome.CleanupSucceeded);
        Assert.Equal(2u, outcome.AcceptedCount);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void PartialPasteRollback_RequiresOnlyModifierAcceptedAndCleanupSucceeded()
    {
        Assert.True(TextCapture.CanRollbackAfterPartialPaste(
            new TextCapture.InputInjectionOutcome(
                TextCapture.InputInjectionStatus.Partial,
                CleanupSucceeded: true,
                AcceptedCount: 1)));
        Assert.False(TextCapture.CanRollbackAfterPartialPaste(
            new TextCapture.InputInjectionOutcome(
                TextCapture.InputInjectionStatus.Partial,
                CleanupSucceeded: true,
                AcceptedCount: 2)));
        Assert.False(TextCapture.CanRollbackAfterPartialPaste(
            new TextCapture.InputInjectionOutcome(
                TextCapture.InputInjectionStatus.Partial,
                CleanupSucceeded: false,
                AcceptedCount: 1)));
    }

    [Fact]
    public void FinalInputClaim_RejectsDismissalAfterEarlierClaim()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);
        Assert.True(operation.TryClaim());
        operation.InvalidateIfCurrent();
        bool sent = false;

        bool started = TextCapture.TrySendKeySequenceForOperation(
            operation,
            BuildPasteStrokes(),
            batch =>
            {
                sent = true;
                return (uint)batch.Count;
            },
            out var outcome);

        Assert.False(started);
        Assert.False(sent);
        Assert.Equal(TextCapture.InputInjectionStatus.Rejected, outcome.Status);
    }

    [Fact]
    public void ClipboardWriteCommit_DoesNotInvokeWriterAfterInvalidation()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);
        bool invoked = false;
        source.Invalidate();

        var written = TextCapture.TryCommitClipboardWrite(operation, () =>
        {
            invoked = true;
            return new TextCapture.ClipboardObservation(
                Sequence: 21, OwnerWindow: new IntPtr(31), OwnerProcessId: 99);
        });

        Assert.Null(written);
        Assert.False(invoked);
    }

    [Fact]
    public void ClipboardWriteCommit_AtomicWriterRejectsChangeAfterPrecheck()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);
        var expected = new TextCapture.ClipboardObservation(
            Sequence: 20, OwnerWindow: new IntPtr(30), OwnerProcessId: 40);
        var currentAfterLock = expected with { Sequence = 21 };
        bool wrote = false;

        var written = TextCapture.TryCommitClipboardWrite(operation, () =>
        {
            if (!TextCapture.CanRestoreClipboard(expected, currentAfterLock))
                return null;
            wrote = true;
            return currentAfterLock;
        });

        Assert.Null(written);
        Assert.False(wrote);
    }

    [Fact]
    public void FinalClipboardClaim_RejectsInvalidationAfterEarlierClaim()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);
        var observation = new TextCapture.ClipboardObservation(
            Sequence: 20, OwnerWindow: new IntPtr(30), OwnerProcessId: 40);
        Assert.True(operation.TryClaim());
        operation.InvalidateIfCurrent();

        Assert.False(TextCapture.TryClaimClipboardMutationAtBoundary(
            operation, observation, observation));
    }

    [Fact]
    public void ClipboardMutationCommit_DoesNotInvokeWriterAfterInvalidation()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);
        bool wrote = false;
        source.Invalidate();

        bool committed = TextCapture.TryCommitClipboardMutation(operation, () =>
        {
            wrote = true;
            return true;
        });

        Assert.False(committed);
        Assert.False(wrote);
    }

    [Fact]
    public void InputCommit_DoesNotInvokeSendAfterInvalidation()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);
        bool sent = false;
        source.Invalidate();

        bool committed = operation.TryCommit(() =>
        {
            sent = true;
            return true;
        });

        Assert.False(committed);
        Assert.False(sent);
    }

    [Fact]
    public void ActionCommit_DoesNotInvokeActionAfterInvalidation()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);
        bool invoked = false;
        source.Invalidate();

        bool started = operation.TryCommit(() =>
        {
            invoked = true;
            return true;
        });

        Assert.False(started);
        Assert.False(invoked);
    }

    [Fact]
    public async Task Invalidation_DoesNotBlockBehindClaimedMutation()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);
        using var mutationEntered = new ManualResetEventSlim();
        using var releaseMutation = new ManualResetEventSlim();

        var commit = Task.Run(() => operation.TryCommit(() =>
        {
            mutationEntered.Set();
            releaseMutation.Wait();
            return true;
        }));
        Assert.True(mutationEntered.Wait(TimeSpan.FromSeconds(5)));

        var invalidate = Task.Run(source.Invalidate);
        try
        {
            await invalidate.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(operation.IsCurrent);
        }
        finally
        {
            releaseMutation.Set();
        }

        Assert.True(await commit);
    }

    [Fact]
    public async Task ToolbarActionGate_AllowsOnlyOneConcurrentStarter()
    {
        var gate = new OperationActionGate();
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<bool> First() => Task.Run(async () =>
        {
            await start.Task;
            return gate.TryStart();
        });

        var contenders = new[] { First(), First() };
        start.SetResult();
        bool[] results = await Task.WhenAll(contenders);

        Assert.Single(results, started => started);
    }

    private static TextCapture.KeyStroke[] BuildPasteStrokes() =>
    [
        new(0x10, KeyUp: false, Extended: false),
        new(0x2D, KeyUp: false, Extended: true),
        new(0x2D, KeyUp: true, Extended: true),
        new(0x10, KeyUp: true, Extended: false),
    ];

}
