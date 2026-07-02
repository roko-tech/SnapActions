using SnapActions.Actions.ContextActions;
using SnapActions.Detection;
using Xunit;

namespace SnapActions.Tests;

/// <summary>
/// Regression pins for the v2.1.0 review fixes that don't belong to an existing test area file.
/// </summary>
public class ReviewFixTests
{
    private readonly TextClassifier _classifier = new();

    // ── CalculateAction: double→long saturation at exactly 2^63 ─────────────

    [Fact]
    public void Calculate_TwoToThe63_IsNotOffByOne()
    {
        // (double)long.MaxValue rounds UP to 2^63, so the old "<= long.MaxValue" guard admitted
        // 2^63 itself and the saturating cast displayed 9223372036854775807 (off by one).
        var result = new CalculateAction().Execute("2^63", new TextAnalysis(TextType.MathExpression, 0.85));
        Assert.True(result.Success);
        Assert.NotEqual("9223372036854775807", result.ResultText);
        // The boundary value must take the G15 branch (approximate scientific form) rather than
        // the exact-integer branch it can't represent.
        Assert.Equal(System.Math.Pow(2, 63).ToString("G15", System.Globalization.CultureInfo.InvariantCulture),
            result.ResultText);
    }

    [Fact]
    public void Calculate_LargeIntegersBelowTheBoundary_StillFormatExact()
    {
        var result = new CalculateAction().Execute("2^62", new TextAnalysis(TextType.MathExpression, 0.85));
        Assert.Equal("4611686018427387904", result.ResultText);
    }

    // ── TranslateAction: only plain text is translatable prose ──────────────

    [Fact]
    public void Translate_OffersForPlainText()
    {
        Assert.True(new TranslateAction().CanExecute("hola amigo", TextAnalysis.PlainText));
    }

    [Theory]
    [InlineData("https://example.com", TextType.Url)]
    [InlineData("{\"a\":1}", TextType.Json)]
    [InlineData("550e8400-e29b-41d4-a716-446655440000", TextType.Uuid)]
    public void Translate_NotOfferedForTypedSelections(string text, TextType type)
    {
        Assert.False(new TranslateAction().CanExecute(text, new TextAnalysis(type, 0.95)));
    }

    // ── Truncate: never split a surrogate pair at the cut point ─────────────

    [Fact]
    public void Truncate_DoesNotSplitSurrogatePair()
    {
        // "ab" + 😀 (U+1F600, a surrogate pair at indices 2-3): cutting at 3 would slice the
        // pair in half and render a lone-surrogate "�" in the preview band.
        var s = "ab\U0001F600cd";
        var cut = UI.ToolbarWindow.Truncate(s, 3);
        Assert.Equal("ab...", cut);
        Assert.DoesNotContain('\uD83D', cut); // no orphaned high surrogate
    }

    [Fact]
    public void Truncate_PlainAsciiUnchangedBehavior()
    {
        Assert.Equal("abc...", UI.ToolbarWindow.Truncate("abcdef", 3));
        Assert.Equal("abc", UI.ToolbarWindow.Truncate("abc", 3));
    }

    // ── FilePathDetector: unix-style paths are not Windows file paths ────────

    [Theory]
    [InlineData("/etc/hosts")]
    [InlineData("/r/programming")]
    public void FilePath_RejectsUnixStylePaths(string text)
    {
        // These almost never resolve on Windows — they only mislabeled the selection with a
        // FILE PATH badge and blocked later detectors.
        Assert.NotEqual(TextType.FilePath, _classifier.Classify(text).Type);
    }
}
