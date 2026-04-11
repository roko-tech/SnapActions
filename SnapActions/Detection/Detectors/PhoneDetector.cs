using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class PhoneDetector : ITextDetector
{
    public TextType Type => TextType.Phone;

    [GeneratedRegex(@"^[\+]?[\d\s\-\(\)\.]{7,20}$")]
    private static partial Regex PhonePattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Contains('\n')) return false;

        if (PhonePattern().IsMatch(trimmed))
        {
            // Must have at least 7 digits
            int digitCount = trimmed.Count(char.IsDigit);
            if (digitCount >= 7 && digitCount <= 15)
            {
                result = new TextAnalysis(TextType.Phone, 0.8,
                    new() { ["phone"] = trimmed });
                return true;
            }
        }
        return false;
    }
}
