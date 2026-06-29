using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class Base64Detector : ITextDetector
{
    public TextType Type => TextType.Base64;

    [GeneratedRegex(@"^[A-Za-z0-9+/]{4,}={0,2}$")]
    private static partial Regex Base64Pattern();

    [GeneratedRegex(@"^[0-9a-fA-F]+$")]
    private static partial Regex HexPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Contains(' ') || trimmed.Contains('\n')) return false;
        if (trimmed.Length % 4 != 0) return false;
        if (trimmed.Length < 12) return false;

        // Pure-hex strings (likely hashes / hex IDs) shouldn't be reported as Base64 —
        // Convert.FromBase64String accepts them but the "decode" is meaningless garbage.
        if (HexPattern().IsMatch(trimmed)) return false;

        // Must contain at least one digit, +, /, = OR have one mixed-case alpha hint.
        // This rejects "Application1"-style false positives without symbols.
        bool hasSymbol = trimmed.Any(c => c == '+' || c == '/' || c == '=' || char.IsDigit(c));
        bool hasMixedCase = trimmed.Any(char.IsUpper) && trimmed.Any(char.IsLower);
        if (!hasSymbol && (!hasMixedCase || trimmed.Length < 32)) return false;

        if (!Base64Pattern().IsMatch(trimmed)) return false;

        try
        {
            var bytes = Convert.FromBase64String(trimmed);
            // Require the decoded bytes to be valid UTF-8. Convert.FromBase64String on a long
            // alpha-with-symbols string often "succeeds" but yields garbage bytes; insist on a
            // clean decode so we only label genuinely-text-bearing base64 as decodable.
            var encoding = new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            string decoded;
            try { decoded = encoding.GetString(bytes); }
            catch (System.Text.DecoderFallbackException) { return false; }
            // Reject if decoded contains too many control characters
            int controlCount = decoded.Count(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t');
            if (controlCount > decoded.Length / 4) return false;

            result = new TextAnalysis(TextType.Base64, 0.85,
                new() { ["decoded"] = decoded });
            return true;
        }
        catch { return false; }
    }
}
