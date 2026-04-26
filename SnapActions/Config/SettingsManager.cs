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
        MigrateActionIds();
    }

    private static void MigrateActionIds()
    {
        // wrap_wrap_X -> wrap_X (B10 fix in WrapAction.Id)
        for (int i = 0; i < Current.PinnedActionIds.Count; i++)
            Current.PinnedActionIds[i] = MigrateId(Current.PinnedActionIds[i]);
        for (int i = 0; i < Current.DisabledActionIds.Count; i++)
            Current.DisabledActionIds[i] = MigrateId(Current.DisabledActionIds[i]);
    }

    private static string MigrateId(string id) =>
        id.StartsWith("wrap_wrap_", StringComparison.Ordinal) ? id["wrap_".Length..] : id;

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
            // Atomic write: temp file + replace, so a crash mid-write can't blank settings.json
            var tmp = SettingsFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, SettingsFile, overwrite: true);
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
