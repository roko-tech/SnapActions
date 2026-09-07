using SnapActions.Detection;
using SnapActions.UI;

namespace SnapActions.Actions.ContextActions;

public class CurrencyConverterAction : IAction
{
    public string Id => "currency_convert";
    public string Name => "Convert Currency";
    public string IconKey => "IconConvert";
    public ActionCategory Category => ActionCategory.Context;

    public bool CanExecute(string text, TextAnalysis analysis) => Helpers.MoneyValue.TryParse(text, out _);

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var trimmed = text.Trim();
        var target = Config.SettingsManager.Current.TargetCurrency;
        ResultPopup.ShowNearCursor($"Convert to {target}",
            ct => Services.LookupService.Shared.ConvertCurrency(trimmed, target, ct));
        return new ActionResult(true);
    }
}
