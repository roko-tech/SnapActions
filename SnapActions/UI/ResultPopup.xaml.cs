using System.Collections.Concurrent;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
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
    private readonly System.Threading.CancellationTokenSource _cts = new();

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public ResultPopup()
    {
        InitializeComponent();

        // Don't steal focus from the user's app — match ToolbarWindow's no-activate behavior.
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };

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

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void SafeClose()
    {
        if (_closed) return;
        _closed = true;
        _checkTimer.Stop();
        try { _cts.Cancel(); } catch { }
        try { Close(); } catch { }
    }

    /// <summary>Static helper: creates popup, positions near cursor, fetches result.</summary>
    public static void ShowNearCursor(string title, Func<HttpClient, System.Threading.CancellationToken, Task<string>> fetchResult)
    {
        var popup = new ResultPopup();
        NativeMethods.GetCursorPos(out var pt);
        popup.ShowAt(pt.X, pt.Y, title, fetchResult);
    }

    public async void ShowAt(double screenX, double screenY, string title,
        Func<HttpClient, System.Threading.CancellationToken, Task<string>> fetchResult)
    {
        TitleText.Text = title;
        LoadingText.Visibility = Visibility.Visible;
        ResultText.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;

        // Use the DPI of the monitor under the cursor, not whatever monitor the window starts on.
        var monitorDpi = ScreenHelper.GetDpiForPoint(new System.Windows.Point(screenX, screenY));
        _dpi = monitorDpi.X > 0 ? monitorDpi.X : 1.0;
        Show();

        // Position above-left of cursor, then clamp to working area
        var sb = ScreenHelper.GetScreenBounds(new System.Windows.Point(screenX, screenY));
        double sL = sb.Left / _dpi, sT = sb.Top / _dpi;
        double sR = sb.Right / _dpi, sB = sb.Bottom / _dpi;

        UpdateLayout();
        double w = ActualWidth > 10 ? ActualWidth : 220;
        double h = ActualHeight > 10 ? ActualHeight : 120;

        double left = (screenX / _dpi) - 100;
        double top = (screenY / _dpi) - 80;
        if (left < sL + 8) left = sL + 8;
        if (left + w > sR - 8) left = sR - 8 - w;
        if (top < sT + 8) top = sT + 8;
        if (top + h > sB - 8) top = sB - 8 - h;

        Left = left;
        Top = top;
        Topmost = true;
        // Intentionally NOT calling Activate() — would steal focus from the user's app.
        _checkTimer.Start();

        try
        {
            _resultText = await fetchResult(Http, _cts.Token);
            if (_closed) return;
            _loaded = true;
            ResultText.Text = _resultText;
            LoadingText.Visibility = Visibility.Collapsed;
            ResultText.Visibility = Visibility.Visible;
            CopyButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { /* user closed before result arrived */ }
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

    // Cache translations for ~30 minutes — MyMemory free tier is 5k chars/day per IP.
    private static readonly ConcurrentDictionary<string, (DateTime fetched, string text)> _translationCache = new();
    private static readonly TimeSpan TranslationCacheTtl = TimeSpan.FromMinutes(30);

    public static async Task<string> FetchTranslation(HttpClient http, string text, string targetLang,
        System.Threading.CancellationToken ct = default)
    {
        var to = string.IsNullOrEmpty(targetLang) ? "en" : targetLang;
        var cacheKey = $"{to}|{text}";

        if (_translationCache.TryGetValue(cacheKey, out var cached) &&
            DateTime.UtcNow - cached.fetched < TranslationCacheTtl)
            return cached.text;

        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=autodetect|{to}";
        string json;
        try { json = await http.GetStringAsync(url, ct); }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
        { return "Translation quota reached. Try again later."; }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var translated = doc.RootElement
                .GetProperty("responseData")
                .GetProperty("translatedText")
                .GetString();
            var result = System.Net.WebUtility.HtmlDecode(translated ?? "Translation not available");
            _translationCache[cacheKey] = (DateTime.UtcNow, result);
            return result;
        }
        catch { return "Translation not available"; }
    }

    private static readonly HashSet<string> DictionarySupportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "es", "fr", "de", "hi", "it", "ja", "ko", "pt-BR", "ru", "tr", "zh-CN"
    };

    public static async Task<string> FetchDefinition(HttpClient http, string word, string lang = "en",
        System.Threading.CancellationToken ct = default)
    {
        var dictLang = DictionarySupportedLanguages.Contains(lang) ? lang : "en";
        var url = $"https://api.dictionaryapi.dev/api/v2/entries/{dictLang}/{Uri.EscapeDataString(word.Trim())}";
        string json;
        try { json = await http.GetStringAsync(url, ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        { return "No definition found"; }

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

    // Cache rates per source currency for ~6h to avoid hammering open.er-api.com.
    // ConcurrentDictionary is required because two popups (rapid back-to-back conversions) can fetch in parallel.
    private static readonly ConcurrentDictionary<string, (DateTime fetched, Dictionary<string, double> rates)> _rateCache = new();
    private static readonly TimeSpan RateCacheTtl = TimeSpan.FromHours(6);

    public static async Task<string> FetchCurrencyConversion(HttpClient http, string text, string targetCurrency = "USD",
        System.Threading.CancellationToken ct = default)
    {
        var numMatch = System.Text.RegularExpressions.Regex.Match(text, @"[\d,]+\.?\d*");
        if (!numMatch.Success) return "No amount found";
        var amount = numMatch.Value.Replace(",", "");

        var src = DetectSourceCurrency(text, numMatch.Index, numMatch.Length);

        try
        {
            var rates = await GetRates(http, src, ct);
            if (rates == null) return "Conversion failed";
            var amt = double.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
            if (!rates.TryGetValue(targetCurrency, out var rate))
                return $"Cannot convert {src} to {targetCurrency}";
            return $"{amount} {src} = {amt * rate:N2} {targetCurrency}";
        }
        catch (OperationCanceledException) { throw; }
        catch { return "Conversion failed"; }
    }

    /// <summary>
    /// Pick the source currency whose code/symbol is closest to the matched number.
    /// "$50 last EUR-trip" → USD (because $ is adjacent to 50, EUR is far away).
    /// Falls back to USD when no symbol/code is found.
    /// </summary>
    private static string DetectSourceCurrency(string text, int numStart, int numLength)
    {
        int numEnd = numStart + numLength;
        string best = "USD";
        int bestDist = int.MaxValue;

        foreach (var (code, symbols) in CurrencySymbols)
        {
            foreach (var sym in symbols)
            {
                int idx = text.IndexOf(sym, StringComparison.OrdinalIgnoreCase);
                while (idx >= 0)
                {
                    int dist = idx >= numEnd ? idx - numEnd
                              : idx + sym.Length <= numStart ? numStart - (idx + sym.Length)
                              : 0; // overlapping = adjacent
                    if (dist < bestDist) { bestDist = dist; best = code; }
                    idx = text.IndexOf(sym, idx + 1, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        return best;
    }

    private static async Task<Dictionary<string, double>?> GetRates(HttpClient http, string src,
        System.Threading.CancellationToken ct)
    {
        if (_rateCache.TryGetValue(src, out var cached) && DateTime.UtcNow - cached.fetched < RateCacheTtl)
            return cached.rates;

        var json = await http.GetStringAsync($"https://open.er-api.com/v6/latest/{src}", ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var ratesEl = doc.RootElement.GetProperty("rates");
        var rates = new Dictionary<string, double>();
        foreach (var p in ratesEl.EnumerateObject())
            if (p.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                rates[p.Name] = p.Value.GetDouble();
        _rateCache[src] = (DateTime.UtcNow, rates);
        return rates;
    }
}
