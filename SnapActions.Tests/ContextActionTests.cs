using SnapActions.Actions.ContextActions;
using SnapActions.Detection;
using Xunit;

namespace SnapActions.Tests;

/// <summary>
/// Tests for context actions that touch ProcessHelper or HttpClient at Execute time are driven
/// through CanExecute (which is pure) plus pure helper actions like Format JSON / Decode JWT
/// where Execute returns a deterministic result.
/// </summary>
public class ContextActionTests
{
    private readonly TextClassifier _classifier = new();

    private TextAnalysis Classify(string text) => _classifier.Classify(text);

    // ── OpenUrlAction.CanExecute ────────────────────────────────

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    [InlineData("ftp://files.example.com")] // regression: B1 in v1.5.3 — used to break ftp
    [InlineData("www.example.com")]
    public void OpenUrl_CanExecuteOnAllUrlSchemes(string text)
    {
        var action = new OpenUrlAction();
        var analysis = Classify(text);
        Assert.Equal(TextType.Url, analysis.Type);
        Assert.True(action.CanExecute(text, analysis));
    }

    // ── DictionaryAction.CanExecute (tightened in v1.5.3 B11) ────

    [Theory]
    [InlineData("hello")]
    [InlineData("hello world")]
    [InlineData("hello world foo")]
    [InlineData("don't")]
    [InlineData("twenty-one")]
    public void Dictionary_CanExecuteOnDictionaryShapedText(string text)
    {
        var action = new DictionaryAction();
        Assert.True(action.CanExecute(text, TextAnalysis.PlainText));
    }

    [Theory]
    [InlineData("a")] // single letter — too short
    [InlineData("hello world foo bar")] // 4 words — over limit
    [InlineData("user@example.com")] // not plain text type
    [InlineData("hello123")] // contains digits
    [InlineData("foo_bar")] // contains underscore (code identifier)
    [InlineData("foo.bar")] // contains dot
    public void Dictionary_RejectsNonDictionaryText(string text)
    {
        var action = new DictionaryAction();
        // Use the classifier so non-PlainText inputs go through the right path
        Assert.False(action.CanExecute(text, Classify(text)));
    }

    // ── CurrencyConverterAction.CanExecute (tightened in v1.5.3 B6) ──

    [Theory]
    [InlineData("$50")]
    [InlineData("100 USD")]
    [InlineData("EUR 200")]
    [InlineData("€1,500.50")]
    [InlineData("€1.500,50")] // European format
    [InlineData("£99.99")]
    [InlineData("¥10000")]
    public void Currency_CanExecuteOnRealCurrencyText(string text)
    {
        var action = new CurrencyConverterAction();
        Assert.True(action.CanExecute(text, TextAnalysis.PlainText));
    }

    [Theory]
    [InlineData("100")] // plain number
    [InlineData("100 monkeys")] // not a currency code
    [InlineData("hello")]
    public void Currency_RejectsNonCurrencyText(string text)
    {
        var action = new CurrencyConverterAction();
        Assert.False(action.CanExecute(text, TextAnalysis.PlainText));
    }

    // ── FormatJsonAction / MinifyJsonAction ──────────────────────

    [Fact]
    public void FormatJson_PrettyPrints()
    {
        var action = new FormatJsonAction();
        var input = "{\"key\":\"val\",\"num\":42}";
        var result = action.Execute(input, Classify(input));
        Assert.True(result.Success);
        Assert.Contains("\n", result.ResultText);
        Assert.Contains("\"key\"", result.ResultText);
    }

    [Fact]
    public void MinifyJson_StripsWhitespace()
    {
        var action = new MinifyJsonAction();
        var input = "{\n  \"key\": \"val\",\n  \"num\": 42\n}";
        var result = action.Execute(input, Classify(input));
        Assert.True(result.Success);
        Assert.DoesNotContain("\n", result.ResultText);
        Assert.DoesNotContain("  ", result.ResultText);
    }

    [Fact]
    public void FormatJson_RejectsInvalid()
    {
        var action = new FormatJsonAction();
        // Force-feed something that isn't JSON; CanExecute would normally gate this.
        var result = action.Execute("{not json", new TextAnalysis(TextType.Json, 0.95));
        Assert.False(result.Success);
    }

    // ── DecodeJwtAction ─────────────────────────────────────────

