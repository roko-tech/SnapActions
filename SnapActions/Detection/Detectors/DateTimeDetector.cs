using System.Globalization;
using System.Text.RegularExpressions;

namespace SnapActions.Detection.Detectors;

public partial class DateTimeDetector : ITextDetector
{
    public TextType Type => TextType.DateTime;

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2})?)?")]
    private static partial Regex IsoDatePattern();

    [GeneratedRegex(@"^\d{1,2}[/\-]\d{1,2}[/\-]\d{2,4}")]
    private static partial Regex CommonDatePattern();

    // Unix timestamp (10 or 13 digits)
    [GeneratedRegex(@"^\d{10,13}$")]
    private static partial Regex UnixTimestampPattern();

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        if (trimmed.Length < 8 || trimmed.Length > 40) return false;

        // Check ISO format first
        if (IsoDatePattern().IsMatch(trimmed) &&
            System.DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
        {
            result = new TextAnalysis(TextType.DateTime, 0.9,
                new() { ["parsed"] = dt.ToString("O"), ["format"] = "iso" });
            return true;
        }

        // Unix timestamp
        if (UnixTimestampPattern().IsMatch(trimmed) && long.TryParse(trimmed, out var ts))
        {
            var epoch = ts > 9999999999 ?
                DateTimeOffset.FromUnixTimeMilliseconds(ts) :
                DateTimeOffset.FromUnixTimeSeconds(ts);
            // Sanity: year between 2000 and 2100
            if (epoch.Year >= 2000 && epoch.Year <= 2100)
            {
                result = new TextAnalysis(TextType.DateTime, 0.8,
                    new() { ["parsed"] = epoch.ToString("O"), ["format"] = "unix" });
                return true;
            }
        }

        // Common date formats
        if (CommonDatePattern().IsMatch(trimmed) &&
            System.DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var dt2))
        {
            result = new TextAnalysis(TextType.DateTime, 0.75,
                new() { ["parsed"] = dt2.ToString("O"), ["format"] = "common" });
            return true;
        }

        return false;
    }
}
