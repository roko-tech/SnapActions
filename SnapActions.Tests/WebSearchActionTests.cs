using SnapActions.Actions.SearchActions;
using SnapActions.Config;
using Xunit;

namespace SnapActions.Tests;

public class WebSearchActionTests
{
    // ── Basic substitution ──────────────────────────────────────

    [Fact]
    public void BuildUrl_SubstitutesQueryAndEscapes()
    {
        var action = new WebSearchAction("g", "Google", "icon",
            "https://www.google.com/search?q={0}");
        Assert.Equal("https://www.google.com/search?q=hello%20world", action.BuildUrl("hello world"));
    }

    [Fact]
    public void BuildUrl_TrimsQueryWhitespace()
    {
        var action = new WebSearchAction("g", "Google", "icon",
            "https://www.google.com/search?q={0}");
        Assert.Equal("https://www.google.com/search?q=hello", action.BuildUrl("  hello  "));
    }

    // ── LangMode.Url with lang ──────────────────────────────────

    [Fact]
    public void BuildUrl_LangUrlMode_SubstitutesLanguage()
    {
        var action = new WebSearchAction("g", "Google", "icon",
            "https://www.google.com/search?q={0}&hl={1}", "fr", LangMode.Url);
        Assert.Equal("https://www.google.com/search?q=test&hl=fr", action.BuildUrl("test"));
    }

    [Fact]
    public void BuildUrl_LangUrlMode_HostPosition()
    {
        var action = new WebSearchAction("w", "Wikipedia", "icon",
            "https://{1}.wikipedia.org/w/index.php?search={0}", "ar", LangMode.Url);
        Assert.Equal("https://ar.wikipedia.org/w/index.php?search=test", action.BuildUrl("test"));
    }

    // ── LangMode.Url with empty lang — fallback paths ────────────

    [Fact]
    public void BuildUrl_LangUrlMode_EmptyLang_HostFallsBackToEn()
    {
        // Wikipedia template with empty lang should default to en. — never produce ".wikipedia.org"
        var action = new WebSearchAction("w", "Wikipedia", "icon",
            "https://{1}.wikipedia.org/w/index.php?search={0}", "", LangMode.Url);
        Assert.Equal("https://en.wikipedia.org/w/index.php?search=test", action.BuildUrl("test"));
    }

    [Fact]
    public void BuildUrl_LangUrlMode_EmptyLang_DropsLangParam()
    {
        // Param like &lr=lang_{1} must be dropped entirely when lang is empty.
        var action = new WebSearchAction("g", "Google", "icon",
            "https://www.google.com/search?q={0}&lr=lang_{1}&hl={1}", "", LangMode.Url);
        // Both {1}-bearing params dropped; trailing & cleaned up.
        Assert.Equal("https://www.google.com/search?q=test", action.BuildUrl("test"));
    }

    [Fact]
    public void BuildUrl_LangUrlMode_EmptyLang_TrimsTrailingAmp()
    {
        var action = new WebSearchAction("g", "Google", "icon",
            "https://example.com/?q={0}&hl={1}", "", LangMode.Url);
        Assert.Equal("https://example.com/?q=test", action.BuildUrl("test"));
    }

    // ── LangMode.Query (Twitter-style) ──────────────────────────

    [Fact]
    public void BuildUrl_LangQueryMode_AppendsLangColon()
    {
        var action = new WebSearchAction("x", "Twitter/X", "icon",
            "https://x.com/search?q={0}&f=top", "ja", LangMode.Query);
        Assert.Equal("https://x.com/search?q=hello%20lang%3Aja&f=top", action.BuildUrl("hello"));
    }

    [Fact]
    public void BuildUrl_LangQueryMode_EmptyLang_NoSuffix()
    {
        var action = new WebSearchAction("x", "Twitter/X", "icon",
            "https://x.com/search?q={0}&f=top", "", LangMode.Query);
        Assert.Equal("https://x.com/search?q=hello&f=top", action.BuildUrl("hello"));
    }

    // ── LangMode.None (DuckDuckGo, Reddit, etc.) ─────────────────

    [Fact]
    public void BuildUrl_LangNone_IgnoresLang()
    {
        var action = new WebSearchAction("ddg", "DuckDuckGo", "icon",
            "https://duckduckgo.com/?q={0}", "fr", LangMode.None);
        Assert.Equal("https://duckduckgo.com/?q=test", action.BuildUrl("test"));
    }

    // ── Special characters ──────────────────────────────────────

    [Fact]
    public void BuildUrl_QueryWithSpecialChars_GetsEscaped()
    {
        var action = new WebSearchAction("g", "Google", "icon",
            "https://www.google.com/search?q={0}");
        // & and = inside the query must be %-encoded so they don't break the URL structure
        Assert.Equal("https://www.google.com/search?q=a%26b%3Dc", action.BuildUrl("a&b=c"));
    }
}