    [Fact]
    public void DecodeJwt_StandardSigned_SplitsHeaderPayloadSig()
    {
        // {"alg":"HS256","typ":"JWT"}.{"sub":"1234567890"}.test
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.test";
        var action = new DecodeJwtAction();
        var result = action.Execute(jwt, Classify(jwt));
        Assert.True(result.Success);
        Assert.Contains("HS256", result.ResultText);
        Assert.Contains("1234567890", result.ResultText);
        Assert.Contains("test", result.ResultText);
    }

    [Fact]
    public void DecodeJwt_AlgNoneEmptySignature()
    {
        // Regression: B18 in v1.5.6 — empty signature must decode cleanly.
        var jwt = "eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.";
        var action = new DecodeJwtAction();
        var result = action.Execute(jwt, Classify(jwt));
        Assert.True(result.Success);
        Assert.Contains("none", result.ResultText);
    }

    // ── DecodeBase64Action ──────────────────────────────────────

    [Fact]
    public void DecodeBase64_ProducesUtf8()
    {
        var input = "SGVsbG8gV29ybGQh"; // "Hello World!"
        var action = new DecodeBase64Action();
        var result = action.Execute(input, Classify(input));
        Assert.True(result.Success);
        Assert.Equal("Hello World!", result.ResultText);
    }

    // ── CalculateAction ─────────────────────────────────────────

    [Theory]
    [InlineData("2+3*4", "14")]
    [InlineData("sqrt(16)", "4")]
    [InlineData("(1+2)*(3+4)", "21")]
    [InlineData("10/3", "3.33333333333333")] // formatted with G15
    public void Calculate_ProducesExpectedResult(string expr, string expectedPrefix)
    {
        var action = new CalculateAction();
        var result = action.Execute(expr, new TextAnalysis(TextType.MathExpression, 0.85));
        Assert.True(result.Success, result.Message);
        Assert.StartsWith(expectedPrefix, result.ResultText);
    }

    [Fact]
    public void Calculate_RejectsDivByZero()
    {
        var action = new CalculateAction();
        var result = action.Execute("1/0", new TextAnalysis(TextType.MathExpression, 0.85));
        Assert.False(result.Success);
    }

    // ── GenerateUuidAction ──────────────────────────────────────

    [Fact]
    public void GenerateUuid_ReturnsValidGuid()
    {
        var action = new GenerateUuidAction();
        var input = "550e8400-e29b-41d4-a716-446655440000";
        var result = action.Execute(input, Classify(input));
        Assert.True(result.Success);
        Assert.True(System.Guid.TryParse(result.ResultText, out _));
        Assert.NotEqual(input, result.ResultText); // should generate a fresh one
    }

    // ── UnitConvertAction (compact one-line format, T3c) ─────────

    [Fact]
    public void UnitConvert_OneLinerFormat()
    {
        var action = new UnitConvertAction();
        var result = action.Execute("5 ft", Classify("5 ft"));
        Assert.True(result.Success);
        // Single line, separator | between conversions
        Assert.DoesNotContain("\n", result.ResultText);
        Assert.Contains("|", result.ResultText);
        Assert.Contains("m", result.ResultText);
    }

    // ── IsPreviewSafe markers (regression: hover preview wiring) ─────

    [Fact]
    public void PreviewColor_IsMarkedPreviewSafe()
    {
        // Regression: v1.6.2. Without this flag the inline-button hover preview wouldn't
        // execute the action and PreviewColor would show only the button name, not "Color: #...".
        Assert.True(new PreviewColorAction().IsPreviewSafe);
    }

    [Fact]
    public void ConvertTimezone_IsMarkedPreviewSafe()
    {
        Assert.True(new ConvertTimezoneAction().IsPreviewSafe);
    }

    [Fact]
    public void PreviewColor_ExposesColorInMessage()
    {
        // The hover preview falls back to Message when ResultText is null. PreviewColor
        // deliberately returns null ResultText (so it doesn't hijack the clipboard) but its
        // Message contains the color text, which is what the preview band should surface.
        var action = new PreviewColorAction();
        var result = action.Execute("#89B4FA", Classify("#89B4FA"));
        Assert.True(result.Success);
        Assert.Null(result.ResultText);
        Assert.NotNull(result.Message);
        Assert.Contains("#89B4FA", result.Message);
    }
}
