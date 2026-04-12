using System.Runtime.InteropServices;
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
        var popup = new ResultPopup();
        var trimmed = text.Trim();
        GetCursorPos(out var pt);
        var target = Config.SettingsManager.Current.TargetCurrency;
        popup.ShowAt(pt.X, pt.Y, $"Convert to {target}",
            async http => await ResultPopup.FetchCurrencyConversion(http, trimmed, target));
        return new ActionResult(true, Message: "Converting...");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);
}
