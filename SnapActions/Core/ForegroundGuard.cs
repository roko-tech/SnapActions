using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace SnapActions.Core;

internal readonly record struct ForegroundTarget(
    IntPtr ForegroundWindow,
    IntPtr FocusedWindow,
    uint ProcessId,
    uint ThreadId,
    string? AutomationRuntimeId = null)
{
    internal bool IsComplete =>
        ForegroundWindow != IntPtr.Zero
        && FocusedWindow != IntPtr.Zero
        && ProcessId != 0
        && ThreadId != 0;
}

/// <summary>
/// Starts at most one worker at a time. A timeout stops awaiting the worker but deliberately
/// keeps the gate occupied until the underlying non-cancelable call actually returns.
/// </summary>
internal sealed class SingleFlightWorkerGate
{
    private int _running;

    internal bool TryStart<T>(Func<T> work, out Task<T>? worker)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            worker = null;
            return false;
        }

        try
        {
            worker = Task.Run(() =>
            {
                try
                {
                    return work();
                }
                finally
                {
                    Volatile.Write(ref _running, 0);
                }
            });
            return true;
        }
        catch
        {
            Volatile.Write(ref _running, 0);
            throw;
        }
    }

    internal Task<T> RunBoundedAsync<T>(
        Func<T> work,
        T onBusyOrTimeout,
        int timeoutMs)
        => RunBoundedAsync(
            work,
            onBusyOrTimeout,
            () => Task.Delay(timeoutMs));

    internal async Task<T> RunBoundedAsync<T>(
        Func<T> work,
        T onBusyOrTimeout,
        Func<Task> timeout)
    {
        if (!TryStart(work, out Task<T>? worker))
            return onBusyOrTimeout;

        var completed = await Task.WhenAny(
            worker!, timeout());
        return completed == worker
            ? await worker
            : onBusyOrTimeout;
    }
}

/// <summary>
/// Captures and validates the input target for an operation. The focused child HWND, process ID,
/// and GUI thread ID prevent an Alt-Tab, native same-window focus move, or HWND reuse from
/// redirecting input; a bounded UI Automation runtime ID also distinguishes logical controls
/// sharing one HWND. Mutating/synthetic input requires that logical identity; a timeout or provider
/// failure therefore cancels the action rather than trusting a possibly shared HWND.
/// </summary>
internal static class ForegroundGuard
{
    // Event-time UIA capture runs while the low-level hook still holds the triggering input.
    // Keep this well below the hook timeout; a miss is recorded as unavailable and destructive
    // input then fails closed when the native focused HWND is not granular enough.
    private const int EventAutomationIdentityTimeoutMs = 50;
    private const int AutomationIdentityTimeoutMs = 250;
    private const int AutomationWarmupTimeoutMs = 500;
    private static readonly SingleFlightWorkerGate AutomationWorkers = new();

    /// <summary>
    /// Initializes UIA client/JIT state against the Windows desktop root before hooks are installed.
    /// This deliberately avoids querying whichever third-party provider happens to own focus.
    /// </summary>
    internal static void WarmUpAutomation()
    {
        if (!AutomationWorkers.TryStart(
                () =>
                {
                    try
                    {
                        _ = AutomationElement.RootElement;
                        _ = TextPattern.Pattern;
                    }
                    catch { /* warmup only — real calls retain their normal fallback behavior */ }

                    return true;
                },
                out Task<bool>? worker))
            return;

        worker!.Wait(AutomationWarmupTimeoutMs);
    }

    internal static ForegroundTarget Capture()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return default;

