using SnapActions.Core;
using Xunit;

namespace SnapActions.Tests;

public class SelectionValidationTests
{
    [Fact]
    public async Task RetryGate_StillAdmitsOnlyOneActionAfterAnAllowedRetry()
    {
        var gate = new OperationActionGate();
        Assert.True(gate.TryStart());
        Assert.False(gate.TryStart());
        gate.AllowRetry();
        var attempts = await Task.WhenAll(Task.Run(gate.TryStart), Task.Run(gate.TryStart));
        Assert.Single(attempts, started => started);
        Assert.False(gate.TryStart());
    }

    [Fact]
    public async Task MissingInputEvidence_KeepsCapturedTextUsableButRejectsMutation()
    {
        var operation = new SelectionOperationSource().Begin(default);
        Assert.True(await operation.CanUseSelectionAsync());
        Assert.False(operation.HasInputValidation);
        Assert.False(operation.ValidateInput());
    }

    [Fact]
    public void InputEvidence_IsRecheckedAfterRangeOrCapabilityChanges()
    {
        bool valid = true;
        var operation = new SelectionOperationSource().Begin(default).WithInputValidation(() => valid);
        Assert.True(operation.ValidateInput());
        valid = false;
        Assert.False(operation.ValidateInput());
    }

    [Fact]
    public void InputEvidence_CannotOutliveInvalidationDuringProviderWork()
    {
        var source = new SelectionOperationSource();
        var operation = source.Begin(default).WithInputValidation(() => { source.Invalidate(); return true; });
        Assert.False(operation.ValidateInput());
    }

    [Fact]
    public void InputEvidence_FailsClosedWhenProviderThrows()
    {
        var operation = new SelectionOperationSource().Begin(default)
            .WithInputValidation(() => throw new InvalidOperationException("provider unavailable"));
        Assert.False(operation.ValidateInput());
    }

    [Fact]
    public void RebindingTargetAndBrowserValidation_PreservesNativeInputEvidence()
    {
        var operation = new SelectionOperationSource().Begin(default).WithInputValidation(() => false)
            .WithTarget(new ForegroundTarget((nint)1, (nint)2, 3, 4, "control"))
            .WithSelectionValidation(() => Task.FromResult(true));
        Assert.True(operation.HasInputValidation);
        Assert.False(operation.ValidateInput());
    }

    [Theory]
    [InlineData(true, true, false, false)] // Explicit read-only ValuePattern beats a permissive text provider.
    [InlineData(true, true, null, false)] // Read-only native Edit control.
    [InlineData(true, false, null, true)]
    [InlineData(true, null, true, false)] // Selectable document, not an editor.
    [InlineData(true, null, false, true)]
    [InlineData(true, null, null, false)] // Unknown capability cannot authorize mutation.
    [InlineData(false, false, false, false)]
    public void Editability_RequiresEnabledAndAffirmativeWritableEvidence(
        bool enabled, bool? valueReadOnly, bool? textReadOnly, bool expected) =>
        Assert.Equal(expected, ForegroundApp.IsEditableEvidence(enabled, valueReadOnly, textReadOnly));
}
