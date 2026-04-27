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

        var sb = new StringBuilder();
        sb.AppendLine($"{Format(value)} {unit.Symbol}");
        sb.AppendLine();

        bool any = false;
        foreach (var target in UnitConverter.TargetsFor(unit.Category))
        {
            if (target.Symbol == unit.Symbol) continue;
            try
            {
                var converted = UnitConverter.Convert(value, unit, target);
                sb.AppendLine($"  = {Format(converted)} {target.Symbol}");
                any = true;
            }
            catch { /* skip incompatible — shouldn't happen since same category */ }
        }
        if (!any) return new ActionResult(false, Message: "No conversions available");

        // For hover preview: just show one common conversion as a one-liner.
        // For the popup: show the full table.
        // We return the full text as ResultText so click → clipboard gets everything;
        // hover preview will Truncate(120) the first conversion line.
        return new ActionResult(true, sb.ToString().TrimEnd(), $"Converted {unit.Symbol}");
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
