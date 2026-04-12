using System.Text.RegularExpressions;
using SnapActions.Detection;
using SnapActions.Helpers;

namespace SnapActions.Actions.ContextActions;

public partial class CurrencyConverterAction : IAction
{
    public string Id => "currency_convert";
    public string Name => "Convert Currency";
    public string IconKey => "IconConvert";
    public ActionCategory Category => ActionCategory.Context;

    [GeneratedRegex(@"[\$\€\£\¥]?\s*[\d,]+\.?\d*\s*(?:USD|EUR|GBP|JPY|SAR|AED|KWD|BHD|QAR|OMR|CAD|AUD|CHF|CNY|INR|BRL|KRW|TRY)?", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyPattern();

    public bool CanExecute(string text, TextAnalysis analysis)
    {
        var t = text.Trim();
        return t.Length <= 50 && !t.Contains('\n') && CurrencyPattern().IsMatch(t)
            && t.Any(char.IsDigit);
    }

    public ActionResult Execute(string text, TextAnalysis analysis)
    {
        var encoded = Uri.EscapeDataString(text.Trim());
        var lang = Config.SettingsManager.Current.SearchLanguage;
        var hl = string.IsNullOrEmpty(lang) ? "" : $"&hl={lang}";
        return ProcessHelper.TryShellOpen(
            $"https://www.google.com/search?q={encoded}+to+USD{hl}",
            "Converting currency...");
    }
}
