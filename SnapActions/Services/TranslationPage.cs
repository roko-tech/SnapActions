using System.Text;
using System.Text.Json;
using SnapActions.Config;

namespace SnapActions.Services;

/// <summary>URL handling for the Google Translate page and reading its translated text for the card.</summary>
internal static class TranslationPage
{
    // Google renders each translated segment in a W297wb span inside one element marked with the
    // target language; the spaces between segments are text nodes beside the spans, and each span
    // carries hidden "alternatives" text. Later W297wb spans can be single-word dictionary entries.
    // An undocumented page detail: when it stops matching, the popup shows the page itself instead.
    internal const string ReadResultScript = """
        (() => {
          const first = document.querySelector('span[jsname="W297wb"]');
          const root = first?.closest('[lang]');
          if (!root || root === document.documentElement || root === document.body) return null;
          const segments = [...root.querySelectorAll('span[jsname="W297wb"]')];
          const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
          let text = '';
          for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            const parent = node.parentElement;
            if (parent.closest('span[jsname="W297wb"]') || segments.some(s => parent.contains(s))) text += node.data;
          }
          return text;
        })()
        """;

    internal static bool CanTranslate(string text) => !string.IsNullOrWhiteSpace(text)
        && Encoding.UTF8.GetByteCount(text) <= 500;

    /// <summary>Decodes <see cref="ReadResultScript"/>'s JSON result; null until Google shows a translation.</summary>
    internal static string? ReadResult(string? scriptResult)
    {
        try
        {
            using var json = JsonDocument.Parse(scriptResult ?? "null");
            if (json.RootElement.ValueKind != JsonValueKind.String) return null;
            var text = json.RootElement.GetString()!.Trim();
            return text.Length > 0 ? text : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Keeps the selection's surrounding whitespace (e.g. a triple-click's line break) when replacing it.</summary>
    internal static string WithOuterWhitespace(string original, string translation)
    {
        int start = original.Length - original.TrimStart().Length;
        int end = original.Length - original.TrimEnd().Length;
        return original[..start] + translation + original[(original.Length - end)..];
    }

    internal static string LanguageName(string code) => code == ""
        ? "Detect language"
        : LanguageOptions.All.FirstOrDefault(l => l.Code == code)?.Name ?? code;

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
