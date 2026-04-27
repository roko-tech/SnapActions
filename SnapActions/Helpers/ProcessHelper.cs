using System.Diagnostics;
using System.IO;
using SnapActions.Actions;

namespace SnapActions.Helpers;

public static class ProcessHelper
{
    // Schemes we'll happily hand to the shell. javascript:, vbscript:, file:, and any custom
    // protocol handlers can do real damage when fed text from a remote source — keep the list tight.
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "ftp", "ftps", "mailto"
    };

    public static ActionResult TryShellOpen(string uri, string successMessage = "Opened")
    {
        if (!IsAllowed(uri))
            return new ActionResult(false, Message: "Refusing to open: unsupported URI scheme");

        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return new ActionResult(true, Message: successMessage);
        }
        catch (Exception ex)
        {
            return new ActionResult(false, Message: ex.Message);
        }
    }

    // Extensions where launching may run code — confirm with the user first.
    private static readonly HashSet<string> RiskyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".scr", ".com", ".pif", ".reg", ".lnk"
    };

    /// <summary>
    /// Opens a local file or directory through Explorer. Validates that the path exists and
    /// is not a remote URL — keep this distinct from the URL allow-list above.
    /// </summary>
    public static ActionResult TryOpenLocalPath(string path, string successMessage = "Opened")
    {
        if (string.IsNullOrWhiteSpace(path)) return new ActionResult(false, Message: "Empty path");
        if (!File.Exists(path) && !Directory.Exists(path))
            return new ActionResult(false, Message: "Path not found");

        // For risky executable extensions, ask before launching. The user just selected text from
        // somewhere — it would be bad to silently run an attacker-supplied path.
        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path);
            if (RiskyExtensions.Contains(ext))
            {
                var msg = $"This will execute:\n\n{path}\n\nAre you sure?";
                var answer = System.Windows.MessageBox.Show(msg, "Run executable?",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No);
                if (answer != System.Windows.MessageBoxResult.Yes)
                    return new ActionResult(false, Message: "Cancelled");
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return new ActionResult(true, Message: successMessage);
        }
        catch (Exception ex)
        {
            return new ActionResult(false, Message: ex.Message);
        }
    }

    private static bool IsAllowed(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        // Some callers pass bare hostnames like "www.example.com" — treat them as http(s).
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return AllowedSchemes.Contains(parsed.Scheme);
        return false;
    }
}
