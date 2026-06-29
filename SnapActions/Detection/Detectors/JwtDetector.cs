using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class JwtDetector : ITextDetector
{
    public TextType Type => TextType.Jwt;

    // header.payload.signature, all base64url. The signature segment may be empty for
    // alg=none JWTs (RFC 7519 §6) — the trailing dot is mandatory but the bytes after it
    // can be zero-length.
    [GeneratedRegex(@"^eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*$")]
    private static partial Regex JwtPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Length < 20 || trimmed.Contains(' ') || trimmed.Contains('\n')) return false;
        if (!JwtPattern().IsMatch(trimmed)) return false;

        // The pattern catches the shape, but a coincidence-of-base64 string can pass too.
        // Validate that the header actually decodes to a JSON object with an "alg" claim — which
        // is mandatory in every real JWT (RFC 7515 §4.1.1).
        if (!HasValidHeader(trimmed)) return false;

        result = new TextAnalysis(TextType.Jwt, 0.95);
        return true;
    }

    private static bool HasValidHeader(string jwt)
    {
        try
        {
            var headerB64Url = jwt.Split('.')[0];
            var s = headerB64Url.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            var bytes = Convert.FromBase64String(s);
            using var doc = System.Text.Json.JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            return doc.RootElement.TryGetProperty("alg", out _);
        }
        catch { return false; }
    }
}
