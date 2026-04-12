using System.Net.Http;
using System.Text;
using System.Windows;
using SnapActions.Helpers;

namespace SnapActions.UI;

public partial class ResultPopup : Window
{
    private string _resultText = "";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private bool _loaded;
    private bool _closed;
    private double _dpi = 1.0;
    private readonly System.Windows.Threading.DispatcherTimer _checkTimer;

    public ResultPopup()
    {
        InitializeComponent();
        Deactivated += (_, _) => SafeClose();

        _checkTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        _checkTimer.Tick += (_, _) =>
        {
            if (!_loaded || _closed) return;
            NativeMethods.GetCursorPos(out var pt);
            double l = Left * _dpi, t = Top * _dpi;
            double r = l + ActualWidth * _dpi, b = t + ActualHeight * _dpi;
            if (pt.X < l || pt.X > r || pt.Y < t || pt.Y > b)
                SafeClose();
        };
    }

    private void SafeClose()
    {
        if (_closed) return;
        _closed = true;
        _checkTimer.Stop();
        try { Close(); } catch { }
    }

    /// <summary>Static helper: creates popup, positions near cursor, fetches result.</summary>
    public static void ShowNearCursor(string title, Func<HttpClient, Task<string>> fetchResult)
    {
        var popup = new ResultPopup();
        NativeMethods.GetCursorPos(out var pt);
        popup.ShowAt(pt.X, pt.Y, title, fetchResult);
    }

    public async void ShowAt(double screenX, double screenY, string title, Func<HttpClient, Task<string>> fetchResult)
    {
        TitleText.Text = title;
        LoadingText.Visibility = Visibility.Visible;
        ResultText.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;

        Show();
        var source = PresentationSource.FromVisual(this);
        _dpi = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        Left = (screenX / _dpi) - 100;
        Top = (screenY / _dpi) - 80;
        Topmost = true;
        Activate();
        _checkTimer.Start();

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
        SafeClose();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SafeClose();

    // ── API fetch helpers ────────────────────────────────────────

    public static async Task<string> FetchTranslation(HttpClient http, string text, string targetLang)
    {
        var to = string.IsNullOrEmpty(targetLang) ? "en" : targetLang;
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=autodetect|{to}";
        var json = await http.GetStringAsync(url);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var translated = doc.RootElement
                .GetProperty("responseData")
                .GetProperty("translatedText")
                .GetString();
            return System.Net.WebUtility.HtmlDecode(translated ?? "Translation not available");
        }
        catch { return "Translation not available"; }
    }

    public static async Task<string> FetchDefinition(HttpClient http, string word)
    {
        var url = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(word.Trim())}";
        var json = await http.GetStringAsync(url);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var entry = doc.RootElement[0];
            var sb = new StringBuilder();

            if (entry.TryGetProperty("phonetic", out var phonetic))
                sb.AppendLine(phonetic.GetString()).AppendLine();

            foreach (var meaning in entry.GetProperty("meanings").EnumerateArray())
            {
                sb.AppendLine(meaning.GetProperty("partOfSpeech").GetString());
                int count = 0;
                foreach (var def in meaning.GetProperty("definitions").EnumerateArray())
                {
                    if (count++ >= 2) break;
                    sb.Append("  ").AppendLine(def.GetProperty("definition").GetString());
                }
                sb.AppendLine();
            }

            var result = sb.ToString().TrimEnd();
            return string.IsNullOrWhiteSpace(result) ? "No definition found" : result;
        }
        catch { return "No definition found"; }
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

        var src = "USD";
        var upper = text.ToUpperInvariant();
        foreach (var (code, symbols) in CurrencySymbols)
        {
            if (symbols.Any(s => upper.Contains(s.ToUpperInvariant()) || text.Contains(s)))
            { src = code; break; }
        }

        var url = $"https://open.er-api.com/v6/latest/{src}";
        var json = await http.GetStringAsync(url);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var rates = doc.RootElement.GetProperty("rates");
            var amt = double.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

            if (!rates.TryGetProperty(targetCurrency, out var rateVal))
                return $"Cannot convert {src} to {targetCurrency}";

            return $"{amount} {src} = {amt * rateVal.GetDouble():N2} {targetCurrency}";
        }
        catch { return "Conversion failed"; }
    }
}
