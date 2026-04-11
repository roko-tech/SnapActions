using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class ColorCodeDetector : ITextDetector
{
    public TextType Type => TextType.ColorCode;

    [GeneratedRegex(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"^rgba?\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}(\s*,\s*[\d.]+)?\s*\)$")]
    private static partial Regex RgbPattern();

    [GeneratedRegex(@"^hsla?\(\s*\d{1,3}\s*,\s*\d{1,3}%?\s*,\s*\d{1,3}%?(\s*,\s*[\d.]+)?\s*\)$")]
    private static partial Regex HslPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();

        string? format = null;
        if (HexPattern().IsMatch(trimmed)) format = "hex";
        else if (RgbPattern().IsMatch(trimmed)) format = "rgb";
        else if (HslPattern().IsMatch(trimmed)) format = "hsl";

        if (format != null)
        {
            result = new TextAnalysis(TextType.ColorCode, 0.95,
                new() { ["format"] = format, ["value"] = trimmed });
            return true;
        }
        return false;
    }
}
