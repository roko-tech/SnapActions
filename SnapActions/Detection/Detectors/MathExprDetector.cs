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

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Length < 3 || trimmed.Contains('\n')) return false;

        if (!HasOperatorOrFunction().IsMatch(trimmed)) return false;
        if (!SimpleMathPattern().IsMatch(trimmed)) return false;
        if (!trimmed.Any(char.IsDigit)) return false;

        // Reject any letter run that isn't a known math token (e.g. "hello+1")
        foreach (Match m in System.Text.RegularExpressions.Regex.Matches(trimmed, "[a-zA-Z]+"))
            if (!AllowedTokens.Contains(m.Value)) return false;

        result = new TextAnalysis(TextType.MathExpression, 0.85);
        return true;
    }
}
