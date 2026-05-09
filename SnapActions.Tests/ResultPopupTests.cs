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
}
