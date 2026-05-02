using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class ColorCodeDetector : ITextDetector
{
    public TextType Type => TextType.ColorCode;

    // CSS hex colors: 3 (#RGB), 4 (#RGBA), 6 (#RRGGBB), or 8 (#RRGGBBAA) hex digits.
    [GeneratedRegex(@"^#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexPattern();

    // Comma form: rgb(r, g, b) / rgba(r, g, b, a)
    [GeneratedRegex(@"^rgba?\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}(\s*,\s*[\d.]+%?)?\s*\)$")]
    private static partial Regex RgbCommaPattern();

    // CSS Color Module 4 space form: rgb(r g b) / rgb(r g b / a)
    [GeneratedRegex(@"^rgba?\(\s*\d{1,3}\s+\d{1,3}\s+\d{1,3}(\s*\/\s*[\d.]+%?)?\s*\)$")]
    private static partial Regex RgbSpacePattern();

    // Comma form: hsl(h, s%, l%) / hsla(...). Hue may be negative (CSS spec — wrapped at render).
    [GeneratedRegex(@"^hsla?\(\s*-?\d{1,3}\s*,\s*\d{1,3}%?\s*,\s*\d{1,3}%?(\s*,\s*[\d.]+%?)?\s*\)$")]
    private static partial Regex HslCommaPattern();

    // CSS Color Module 4 space form: hsl(h s% l%) / hsl(h s% l% / a)
    [GeneratedRegex(@"^hsla?\(\s*-?\d{1,3}\s+\d{1,3}%?\s+\d{1,3}%?(\s*\/\s*[\d.]+%?)?\s*\)$")]
    private static partial Regex HslSpacePattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();

        string? format = null;
        if (HexPattern().IsMatch(trimmed)) format = "hex";
        else if (RgbCommaPattern().IsMatch(trimmed) || RgbSpacePattern().IsMatch(trimmed)) format = "rgb";
        else if (HslCommaPattern().IsMatch(trimmed) || HslSpacePattern().IsMatch(trimmed)) format = "hsl";

        if (format != null)
        {
            result = new TextAnalysis(TextType.ColorCode, 0.95,
                new() { ["format"] = format, ["value"] = trimmed });
            return true;
        }
        return false;
    }
}
