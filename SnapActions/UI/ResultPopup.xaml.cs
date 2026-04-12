using System.Net.Http;
using System.Windows;

namespace SnapActions.UI;

public partial class ResultPopup : Window
{
    private string _resultText = "";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private bool _loaded;
    private bool _closed;

    public ResultPopup()
    {
        InitializeComponent();
        Deactivated += (_, _) => SafeClose();
        MouseLeave += (_, _) => { if (_loaded) SafeClose(); };
    }

    private void SafeClose()
    {
        if (_closed) return;
        _closed = true;
        try { Close(); } catch { }
    }

    public async void ShowAt(double screenX, double screenY, string title, Func<HttpClient, Task<string>> fetchResult)
    {
        TitleText.Text = title;
        LoadingText.Visibility = Visibility.Visible;
        ResultText.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;

        // Position near cursor using raw screen pixels / 96 DPI as baseline
        // (WPF handles DPI scaling for us when we set Left/Top)
        // Show first, then position (need PresentationSource for DPI)
        Show();

        double dpi = 1.0;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
            dpi = source.CompositionTarget.TransformToDevice.M11;

        Left = (screenX / dpi) - 100;
        Top = (screenY / dpi) - 80;
        Topmost = true;
        Activate();

        try
        {
            _resultText = await fetchResult(Http);
            if (_closed) return;
            _loaded = true;
            ResultText.Text = _resultText;
            LoadingText.Visibility = Visibility.Collapsed;
            ResultText.Visibility = Visibility.Visible;
            CopyButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _loaded = true;
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

        // Parse with System.Text.Json to properly decode Unicode escapes
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var translated = doc.RootElement
                .GetProperty("responseData")
                .GetProperty("translatedText")
                .GetString();
            return System.Net.WebUtility.HtmlDecode(translated ?? "Translation not available");
        }
        catch
        {
            return "Translation not available";
        }
    }

    public static async Task<string> FetchDefinition(HttpClient http, string word)
    {
        var url = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(word.Trim())}";
        var json = await http.GetStringAsync(url);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var entry = doc.RootElement[0];
            var result = "";

            if (entry.TryGetProperty("phonetic", out var phonetic))
                result += phonetic.GetString() + "\n\n";

            var meanings = entry.GetProperty("meanings");
            foreach (var meaning in meanings.EnumerateArray())
            {
                var pos = meaning.GetProperty("partOfSpeech").GetString();
                result += $"{pos}\n";
                var defs = meaning.GetProperty("definitions");
                int count = 0;
                foreach (var def in defs.EnumerateArray())
                {
                    if (count >= 2) break;
                    result += $"  {def.GetProperty("definition").GetString()}\n";
                    count++;
                }
                result += "\n";
            }

            return string.IsNullOrWhiteSpace(result) ? "No definition found" : result.TrimEnd();
        }
        catch
        {
            return "No definition found";
        }
    }

    private static readonly Dictionary<string, string[]> CurrencySymbols = new()
    {
        ["EUR"] = ["EUR", "\u20AC"], ["GBP"] = ["GBP", "\u00A3"],
        ["JPY"] = ["JPY", "\u00A5"], ["SAR"] = ["SAR", "\uFDFC"],
        ["AED"] = ["AED"], ["KWD"] = ["KWD"], ["CAD"] = ["CAD"],
        ["AUD"] = ["AUD"], ["CHF"] = ["CHF"], ["CNY"] = ["CNY"],
        ["INR"] = ["INR", "\u20B9"], ["BRL"] = ["BRL"], ["KRW"] = ["KRW"],
        ["TRY"] = ["TRY", "\u20BA"], ["USD"] = ["USD", "$"],
    };

    public static async Task<string> FetchCurrencyConversion(HttpClient http, string text, string targetCurrency = "USD")
    {
        var numMatch = System.Text.RegularExpressions.Regex.Match(text, @"[\d,]+\.?\d*");
        if (!numMatch.Success) return "No amount found";
        var amount = numMatch.Value.Replace(",", "");

        // Detect source currency
        var src = "USD";
        var upper = text.ToUpperInvariant();
        foreach (var (code, symbols) in CurrencySymbols)
        {
            if (symbols.Any(s => upper.Contains(s.ToUpperInvariant()) || text.Contains(s)))
            { src = code; break; }
        }

        // Build target list: always include the user's target + a few common ones
        var targets = new HashSet<string> { targetCurrency, "USD", "EUR", "GBP", "SAR" };
        targets.Remove(src); // don't convert to self
        var targetStr = string.Join(",", targets.Take(5));
        var url = $"https://api.frankfurter.app/latest?amount={amount}&from={src}&to={targets}";
        var json = await http.GetStringAsync(url);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var rates = doc.RootElement.GetProperty("rates");
            var result = $"{amount} {src} =\n";
            foreach (var rate in rates.EnumerateObject())
                result += $"  {rate.Value} {rate.Name}\n";
            return result.TrimEnd();
        }
        catch
        {
            return "Conversion failed";
        }
    }
}
