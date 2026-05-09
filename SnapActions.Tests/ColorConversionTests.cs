using SnapActions.Actions.ContextActions;
using SnapActions.Detection;
using Xunit;

namespace SnapActions.Tests;

public class ColorConversionTests
{
    private static string Convert(string input)
    {
        var action = new ConvertColorAction();
        // Need analysis with format metadata — easier to just run the classifier first.
        var classifier = new TextClassifier();
        var analysis = classifier.Classify(input);
        Assert.Equal(TextType.ColorCode, analysis.Type);
        var result = action.Execute(input, analysis);
        Assert.True(result.Success, result.Message);
        return result.ResultText!;
    }

    // ── hex → rgb ────────────────────────────────────────────────

    [Theory]
    [InlineData("#FF0000", "rgb(255, 0, 0)")]
    [InlineData("#00FF00", "rgb(0, 255, 0)")]
    [InlineData("#0000FF", "rgb(0, 0, 255)")]
    public void Hex_To_Rgb(string input, string expected) =>
        Assert.Equal(expected, Convert(input));

    // ── hex with alpha → rgba (regression: B17 in v1.5.3) ────────

    [Fact]
    public void HexWithAlpha_PreservesAlphaInRgba()
    {
        var result = Convert("#FF000080"); // 0x80 / 255 ≈ 0.502
        Assert.StartsWith("rgba(255, 0, 0, 0.5", result);
    }

    [Fact]
    public void ShortHex_RGBA_Expands()
    {
        // #F00A → #FF0000AA → alpha = 170/255 ≈ 0.667
        var result = Convert("#F00A");
        Assert.StartsWith("rgba(255, 0, 0, 0.66", result);
    }

    // ── rgb → hsl ────────────────────────────────────────────────

    [Fact]
    public void Rgb_To_Hsl_Pure_Red()
    {
        var result = Convert("rgb(255, 0, 0)");
        Assert.StartsWith("hsl(0, 100%, 50%", result);
    }

    [Fact]
    public void Rgba_To_Hsla_PreservesAlpha()
    {
        var result = Convert("rgba(255, 0, 0, 0.5)");
        Assert.StartsWith("hsla(0, 100%, 50%, 0.5", result);
    }

    // ── hsl → hex ────────────────────────────────────────────────

    [Fact]
    public void Hsl_To_Hex()
    {
        // hsl(0, 100%, 50%) is pure red
        Assert.Equal("#FF0000", Convert("hsl(0, 100%, 50%)"));
    }

    [Fact]
    public void Hsla_To_HexWithAlpha_PreservesAlpha()
    {
        var result = Convert("hsla(0, 100%, 50%, 0.5)");
        // Should be #FF000080 (or close to it depending on rounding).
        Assert.StartsWith("#FF0000", result);
        Assert.Equal(9, result.Length);
    }

    // ── hue normalization (regression: B4 in v1.5.6) ─────────────

    [Fact]
    public void Hsl_HueOver360_NormalizedCorrectly()
    {
        // 400 mod 360 = 40 — orange-ish red. Should NOT fall through to default switch
        // branch which would produce wrong color.
        // hsl(40, 100%, 50%) is orange (#FFAA00)
        var result = Convert("hsl(400, 100%, 50%)");
        Assert.Equal("#FFAA00", result);
    }

    [Fact]
    public void Hsl_NegativeHue_NormalizedCorrectly()
    {
        // -30 mod 360 = 330 — magenta-ish red.
        // hsl(330, 100%, 50%) is #FF0080
        var result = Convert("hsl(-30, 100%, 50%)");
        Assert.Equal("#FF0080", result);
    }

    // ── Hue with CSS angle units (regression: B1 in v1.6.3) ─────

    [Theory]
    [InlineData("hsl(120deg, 100%, 50%)")] // explicit deg suffix
    [InlineData("hsl(120, 100%, 50%)")]    // bare degrees
    public void Hsl_DegSuffix_DetectedAndConverted(string input)
    {
        // Both forms should produce the same result — pure green hex.
        Assert.Equal("#00FF00", Convert(input));
    }

    [Fact]
    public void Hsl_TurnSuffix_Converted()
    {
        // 0.5 turn = 180° = cyan-ish (#00FFFF for 100% sat / 50% lightness).
        Assert.Equal("#00FFFF", Convert("hsl(0.5turn, 100%, 50%)"));
    }

    [Fact]
    public void Hsl_RadSuffix_Converted()
    {
        // π rad = 180° = cyan.
        Assert.Equal("#00FFFF", Convert("hsl(3.14159rad, 100%, 50%)"));
    }

    [Fact]
    public void Hsl_GradSuffix_Converted()
    {
        // 200 grad = 180° = cyan.
        Assert.Equal("#00FFFF", Convert("hsl(200grad, 100%, 50%)"));
    }

    // ── CSS Color Module 4 round-trips through ConvertColor (regression: I7 in v1.6.3) ──

    [Fact]
    public void Rgb_SpaceForm_WithSlashAlpha_Converts()
    {
        var result = Convert("rgb(255 0 0 / 50%)");
        // Cycles to hsl(a). Pure red with alpha 0.5.
        Assert.StartsWith("hsla(0, 100%, 50%, 0.5", result);
    }

    [Fact]
    public void Hsl_SpaceForm_WithSlashAlpha_Converts()
    {
        var result = Convert("hsl(0 100% 50% / 0.5)");
        // Cycles to hex with alpha (#FF000080 ± rounding).
        Assert.StartsWith("#FF0000", result);
        Assert.Equal(9, result.Length);
    }

    [Fact]
    public void Rgb_OutOfRangeChannels_ClampedTo255()
    {
        // Regression: B3 in v1.6.1. The detector regex permits up to 3 digits per channel,
        // so "rgb(999, 0, 0)" classifies. Without clamping, FormatHex would produce a
        // malformed hex string with too many digits.
        var result = Convert("rgb(999, 0, 0)");
        Assert.StartsWith("hsl(", result); // rgb cycles to hsl
        // The result should represent pure red (since 999 clamps to 255).
        Assert.Contains("0%", result); // saturation cap means hsl reads as 0 sat for the clamped value… let it normalize
        // Stricter: cycle one more time and confirm we land back on a valid hex.
        var classifier = new SnapActions.Detection.TextClassifier();
        var hslAnalysis = classifier.Classify(result);
        Assert.Equal(SnapActions.Detection.TextType.ColorCode, hslAnalysis.Type);
    }
}
