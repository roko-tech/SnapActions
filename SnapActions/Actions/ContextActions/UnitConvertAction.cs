using System.Text;
using SnapActions.Detection;
using SnapActions.Helpers;
using SnapActions.UI;

namespace SnapActions.Actions.ContextActions;

public class UnitConvertAction : IAction
{
    public string Id => "unit_convert";
    public string Name => "Convert Units";
    public string IconKey => "IconConvert";
    public ActionCategory Category => ActionCategory.Context;
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.Unit;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        if (!UnitConverter.TryParse(text.Trim(), out var value, out var unit) || unit is null)
            return new ActionResult(false, Message: "Couldn't parse value/unit");

        // Compact one-line summary: "5 ft = 1.524 m | 60 in | 1.667 yd | 0.001 mi"
        // Previously the click-to-copy result was a multi-line table, which is rarely what users
        // wanted on the clipboard. The full table is still useful but it lives in the popup
        // hover; on click we hand back a tighter representation that pastes cleanly into any
        // single-line context (Slack, search bar, table cell).
        var converted = new List<string>();
        foreach (var target in UnitConverter.TargetsFor(unit.Category))
        {
            if (target.Symbol == unit.Symbol) continue;
            try
            {
                var v = UnitConverter.Convert(value, unit, target);
                converted.Add($"{Format(v)} {target.Symbol}");
            }
            catch { /* skip incompatible — shouldn't happen since same category */ }
        }
        if (converted.Count == 0)
            return new ActionResult(false, Message: "No conversions available");

        var oneLiner = $"{Format(value)} {unit.Symbol} = {string.Join(" | ", converted)}";
        return new ActionResult(true, oneLiner, $"Converted {unit.Symbol}");
    }

    private static string Format(double v)
    {
        // Drop trailing zeros: 5 → "5", 5.0 → "5", 5.123456 → "5.1235"
        if (Math.Abs(v) >= 1e6 || (Math.Abs(v) < 1e-3 && v != 0))
            return v.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        return Math.Round(v, 4, MidpointRounding.AwayFromZero)
            .ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
