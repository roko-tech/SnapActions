using System.Diagnostics;
using SnapActions.Detection;

namespace SnapActions.Core;

/// <summary>Chooses a provider and binds its result to the original operation before presentation.</summary>
internal sealed class SelectionCoordinator(BrowserSelectionBridge browser)
{
    internal async Task<SelectionSnapshot?> CaptureAsync(SelectionOperation operation,
        UiaSelectionProvider.SelectionGesture gesture, int x, int y)
    {
        long started = Stopwatch.GetTimestamp();
        var captured = await browser.CaptureAsync(operation.Target);
        CaptureDiagnostics.Record("Browser read", started);
        string? text;
        var provider = captured.Handled ? SelectionProviderKind.Browser : SelectionProviderKind.UiAutomation;
        if (captured.Handled)
        {
            text = captured.Text;
            var target = operation.Target;
            operation = operation.WithSelectionValidation(() => browser.StillSelectedAsync(captured, target));
        }
        else
        {
            started = Stopwatch.GetTimestamp();
            var result = await UiaSelectionProvider.CaptureSelectedTextAsync(operation, gesture, x, y);
            CaptureDiagnostics.Record("UIA read", started);
            text = result.Text;
            operation = result.Operation;
        }
        if (!operation.CanInjectInput || string.IsNullOrWhiteSpace(text) || text.Length > SelectionSnapshot.MaximumTextLength)
        {
            if (!captured.Handled) CaptureDiagnostics.SetStatus("UIA selection unavailable, empty, ambiguous, or busy");
            return null;
        }
        bool editable = await ForegroundGuard.RunBoundedAutomationAsync(ForegroundApp.IsEditableFieldFocused, false, 500);
        if (captured.Handled) editable &= captured.Editable;
        if (!operation.CanInjectInput) { CaptureDiagnostics.SetStatus("Selection became stale"); return null; }
        CaptureDiagnostics.SetStatus($"{provider}: selection captured");
        started = Stopwatch.GetTimestamp();
        var analysis = new TextClassifier().Classify(text);
        CaptureDiagnostics.Record("Classification", started);
        return new SelectionSnapshot(text, analysis, operation, editable, provider, captured.RightToLeft);
    }
}
