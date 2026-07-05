using SnapActions.UI;
using Xunit;

namespace SnapActions.Tests;

/// <summary>
/// Pure-function tests for ResultPopup helpers. Network-touching paths
/// (FetchTranslation/FetchDefinition/FetchCurrencyConversion) need a stubbed
/// HttpClient and stay out of scope here.
/// </summary>
public class ResultPopupTests
{
    private static (int start, int length) NumPos(string text, string number)
    {
        int idx = text.IndexOf(number, System.StringComparison.Ordinal);
        return (idx, number.Length);
    }

    [Fact]
    public void DetectSourceCurrency_PrefersAdjacentSymbol()
    {
        // $ is adjacent to 50; EUR is far away. The proximity heuristic should pick USD.
        var (s, l) = NumPos("$50 last EUR-trip", "50");
        Assert.Equal("USD", ResultPopup.DetectSourceCurrency("$50 last EUR-trip", s, l));
    }

    [Fact]
    public void DetectSourceCurrency_TrailingCode()
    {
        var (s, l) = NumPos("100 USD", "100");
        Assert.Equal("USD", ResultPopup.DetectSourceCurrency("100 USD", s, l));
    }

    [Fact]
    public void DetectSourceCurrency_LeadingCode()
    {
        var (s, l) = NumPos("EUR 200", "200");
        Assert.Equal("EUR", ResultPopup.DetectSourceCurrency("EUR 200", s, l));
    }

    [Theory]
    [InlineData("€1500", "EUR")]
    [InlineData("£99.99", "GBP")]
    [InlineData("¥10000", "JPY")]
    public void DetectSourceCurrency_RecognizesUnicodeSymbols(string text, string expected)
    {
        var num = System.Text.RegularExpressions.Regex.Match(text, @"[\d][\d.,]*").Value;
        var (s, l) = NumPos(text, num);
        Assert.Equal(expected, ResultPopup.DetectSourceCurrency(text, s, l));
    }

    [Fact]
    public void DetectSourceCurrency_FallsBackToUsdWhenNoSymbol()
    {
        var (s, l) = NumPos("just 100 monkeys", "100");
        Assert.Equal("USD", ResultPopup.DetectSourceCurrency("just 100 monkeys", s, l));
    }

    // ── DetectSourceLang: cross-script text gets an explicit source (fixes autodetect resolving
    //    to the target and failing with "PLEASE SELECT TWO DISTINCT LANGUAGES") ──

    [Fact]
    public void DetectSourceLang_LatinWordToArabicTarget_IsEnglish()
    {
        // The reported bug: "literacy" selected with an Arabic (ar) target. autodetect returned
        // ar (== target) → same-language error. Latin script ≠ Arabic → force en|ar.
        Assert.Equal("en", ResultPopup.DetectSourceLang("literacy", "ar"));
    }

    [Theory]
    [InlineData("مرحبا", "en", "ar")]       // Arabic word, English target → ar|en
    [InlineData("Привет", "en", "ru")]      // Cyrillic → ru|en
    [InlineData("שלום", "en", "he")]        // Hebrew → he|en
    [InlineData("Ελληνικά", "en", "el")]    // Greek → el|en
    public void DetectSourceLang_CrossScript_PicksScriptLanguage(string text, string to, string expected)
    {
        Assert.Equal(expected, ResultPopup.DetectSourceLang(text, to));
    }

    [Theory]
    [InlineData("bonjour", "de")]   // French (Latin) → German (Latin): same script, let MyMemory detect fr
    [InlineData("hola", "en")]      // Spanish (Latin) → English (Latin): same script
    [InlineData("مرحبا", "ar")]     // Arabic → Arabic: same script (genuinely same-language)
    [InlineData("你好", "en")]       // CJK is ambiguous (shared Han) → autodetect
    [InlineData("12345", "ar")]     // no letters → autodetect
    public void DetectSourceLang_SameScriptOrAmbiguous_FallsBackToAutodetect(string text, string to)
    {
        Assert.Equal("autodetect", ResultPopup.DetectSourceLang(text, to));
    }

    [Fact]
    public void DetectSourceLang_MixedText_UsesDominantScript()
    {
        // A mostly-Arabic selection that trails an English word still reads as Arabic-dominant.
        Assert.Equal("ar", ResultPopup.DetectSourceLang("أحيانا أحس media", "en"));
    }
}
