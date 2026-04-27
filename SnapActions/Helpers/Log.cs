using System.Diagnostics;
using System.IO;

namespace SnapActions.Helpers;

/// <summary>
/// Small file logger writing to %AppData%\SnapActions\logs\YYYY-MM-DD.log.
/// Thread-safe via a simple lock — volume is low (errors / lifecycle events).
/// Old logs (more than 7 days) are deleted on first write each session.
/// </summary>
public static class Log
{
    private static readonly object _lock = new();
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapActions", "logs");
    private static bool _retentionRunForSession;

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) =>
        Write("ERR ", ex != null ? $"{msg} :: {ex.GetType().Name}: {ex.Message}" : msg);

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {msg}";
        Trace.WriteLine($"[SnapActions] {line}");
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(LogDir);
                if (!_retentionRunForSession)
                {
                    PruneOldLogs();
                    _retentionRunForSession = true;
                }
                var file = Path.Combine(LogDir, $"{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch { /* logging must never throw */ }
    }

    private static void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var f in Directory.EnumerateFiles(LogDir, "*.log"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch { }
    }
}
