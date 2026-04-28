using System.Diagnostics;
using System.IO;

namespace SnapActions.Helpers;

/// <summary>
/// Small file logger writing to %AppData%\SnapActions\logs\YYYY-MM-DD.log (UTC dates).
/// Thread-safe via a simple lock — volume is low (errors / lifecycle events).
/// Old logs (more than 7 days) are deleted on first write each session.
/// Each log file is capped at MaxBytesPerFile; older content rotates to .1.log, .2.log, etc.
/// </summary>
public static class Log
{
    private const long MaxBytesPerFile = 10L * 1024 * 1024; // 10 MB
    private const int MaxRotatedFiles = 4; // .1, .2, .3, .4

    private static readonly object _lock = new();
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapActions", "logs");
    // Prune old log files at most once every 24 hours of process uptime — keeps long-running
    // sessions from filling the log dir without doing the work on every Write call.
    private static DateTime _nextPruneUtc = DateTime.MinValue;
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(24);

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);

    public static void Error(string msg, Exception? ex = null)
    {
        // Include the full ToString() — contains type, message, and stack trace.
        // For NullReferenceException etc., the message alone is useless without the trace.
        var full = ex != null ? $"{msg}\n{ex}" : msg;
        Write("ERR ", full);
    }

    private static void Write(string level, string msg)
    {
        // UTC timestamps so logs are unambiguous when shared across timezones.
        var line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {level} {msg}";
        Trace.WriteLine($"[SnapActions] {line}");
        try
        {
            lock (_lock)
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
