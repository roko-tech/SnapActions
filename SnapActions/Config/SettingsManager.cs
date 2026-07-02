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
            // Don't silently overwrite a corrupted settings file — MOVE it aside so the user can
            // recover it AND the next launch starts clean. A copy would leave the corrupt bytes
            // in place: every subsequent launch re-fails the load and spawns another .broken-*
            // backup until something happens to call Save.
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                    var backup = SettingsFile + $".broken-{stamp}";
                    File.Move(SettingsFile, backup, overwrite: true);
                    SnapActions.Helpers.Log.Error($"Settings load failed; corrupted file moved to {backup}", ex);
                }
            }
            catch { /* best effort */ }
            Current = new AppSettings();
        }

        // Migrate: update built-in search engines to latest defaults
        MigrateSearchEngines();
        MigrateActionIds();
        MigrateExcludedAppsDefaults();
        PruneStaleActionIds();
        PruneStaleBackups();
    }

    /// <summary>
    /// Newly-added default ExcludedApps entries, grouped by the version that introduced them.
    /// Each existing settings.json moves forward through this list once — users keep their
    /// own additions, and any entry they explicitly remove stays removed because we record
    /// the highest applied version in ExcludedAppsDefaultsVersion. New entries should
    /// always be appended with the next sequential version.
    /// </summary>
    private static readonly (int Version, string[] Apps)[] ExcludedAppsDefaultsHistory =
    {
        // v1.6.15: PotPlayer reacts to a synthetic Ctrl+Insert (the capture-cascade's last
        // resort) as a non-copy shortcut, and its custom-chrome title bar reports as client
        // area to NCHITTEST so a drag-as-title-bar isn't suppressed earlier. Excluding it
        // entirely is the surgical fix for the user-reported interference; users who do want
        // capture in PotPlayer can remove these from Settings → Excluded apps.
        (Version: 1, Apps: new[] { "PotPlayerMini64", "PotPlayerMini" }),
    };

    private static void MigrateExcludedAppsDefaults()
    {
        foreach (var (version, apps) in ExcludedAppsDefaultsHistory)
        {
            if (Current.ExcludedAppsDefaultsVersion >= version) continue;
            foreach (var app in apps)
            {
                if (!Current.ExcludedApps.Any(e => e.Equals(app, StringComparison.OrdinalIgnoreCase)))
                    Current.ExcludedApps.Add(app);
            }
            Current.ExcludedAppsDefaultsVersion = version;
        }
    }

    /// <summary>
    /// Keep only the 5 most recent settings.json.broken-* backups. Without this, repeated load
    /// failures (dying disk, AV scanner racing) accumulate junk in %AppData%\SnapActions forever.
    /// </summary>
    private static void PruneStaleBackups()
    {
        try
        {
            if (!Directory.Exists(SettingsDir)) return;
            const int keep = 5;
            var files = Directory.EnumerateFiles(SettingsDir, "settings.json.broken-*")
                .Select(f => (path: f, time: File.GetLastWriteTimeUtc(f)))
                .OrderByDescending(x => x.time)
                .Skip(keep)
                .ToList();
            foreach (var (path, _) in files)
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
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
        // Same for per-app hidden lists; drop now-empty app entries so they don't accumulate.
        foreach (var app in Current.AppHiddenActions.Keys.ToList())
        {
            Current.AppHiddenActions[app].RemoveAll(id => !validIds.Contains(id));
            if (Current.AppHiddenActions[app].Count == 0)
                Current.AppHiddenActions.Remove(app);
        }
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
                // Refresh built-in templates so URL/LangMode bug fixes flow into existing installs.
                // The Settings UI doesn't expose template editing for built-ins, so there are no
                // user customizations on built-ins to preserve.
                saved.UrlTemplate = def.UrlTemplate;
                saved.LangMode = def.LangMode;
            }
            else
            {
                // New built-in engine, add it
                existing.Add(def);
            }
        }
    }

    // Save is currently called only from the WPF UI thread (Settings handlers, toolbar edits,
    // tray menu, post-SetAutoStart). Keep this lock as a defense-in-depth guard so a future
    // non-UI-thread caller can't clobber the .tmp file mid-write — but note that the lock does
    // NOT protect against a UI-thread mutation of SettingsManager.Current racing with this Save
    // (e.g. JsonSerializer.Serialize iterating a List that's being modified). The single-threaded
    // invariant above is what keeps the serializer safe; the lock is only for the file write.
    private static readonly object _saveLock = new();

    public static void Save()
    {
        // Enforce the single-threaded invariant the serializer relies on: Current is mutated only on
        // the UI dispatcher, so Save must run there too — otherwise JsonSerializer.Serialize can
        // iterate a List<T> that a UI-thread edit is concurrently mutating and throw mid-write.
        System.Diagnostics.Debug.Assert(
            System.Windows.Application.Current?.Dispatcher.CheckAccess() ?? true,
            "SettingsManager.Save must run on the UI dispatcher (Current is mutated there single-threaded).");

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

    // Defense-in-depth. Today both call sites (SettingsWindow checkbox handler + tray menu)
    // run on the WPF UI dispatcher, so concurrent SetAutoStart can't actually happen — the
    // single-threaded dispatcher serializes them for free. The lock survives any future caller
    // that moves back to Task.Run, keeping the registry write + Save serialized so rapid
    // toggles can't produce a settings.json that disagrees with the registry value.
    private static readonly object _autoStartLock = new();

    public static void SetAutoStart(bool enable)
    {
        lock (_autoStartLock)
        {
        // Apply the registry change first, then commit Current/Save only on success — otherwise
        // a failed registry write would leave the in-memory flag and disk file out of sync with
        // reality.
        bool applied = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
            {
                // Environment.ProcessPath is null when running under a non-PE host (e.g.
                // dotnet some.dll). Without this guard the registry would get an empty quoted
                // string and Windows would silently fail to autostart anything.
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    SnapActions.Helpers.Log.Warn("SetAutoStart: ProcessPath is null/empty; skipping registry write");
                    return;
                }
                key.SetValue("SnapActions", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("SnapActions", false);
            }
            applied = true;
        }
        catch (Exception ex)
        {
            SnapActions.Helpers.Log.Warn($"SetAutoStart: registry write failed: {ex.Message}");
        }

        if (applied)
        {
            Current.AutoStart = enable;
            Save();
        }
        }
    }
}
