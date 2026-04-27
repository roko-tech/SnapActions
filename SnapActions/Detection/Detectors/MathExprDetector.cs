using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class MathExprDetector : ITextDetector
{
    public TextType Type => TextType.MathExpression;

    // Allow digits, operators, parens, spaces, dots, commas, and known function/constant letters.
    [GeneratedRegex(@"^[\d\s\+\-\*/%\^\(\)\.,a-zA-Z]+$")]
    private static partial Regex SimpleMathPattern();

    [GeneratedRegex(@"[\+\-\*/%\^]|sqrt|sin|cos|tan|log|abs|round|floor|ceil|exp", RegexOptions.IgnoreCase)]
    private static partial Regex HasOperatorOrFunction();

    private static readonly HashSet<string> AllowedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqrt", "sin", "cos", "tan", "log", "ln", "log10", "log2",
        "abs", "round", "floor", "ceil", "exp", "pi", "e", "tau"
    };

    // Rejects ISO-date-shaped strings (e.g. "2024-01-99" — invalid date that would otherwise
    // evaluate as 2024 - 1 - 99 = 1924, which is surprising).
    [GeneratedRegex(@"^\d{4}-\d{1,2}-\d{1,2}$")]
    private static partial Regex IsoDateShape();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Length < 3 || trimmed.Contains('\n')) return false;

        if (!HasOperatorOrFunction().IsMatch(trimmed)) return false;
        if (!SimpleMathPattern().IsMatch(trimmed)) return false;

        // Don't classify ISO-date-shaped strings as math even if the date detector rejected them.
        if (IsoDateShape().IsMatch(trimmed)) return false;

        // Reject any letter run that isn't a known math token (e.g. "hello+1")
        foreach (Match m in System.Text.RegularExpressions.Regex.Matches(trimmed, "[a-zA-Z]+"))
            if (!AllowedTokens.Contains(m.Value)) return false;

        // Need at least one digit OR at least two known constant/function tokens
        // (so "pi+e" or "sqrt(pi)" pass even without a digit).
        if (!trimmed.Any(char.IsDigit))
        {
            int tokenCount = 0;
            foreach (Match m in System.Text.RegularExpressions.Regex.Matches(trimmed, "[a-zA-Z]+"))
                if (AllowedTokens.Contains(m.Value)) tokenCount++;
            if (tokenCount < 2) return false;
        }

        result = new TextAnalysis(TextType.MathExpression, 0.85);
        return true;
    }
}
