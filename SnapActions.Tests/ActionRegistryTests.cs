using SnapActions.Actions;
using SnapActions.Config;
using Xunit;

namespace SnapActions.Tests;

public class ActionRegistryTests
{
    [Fact]
    public void GetAllKnownActionIds_IncludesEveryActionInRegistry()
    {
        // Regression: B3 in v1.5.3. The hand-maintained ID list used to drift from the actual
        // registry, silently dropping pinned/disabled entries on Load. Now derived from a
        // transient registry; these two enumerations must always agree.
        var registry = new ActionRegistry();
        var registryIds = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var category in System.Enum.GetValues<ActionCategory>())
            foreach (var a in registry.GetAllActionsForCategory(category))
                registryIds.Add(a.Id);

        var known = ActionRegistry.GetAllKnownActionIds(SettingsManager.Current.SearchEngines);
        foreach (var id in registryIds)
            Assert.Contains(id, known);
    }

    [Fact]
    public void GetAllKnownActionIds_IncludesSearchEngineActions()
    {
        var engines = new List<SearchEngine>
        {
            new() { Id = "google", Name = "Google" },
            new() { Id = "bing", Name = "Bing" },
        };
        var ids = ActionRegistry.GetAllKnownActionIds(engines);
        Assert.Contains("search_google", ids);
        Assert.Contains("search_bing", ids);
    }

    [Fact]
    public void Registry_HasUniqueActionIds()
    {
        // Two actions with the same ID would make Pin/Disable lookups ambiguous.
        var registry = new ActionRegistry();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var category in System.Enum.GetValues<ActionCategory>())
            foreach (var a in registry.GetAllActionsForCategory(category))
                Assert.True(seen.Add(a.Id), $"Duplicate action ID: {a.Id}");
    }
}
