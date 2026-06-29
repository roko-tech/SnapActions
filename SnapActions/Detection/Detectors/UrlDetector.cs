using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class UrlDetector : ITextDetector
{
    public TextType Type => TextType.Url;

    // For www-prefixed URLs require a recognizable host (at least one extra dot + 2-char label).
    // The previous looser `www\.\S+` accepted "www.x" and OpenUrlAction would happily prepend
    // https:// and shell that to the browser, which then 404'd ungracefully.
    [GeneratedRegex(@"^(https?://\S+|ftp://\S+|www\.[A-Za-z0-9\-]+\.[A-Za-z0-9\-]{2,}\S*)", RegexOptions.IgnoreCase)]
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
