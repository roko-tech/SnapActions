using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.ContextActions;

public class CalculateAction : IAction
{
    public string Id => "calculate";
    public string Name => "Calculate";
    public string IconKey => "IconCalculate";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) => analysis.Type == TextType.MathExpression;

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        try
        {
            var result = MathEvaluator.Evaluate(text.Trim());
            string formatted;
            if (double.IsNaN(result) || double.IsInfinity(result))
                return new ActionResult(false, Message: "Result is not a finite number");

            if (result % 1 == 0 && result >= long.MinValue && result <= long.MaxValue)
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
