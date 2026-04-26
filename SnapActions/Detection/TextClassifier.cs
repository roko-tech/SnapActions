using SnapActions.Detection.Detectors;

namespace SnapActions.Detection;

public class TextClassifier
{
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
        foreach (var detector in _detectors)
        {
            if (detector.TryDetect(trimmed, out var analysis) && analysis.Confidence >= 0.7)
                return analysis;
        }

        return TextAnalysis.PlainText;
    }
}
