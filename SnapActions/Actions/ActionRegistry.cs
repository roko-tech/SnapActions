using SnapActions.Actions.ContextActions;
using SnapActions.Actions.TransformActions;
using SnapActions.Detection;

namespace SnapActions.Actions;

public class ActionRegistry
{
    private readonly List<IAction> _allActions;

    public ActionRegistry()
    {
        _allActions =
        [
            // Context actions
            new OpenUrlAction(),
            new SendEmailAction(),
            new OpenFilePathAction(),
            new OpenContainingFolderAction(),
            new PreviewColorAction(),
            new ConvertColorAction(),
            new FormatJsonAction(),
            new MinifyJsonAction(),
            new FormatXmlAction(),
            new StripTagsAction(),
            new CalculateAction(),
            new IpLookupAction(),
            new DecodeBase64Action(),
            new DecodeJwtAction(),
            new GenerateQrAction(),
            new GenerateUuidAction(),
            new ConvertTimezoneAction(),
            new TranslateAction(),
            new DictionaryAction(),
            new CurrencyConverterAction(),
            new DeleteTextAction(),
            new PastePlainTextAction(),

            // Transform actions
            new CaseTransformAction("upper", "UPPERCASE", "IconUppercase", text => text.ToUpperInvariant()),
            new CaseTransformAction("lower", "lowercase", "IconLowercase", text => text.ToLowerInvariant()),
            new CaseTransformAction("title", "Title Case", "IconTitleCase", ToTitleCase),
            new CaseTransformAction("camel", "camelCase", "IconCamelCase", ToCamelCase),
            new CaseTransformAction("snake", "snake_case", "IconSnakeCase", ToSnakeCase),
            new CaseTransformAction("kebab", "kebab-case", "IconKebabCase", ToKebabCase),
            new CaseTransformAction("pascal", "PascalCase", "IconPascalCase", ToPascalCase),
            new CaseTransformAction("reverse", "Reverse", "IconReverse", ReverseGraphemes),

            new WhitespaceAction("trim", "Trim", text => text.Trim()),
            new WhitespaceAction("remove_extra_spaces", "Remove Extra Spaces",
                text => System.Text.RegularExpressions.Regex.Replace(text, @" {2,}", " ")),
            new WhitespaceAction("sort_lines", "Sort Lines", text => SortLines(text, distinct: false)),
            new WhitespaceAction("dedup_lines", "Remove Duplicates", text => SortLines(text, distinct: true)),
            new WhitespaceAction("remove_linebreaks", "Remove Line Breaks",
                text => System.Text.RegularExpressions.Regex.Replace(text, @"[\r\n]+", " ").Trim()),

            // Encoding actions
            new EncodingAction("url_encode", "URL Encode", "IconEncode",
                text => Uri.EscapeDataString(text)),
            new EncodingAction("url_decode", "URL Decode", "IconDecode",
                text => Uri.UnescapeDataString(text)),
            new EncodingAction("base64_encode", "Base64 Encode", "IconEncode",
                text => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text))),
            new EncodingAction("base64_decode", "Base64 Decode", "IconDecode",
                text => { try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(text)); } catch { return "[Invalid Base64]"; } }),
            new EncodingAction("html_encode", "HTML Encode", "IconEncode",
                text => System.Net.WebUtility.HtmlEncode(text)),
            new EncodingAction("html_decode", "HTML Decode", "IconDecode",
                text => System.Net.WebUtility.HtmlDecode(text)),

            // Wrap actions
            new WrapAction("wrap_quotes", "Wrap \"quotes\"", "\"", "\""),
            new WrapAction("wrap_single_quotes", "Wrap 'quotes'", "'", "'"),
            new WrapAction("wrap_parens", "Wrap (parens)", "(", ")"),
            new WrapAction("wrap_brackets", "Wrap [brackets]", "[", "]"),
            new WrapAction("wrap_braces", "Wrap {braces}", "{", "}"),
            new WrapAction("wrap_backticks", "Wrap `backticks`", "`", "`"),

            // Hex / ROT13
            new EncodingAction("hex_encode", "Hex Encode", "IconEncode",
                text => Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(text)).ToLowerInvariant()),
            new EncodingAction("hex_decode", "Hex Decode", "IconDecode",
                text => { try { return System.Text.Encoding.UTF8.GetString(Convert.FromHexString(text.Trim())); } catch { return "[Invalid hex]"; } }),
            new EncodingAction("rot13", "ROT13", "IconEncode", Rot13),

            // Hash actions
            new EncodingAction("md5", "MD5", "IconHash", text => Hash(System.Security.Cryptography.MD5.HashData, text)),
            new EncodingAction("sha1", "SHA-1", "IconHash", text => Hash(System.Security.Cryptography.SHA1.HashData, text)),
            new EncodingAction("sha256", "SHA-256", "IconHash", text => Hash(System.Security.Cryptography.SHA256.HashData, text)),
            new EncodingAction("sha512", "SHA-512", "IconHash", text => Hash(System.Security.Cryptography.SHA512.HashData, text)),

        ];
    }

    private static string Rot13(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= 'a' and <= 'z') sb.Append((char)('a' + (c - 'a' + 13) % 26));
            else if (c is >= 'A' and <= 'Z') sb.Append((char)('A' + (c - 'A' + 13) % 26));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// All action IDs known to the registry, including the generated `search_<engine.Id>` ones.
    /// Used by SettingsManager.PruneStaleActionIds to drop orphan entries on Load.
    /// </summary>
    public static IReadOnlySet<string> GetAllKnownActionIds(IEnumerable<Config.SearchEngine> engines)
    {
        // Mirror the ActionRegistry constructor list. The registry instance isn't used because
        // SettingsManager.Load runs before any registry exists.
        var ids = new HashSet<string>(StringComparer.Ordinal)
        {
            // Context actions
            "open_url", "send_email", "open_filepath", "open_folder",
            "preview_color", "convert_color",
            "format_json", "minify_json", "format_xml", "strip_tags",
            "calculate", "ip_lookup", "decode_base64", "decode_jwt",
            "generate_qr", "generate_uuid", "convert_timezone",
            "translate", "dictionary", "currency_convert",
            "delete_text", "paste_plain",

            // Case transforms
            "case_upper", "case_lower", "case_title", "case_camel",
            "case_snake", "case_kebab", "case_pascal", "case_reverse",

            // Whitespace
            "ws_trim", "ws_remove_extra_spaces", "ws_sort_lines",
            "ws_dedup_lines", "ws_remove_linebreaks",

            // Encode
            "enc_url_encode", "enc_url_decode", "enc_base64_encode", "enc_base64_decode",
            "enc_html_encode", "enc_html_decode",
            "enc_hex_encode", "enc_hex_decode", "enc_rot13",
            "enc_md5", "enc_sha1", "enc_sha256", "enc_sha512",

            // Wrap
            "wrap_quotes", "wrap_single_quotes", "wrap_parens",
            "wrap_brackets", "wrap_braces", "wrap_backticks",
        };
        foreach (var e in engines) ids.Add($"search_{e.Id}");
        return ids;
    }

    public List<ActionGroup> GetActions(string text, TextAnalysis analysis)
    {
        var s = Config.SettingsManager.Current;
        var groups = new List<ActionGroup>();
        var disabled = s.DisabledActionIds;
        var applicable = _allActions
            .Where(a => a.CanExecute(text, analysis) && !disabled.Contains(a.Id))
            .ToList();

        var contextActions = applicable.Where(a => a.Category == ActionCategory.Context).ToList();
        if (contextActions.Count > 0)
            groups.Add(new ActionGroup("Context", "IconContext", contextActions));

        if (s.ShowTransformActions)
        {
            var list = applicable.Where(a => a.Category == ActionCategory.Transform).ToList();
            if (list.Count > 0) groups.Add(new ActionGroup("Transform", "IconTransform", list));
        }
        if (s.ShowEncodeActions)
        {
            var list = applicable.Where(a => a.Category == ActionCategory.Encode).ToList();
            if (list.Count > 0) groups.Add(new ActionGroup("Encode", "IconEncode", list));
        }
        if (s.ShowSearchActions && !string.IsNullOrEmpty(text.Trim()))
        {
            var lang = s.SearchLanguage ?? "";
            var searchActions = s.SearchEngines
                .Where(e => e.Enabled)
                .Select(e => (IAction)new SearchActions.WebSearchAction(
                    e.Id, e.Name, "IconSearch", e.UrlTemplate,
                    e.UseLanguageFilter ? lang : "", e.LangMode))
                .ToList();
            if (searchActions.Count > 0)
                groups.Add(new ActionGroup("Search", "IconSearch", searchActions));
        }

        return groups;
    }

    /// <summary>Get all actions for a category (including disabled ones) for the edit mode UI.</summary>
    public List<IAction> GetAllActionsForCategory(ActionCategory category)
    {
        if (category == ActionCategory.Search)
        {
            // Search actions are built from settings, not from _allActions
            var lang = Config.SettingsManager.Current.SearchLanguage ?? "";
            return Config.SettingsManager.Current.SearchEngines
                .Select(e => (IAction)new SearchActions.WebSearchAction(
                    e.Id, e.Name, "IconSearch", e.UrlTemplate,
                    e.UseLanguageFilter ? lang : "", e.LangMode))
                .ToList();
        }
        return _allActions.Where(a => a.Category == category).ToList();
    }

    // Text transformation helpers
    private static string ToTitleCase(string text) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower());

    private static string ToCamelCase(string text)
    {
        var words = SplitWords(text);
        if (words.Length == 0) return text;
        return words[0].ToLower() + string.Concat(words.Skip(1).Select(w =>
            char.ToUpper(w[0]) + w[1..].ToLower()));
    }

    private static string ToPascalCase(string text)
    {
        var words = SplitWords(text);
        return string.Concat(words.Select(w => char.ToUpper(w[0]) + w[1..].ToLower()));
    }

    private static string ToSnakeCase(string text) =>
        string.Join('_', SplitWords(text).Select(w => w.ToLower()));

    private static string ToKebabCase(string text) =>
        string.Join('-', SplitWords(text).Select(w => w.ToLower()));

    private static string ReverseGraphemes(string text)
    {
        // Iterate Unicode text elements so emoji and combining marks survive
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        var stack = new Stack<string>();
        while (enumerator.MoveNext())
            stack.Push((string)enumerator.Current);
        return string.Concat(stack);
    }

    private static string Hash(Func<byte[], byte[]> hashFn, string text)
    {
        var bytes = hashFn(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string SortLines(string text, bool distinct)
    {
        // Detect line ending: keep \r\n if input uses it, else \n
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        IEnumerable<string> seq = lines.OrderBy(l => l, StringComparer.OrdinalIgnoreCase);
        if (distinct) seq = seq.Distinct();
        return string.Join(nl, seq);
    }

    private static string[] SplitWords(string text)
    {
        // Split on spaces, underscores, hyphens, and camelCase boundaries
        var result = new List<string>();
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ' || c == '_' || c == '-' || c == '.')
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            }
            else if (i > 0 && char.IsUpper(c) && char.IsLower(text[i - 1]))
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                current.Append(c);
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result.Where(w => w.Length > 0).ToArray();
    }
}

public record ActionGroup(string Name, string IconKey, List<IAction> Actions);
