using SnapActions.Detection;
using SnapActions.Detection.Detectors;
using Xunit;

namespace SnapActions.Tests;

public class DetectorTests
{
    private readonly TextClassifier _classifier = new();

    private TextType Classify(string text) => _classifier.Classify(text).Type;

    // ── URL ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?q=1")]
    [InlineData("ftp://files.example.com/")]
    [InlineData("www.example.com")]
    public void Url_Matches(string text) => Assert.Equal(TextType.Url, Classify(text));

    [Fact]
    public void Url_RejectsPlainWord() => Assert.NotEqual(TextType.Url, Classify("example"));

    [Fact]
    public void Url_RejectsMultiLineSelection()
    {
        // Regression: B4 in v1.6.1. Previously up-to-3 newlines was allowed; a selection like
        // "https://example.com\nrest of the paragraph" classified as URL and then OpenUrlAction
        // fed the multi-line string to the shell.
        Assert.NotEqual(TextType.Url, Classify("https://example.com\nmore prose"));
    }

    // ── Email ────────────────────────────────────────────────────

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last+tag@sub.example.co.uk")]
    public void Email_Matches(string text) => Assert.Equal(TextType.Email, Classify(text));

    [Fact]
    public void Email_RejectsBareUser() => Assert.NotEqual(TextType.Email, Classify("user"));

    // ── JSON ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("{\"key\":\"val\"}")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"a\": [1, {\"b\": 2}]}")]
    public void Json_Matches(string text) => Assert.Equal(TextType.Json, Classify(text));

    [Theory]
    [InlineData("not json")]
    [InlineData("{ unbalanced")]
    public void Json_Rejects(string text) => Assert.NotEqual(TextType.Json, Classify(text));

    // ── UUID ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Uuid_Matches(string text) => Assert.Equal(TextType.Uuid, Classify(text));

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("550e8400-e29b-41d4-a716-44665544")] // too short
    public void Uuid_Rejects(string text) => Assert.NotEqual(TextType.Uuid, Classify(text));

    // ── JWT ──────────────────────────────────────────────────────

    [Fact]
    public void Jwt_Matches_StandardSigned()
    {
        // header={"alg":"HS256","typ":"JWT"}, payload={"sub":"1234567890"}, signature=test
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.test";
        Assert.Equal(TextType.Jwt, Classify(jwt));
    }

    [Fact]
    public void Jwt_Matches_AlgNoneEmptySignature()
    {
        // header={"alg":"none"}, payload={"sub":"1"}, signature= (empty — RFC 7519 §6)
        var jwt = "eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.";
        Assert.Equal(TextType.Jwt, Classify(jwt));
    }

    [Fact]
    public void Jwt_Rejects_HeaderWithoutAlgClaim()
    {
        // header={"typ":"JWT"} — no alg ⇒ not a real JWT
        var fake = "eyJ0eXAiOiJKV1QifQ.eyJzdWIiOiIxIn0.sig";
        Assert.NotEqual(TextType.Jwt, Classify(fake));
    }

    // ── IP ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.1")]
    [InlineData("2001:db8::1")]
    [InlineData("::1")]
    public void IpAddress_Matches(string text) => Assert.Equal(TextType.IpAddress, Classify(text));

    [Theory]
    [InlineData("3232235521")] // bare integer that would parse as IPv4 — must be rejected
    [InlineData("999.999.999.999")]
    public void IpAddress_Rejects(string text) => Assert.NotEqual(TextType.IpAddress, Classify(text));

    // ── Color ────────────────────────────────────────────────────

    [Theory]
    [InlineData("#89B4FA")]
    [InlineData("#fff")]
    [InlineData("#89B4FAAA")]
    [InlineData("#abcd")] // 4-char with alpha
    [InlineData("rgb(255, 0, 0)")]
    [InlineData("rgba(255, 0, 0, 0.5)")]
    [InlineData("rgb(255 0 0)")] // CSS Color Module 4 space form
    [InlineData("rgb(255 0 0 / 50%)")] // CSS4 with slash + percent alpha
    [InlineData("hsl(120, 50%, 50%)")]
    [InlineData("hsl(120 50% 50% / 0.5)")] // CSS4 space form
    public void Color_Matches(string text) => Assert.Equal(TextType.ColorCode, Classify(text));

    [Theory]
    [InlineData("#XYZ")]
    [InlineData("rgb(300, 0, 0, 0, 0)")]
    public void Color_Rejects(string text) => Assert.NotEqual(TextType.ColorCode, Classify(text));

    // ── Base64 ───────────────────────────────────────────────────

    [Fact]
    public void Base64_MatchesValidUtf8() =>
        Assert.Equal(TextType.Base64, Classify("SGVsbG8gV29ybGQh")); // "Hello World!"

    [Fact]
    public void Base64_RejectsPureHex() =>
        // 16 hex chars decode as base64 but the result is meaningless garbage
        Assert.NotEqual(TextType.Base64, Classify("DEADBEEF12345678"));

    // ── DateTime ─────────────────────────────────────────────────

    [Theory]
    [InlineData("2024-01-15")]
    [InlineData("2024-01-15T10:00:00")]
    [InlineData("2024-01-15T10:00:00+05:00")]
    public void DateTime_MatchesIso(string text) =>
        Assert.Equal(TextType.DateTime, Classify(text));

    [Fact]
    public void DateTime_MatchesUnixSeconds() =>
        Assert.Equal(TextType.DateTime, Classify("1700000000"));

    [Fact]
    public void DateTime_RejectsInvalidIso() =>
        Assert.NotEqual(TextType.DateTime, Classify("2024-01-99"));

    // ── Math ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("2+3*4")]
    [InlineData("sqrt(16)")]
    [InlineData("(1 + 2) * 3")]
    public void Math_Matches(string text) =>
        Assert.Equal(TextType.MathExpression, Classify(text));

    [Theory]
    [InlineData("hello+1")]
    [InlineData("2024-01-99")] // ISO-shape carve-out
    [InlineData("2024/01-15")] // mixed-separator date (regression: B-pattern in v1.5.6)
    public void Math_RejectsDateShapes(string text) =>
        Assert.NotEqual(TextType.MathExpression, Classify(text));

    // ── Unit ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("5 ft")]
    [InlineData("100 km/h")]
    [InlineData("20°C")]
    [InlineData("2 cups")]
    [InlineData("5 fl oz")] // regression: B1 in v1.5.6 — alias was missing
    [InlineData("100 lb")]
    [InlineData("3 kg")]
    public void Unit_Matches(string text) => Assert.Equal(TextType.Unit, Classify(text));

    // ── FilePath ─────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"D:\projects\file.txt")]
    [InlineData(@"\\server\share\file.txt")] // UNC — should classify, but exists must be False
    public void FilePath_Matches(string text) =>
        Assert.Equal(TextType.FilePath, Classify(text));

    [Fact]
    public void FilePath_UncDoesNotProbeExistence()
    {
        // Regression: B2 in v1.5.3. Previously Path.Exists on a UNC path could block the UI.
        var analysis = _classifier.Classify(@"\\unreachable.example.invalid\share\foo.txt");
        Assert.Equal(TextType.FilePath, analysis.Type);
        Assert.Equal("False", analysis.Metadata?.GetValueOrDefault("exists"));
    }
}
