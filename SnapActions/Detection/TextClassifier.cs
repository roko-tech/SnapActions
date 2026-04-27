using SnapActions.Detection.Detectors;

namespace SnapActions.Detection;

public class TextClassifier
{
    /// <summary>
    /// Detectors that allocate-and-parse the whole input (JsonDocument.Parse, XDocument.Parse) are skipped
    /// when text exceeds this cap. Treat everything beyond as plain text.
    /// </summary>
    public const int MaxClassifyChars = 256 * 1024;

    private readonly ITextDetector[] _detectors;

    public TextClassifier()
    {
        // Priority order matters - more specific types first
        _detectors =
        [
            new UrlDetector(),
            new EmailDetector(),
            new FilePathDetector(),
            new JsonDetector(),
            new XmlHtmlDetector(),
            new UuidDetector(),
            new JwtDetector(),
            new IpAddressDetector(),
            new ColorCodeDetector(),
            new Base64Detector(),
            new DateTimeDetector(),
            new MathExprDetector(),
            new CodeSnippetDetector()
        ];
    }

    public TextAnalysis Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TextAnalysis.PlainText;

        var trimmed = text.Trim();
        // Don't run heavy parsers on huge selections.
        if (trimmed.Length > MaxClassifyChars)
            return TextAnalysis.PlainText;

        foreach (var detector in _detectors)
        {
            if (detector.TryDetect(trimmed, out var analysis) && analysis.Confidence >= 0.7)
                return analysis;
        }

        return TextAnalysis.PlainText;
    }
}
