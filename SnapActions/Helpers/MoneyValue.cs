using System.Text.RegularExpressions;

namespace SnapActions.Helpers;

/// <summary>A single, explicitly identified amount. Availability and execution share this parser.</summary>
public sealed record MoneyValue(decimal Amount, string Currency)
{
    public static readonly IReadOnlyDictionary<string, string[]> Currencies = new Dictionary<string, string[]>
    {
        ["USD"] = ["USD", "$"], ["EUR"] = ["EUR", "€"], ["GBP"] = ["GBP", "£"],
        ["JPY"] = ["JPY", "¥"], ["SAR"] = ["SAR", "﷼"], ["AED"] = ["AED"],
        ["KWD"] = ["KWD"], ["BHD"] = ["BHD"], ["QAR"] = ["QAR"], ["OMR"] = ["OMR"],
        ["CAD"] = ["CAD"], ["AUD"] = ["AUD"], ["CHF"] = ["CHF"], ["CNY"] = ["CNY"],
        ["INR"] = ["INR", "₹"], ["BRL"] = ["BRL"], ["KRW"] = ["KRW", "₩"], ["TRY"] = ["TRY", "₺"]
    };
    private static readonly string Tokens = string.Join("|", Currencies.Values.SelectMany(v => v).Select(Regex.Escape));
    private static readonly Regex Pattern = new(
        @"\A\s*(?:(?<sign>[+-])?\s*(?<currency>" + Tokens + @")\s*(?<amount>[+-]?[0-9]+(?:[.,][0-9]+)*)|(?<amount>[+-]?[0-9]+(?:[.,][0-9]+)*)\s*(?<currency>" + Tokens + @"))\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public static bool TryParse(string text, out MoneyValue value)
    {
        value = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 128 || text.Contains('\n')) return false;
        var match = Pattern.Match(text);
        if (!match.Success) return false;
        var amountText = match.Groups["amount"].Value;
        var sign = match.Groups["sign"].Value;
        if (sign.Length > 0 && (amountText[0] == '-' || amountText[0] == '+')) return false;
        if (!LocaleNumber.TryParseDecimal(sign + amountText, out var amount)) return false;
        var token = match.Groups["currency"].Value;
        var currency = Currencies.Single(p => p.Value.Contains(token, StringComparer.OrdinalIgnoreCase)).Key;
        value = new MoneyValue(amount, currency);
        return true;
    }
}
