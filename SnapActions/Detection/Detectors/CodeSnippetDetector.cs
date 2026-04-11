using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class CodeSnippetDetector : ITextDetector
{
    public TextType Type => TextType.CodeSnippet;

    private static readonly string[] Keywords =
    [
        "function ", "def ", "class ", "import ", "#include", "var ", "let ", "const ",
        "public ", "private ", "protected ", "static ", "void ", "return ",
        "if (", "if(", "for (", "for(", "while (", "while(",
        "=> ", "->", "async ", "await ", "try {", "catch ", "switch ",
        "SELECT ", "INSERT ", "UPDATE ", "DELETE ", "FROM ", "WHERE ",
        "namespace ", "using ", "package ", "module "
    ];

    [GeneratedRegex(@"[{};]\s*$", RegexOptions.Multiline)]
    private static partial Regex CodeEndingPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Length < 10) return false;

        int signals = 0;

        // Check for language keywords
        foreach (var kw in Keywords)
        {
            if (trimmed.Contains(kw, StringComparison.OrdinalIgnoreCase))
                signals++;
        }

        // Check for code-like endings ({, }, ;)
        if (CodeEndingPattern().IsMatch(trimmed)) signals += 2;

        // Check for indentation
        var lines = trimmed.Split('\n');
        if (lines.Length >= 2)
        {
            int indented = lines.Count(l => l.StartsWith("  ") || l.StartsWith("\t"));
            if (indented > lines.Length / 2) signals++;
        }

        if (signals >= 3)
        {
            result = new TextAnalysis(TextType.CodeSnippet, 0.7);
            return true;
        }
        return false;
    }
}
