using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SnapActions.Helpers;

/// <summary>
/// Small file logger writing to %AppData%\SnapActions\logs\YYYY-MM-DD.log (UTC dates).
/// Callers only enqueue: all disk work (create/rotate/prune/append) runs on a dedicated
/// background thread. That matters because Log.Info fires on the mouse-hook thread inside the
/// WH_MOUSE_LL callback (the gate-suppression lines) — synchronous file I/O there delays
/// system-wide input delivery, and a callback that repeatedly exceeds LowLevelHooksTimeout can
/// get the hook silently removed. %AppData% is the Roaming profile, which can be network-backed.
/// Old logs (older than 7 days) are pruned at most once every 24 hours of process uptime.
/// Each log file is capped at MaxBytesPerFile; older content rotates to .1.log, .2.log, etc.
/// </summary>
public static class Log
{
    private const long MaxBytesPerFile = 10L * 1024 * 1024; // 10 MB
    private const int MaxRotatedFiles = 4; // .1, .2, .3, .4

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapActions", "logs");
    // Prune old log files at most once every 24 hours of process uptime — keeps long-running
    // sessions from filling the log dir without doing the work on every write. Touched only by
    // the writer thread.
    private static DateTime _nextPruneUtc = DateTime.MinValue;
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(24);

    private static readonly System.Collections.Concurrent.BlockingCollection<string> _queue =
        new(new System.Collections.Concurrent.ConcurrentQueue<string>());
    private static readonly Thread _writerThread = StartWriter();

    private static Thread StartWriter()
    {
        var t = new Thread(() =>
        {
            foreach (var line in _queue.GetConsumingEnumerable())
                WriteToFile(line);
        })
        { IsBackground = true, Name = "SnapActions.Log" };
        t.Start();
        return t;
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);

    public static void Error(string msg, Exception? ex = null)
    {
        // Include the full ToString() — contains type, message, and stack trace.
        // For NullReferenceException etc., the message alone is useless without the trace.
        var full = ex != null ? $"{msg}\n{ex}" : msg;
        Write("ERR ", full);
    }

    /// <summary>
    /// Drains pending lines and stops the writer thread. Call once when the process is going
    /// away (normal exit, or the terminating unhandled-exception path — the writer is a
    /// background thread, so without this join the final lines can be lost). Lines logged after
    /// this go only to Trace.
    /// </summary>
    public static void Shutdown()
    {
        try
        {
            _queue.CompleteAdding();
            _writerThread.Join(2000);
        }
        catch { /* logging must never throw */ }
    }

    private static void Write(string level, string msg)
    {
        // Timestamp at call time — UTC so logs are unambiguous when shared across timezones.
        var line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {level} {msg}";
        Trace.WriteLine($"[SnapActions] {line}");
        try { _queue.Add(line); }
        catch { /* post-Shutdown — logging must never throw */ }
    }

    // Runs on the writer thread only, which is also what makes the prune/rotate state safe
    // without a lock.
    private static void WriteToFile(string line)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var now = DateTime.UtcNow;
            if (now >= _nextPruneUtc)
            {
                PruneOldLogs();
                _nextPruneUtc = now + PruneInterval;
            }
            var file = Path.Combine(LogDir, $"{now:yyyy-MM-dd}.log");
            RotateIfTooBig(file);
            File.AppendAllText(file, line + Environment.NewLine);
        }
        catch { /* logging must never throw */ }
    }

    private static void RotateIfTooBig(string file)
    {
        try
        {
            if (!File.Exists(file)) return;
            if (new FileInfo(file).Length < MaxBytesPerFile) return;

            // Cascade .3.log -> .4.log, .2 -> .3, .1 -> .2, current -> .1
            for (int i = MaxRotatedFiles - 1; i >= 1; i--)
            {
                var src = $"{file}.{i}";
                var dst = $"{file}.{i + 1}";
                if (File.Exists(src))
                {
                    try { File.Delete(dst); } catch { }
                    File.Move(src, dst);
                }
            }
            try { File.Delete($"{file}.1"); } catch { }
            File.Move(file, $"{file}.1");
        }
        catch { /* rotation is best-effort */ }
    }

    private static void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var f in Directory.EnumerateFiles(LogDir, "*.log*"))
            {
                if (File.GetLastWriteTimeUtc(f) < cutoff)
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch { }
    }
}
