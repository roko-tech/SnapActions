using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class UuidDetector : ITextDetector
{
    public TextType Type => TextType.Uuid;

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex UuidPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (UuidPattern().IsMatch(trimmed) && Guid.TryParse(trimmed, out _))
        {
            result = new TextAnalysis(TextType.Uuid, 0.98);
            return true;
        }
        return false;
    }
}
