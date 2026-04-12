using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SnapActions.UI;

public partial class ResultPopup : Window
{
    private string _resultText = "";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public ResultPopup()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    public async void ShowAt(double screenX, double screenY, string title, Func<HttpClient, Task<string>> fetchResult)
    {
        TitleText.Text = title;
        LoadingText.Visibility = Visibility.Visible;
        ResultText.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;

        // Position near cursor
        Left = screenX / GetDpiX() - 100;
        Top = screenY / GetDpiY() - 80;
        Show();

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

    private double GetDpiX()
    {
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    private double GetDpiY()
    {
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
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
        // Uses MyMemory free translation API (no key needed, 5000 chars/day)
        var from = "autodetect";
        var to = string.IsNullOrEmpty(targetLang) ? "en" : targetLang;
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={from}|{to}";
        var json = await http.GetStringAsync(url);

        // Parse responseData.translatedText from JSON
        var match = System.Text.RegularExpressions.Regex.Match(json, "\"translatedText\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) : "Translation not available";
    }

    public static async Task<string> FetchDefinition(HttpClient http, string word)
    {
        // Uses free dictionary API (no key needed)
        var url = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(word.Trim())}";
        var json = await http.GetStringAsync(url);

        // Parse first definition
        var defMatch = System.Text.RegularExpressions.Regex.Match(json, "\"definition\"\\s*:\\s*\"([^\"]+)\"");
        var posMatch = System.Text.RegularExpressions.Regex.Match(json, "\"partOfSpeech\"\\s*:\\s*\"([^\"]+)\"");
        var phoneticMatch = System.Text.RegularExpressions.Regex.Match(json, "\"phonetic\"\\s*:\\s*\"([^\"]+)\"");

        var result = "";
        if (phoneticMatch.Success)
            result += phoneticMatch.Groups[1].Value + "\n\n";
        if (posMatch.Success)
            result += posMatch.Groups[1].Value + "\n";
        if (defMatch.Success)
            result += defMatch.Groups[1].Value;
        else
            result = "No definition found";

        return result;
    }

    public static async Task<string> FetchCurrencyConversion(HttpClient http, string text)
    {
        // Extract number from text
        var numMatch = System.Text.RegularExpressions.Regex.Match(text, @"[\d,]+\.?\d*");
        if (!numMatch.Success) return "No amount found";

        var amount = numMatch.Value.Replace(",", "");

        // Detect source currency
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

        // Use frankfurter.app (free, no key, ECB rates)
        var targets = src == "USD" ? "EUR,GBP,SAR,JPY" : "USD,EUR,GBP,SAR";
        var url = $"https://api.frankfurter.app/latest?amount={amount}&from={src}&to={targets}";
        var json = await http.GetStringAsync(url);

        // Parse rates
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

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
