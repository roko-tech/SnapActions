using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using SnapActions.Config;

namespace SnapActions.Services;

internal static class BrowserSetupService
{
    internal const string HostName = "com.snapactions.selection";
    internal static string ExtensionDirectory => Path.Combine(AppContext.BaseDirectory, "browser-extension");
    internal static string ManifestPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SnapActions", "Browser", HostName + ".json");

    internal static IReadOnlyList<string> RegistryPaths(string browser) => browser switch
    {
        "Brave" => [@"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\" + HostName,
                    @"Software\Google\Chrome\NativeMessagingHosts\" + HostName],
        "Chrome" => [@"Software\Google\Chrome\NativeMessagingHosts\" + HostName],
        "Edge" => [@"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName],
        _ => throw new ArgumentException("Choose Brave, Chrome, or Edge.")
    };

    internal static string ManifestJson(string executable) => JsonSerializer.Serialize(new
    {
        name = HostName, description = "SnapActions clipboard-free browser selection",
        path = executable, type = "stdio", allowed_origins = new[] { Core.BrowserNativeHost.ExtensionOrigin }
    }, new JsonSerializerOptions { WriteIndented = true });

    internal static void Register(string browser)
    {
        if (RuntimePaths.IsIsolated) throw new InvalidOperationException("Browser registration is disabled in isolated test instances.");
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
        if (!Path.GetFileName(executable).Equals("SnapActions.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Launch the published SnapActions.exe before registering the browser helper.");
        if (!File.Exists(Path.Combine(ExtensionDirectory, "manifest.json")))
            throw new InvalidOperationException("The browser-extension folder is missing. Extract the complete release package first.");
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        var temporary = ManifestPath + ".tmp";
        File.WriteAllText(temporary, ManifestJson(executable));
        File.Move(temporary, ManifestPath, true);
        foreach (var path in RegistryPaths(browser))
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, true);
            key.SetValue("", ManifestPath);
        }
    }

    internal static string Status(string browser)
    {
        if (RuntimePaths.IsIsolated) return "Isolated test instance — browser registration is disabled.";
        try
        {
            foreach (var path in RegistryPaths(browser))
            {
                using var key = Registry.CurrentUser.OpenSubKey(path);
                if (key?.GetValue("") is not string file || !File.Exists(file)) continue;
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                if (json.RootElement.TryGetProperty("path", out var exe) && exe.GetString() is { } value)
                    return File.Exists(value)
                        ? "Registered helper: " + value + (string.Equals(value, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) ? "" : "\nThis points to a different app location. Register again to use this copy.")
                        : "The registered app was moved or deleted. Register this copy again.";
            }
            return "Browser helper is not registered for " + browser + ".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return "Browser registration could not be read. Register this copy again."; }
    }
}
