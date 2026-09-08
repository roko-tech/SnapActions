using SnapActions.Actions;
using SnapActions.Config;
using Xunit;

namespace SnapActions.Tests;

public class ToolbarPreferencesTests
{
    private static IAction Action(string id) => new ActionRegistry()
        .GetAllActionsForCategory(ActionCategory.Transform).Single(a => a.Id == id);

    [Fact]
    public void PinningHiddenActionShowsItAndKeepsExistingOrder()
    {
        var settings = new AppSettings { PinnedActionIds = ["case_upper", "delete_text"], DisabledActionIds = ["paste_plain"] };
        ToolbarPreferences.Pin(settings, Action("paste_plain"), "delete_text");
        Assert.Equal(["case_upper", "paste_plain", "delete_text"], settings.PinnedActionIds);
        Assert.DoesNotContain("paste_plain", settings.DisabledActionIds);
    }

    [Theory]
    [InlineData("case_upper", "paste_plain", true, "delete_text,paste_plain,case_upper")]
    [InlineData("paste_plain", "case_upper", false, "paste_plain,case_upper,delete_text")]
    [InlineData("case_upper", "delete_text", true, "delete_text,case_upper,paste_plain")]
    [InlineData("delete_text", "delete_text", true, "case_upper,delete_text,paste_plain")]
    [InlineData("case_upper", null, false, "delete_text,paste_plain,case_upper")]
    public void DropPositionReordersWithoutDuplicates(string id, string? target, bool after, string expected)
    {
        var settings = new AppSettings { PinnedActionIds = ["case_upper", "delete_text", "paste_plain"] };
        ToolbarPreferences.Pin(settings, Action(id), target, after);
        Assert.Equal(expected.Split(','), settings.PinnedActionIds);
    }

    [Fact]
    public void HideAndShowKeepTheSavedPinPosition()
    {
        var settings = new AppSettings { PinnedActionIds = ["delete_text", "paste_plain"] };
        var action = Action("delete_text");
        ToolbarPreferences.SetHidden(settings, action, true);
        Assert.True(ToolbarPreferences.IsHidden(settings, action));
        ToolbarPreferences.SetHidden(settings, action, false);
        Assert.False(ToolbarPreferences.IsHidden(settings, action));
        Assert.Equal(["delete_text", "paste_plain"], settings.PinnedActionIds);
    }

    [Fact]
    public void PinRestoresSearchEngineAndSettingsCanEnableItAfterHiding()
    {
        var settings = new AppSettings();
        var engine = settings.SearchEngines.Single(e => e.Id == "google");
        engine.Enabled = false;
        settings.DisabledActionIds.Add("search_google");
        var action = new ActionRegistry().GetAllActionsForCategory(ActionCategory.Search).Single(a => a.Id == "search_google");
        ToolbarPreferences.Pin(settings, action);
        Assert.True(engine.Enabled);
        Assert.False(ToolbarPreferences.IsHidden(settings, action));
        ToolbarPreferences.SetHidden(settings, action, true);
        Assert.False(engine.Enabled);
        engine.Enabled = true;
        Assert.False(ToolbarPreferences.IsHidden(settings, action));
    }

    [Fact]
    public void RetiredActionsArePrunedWithoutLosingPasteAndDelete()
    {
        var settings = SettingsManager.Parse("""{"PinnedActionIds":["generate_qr","inspect_text","delete_text","paste_plain"],"DisabledActionIds":["generate_qr","inspect_text"]}""");
        Assert.Equal(["delete_text", "paste_plain"], settings.PinnedActionIds);
        Assert.Empty(settings.DisabledActionIds);
        var ids = new ActionRegistry().AllActionDescriptors().Select(a => a.Id).ToList();
        Assert.DoesNotContain("generate_qr", ids);
        Assert.DoesNotContain("inspect_text", ids);
    }
}