        uint threadId = GetWindowThreadProcessId(foreground, out uint processId);
        if (threadId == 0 || processId == 0) return default;

        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == IntPtr.Zero)
            return default;

        return new ForegroundTarget(foreground, info.hwndFocus, processId, threadId);
    }

    internal static ForegroundTarget CaptureWithAutomationIdentity()
    {
        var target = Capture();
        if (!target.IsComplete) return target;

        if (!AutomationWorkers.TryStart(
                () => Enrich(target), out Task<ForegroundTarget>? worker))
            return target;
        var activeWorker = worker!;
        return activeWorker.Wait(EventAutomationIdentityTimeoutMs)
            ? activeWorker.GetAwaiter().GetResult()
            : target;
    }

    internal static bool StillValid(ForegroundTarget expected) =>
        MatchesWindow(expected, Capture());

    internal static bool Matches(ForegroundTarget expected, ForegroundTarget current) =>
        MatchesWindow(expected, current)
        && (expected.AutomationRuntimeId == null
            || expected.AutomationRuntimeId == current.AutomationRuntimeId);

    internal static bool MatchesWindow(ForegroundTarget expected, ForegroundTarget current) =>
        expected.IsComplete
        && current.IsComplete
        && expected.ForegroundWindow == current.ForegroundWindow
        && expected.FocusedWindow == current.FocusedWindow
        && expected.ProcessId == current.ProcessId
        && expected.ThreadId == current.ThreadId;

    internal static bool HasSufficientInputIdentity(ForegroundTarget target) =>
        target.IsComplete
        && target.AutomationRuntimeId != null;

    internal static async Task<bool> StillValidAsync(ForegroundTarget expected)
    {
        if (!StillValid(expected)) return false;
        if (expected.AutomationRuntimeId == null) return true;

        if (!AutomationWorkers.TryStart(
                () => CaptureAutomationRuntimeId(expected.ProcessId),
                out Task<string?>? worker))
            return false;
        var activeWorker = worker!;
        var completed = await Task.WhenAny(
            activeWorker, Task.Delay(AutomationIdentityTimeoutMs));
        if (completed != activeWorker) return false;

        string? currentRuntimeId = await activeWorker;
        return currentRuntimeId == expected.AutomationRuntimeId
               && StillValid(expected);
    }

    /// <summary>
    /// Captures the full current target and, without another await/scheduler hop, runs a short
    /// commit callback only if it still exactly matches <paramref name="expected"/>. If UIA
    /// exceeds the bound, a gate prevents the abandoned worker from committing input later.
    /// </summary>
    internal static async Task<bool> TryRunWithExactInputTargetAsync(
        ForegroundTarget expected, Func<ForegroundTarget, bool> commit)
    {
        if (!HasSufficientInputIdentity(expected)) return false;

        int gate = 0;
        if (!AutomationWorkers.TryStart(
                () =>
                {
                    var current = Enrich(Capture());
                    if (!Matches(expected, current)) return false;
                    if (Interlocked.CompareExchange(ref gate, 1, 0) != 0)
                        return false;
                    return commit(current);
                },
                out Task<bool>? worker))
            return false;

        var activeWorker = worker!;
        var completed = await Task.WhenAny(
            activeWorker, Task.Delay(AutomationIdentityTimeoutMs));
        if (completed == activeWorker) return await activeWorker;

        // If the worker has not crossed the commit gate, abandon it permanently. If it already
        // crossed, its callback is the bounded final check + SendInput sequence, so await it.
        if (Interlocked.CompareExchange(ref gate, 2, 0) == 0) return false;
        return await activeWorker;
    }

    internal static Task<T> RunBoundedAutomationAsync<T>(
        Func<T> work,
        T onBusyOrTimeout,
        int timeoutMs) =>
        AutomationWorkers.RunBoundedAsync(
            work, onBusyOrTimeout, timeoutMs);

    private static ForegroundTarget Enrich(ForegroundTarget target)
    {
        if (!StillValid(target)) return target;
        string? runtimeId = CaptureAutomationRuntimeId(target.ProcessId);
        return runtimeId != null && StillValid(target)
            ? target with { AutomationRuntimeId = runtimeId }
            : target;
    }

    private static string? CaptureAutomationRuntimeId(uint expectedProcessId)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null || (uint)element.Current.ProcessId != expectedProcessId)
                return null;

            int[] runtimeId = element.GetRuntimeId();
            return runtimeId.Length == 0 ? null : string.Join(",", runtimeId);
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
}
