using System.Diagnostics;

namespace SnapActions.Core;

internal static class CaptureDiagnostics
{
    private static readonly object Sync = new();
    private static readonly Queue<(string Stage, double Milliseconds)> Samples = new();
    private static string _status = "No capture attempted yet";
    private static long _uiaCompleted, _uiaBusy, _uiaTimedOut;
    internal static int ConnectedBrowsers;

    internal static void UiaOutcome(bool busy = false, bool timedOut = false)
    {
        if (busy) Interlocked.Increment(ref _uiaBusy);
        else if (timedOut) Interlocked.Increment(ref _uiaTimedOut);
        else Interlocked.Increment(ref _uiaCompleted);
    }

    internal static void SetStatus(string status) { lock (Sync) _status = status; }
    internal static void Record(string stage, long started)
    {
        lock (Sync)
        {
            if (Samples.Count == 256) Samples.Dequeue();
            Samples.Enqueue((stage, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        }
    }

    internal static string Summary()
    {
        lock (Sync)
        {
            var lines = new List<string>
            {
                $"Browser connections: {Volatile.Read(ref ConnectedBrowsers)}",
                $"Last capture: {_status}",
                $"Keyboard palette: {(GlobalHotkey.IsRegistered ? "Ctrl+Shift+Space registered" : "shortcut unavailable or not started")}",
                $"Bounded UIA calls: {Interlocked.Read(ref _uiaCompleted)} completed, {Interlocked.Read(ref _uiaBusy)} busy, {Interlocked.Read(ref _uiaTimedOut)} timed out"
            };
            foreach (var stage in Samples.GroupBy(s => s.Stage))
            {
                var values = stage.Select(s => s.Milliseconds).Order().ToArray();
                lines.Add($"{stage.Key}: median {values[values.Length / 2]:F1} ms, p95 {values[(int)Math.Ceiling(values.Length * .95) - 1]:F1} ms ({values.Length} samples)");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
