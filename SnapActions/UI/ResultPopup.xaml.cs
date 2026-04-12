using System.Net.Http;
using System.Windows;

namespace SnapActions.UI;

public partial class ResultPopup : Window
{
    private string _resultText = "";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public ResultPopup()
    {
        InitializeComponent();
    }

    public async void ShowAt(double screenX, double screenY, string title, Func<HttpClient, Task<string>> fetchResult)
    {
        TitleText.Text = title;
        LoadingText.Visibility = Visibility.Visible;
        ResultText.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;

        // Position near cursor using raw screen pixels / 96 DPI as baseline
        // (WPF handles DPI scaling for us when we set Left/Top)
        Left = screenX - 100;
        Top = screenY - 80;
        Show();
        Activate();

        try
        {
            _resultText = await fetchResult(Http);
            ResultText.Text = _resultText;
            LoadingText.Visibility = Visibility.Collapsed;
            ResultText.Visibility = Visibility.Visible;
            CopyButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LoadingText.Text = $"Error: {ex.Message}";
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_resultText))
            Clipboard.SetText(_resultText);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // Static helpers for fetching results

    public static async Task<string> FetchTranslation(HttpClient http, string text, string targetLang)
    {
        var to = string.IsNullOrEmpty(targetLang) ? "en" : targetLang;
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=autodetect|{to}";
        var json = await http.GetStringAsync(url);
        var match = System.Text.RegularExpressions.Regex.Match(json, "\"translatedText\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) : "Translation not available";
    }

    public static async Task<string> FetchDefinition(HttpClient http, string word)
    {
        var url = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(word.Trim())}";
        var json = await http.GetStringAsync(url);

        var defMatch = System.Text.RegularExpressions.Regex.Match(json, "\"definition\"\\s*:\\s*\"([^\"]+)\"");
        var posMatch = System.Text.RegularExpressions.Regex.Match(json, "\"partOfSpeech\"\\s*:\\s*\"([^\"]+)\"");
        var phoneticMatch = System.Text.RegularExpressions.Regex.Match(json, "\"phonetic\"\\s*:\\s*\"([^\"]+)\"");

        var result = "";
        if (phoneticMatch.Success) result += phoneticMatch.Groups[1].Value + "\n\n";
        if (posMatch.Success) result += posMatch.Groups[1].Value + "\n";
        if (defMatch.Success) result += defMatch.Groups[1].Value;
        else result = "No definition found";
        return result;
    }

    public static async Task<string> FetchCurrencyConversion(HttpClient http, string text)
    {
        var numMatch = System.Text.RegularExpressions.Regex.Match(text, @"[\d,]+\.?\d*");
        if (!numMatch.Success) return "No amount found";
        var amount = numMatch.Value.Replace(",", "");

        var src = "USD";
        var upper = text.ToUpperInvariant();
        if (upper.Contains("EUR") || text.Contains('\u20AC')) src = "EUR";
        else if (upper.Contains("GBP") || text.Contains('\u00A3')) src = "GBP";
        else if (upper.Contains("JPY") || text.Contains('\u00A5')) src = "JPY";
        else if (upper.Contains("SAR")) src = "SAR";
        else if (upper.Contains("AED")) src = "AED";
        else if (upper.Contains("KWD")) src = "KWD";
        else if (upper.Contains("CAD")) src = "CAD";
        else if (upper.Contains("AUD")) src = "AUD";
        else if (upper.Contains("CHF")) src = "CHF";
        else if (upper.Contains("CNY")) src = "CNY";
        else if (upper.Contains("INR")) src = "INR";
        else if (upper.Contains("BRL")) src = "BRL";
        else if (upper.Contains("KRW")) src = "KRW";
        else if (upper.Contains("TRY")) src = "TRY";

        var targets = src == "USD" ? "EUR,GBP,SAR,JPY" : "USD,EUR,GBP,SAR";
        var url = $"https://api.frankfurter.app/latest?amount={amount}&from={src}&to={targets}";
        var json = await http.GetStringAsync(url);

        var rates = System.Text.RegularExpressions.Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*([\\d.]+)");
        var result = $"{amount} {src} =\n";
        foreach (System.Text.RegularExpressions.Match m in rates)
        {
            var currency = m.Groups[1].Value;
            var value = m.Groups[2].Value;
            if (currency.Length == 3 && currency != "amount")
                result += $"  {value} {currency}\n";
        }
        return result.TrimEnd();
    }
}
