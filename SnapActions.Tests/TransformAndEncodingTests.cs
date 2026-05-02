using SnapActions.Actions;
using SnapActions.Detection;
using Xunit;

namespace SnapActions.Tests;

/// <summary>
/// Drives transform/encode/wrap actions through the registry rather than constructing them
/// directly — this exercises the same wiring the toolbar uses, including ID and category
/// assignment.
/// </summary>
public class TransformAndEncodingTests
{
    private readonly ActionRegistry _registry = new();

    private string Run(ActionCategory category, string actionId, string input)
    {
        var action = _registry.GetAllActionsForCategory(category)
            .First(a => a.Id == actionId);
        var result = action.Execute(input, TextAnalysis.PlainText);
        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.ResultText);
        return result.ResultText!;
    }

    // ── Case transforms ─────────────────────────────────────────

    [Theory]
    [InlineData("case_upper", "Hello World", "HELLO WORLD")]
    [InlineData("case_lower", "Hello World", "hello world")]
    [InlineData("case_title", "hello world", "Hello World")]
    [InlineData("case_camel", "hello_world_foo", "helloWorldFoo")]
    [InlineData("case_camel", "hello-world", "helloWorld")]
    [InlineData("case_pascal", "hello world", "HelloWorld")]
    [InlineData("case_snake", "Hello World", "hello_world")]
    [InlineData("case_snake", "helloWorld", "hello_world")] // camelCase boundary detection
    [InlineData("case_kebab", "Hello World", "hello-world")]
    [InlineData("case_kebab", "helloWorld", "hello-world")]
    public void Case_Transforms(string id, string input, string expected) =>
        Assert.Equal(expected, Run(ActionCategory.Transform, id, input));

    [Fact]
    public void Case_TitleCase_UsesInvariantCulture()
    {
        // Regression: M15 in v1.5.3. CurrentCulture-based title case turned "i" into "İ" on
        // Turkish locale; we now use InvariantCulture explicitly.
        var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.Equal("Hello India", Run(ActionCategory.Transform, "case_title", "hello india"));
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = prev; }
    }

    [Fact]
    public void Case_Reverse_HandlesGraphemes()
    {
        // The reverse transform iterates Unicode text elements so emojis and combining marks survive.
        // "ab😀" reversed naively (per char16) would split the emoji surrogate pair.
        Assert.Equal("😀ba", Run(ActionCategory.Transform, "case_reverse", "ab😀"));
    }

    // ── Whitespace ──────────────────────────────────────────────

    [Theory]
    [InlineData("ws_trim", "  hello  ", "hello")]
    [InlineData("ws_remove_extra_spaces", "a   b    c", "a b c")]
    [InlineData("ws_remove_linebreaks", "a\nb\r\nc", "a b c")]
    public void Whitespace_Basic(string id, string input, string expected) =>
        Assert.Equal(expected, Run(ActionCategory.Transform, id, input));

    [Fact]
    public void Whitespace_SortLines_Ascending()
    {
        var input = "banana\napple\ncherry";
        Assert.Equal("apple\nbanana\ncherry", Run(ActionCategory.Transform, "ws_sort_lines", input));
    }

    [Fact]
    public void Whitespace_SortLines_PreservesCrlf()
    {
        var input = "banana\r\napple\r\ncherry";
        Assert.Equal("apple\r\nbanana\r\ncherry", Run(ActionCategory.Transform, "ws_sort_lines", input));
    }

    [Fact]
    public void Whitespace_Dedup_CaseInsensitive()
    {
        // Regression: case-insensitive dedup matches the case-insensitive sort comparer so
        // "Hello"/"hello" collapse to one entry rather than appearing twice.
        var input = "Hello\nhello\nHELLO\nworld";
        var output = Run(ActionCategory.Transform, "ws_dedup_lines", input);
        var lines = output.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("world", lines);
    }

    // ── Wrap ────────────────────────────────────────────────────

    [Theory]
    [InlineData("wrap_quotes", "hi", "\"hi\"")]
    [InlineData("wrap_single_quotes", "hi", "'hi'")]
    [InlineData("wrap_parens", "hi", "(hi)")]
    [InlineData("wrap_brackets", "hi", "[hi]")]
    [InlineData("wrap_braces", "hi", "{hi}")]
    [InlineData("wrap_backticks", "hi", "`hi`")]
    public void Wrap_Actions(string id, string input, string expected) =>
        Assert.Equal(expected, Run(ActionCategory.Transform, id, input));

    // ── Encoding (round-trips) ──────────────────────────────────

    [Theory]
    [InlineData("hello world")]
    [InlineData("a&b=c?d#e")]
    [InlineData("café")]
    public void Encoding_UrlRoundTrip(string text)
    {
        var encoded = Run(ActionCategory.Encode, "enc_url_encode", text);
        Assert.Equal(text, Run(ActionCategory.Encode, "enc_url_decode", encoded));
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("Привет, мир")]
    [InlineData("👋 emoji")]
    public void Encoding_Base64RoundTrip(string text)
    {
        var encoded = Run(ActionCategory.Encode, "enc_base64_encode", text);
        Assert.Equal(text, Run(ActionCategory.Encode, "enc_base64_decode", encoded));
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("a < b > c")]
    [InlineData("\"quoted\" & 'apostrophe'")]
    public void Encoding_HtmlRoundTrip(string text)
    {
        var encoded = Run(ActionCategory.Encode, "enc_html_encode", text);
        Assert.Equal(text, Run(ActionCategory.Encode, "enc_html_decode", encoded));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("café")]
    public void Encoding_HexRoundTrip(string text)
    {
        var encoded = Run(ActionCategory.Encode, "enc_hex_encode", text);
        Assert.Equal(text, Run(ActionCategory.Encode, "enc_hex_decode", encoded));
    }

    [Fact]
    public void Encoding_Rot13_Involutive()
    {
        // ROT13 applied twice returns the original.
        var once = Run(ActionCategory.Encode, "enc_rot13", "Hello, World!");
        Assert.Equal("Uryyb, Jbeyq!", once);
        Assert.Equal("Hello, World!", Run(ActionCategory.Encode, "enc_rot13", once));
    }

    // ── Hashes (known-vector spot checks) ───────────────────────

    [Theory]
    [InlineData("enc_md5", "hello", "5d41402abc4b2a76b9719d911017c592")]
    [InlineData("enc_sha1", "hello", "aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d")]
    [InlineData("enc_sha256", "hello", "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824")]
    public void Hash_KnownVectors(string id, string input, string expected) =>
        Assert.Equal(expected, Run(ActionCategory.Encode, id, input));
}
