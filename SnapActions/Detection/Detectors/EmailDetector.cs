using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class EmailDetector : ITextDetector
{
    public TextType Type => TextType.Email;

    [GeneratedRegex(@"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$")]
    private static partial Regex EmailPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (EmailPattern().IsMatch(trimmed))
        {
            result = new TextAnalysis(TextType.Email, 0.95, new() { ["email"] = trimmed });
            return true;
        }
        return false;
    }
}
