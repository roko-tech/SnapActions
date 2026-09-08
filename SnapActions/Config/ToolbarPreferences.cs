using SnapActions.Actions;

namespace SnapActions.Config;

internal static class ToolbarPreferences
{
    internal static bool IsHidden(AppSettings settings, IAction action) =>
        settings.DisabledActionIds.Contains(action.Id)
        || settings.SearchEngines.Any(e => action.Id == $"search_{e.Id}" && !e.Enabled)
        || settings.UserActions.Any(a => action.Id == $"user_{a.Id}" && !a.Enabled)
        || settings.TextRecipes.Any(r => action.Id == $"recipe_{r.Id}" && !r.Enabled);

    internal static void SetHidden(AppSettings settings, IAction action, bool hidden)
    {
        settings.DisabledActionIds.RemoveAll(id => id == action.Id);
        // Showing or pinning an action also restores its underlying custom action or engine.
        var engine = settings.SearchEngines.FirstOrDefault(e => action.Id == $"search_{e.Id}");
        var custom = settings.UserActions.FirstOrDefault(a => action.Id == $"user_{a.Id}");
        var recipe = settings.TextRecipes.FirstOrDefault(r => action.Id == $"recipe_{r.Id}");
        if (engine != null) engine.Enabled = !hidden;
        else if (custom != null) custom.Enabled = !hidden;
        else if (recipe != null) recipe.Enabled = !hidden;
        else if (hidden) settings.DisabledActionIds.Add(action.Id);
    }

    internal static void Pin(AppSettings settings, IAction action, string? relativeTo = null, bool after = false)
    {
        SetHidden(settings, action, false);
        var pinned = settings.PinnedActionIds;
        if (relativeTo == action.Id && pinned.Contains(action.Id)) return;
        pinned.RemoveAll(id => id == action.Id);
        int index = relativeTo == null ? -1 : pinned.IndexOf(relativeTo);
        pinned.Insert(index < 0 ? pinned.Count : index + (after ? 1 : 0), action.Id);
    }
}
