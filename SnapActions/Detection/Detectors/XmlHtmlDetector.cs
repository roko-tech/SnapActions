using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class XmlHtmlDetector : ITextDetector
{
    public TextType Type => TextType.XmlHtml;

    [GeneratedRegex(@"<\/?[a-zA-Z][a-zA-Z0-9]*(\s[^>]*)?>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^<\?xml\s")]
    private static partial Regex XmlDeclPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('<')) return false;

        var matches = TagPattern().Matches(trimmed);
        if (matches.Count < 1) return false;

        bool isXml = XmlDeclPattern().IsMatch(trimmed);
        var subtype = isXml ? "xml" : "html";

        result = new TextAnalysis(TextType.XmlHtml, matches.Count >= 2 ? 0.9 : 0.7,
            new() { ["subtype"] = subtype });
        return true;
    }
}
