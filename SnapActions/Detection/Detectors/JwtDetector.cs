using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class JwtDetector : ITextDetector
{
    public TextType Type => TextType.Jwt;

    // header.payload.signature, all base64url
    [GeneratedRegex(@"^eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+$")]
    private static partial Regex JwtPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Length < 20 || trimmed.Contains(' ') || trimmed.Contains('\n')) return false;
        if (!JwtPattern().IsMatch(trimmed)) return false;

        result = new TextAnalysis(TextType.Jwt, 0.95);
        return true;
    }
}
