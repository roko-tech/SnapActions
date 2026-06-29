using System.Collections.Generic;
using System.Linq;
using SnapActions.Actions;
using SnapActions.Actions.UserActions;
using SnapActions.Config;
using SnapActions.Detection;
using Xunit;

namespace SnapActions.Tests;

public class UserActionTests
{
    // ── User recipe actions (data-driven custom actions) ─────────

    [Fact]
    public void UserRecipeAction_AppliesToAny_WhenTypeBlank()
    {
        var a = new UserRecipeAction(new UserAction { Id = "a", Name = "A", UrlTemplate = "https://x/{0}" });
        Assert.True(a.CanExecute("hello", TextAnalysis.PlainText));
    }

    [Fact]
    public void UserRecipeAction_AppliesToType_Filters()
    {
        var urlOnly = new UserRecipeAction(new UserAction
        { Id = "u", Name = "U", UrlTemplate = "https://x/{0}", AppliesToType = "Url" });
        Assert.False(urlOnly.CanExecute("hello", TextAnalysis.PlainText));
        Assert.True(urlOnly.CanExecute("http://e.com", new TextAnalysis(TextType.Url, 0.95)));
    }

    [Theory]
    [InlineData("{\"name\":\"octocat\"}", "name", "octocat")]
    [InlineData("{\"data\":{\"title\":\"hi\"}}", "data.title", "hi")]
    [InlineData("{\"name\":\"x\"}", "missing", "(field not found)")]
    [InlineData("not json", "name", "(invalid JSON response)")]
    [InlineData("{\"n\":\"keep\"}", "", "{\"n\":\"keep\"}")] // empty field returns raw body
    public void UserRecipeAction_ExtractField(string body, string field, string expected) =>
        Assert.Equal(expected, UserRecipeAction.ExtractField(body, field));

    [Fact]
    public void GetActions_IncludesEnabledUserActions_AndSkipsDisabled()
    {
        var registry = new ActionRegistry();
        var list = SettingsManager.Current.UserActions;
        try
        {
            list.Add(new UserAction { Id = "t_on", Name = "On", UrlTemplate = "https://x/{0}", Enabled = true });
            list.Add(new UserAction { Id = "t_off", Name = "Off", UrlTemplate = "https://x/{0}", Enabled = false });
            var ids = registry.GetActions("hello", TextAnalysis.PlainText, null)
                .SelectMany(g => g.Actions).Select(a => a.Id).ToList();
            Assert.Contains("user_t_on", ids);
            Assert.DoesNotContain("user_t_off", ids);
        }
        finally { list.RemoveAll(u => u.Id is "t_on" or "t_off"); }
    }

    // ── App-aware profiles (per-app hidden actions) ──────────────

    [Fact]
    public void GetActions_AppProfile_HidesActionForThatAppOnly()
    {
        var registry = new ActionRegistry();
        var analysis = new TextAnalysis(TextType.MathExpression, 0.9);
        bool HasCalc(string? app) => registry.GetActions("2+2", analysis, app)
            .SelectMany(g => g.Actions).Any(a => a.Id == "calculate");

        Assert.True(HasCalc(null)); // baseline — Calculate is offered for a math expression

        var map = SettingsManager.Current.AppHiddenActions;
        try
        {
            map["TestApp"] = new List<string> { "calculate" };
            Assert.False(HasCalc("TestApp"));  // hidden for the configured app
            Assert.True(HasCalc("OtherApp"));  // unaffected elsewhere
            Assert.True(HasCalc(null));        // unaffected with no app context
        }
        finally { map.Remove("TestApp"); }
    }

    [Fact]
    public void GetAllKnownActionIds_IncludesUserActions()
    {
        var list = SettingsManager.Current.UserActions;
        try
        {
            list.Add(new UserAction { Id = "k1", Name = "K", UrlTemplate = "https://x/{0}" });
            var ids = ActionRegistry.GetAllKnownActionIds(SettingsManager.Current.SearchEngines);
            Assert.Contains("user_k1", ids);
        }
        finally { list.RemoveAll(u => u.Id == "k1"); }
    }
}
