using SnapActions.Helpers;
using Xunit;

namespace SnapActions.Tests;

public class UnitConverterTests
{
    [Theory]
    [InlineData("5 ft", 5, "ft")]
    [InlineData("100 km/h", 100, "km/h")]
    [InlineData("20°C", 20, "°C")]
    [InlineData("20 c", 20, "°C")]
    [InlineData("2 cups", 2, "cup")]
    [InlineData("100 lb", 100, "lb")]
    [InlineData("100 lbs", 100, "lb")]
    [InlineData("1 kg", 1, "kg")]
    [InlineData("5 fl oz", 5, "fl oz")] // regression: B1 in v1.5.6
    [InlineData("5 fl. oz", 5, "fl oz")]
    // Locale handling — the parser must agree with LocaleNumber, not strip commas blindly.
    // Pre-fix, "1,5 kg" silently parsed as 15 kg.
    [InlineData("1,5 kg", 1.5, "kg")]      // European decimal-comma
    [InlineData("1,500 kg", 1500, "kg")]   // American thousands-comma
    [InlineData("1,234.56 kg", 1234.56, "kg")] // American with both
    [InlineData("-40 c", -40, "°C")]       // negative
    public void TryParse_Matches(string text, double expectedValue, string expectedSymbol)
    {
        Assert.True(UnitConverter.TryParse(text, out var v, out var unit));
        Assert.Equal(expectedValue, v);
        Assert.NotNull(unit);
        Assert.Equal(expectedSymbol, unit!.Symbol);
    }

    [Theory]
    [InlineData("not a unit")]
    [InlineData("5 banana")]
    public void TryParse_Rejects(string text) =>
        Assert.False(UnitConverter.TryParse(text, out _, out _));

    // ── Length round-trips ──────────────────────────────────────

    [Fact]
    public void Convert_FtToM()
    {
        UnitConverter.TryParse("1 ft", out _, out var ft);
        UnitConverter.TryParse("1 m", out _, out var m);
        Assert.NotNull(ft); Assert.NotNull(m);
        Assert.Equal(0.3048, UnitConverter.Convert(1, ft!, m!), 6);
    }

    // ── Temperature (offset, not multiplicative) ─────────────────

    [Fact]
    public void Convert_CelsiusToFahrenheit()
    {
        UnitConverter.TryParse("0 c", out _, out var c);
        UnitConverter.TryParse("0 f", out _, out var f);
        Assert.NotNull(c); Assert.NotNull(f);
        // 0°C = 32°F
        Assert.Equal(32, UnitConverter.Convert(0, c!, f!), 4);
        // 100°C = 212°F
        Assert.Equal(212, UnitConverter.Convert(100, c!, f!), 4);
        // -40 is the same in both
        Assert.Equal(-40, UnitConverter.Convert(-40, c!, f!), 4);
    }

    [Fact]
    public void Convert_FahrenheitToKelvin()
    {
        UnitConverter.TryParse("0 f", out _, out var f);
        UnitConverter.TryParse("0 k", out _, out var k);
        Assert.NotNull(f); Assert.NotNull(k);
        // 32°F = 273.15 K
        Assert.Equal(273.15, UnitConverter.Convert(32, f!, k!), 2);
    }

    // ── "ton" is the US short ton, not the metric tonne (regression) ─────

    [Fact]
    public void Convert_ShortTon_Is2000Pounds()
    {
        // "ton"/"tons" previously aliased to the metric tonne (1000 kg). In the US-English context
        // this imperial table targets, "ton" means the short ton = exactly 2000 lb (907.18474 kg).
        Assert.True(UnitConverter.TryParse("3 tons", out var v, out var ton));
        Assert.Equal(3, v);
        Assert.Equal("ton", ton!.Symbol);
        UnitConverter.TryParse("1 lb", out _, out var lb);
        Assert.Equal(6000, UnitConverter.Convert(3, ton!, lb!), 1);
    }

    [Fact]
    public void Convert_Tonne_StaysMetric()
    {
        // "tonne"/"tonnes"/"t" remain the metric tonne = 1000 kg.
        Assert.True(UnitConverter.TryParse("2 tonnes", out _, out var t));
        Assert.Equal("t", t!.Symbol);
        UnitConverter.TryParse("1 kg", out _, out var kg);
        Assert.Equal(2000, UnitConverter.Convert(2, t!, kg!), 6);
    }

    [Fact]
    public void Convert_DifferentCategories_Throws()
    {
        UnitConverter.TryParse("1 m", out _, out var m);
        UnitConverter.TryParse("1 kg", out _, out var kg);
        Assert.NotNull(m); Assert.NotNull(kg);
        Assert.Throws<System.ArgumentException>(() => UnitConverter.Convert(1, m!, kg!));
    }
}
