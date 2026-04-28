using System.Text.RegularExpressions;
using SnapActions.Detection;
using SnapActions.UI;

namespace SnapActions.Actions.ContextActions;

public partial class CurrencyConverterAction : IAction
{
    public string Id => "currency_convert";
    public string Name => "Convert Currency";
    public string IconKey => "IconConvert";
    public ActionCategory Category => ActionCategory.Context;

    // Require an actual currency token — symbol prefix/suffix, or a 3-letter ISO code adjacent
    // to digits. Without the requirement, plain numbers like "100 monkeys" matched and the
    // toolbar offered to convert any numeric selection.
    [GeneratedRegex(
        @"(?:[\$€£¥]\s*[\d,]+\.?\d*|[\d,]+\.?\d*\s*[\$€£¥]|[\d,]+\.?\d*\s*(?:USD|EUR|GBP|JPY|SAR|AED|KWD|BHD|QAR|OMR|CAD|AUD|CHF|CNY|INR|BRL|KRW|TRY)\b|\b(?:USD|EUR|GBP|JPY|SAR|AED|KWD|BHD|QAR|OMR|CAD|AUD|CHF|CNY|INR|BRL|KRW|TRY)\s*[\d,]+\.?\d*)",
        RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyPattern();

    public bool CanExecute(string text, TextAnalysis analysis)
    {
        var t = text.Trim();
        return t.Length <= 50 && !t.Contains('\n') && CurrencyPattern().IsMatch(t);
    }

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var trimmed = text.Trim();
        var target = Config.SettingsManager.Current.TargetCurrency;
        ResultPopup.ShowNearCursor($"Convert to {target}",
            (http, ct) => ResultPopup.FetchCurrencyConversion(http, trimmed, target, ct));
        return new ActionResult(true);
    }
}
