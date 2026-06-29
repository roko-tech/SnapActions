using SnapActions.Helpers;
using Xunit;

namespace SnapActions.Tests;

public class LocaleNumberTests
{
    [Theory]
    [InlineData("1000", 1000)]
    [InlineData("1.5", 1.5)]
    [InlineData("1,5", 1.5)]
    public void TryParse_NoSeparatorOrSimpleDecimal(string s, double expected)
    {
        Assert.True(LocaleNumber.TryParse(s, out var v));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    // Exactly 3 digits after a single separator => thousand separator (American or European).
    [InlineData("1,000", 1000)]
    [InlineData("1.000", 1000)]
    public void TryParse_ThousandHeuristic(string s, double expected)
    {
        Assert.True(LocaleNumber.TryParse(s, out var v));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    // Both separators present — the trailing one is decimal.
    [InlineData("1,000.50", 1000.50)] // American
    [InlineData("1.000,50", 1000.50)] // European (regression: B17 in v1.5.6)
    [InlineData("1,234,567.89", 1234567.89)]
    [InlineData("1.234.567,89", 1234567.89)]
    public void TryParse_BothSeparators(string s, double expected)
    {
        Assert.True(LocaleNumber.TryParse(s, out var v));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a number")]
    public void TryParse_Rejects(string s)
    {
        Assert.False(LocaleNumber.TryParse(s, out _));
    }
}
