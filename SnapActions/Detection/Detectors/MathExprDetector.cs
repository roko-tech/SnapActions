using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class MathExprDetector : ITextDetector
{
    public TextType Type => TextType.MathExpression;

    // Allow digits, operators, parens, spaces, dots, commas, and known function/constant letters.
    [GeneratedRegex(@"^[\d\s\+\-\*/%\^\(\)\.,a-zA-Z]+$")]
    private static partial Regex SimpleMathPattern();

    [GeneratedRegex(@"[a-zA-Z]+")]
    private static partial Regex LetterRunPattern();

    // Operator characters that count as a "this is math" signal. Kept as a single source of
    // truth so the pre-filter agrees with what ParseExpression actually consumes.
    private static readonly char[] MathOperators = ['+', '-', '*', '/', '%', '^'];

    // Functions and constants the evaluator recognizes. Single source of truth for both the
    // letter-run-validity check below AND the operator-or-known-token pre-filter. Pre-fix,
    // the pre-filter was a separate regex that omitted `ln`, so `ln(10)` silently classified
    // as PlainText even though both this set and MathEvaluator.ApplyFunction supported it.
    private static readonly HashSet<string> AllowedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqrt", "sin", "cos", "tan", "log", "ln", "log10", "log2",
        "abs", "round", "floor", "ceil", "exp", "pi", "e", "tau"
    };

    // Rejects date-shaped strings (e.g. "2024-01-99" — invalid date that would otherwise
    // evaluate as 2024 - 1 - 99 = 1924, which is surprising). Covers slash-separated and
    // mixed-separator forms too, which the DateTime detector won't catch when the date is
    // syntactically wrong but still recognizable as a date attempt. Requires a 4-digit year
    // segment somewhere — without that gate, plain arithmetic like "1-2-3" matched and got
    // silently dropped from math classification.
    [GeneratedRegex(@"^(\d{4}[/\-]\d{1,2}[/\-]\d{1,4}|\d{1,4}[/\-]\d{1,2}[/\-]\d{4})$")]
    private static partial Regex IsoDateShape();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Length < 3 || trimmed.Contains('\n')) return false;

        if (!SimpleMathPattern().IsMatch(trimmed)) return false;

        // Don't classify ISO-date-shaped strings as math even if the date detector rejected them.
        if (IsoDateShape().IsMatch(trimmed)) return false;

        // Every letter run must be a recognized function or constant token. Counted in the same
        // pass so the digit-vs-2-token rule below doesn't re-walk the string.
        int tokenCount = 0;
        foreach (Match m in LetterRunPattern().Matches(trimmed))
        {
            if (!AllowedTokens.Contains(m.Value)) return false;
            tokenCount++;
        }

        // Need a "this is math" signal:
        //   - an operator (covers "2+3", "pi*tau")
        //   - or a digit AND at least one known token (covers "ln(10)", "sqrt(16)")
        //   - or at least two known tokens (covers "pi+e" via operator, "sqrt(pi)" via tokens)
        // Without any of these, a single token like "tau" or a bare number like "200" doesn't
        // qualify as a math *expression*.
        bool hasOperator = trimmed.IndexOfAny(MathOperators) >= 0;
        bool hasDigit = trimmed.Any(char.IsDigit);
        if (!hasOperator && tokenCount < 2 && !(hasDigit && tokenCount >= 1))
            return false;

        result = new TextAnalysis(TextType.MathExpression, 0.85);
        return true;
    }
}
