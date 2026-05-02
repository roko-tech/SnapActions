using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class UrlDetector : ITextDetector
{
    public TextType Type => TextType.Url;

    [GeneratedRegex(@"^(https?://|ftp://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        // URLs don't contain newlines. The previous "up to 3 lines" cutoff let multi-line
        // selections classify as URL when only line 1 was actually a URL — then OpenUrlAction
        // would feed a multi-line string to the shell.
        if (trimmed.Contains('\n')) return false;

        if (UrlPattern().IsMatch(trimmed) || Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "ftp"))
        {
            result = new TextAnalysis(TextType.Url, 0.95, new() { ["url"] = trimmed });
            return true;
        }
        return false;
    }
}
