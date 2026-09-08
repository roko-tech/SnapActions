using System.Text;
using SnapActions.Config;

namespace SnapActions.Services;

/// <summary>URL handling for the visible Google Translate page; never reads page content.</summary>
internal static class TranslationPage
{
    internal static bool CanTranslate(string text) => !string.IsNullOrWhiteSpace(text)
        && Encoding.UTF8.GetByteCount(text) <= 500;

    internal static Uri BuildUri(string text, string source, string target)
    {
        var from = CanonicalLanguage(source) ?? "auto";
        var to = CanonicalLanguage(target) ?? "en";
        return new Uri($"https://translate.google.com/?sl={Uri.EscapeDataString(from)}&tl={Uri.EscapeDataString(to)}&text={Uri.EscapeDataString(text)}&op=translate");
    }

    internal static bool IsAllowedNavigation(string? address) => Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Host is "translate.google.com" or "consent.google.com";

    internal static bool TryReadLanguages(string? address, out string source, out string target)
    {
        source = target = "";
        if (!IsAllowedNavigation(address)) return false;
        var uri = new Uri(address!);
        if (uri.Host != "translate.google.com") return false;
        string? from = null, to = null;
        foreach (var item in uri.Query.TrimStart('?').Split('&'))
        {
            var parts = item.Split('=', 2);
            if (parts.Length != 2 || parts[0] is not ("sl" or "tl")) continue;
            var value = Uri.UnescapeDataString(parts[1].Replace('+', ' '));
            if (parts[0] == "sl")
            {
                if (from != null) return false;
                from = value;
            }
            else
            {
                if (to != null) return false;
                to = value;
            }
        }
        var knownSource = from == "auto" ? "" : CanonicalLanguage(from);
        var knownTarget = CanonicalLanguage(to);
        if (knownSource == null || knownTarget == null) return false;
        source = knownSource;
        target = knownTarget;
        return true;
    }

    private static string? CanonicalLanguage(string? code)
    {
        code = code?.ToLowerInvariant() switch
        {
            "iw" => "he",
            "pt" => "pt-BR",
            "zh" or "zh-hans" => "zh-CN",
            "zh-hant" => "zh-TW",
            _ => code
        };
        return LanguageOptions.All.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Code;
    }
}
