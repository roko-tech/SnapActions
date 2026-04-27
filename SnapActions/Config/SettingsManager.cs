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
        catch (Exception ex)
        {
            // Don't silently overwrite a corrupted settings file — back it up so the user can recover.
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    var backup = SettingsFile + $".broken-{stamp}";
                    File.Copy(SettingsFile, backup, overwrite: true);
                    SnapActions.Helpers.Log.Error($"Settings load failed; backed up to {backup}", ex);
                }
            }
            catch { /* best effort */ }
            Current = new AppSettings();
        }

        // Migrate: update built-in search engines to latest defaults
        MigrateSearchEngines();
        MigrateActionIds();
        PruneStaleActionIds();
    }

    /// <summary>
    /// Drops Pinned/Disabled IDs that no longer correspond to any action. Avoids unbounded growth
    /// when users delete custom search engines or after action renames in future versions.
    /// </summary>
    private static void PruneStaleActionIds()
    {
        // Single source of truth: ask the registry for the full ID list.
        var validIds = SnapActions.Actions.ActionRegistry.GetAllKnownActionIds(Current.SearchEngines);
        Current.PinnedActionIds.RemoveAll(id => !validIds.Contains(id));
        Current.DisabledActionIds.RemoveAll(id => !validIds.Contains(id));
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
                saved.IsBuiltIn = true;
                // Don't overwrite user-edited template/mode. Without this, every Load reverts
                // any local tweak a user made to a built-in engine's URL or LangMode.
                if (!saved.UserModified)
                {
                    saved.UrlTemplate = def.UrlTemplate;
                    saved.LangMode = def.LangMode;
                }
            }
            else
            {
                // New built-in engine, add it
                existing.Add(def);
            }
        }
    }

    // Save can be invoked from multiple threads (auto-save Task.Run, in-toolbar edits, tray menu).
    // The temp-file + Move dance isn't safe under concurrent writers — both could clobber the .tmp.
    private static readonly object _saveLock = new();

    public static void Save()
    {
        lock (_saveLock)
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
            catch (Exception ex)
            {
                SnapActions.Helpers.Log.Error("Failed to save settings", ex);
            }
        }
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
