using SnapActions.Helpers;

namespace SnapActions.Detection.Detectors;

public class UnitDetector : ITextDetector
{
    public TextType Type => TextType.Unit;

    public bool TryDetect(string text, out TextAnalysis result)
    {
        result = default!;
        var trimmed = text.Trim();
        // Don't waste time on long inputs — units are usually short like "5 ft" or "100 km/h".
        if (trimmed.Length is < 2 or > 40) return false;
        if (trimmed.Contains('\n')) return false;

        if (!UnitConverter.TryParse(trimmed, out _, out var unit) || unit is null)
            return false;

        result = new TextAnalysis(TextType.Unit, 0.85, new()
        {
            ["category"] = unit.Category.ToString(),
            ["symbol"] = unit.Symbol,
        });
        return true;
    }
}
