using System.Net.Mail;
using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class EmailDetector : ITextDetector
{
    public TextType Type => TextType.Email;

    // Loose pre-filter that screens out obvious non-email selections cheaply. The TLD allows
    // letters, digits, and hyphens (covers numeric/punycoded TLDs like xn--p1ai), then we hand
    // off to MailAddress.TryCreate for full RFC validation.
    [GeneratedRegex(@"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z][a-zA-Z0-9\-]+$")]
    private static partial Regex EmailPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (!EmailPattern().IsMatch(trimmed)) return false;
        // Defense in depth — the regex catches shape but accepts some malformed locals; the
        // framework parser rejects e.g. consecutive dots and zero-length labels.
        if (!MailAddress.TryCreate(trimmed, out _)) return false;

        result = new TextAnalysis(TextType.Email, 0.95, new() { ["email"] = trimmed });
        return true;
    }
}
