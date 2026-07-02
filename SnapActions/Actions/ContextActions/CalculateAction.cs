using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.ContextActions;

public class CalculateAction : IAction
{
    public string Id => "calculate";
    public string Name => "Calculate";
    public string IconKey => "IconCalculate";
    public ActionCategory Category => ActionCategory.Context;
    public bool IsPreviewSafe => true;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.MathExpression;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        try
        {
            var result = MathEvaluator.Evaluate(text.Trim());
            string formatted;
            if (double.IsNaN(result) || double.IsInfinity(result))
                return new ActionResult(false, Message: "Result is not a finite number");

            // Strict '<' on the upper bound: (double)long.MaxValue rounds UP to 2^63, so "<="
            // admits 2^63 itself and the saturating cast then truncates it to long.MaxValue —
            // "2^63" displayed as ...807 instead of ...808 (verified on .NET 9). long.MinValue
            // (-2^63) is exactly representable, so ">=" is safe on the lower bound.
            if (result % 1 == 0 && result >= long.MinValue && result < (double)long.MaxValue)
                formatted = ((long)result).ToString(System.Globalization.CultureInfo.InvariantCulture);
            else
                formatted = result.ToString("G15", System.Globalization.CultureInfo.InvariantCulture);

            return new ActionResult(true, formatted, $"= {formatted}");
        }
        catch (Exception ex)
        {
            return new ActionResult(false, Message: $"Error: {ex.Message}");
        }
    }
}
