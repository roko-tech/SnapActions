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
            var formatted = result % 1 == 0 ? ((long)result).ToString() : result.ToString("G15");
            return new ActionResult(true, formatted, $"= {formatted}");
        }
        catch (Exception ex)
        {
            return new ActionResult(false, Message: $"Error: {ex.Message}");
        }
    }
}
