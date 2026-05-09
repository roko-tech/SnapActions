using SnapActions.Helpers;
using Xunit;

namespace SnapActions.Tests;

public class MathEvaluatorTests
{
    [Theory]
    [InlineData("1+1", 2)]
    [InlineData("2*3", 6)]
    [InlineData("10/4", 2.5)]
    [InlineData("10%3", 1)]
    [InlineData("2^10", 1024)]
    [InlineData("-5+3", -2)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("1,000+200", 1200)] // commas stripped
    public void Evaluate_BasicOperators(string expr, double expected) =>
        Assert.Equal(expected, MathEvaluator.Evaluate(expr), 9);

    [Theory]
    [InlineData("sqrt(16)", 4)]
    [InlineData("abs(-7)", 7)]
    [InlineData("floor(3.9)", 3)]
    [InlineData("ceil(3.1)", 4)]
    [InlineData("round(3.5)", 4)]
    public void Evaluate_Functions(string expr, double expected) =>
        Assert.Equal(expected, MathEvaluator.Evaluate(expr), 9);

    [Fact]
    public void Evaluate_Pi() =>
        Assert.Equal(System.Math.PI, MathEvaluator.Evaluate("pi"));

    [Fact]
    public void Evaluate_E() =>
        Assert.Equal(System.Math.E, MathEvaluator.Evaluate("e"));

    [Fact]
    public void Evaluate_DivByZero_Throws() =>
        Assert.Throws<System.DivideByZeroException>(() => MathEvaluator.Evaluate("1/0"));

    [Fact]
    public void Evaluate_DeeplyNested_Throws()
    {
        // Regression: B16 in v1.5.6 — recursion depth cap.
        var deeplyNested = new string('(', 200) + "1" + new string(')', 200);
        Assert.Throws<System.FormatException>(() => MathEvaluator.Evaluate(deeplyNested));
    }

    [Fact]
    public void Evaluate_ModeratelyNested_Works()
    {
        // 10 levels is well under the 64 cap.
        var moderate = "((((((((((1+2))))))))))";
        Assert.Equal(3, MathEvaluator.Evaluate(moderate));
    }

    // ── ^ is right-associative (regression: B3 in v1.6.3) ───────

    [Fact]
    public void Evaluate_PowerIsRightAssociative()
    {
        // 2^3^2 should be 2^(3^2) = 2^9 = 512, matching standard math convention.
        Assert.Equal(512, MathEvaluator.Evaluate("2^3^2"));
    }

    // ── Scientific notation (regression: B4 in v1.6.3) ──────────

    [Theory]
    [InlineData("1e10", 1e10)]
    [InlineData("2.5e3", 2500)]
    [InlineData("1e-3", 0.001)]
    [InlineData("1.5e+2", 150)]
    public void Evaluate_ScientificNotation(string expr, double expected) =>
        Assert.Equal(expected, MathEvaluator.Evaluate(expr), 9);

    [Fact]
    public void Evaluate_ConstantE_StillWorksWithoutExponent()
    {
        // Bare "e" must still resolve to Math.E rather than getting interpreted as the start
        // of scientific notation — the parser rolls back when no exponent digits follow.
        Assert.Equal(System.Math.E, MathEvaluator.Evaluate("e"));
        Assert.Equal(2 * System.Math.E, MathEvaluator.Evaluate("2*e"), 9);
    }
}
