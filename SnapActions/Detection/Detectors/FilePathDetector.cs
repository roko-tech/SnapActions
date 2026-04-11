using System.IO;
using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class FilePathDetector : ITextDetector
{
    public TextType Type => TextType.FilePath;

    [GeneratedRegex(@"^([A-Za-z]:\\|\\\\|/[^/\s])")]
    private static partial Regex PathPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim().Replace("\"", "");
        if (trimmed.Contains('\n')) return false;

        if (PathPattern().IsMatch(trimmed))
        {
            bool exists = Path.Exists(trimmed);
            result = new TextAnalysis(TextType.FilePath, exists ? 0.98 : 0.8,
                new() { ["path"] = trimmed, ["exists"] = exists.ToString() });
            return true;
        }
        return false;
    }
}
