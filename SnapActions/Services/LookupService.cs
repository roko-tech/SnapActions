using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SnapActions.Helpers;

namespace SnapActions.Services;

/// <summary>Network lookups and success-only caches, independent of WPF and user configuration.</summary>
public sealed class LookupService(HttpClient http)
{
    public static LookupService Shared { get; } = new(new HttpClient { Timeout = TimeSpan.FromSeconds(8) });
    private readonly ConcurrentDictionary<string, (DateTime Time, string Text)> _translations = new();
    private readonly ConcurrentDictionary<string, (DateTime Time, Dictionary<string, decimal> Rates)> _rates = new();
    private static readonly HashSet<string> DictionaryLanguages = new(StringComparer.OrdinalIgnoreCase)
        { "en" }; // The provider's current documented endpoint is English only.

    public static bool CanTranslate(string text) => !string.IsNullOrWhiteSpace(text) && Encoding.UTF8.GetByteCount(text) <= 500;

    public async Task<LookupResult> Translate(string text, string source, string target, CancellationToken ct = default)
    {
        if (!CanTranslate(text)) return LookupResult.Error("Select text containing at most 500 UTF-8 bytes.");
        if (!Config.LanguageOptions.IsSupported(source))
            return LookupResult.Error("Choose the source language, then select Translate.");
        if (!Config.LanguageOptions.IsSupported(target)) return LookupResult.Error("Choose the target language.");
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
            return LookupResult.Error("Choose two different languages.");
        var key = $"{source}|{target}|{text}";
        foreach (var item in _translations)
            if (DateTime.UtcNow - item.Value.Time >= TimeSpan.FromMinutes(30)) _translations.TryRemove(item.Key, out _);
        if (_translations.TryGetValue(key, out var cached)) return LookupResult.Success(cached.Text);
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={Uri.EscapeDataString(source + "|" + target)}";
        var json = await BoundedHttp.GetStringAsync(http, url, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("responseStatus", out var status) || status.ToString() != "200")
            return LookupResult.Error(status.ToString() == "429"
                ? "Translation quota reached. Try again later." : "Translation was rejected. Check the languages and try again.");
        var translated = WebUtility.HtmlDecode(root.GetProperty("responseData").GetProperty("translatedText").GetString());
        if (string.IsNullOrWhiteSpace(translated)) return new(LookupStatus.Empty, "No translation found");
        if (translated.Contains("SELECT TWO DISTINCT LANGUAGES", StringComparison.OrdinalIgnoreCase))
            return LookupResult.Error("Choose two different languages.");
        ct.ThrowIfCancellationRequested();
        if (_translations.Count >= 500)
            foreach (var item in _translations.OrderBy(p => p.Value.Time).Take(_translations.Count - 499))
                _translations.TryRemove(item.Key, out _);
        _translations[key] = (DateTime.UtcNow, translated);
        return LookupResult.Success(translated);
    }

    public async Task<LookupResult> Define(string word, string language, CancellationToken ct = default)
    {
        if (!DictionaryLanguages.Contains(language))
            return LookupResult.Error("This dictionary does not support the selected language. Choose another dictionary language in Settings.");
        string json;
        try { json = await BoundedHttp.GetStringAsync(http,
            $"https://api.dictionaryapi.dev/api/v2/entries/{language}/{Uri.EscapeDataString(word.Trim())}", ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        { return new(LookupStatus.Empty, "No definition found"); }
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetArrayLength() == 0) return new(LookupStatus.Empty, "No definition found");
        var entry = doc.RootElement[0];
        var sb = new StringBuilder();
        if (entry.TryGetProperty("phonetic", out var phonetic)) sb.AppendLine(phonetic.GetString()).AppendLine();
        foreach (var meaning in entry.GetProperty("meanings").EnumerateArray())
        {
            sb.AppendLine(meaning.GetProperty("partOfSpeech").GetString());
            foreach (var definition in meaning.GetProperty("definitions").EnumerateArray().Take(2))
                sb.Append("  ").AppendLine(definition.GetProperty("definition").GetString());
            sb.AppendLine();
        }
        return LookupResult.Success(sb.ToString().TrimEnd());
    }

    public async Task<LookupResult> ConvertCurrency(string text, string target, CancellationToken ct = default)
    {
        if (!MoneyValue.TryParse(text, out var money)) return LookupResult.Error("Select one amount with its currency, such as -50 USD.");
        if (!MoneyValue.Currencies.ContainsKey(target)) return LookupResult.Error("Choose a supported target currency.");
        if (!_rates.TryGetValue(money.Currency, out var cached) || DateTime.UtcNow - cached.Time >= TimeSpan.FromHours(6))
        {
            var json = await BoundedHttp.GetStringAsync(http, $"https://open.er-api.com/v6/latest/{money.Currency}", ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var status) && status.GetString() != "success")
                return LookupResult.Error("The rate service could not convert this currency.");
            var rates = new Dictionary<string, decimal>();
            foreach (var rate in doc.RootElement.GetProperty("rates").EnumerateObject())
                if (rate.Value.TryGetDecimal(out var value) && value > 0) rates[rate.Name] = value;
            ct.ThrowIfCancellationRequested();
            cached = (DateTime.UtcNow, rates);
            _rates[money.Currency] = cached;
        }
        if (!cached.Rates.TryGetValue(target, out var multiplier))
            return LookupResult.Error($"Cannot convert {money.Currency} to {target}");
        try { return LookupResult.Success($"{money.Amount:N2} {money.Currency} = {money.Amount * multiplier:N2} {target}"); }
        catch (OverflowException) { return LookupResult.Error("The amount is too large to convert."); }
    }

    public async Task<LookupResult> FetchText(string url, string field, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return LookupResult.Error("Fetch actions require an HTTP or HTTPS URL.");
        var body = await BoundedHttp.GetStringAsync(http, url, ct, 64 * 1024);
        if (string.IsNullOrWhiteSpace(field)) return LookupResult.Success(body);
        using var doc = JsonDocument.Parse(body);
        var value = doc.RootElement;
        foreach (var part in field.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out var next))
                return new(LookupStatus.Empty, "The requested field was not found.");
            value = next;
        }
        return LookupResult.Success(value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString());
    }
}
