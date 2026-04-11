using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class MathExprDetector : ITextDetector
{
    public TextType Type => TextType.MathExpression;

    // Matches expressions with numbers and operators: 2+3, 4*5-1, sqrt(16), etc.
    [GeneratedRegex(@"^[\d\s\+\-\*/%\^\(\)\.,]+$")]
    private static partial Regex SimpleMathPattern();

    [GeneratedRegex(@"[\+\-\*/%\^]")]
    private static partial Regex HasOperator();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Length < 3 || trimmed.Contains('\n')) return false;

        // Must contain at least one operator
        if (!HasOperator().IsMatch(trimmed)) return false;

        // Allow digits, operators, parens, spaces, dots
        if (SimpleMathPattern().IsMatch(trimmed))
        {
            // Must have at least one digit
            if (!trimmed.Any(char.IsDigit)) return false;

            result = new TextAnalysis(TextType.MathExpression, 0.85);
            return true;
        }
        return false;
    }
}
