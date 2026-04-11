using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace SnapActions.Config;

public static class SettingsManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapActions");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();
            }
        }
        catch
        {
            Current = new AppSettings();
        }

        // Migrate: update built-in search engines to latest defaults
        MigrateSearchEngines();
    }

    private static void MigrateSearchEngines()
    {
        var defaults = AppSettings.GetDefaultEngines();
        var existing = Current.SearchEngines;

        foreach (var def in defaults)
        {
            var saved = existing.FirstOrDefault(e => e.Id == def.Id);
            if (saved != null)
            {
                // Update URL template and LangMode from defaults (user keeps Enabled state)
                saved.UrlTemplate = def.UrlTemplate;
                saved.LangMode = def.LangMode;
                saved.IsBuiltIn = true;
            }
            else
            {
                // New built-in engine, add it
                existing.Add(def);
            }
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }

    public static void SetAutoStart(bool enable)
    {
        Current.AutoStart = enable;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? "";
                key.SetValue("SnapActions", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("SnapActions", false);
            }
        }
        catch { }
        Save();
    }
}
