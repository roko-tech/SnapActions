using SnapActions.Actions;
using SnapActions.Core;
using SnapActions.Detection;
using Xunit;

namespace SnapActions.Tests;

public class PaletteSelectionReadinessTests
{
    private static readonly ForegroundTarget Target = new((nint)10, (nint)11, 12, 13);

    [Fact]
    public async Task ActivationHandoff_WaitsForTheOriginalNativeTarget()
    {
        int reads = 0;
        var operation = new SelectionOperationSource().Begin(Target);

        Assert.True(await GlobalHotkey.WaitForActivationAsync(operation, expected =>
        {
            Assert.Equal(Target, expected);
            return ++reads > 1;
        }));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task ReadyTarget_ReturnsWithoutAnotherFocusRead()
    {
        int reads = 0;
        var operation = new SelectionOperationSource().Begin(Target);

        Assert.True(await GlobalHotkey.WaitForActivationAsync(operation, _ => { reads++; return true; }));
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task AnotherFocusedChild_IsNeverAcceptedAsOriginalTarget()
    {
        var operation = new SelectionOperationSource().Begin(Target);
        var changed = Target with { FocusedWindow = (nint)99 };

        Assert.False(await GlobalHotkey.WaitForActivationAsync(operation,
            expected => ForegroundGuard.MatchesWindow(expected, changed)));
    }

    [Fact]
    public async Task NewSelection_StopsPendingActivationWait()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int ready = 0;
        var pending = GlobalHotkey.WaitForActivationAsync(operation, _ =>
        {
            entered.TrySetResult();
            return Volatile.Read(ref ready) != 0;
        });
        await entered.Task;

        source.Begin(Target);
        Volatile.Write(ref ready, 1);

        Assert.False(await pending);
    }

    [Fact]
    public async Task InvalidationDuringFocusRead_CannotAuthorizeAction()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(Target);

        Assert.False(await GlobalHotkey.WaitForActivationAsync(operation, _ =>
        {
            source.Invalidate();
            return true;
        }));
    }

    [Fact]
    public async Task RestoredFocus_DoesNotRetryOrAcceptChangedSelection()
    {
        int selectionReads = 0;
        var operation = new SelectionOperationSource().Begin(Target)
            .WithSelectionValidation(() => Task.FromResult(++selectionReads > 1));
        var selection = new SelectionSnapshot("The amber lantern is beside the window.",
            TextAnalysis.PlainText, operation, false, SelectionProviderKind.Browser);
        var action = new RecordingAction();
        Assert.True(await GlobalHotkey.WaitForActivationAsync(operation, _ => true));

        Assert.False((await ActionRunner.ExecuteAsync(action, selection)).Success);
        Assert.Equal(1, selectionReads);
        Assert.Equal(0, action.Executions);
    }

    private sealed class RecordingAction : IAction
    {
        public int Executions { get; private set; }
        public string Id => "recording";
        public string Name => "Recording action";
        public string IconKey => "TranslateIcon";
        public ActionCategory Category => ActionCategory.Context;
        public bool CanExecute(string text, TextAnalysis analysis) => true;
        public ActionResult Execute(string text, TextAnalysis analysis)
        {
            Executions++;
            return new(true);
        }
    }
}
